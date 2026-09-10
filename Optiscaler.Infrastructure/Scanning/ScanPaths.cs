namespace Optiscaler.Infrastructure.Scanning;

/// <summary>Provides consistent path identity and directory filtering for game discovery.</summary>
internal static class ScanPaths
{
    internal static string NormalizeAbsoluteGamePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            throw new ArgumentException("An absolute path is required.");

        var fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

        // Resolve existing directory components so Linux Steam aliases share one identity.
        var current = Path.GetPathRoot(fullPath)!;

        foreach (var component in fullPath[current.Length..].Split(Path.DirectorySeparatorChar,
                                                                   StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, component);
            var directory = new DirectoryInfo(current);
            if (directory.Exists && directory.LinkTarget is not null)
                current = NormalizeAbsoluteGamePath(directory.ResolveLinkTarget(true)!.FullName);
        }

        return Path.TrimEndingDirectorySeparator(current);
    }

    internal static bool IsPathWithinRoot(string path, string root)
    {
        return string.Equals(path, root,
                             OperatingSystem.IsWindows()
                                 ? StringComparison.OrdinalIgnoreCase
                                 : StringComparison.Ordinal) ||
               path.StartsWith(Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar,
                               OperatingSystem.IsWindows()
                                   ? StringComparison.OrdinalIgnoreCase
                                   : StringComparison.Ordinal);
    }
}