using System.Security.Cryptography;
using System.Text;

namespace OptiscalerApp.Management;

/// <summary>File operations shared by installation, verification, and recovery.</summary>
internal static class SafeFiles
{
    /// <summary>Returns the SHA-256 of a file, or null when it does not exist.</summary>
    internal static async Task<string?> ComputeFileHash_Async(string path, CancellationToken cancellationToken)
    {
        if (Directory.Exists(path)) throw new IOException($"Expected a file, found a directory: {path}");

        if (!File.Exists(path)) return null;

        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);

        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
    }

    internal static string ComputeTextHash(string text)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }

    internal static Task CopyFileAtomically_Async(string source, string destination,
                                                  CancellationToken cancellationToken)
    {
        return WriteAtomically_Async(destination, async output =>
        {
            await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 65536,
                                                   true);
            await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
        });
    }

    /// <summary>Writes UTF-8 without a byte order mark, matching <see cref="ComputeTextHash" />.</summary>
    internal static Task WriteTextAtomically_Async(string destination, string text,
                                                   CancellationToken cancellationToken)
    {
        return WriteAtomically_Async(destination,
                                     output => output.WriteAsync(Encoding.UTF8.GetBytes(text), cancellationToken)
                                         .AsTask());
    }

    // Stage beside the destination so the final rename stays on the same filesystem.
    private static async Task WriteAtomically_Async(string destination, Func<Stream, Task> write)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temp = destination + $".{Guid.NewGuid():N}.tmp";

        try
        {
            await using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                                                     65536, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await write(output).ConfigureAwait(false);
            }

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
