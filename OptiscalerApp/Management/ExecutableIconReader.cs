using System.Reflection.PortableExecutable;

namespace OptiscalerApp.Management;

internal static class ExecutableIconReader
{
    /// <summary>Reads Windows icon resources as data, including on Linux and macOS.</summary>
    public static byte[]? Read(string executable)
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
