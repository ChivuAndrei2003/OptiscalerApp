using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using OptiscalerApp.Models;
using OptiscalerApp.Paths;

namespace OptiscalerApp.Management;

/// <summary>Finds local game artwork/ image</summary>
public sealed class GameArtworkService(IAppPaths paths)
{
    private static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".webp", ".ico"];
    private static readonly string[] ImageNames = ["cover", "poster", "folder", "icon", "game", "header"];

    public Task<bool> PopulateArtwork_Async(IEnumerable<GameRecord> games,
                                            CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            var changed = false;

            foreach (var game in games)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (File.Exists(game.CoverImage)) continue;

                foreach (var installation in game.Installations)
                {
                    var cover = FindArtwork(installation);

                    if (cover is null) continue;

                    game.CoverImage = cover;
                    changed = true;

                    break;
                }
            }

            return changed;
        }, cancellationToken);
    }

    private string? FindArtwork(GameInstallation installation)
    {
        try
        {
            var root = SafeFiles.NormalizeAndValidateAbsolutePath(installation.RootPath);

            if (!Directory.Exists(root)) return null;

            var cover = FindCoverImage(root);

            if (cover is not null) return SafeFiles.NormalizeAndValidateAbsolutePath(cover);

            var executable = installation.PrimaryExecutablePath;

            if (string.IsNullOrWhiteSpace(executable))
            {
                var executables = Directory.EnumerateFiles(root)
                    .Where(file => Path.GetExtension(file).Equals(".exe", StringComparison.OrdinalIgnoreCase))
                    .Take(2).ToList();
                if (executables.Count == 1) executable = executables[0];
            }

            if (executable is null || !File.Exists(executable)) return null;

            var icon = ExtractExecutableIcon(executable);

            if (icon is null) return null;

            var cache = SafeFiles.NormalizeAndValidateAbsolutePath(Path.Combine(paths.RootDirectory, "covers"));
            Directory.CreateDirectory(cache);
            var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(executable)));
            var destination = SafeFiles.ResolveSafeChildPath(cache, key + ".ico");
            File.WriteAllBytes(destination, icon);

            return destination;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                                       or BadImageFormatException or InvalidOperationException or OverflowException)
        {
            // Missing or malformed artwork must not prevent adding the game.
            return null;
        }
    }

    private static string? FindCoverImage(string root)
    {
        var folders = new[]
        {
            root, Path.Combine(root, "assets"), Path.Combine(root, "images"), Path.Combine(root, "artwork")
        };

        return folders.Where(Directory.Exists)
            .SelectMany(folder => Directory.EnumerateFiles(folder))
            .Where(file => ImageExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
            .Where(file => ImageNames.Contains(Path.GetFileNameWithoutExtension(file),
                                               StringComparer.OrdinalIgnoreCase))
            .OrderByDescending(file => Path.GetFileNameWithoutExtension(file)
                                   .Equals("cover", StringComparison.OrdinalIgnoreCase))
            .ThenBy(file => file, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    /// <summary>Reads Windows icon resources as data, including on Linux and macOS.</summary>
    internal static byte[]? ExtractExecutableIcon(string executable)
    {
        using var stream = File.OpenRead(SafeFiles.NormalizeAndValidateAbsolutePath(executable));
        using var pe = new PEReader(stream);
        var directory = pe.PEHeaders.PEHeader?.ResourceTableDirectory;

        if (directory is null || directory.Value.Size == 0) return null;

        if (directory.Value.Size > 32 * 1024 * 1024)
            throw new InvalidDataException("Icon resource table is too large.");

        var data = pe.GetSectionData(directory.Value.RelativeVirtualAddress).GetContent(0, directory.Value.Size)
            .ToArray();

        int ReadInt32(int offset)
        {
            ValidateRange(offset, 4);

            return BitConverter.ToInt32(data, offset);
        }

        ushort ReadUInt16(int offset)
        {
            ValidateRange(offset, 2);

            return BitConverter.ToUInt16(data, offset);
        }

        void ValidateRange(int offset, int length)
        {
            if (offset < 0 || length < 0 || offset > data.Length - length)
                throw new InvalidDataException("Invalid icon resource.");
        }

        int FindResourceEntry(int table, int? id)
        {
            var count = ReadUInt16(table + 12) + ReadUInt16(table + 14);
            ValidateRange(table + 16, count * 8);

            for (var i = 0; i < count; i++)
            {
                var entry = table + 16 + i * 8;

                if (id is null || ReadInt32(entry) == id) return ReadInt32(entry + 4);
            }

            throw new InvalidDataException("Icon resource not found.");
        }

        byte[] ReadResource(int type, int? id)
        {
            // The high bit marks a subdirectory; the other bits are its offset in the resource table.
            var types = FindResourceEntry(0, type);

            if (types >= 0) throw new InvalidDataException("Invalid resource directory.");

            var names = FindResourceEntry(types & int.MaxValue, id);

            if (names >= 0) throw new InvalidDataException("Invalid resource name.");

            var leaf = FindResourceEntry(names & int.MaxValue, null);

            if (leaf < 0) throw new InvalidDataException("Invalid resource language.");

            var rva = ReadInt32(leaf);
            var length = ReadInt32(leaf + 4);

            if (length < 0 || length > 16 * 1024 * 1024) throw new InvalidDataException("Invalid icon size.");

            return pe.GetSectionData(rva).GetContent(0, length).ToArray();
        }

        // Windows stores icon metadata in RT_GROUP_ICON (14), and image bytes in RT_ICON (3).
        var group = ReadResource(14, null);

        if (group.Length < 6) return null;

        var imageCount = BitConverter.ToUInt16(group, 4);

        if (imageCount == 0 || imageCount > 256 || group.Length < 6 + imageCount * 14) return null;

        using var output = new MemoryStream();
        using var writer = new BinaryWriter(output);
        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write(imageCount);
        var icons = new List<byte[]>();

        // ICO entries use 16 bytes with a file offset; group entries use 14 bytes with a resource ID.
        var imageOffset = 6 + imageCount * 16;

        for (var i = 0; i < imageCount; i++)
        {
            var entry = 6 + i * 14;
            var icon = ReadResource(3, BitConverter.ToUInt16(group, entry + 12));
            writer.Write(group, entry, 8);
            writer.Write(icon.Length);
            writer.Write(imageOffset);
            imageOffset += icon.Length;
            icons.Add(icon);
        }

        foreach (var icon in icons) writer.Write(icon);

        return output.ToArray();
    }
}