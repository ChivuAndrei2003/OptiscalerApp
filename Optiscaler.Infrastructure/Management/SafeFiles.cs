using System.Security.Cryptography;

namespace Optiscaler.Infrastructure.Management;

/// <summary>Filesystem rules shared by preview, installation, verification, and recovery.</summary>
internal static class SafeFiles
{
    /// <summary>Normalizes an absolute path and rejects links that could redirect file operations outside trusted locations.</summary>
    internal static string NormalizeAndValidateAbsolutePath(string path)
    {
        if (!Path.IsPathFullyQualified(path)) throw new InvalidDataException("Choose an absolute local path.");

        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var current = Path.GetPathRoot(full)!;

        foreach (var part in full[current.Length..]
                     .Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);
            // Also inspect dangling links. Following links could redirect a write outside the preview.
            var info = new FileInfo(current);

            if (info.LinkTarget is not null || (info.Attributes != (FileAttributes)(-1) &&
                                                (info.Attributes & FileAttributes.ReparsePoint) != 0))
                throw new InvalidDataException($"Symbolic links/reparse points are not supported: {current}");
        }

        return full;
    }

    /// <summary>Resolves a relative path under a root while preventing traversal outside the operation directory.</summary>
    internal static string ResolveSafeChildPath(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Contains(':') ||
            relative.Split('/', '\\').Any(p => p is ".." or "." or "" || p.EndsWith(' ') || p.EndsWith('.')))
            throw new InvalidDataException("Invalid relative file path in operation.");

        var path = NormalizeAndValidateAbsolutePath(Path.Combine(root, relative));

        if (!IsPathWithinRoot(path, root)) throw new InvalidDataException("File escapes its operation directory.");

        return path;
    }

    /// <summary>Checks whether a path belongs to a root using the platform's path comparison rules.</summary>
    internal static bool IsPathWithinRoot(string path, string root)
    {
        return path.StartsWith(
                               Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar,
                               OperatingSystem.IsWindows()
                                   ? StringComparison.OrdinalIgnoreCase
                                   : StringComparison.Ordinal);
    }

    /// <summary>Computes a SHA-256 hash so callers can detect missing or changed files before applying an operation.</summary>
    internal static async Task<string?> ComputeFileHash_Async(string path, CancellationToken cancellationToken)
    {
        NormalizeAndValidateAbsolutePath(path);

        if (Directory.Exists(path)) throw new IOException($"Expected a file, found a directory: {path}");

        if (!File.Exists(path)) return null;

        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);

        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>Copies a file exclusively and flushes it to storage to avoid silent overwrites or incomplete writes.</summary>
    internal static async Task CopyFile_Async(string source, string destination, CancellationToken cancellationToken)
    {
        NormalizeAndValidateAbsolutePath(source);
        NormalizeAndValidateAbsolutePath(destination);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                                                65536, FileOptions.Asynchronous | FileOptions.WriteThrough);
        await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Stages beside the destination so the final rename stays on the same filesystem.</summary>
    internal static async Task ReplaceFileAtomically_Async(string source, string destination,
                                                           CancellationToken cancellationToken)
    {
        var temp = destination + $".{Guid.NewGuid():N}.tmp";

        try
        {
            await CopyFile_Async(source, temp, cancellationToken).ConfigureAwait(false);
            NormalizeAndValidateAbsolutePath(destination);
            File.Move(temp, destination, true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    /// <summary>Validates the PE type and x64 architecture before a binary is used in a game operation.</summary>
    internal static void RequireX64PeFile(string path, bool dll)
    {
        NormalizeAndValidateAbsolutePath(path);
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);

        if (stream.Length < 64 || reader.ReadUInt16() != 0x5a4d)
            throw new InvalidDataException("Select a Windows x64 PE file.");

        stream.Position = 0x3c;
        var offset = reader.ReadInt32();

        if (offset < 64 || offset > stream.Length - 24) throw new InvalidDataException("Invalid PE header.");

        stream.Position = offset;

        if (reader.ReadUInt32() != 0x4550 || reader.ReadUInt16() != 0x8664)
            throw new InvalidDataException("Only Windows x64 games and DLLs are supported.");

        stream.Position = offset + 22;

        if ((reader.ReadUInt16() & 0x2000) != 0 != dll)
            throw new InvalidDataException(dll ? "Expected a DLL." : "Expected a game executable, not a DLL.");
    }
}