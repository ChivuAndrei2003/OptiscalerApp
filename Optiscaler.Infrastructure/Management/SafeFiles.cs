using System.Security.Cryptography;

namespace Optiscaler.Infrastructure.Management;

/// <summary>Filesystem rules shared by preview, installation, verification, and recovery.</summary>
internal static class SafeFiles
{
    internal static string Absolute(string path)
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

    internal static string Child(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Contains(':') ||
            relative.Split('/', '\\').Any(p => p is ".." or "." or "" || p.EndsWith(' ') || p.EndsWith('.')))
            throw new InvalidDataException("Invalid relative file path in operation.");

        var path = Absolute(Path.Combine(root, relative));

        if (!IsWithin(path, root)) throw new InvalidDataException("File escapes its operation directory.");

        return path;
    }

    internal static bool IsWithin(string path, string root)
    {
        return path.StartsWith(
                               Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar,
                               OperatingSystem.IsWindows()
                                   ? StringComparison.OrdinalIgnoreCase
                                   : StringComparison.Ordinal);
    }

    internal static async Task<string?> HashAsync(string path, CancellationToken cancellationToken)
    {
        Absolute(path);

        if (Directory.Exists(path)) throw new IOException($"Expected a file, found a directory: {path}");

        if (!File.Exists(path)) return null;

        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);

        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
    }

    internal static async Task CopyAsync(string source, string destination, CancellationToken cancellationToken)
    {
        Absolute(source);
        Absolute(destination);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                                                65536, FileOptions.Asynchronous | FileOptions.WriteThrough);
        await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Stages beside the destination so the final rename stays on the same filesystem.</summary>
    internal static async Task ReplaceAsync(string source, string destination, CancellationToken cancellationToken)
    {
        var temp = destination + $".{Guid.NewGuid():N}.tmp";

        try
        {
            await CopyAsync(source, temp, cancellationToken).ConfigureAwait(false);
            Absolute(destination);
            File.Move(temp, destination, true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    internal static void RequireX64Pe(string path, bool dll)
    {
        Absolute(path);
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