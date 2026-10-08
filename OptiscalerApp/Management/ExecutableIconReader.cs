using AsmResolver;
using AsmResolver.IO;
using AsmResolver.PE;
using AsmResolver.PE.File;
using AsmResolver.PE.Win32Resources.Icon;

namespace OptiscalerApp.Management;

internal static class ExecutableIconReader
{
    // Real icon groups stay well under a megabyte; anything larger is a damaged or hostile file.
    private const long MaxIconBytes = 16 * 1024 * 1024;

    /// <summary>
    ///     Returns the executable's first icon as .ico file content, or null when it has none or cannot be read.
    ///     Windows icon resources are read as data, including on Linux and macOS.
    /// </summary>
    public static byte[]? Read(string executable)
    {
        try
        {
            // Mapped rather than loaded: game executables can be hundreds of megabytes.
            using var files = new MemoryMappedFileService();
            var resources = PEImage.FromFile(PEFile.FromFile(files.OpenFile(executable))).Resources;
            var icons = resources is null
                ? null
                : IconResource.FromDirectory(resources, IconType.Icon)?.Groups.FirstOrDefault()?.Icons;

            if (icons is null || icons.Count == 0 ||
                icons.Sum(icon => (long)(icon.PixelData?.GetPhysicalSize() ?? 0)) > MaxIconBytes)
                return null;

            var images = icons.Select(icon => icon.PixelData?.WriteIntoArray() ?? []).ToList();
            using var output = new MemoryStream();
            using var writer = new BinaryWriter(output);

            // An .ico file is the icon group followed by its images, with each entry pointing at its image.
            writer.Write((ushort)0);
            writer.Write((ushort)1);
            writer.Write((ushort)icons.Count);
            var offset = 6 + 16 * icons.Count;

            for (var i = 0; i < icons.Count; i++)
            {
                writer.Write(icons[i].Width);
                writer.Write(icons[i].Height);
                writer.Write(icons[i].ColorCount);
                writer.Write(icons[i].Reserved);
                writer.Write(icons[i].Planes);
                writer.Write(icons[i].BitsPerPixel);
                writer.Write(images[i].Length);
                writer.Write(offset);
                offset += images[i].Length;
            }

            foreach (var image in images) writer.Write(image);

            return output.ToArray();
        }
        catch (Exception)
        {
            // Executables are untrusted and a damaged one fails in many ways; all of them mean "no icon".
            return null;
        }
    }
}
