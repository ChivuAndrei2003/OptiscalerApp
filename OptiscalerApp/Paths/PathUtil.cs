namespace OptiscalerApp.Paths;

/// <summary>Path identity and containment rules shared by discovery, installation, and downloads.</summary>
public static class PathUtil
{
    public static StringComparison Comparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public static StringComparer Comparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    /// <summary>
    ///     Returns the absolute path with every existing symbolic link or junction resolved, so a link can never
    ///     make a path look like it is inside a folder when its data lives elsewhere.
    /// </summary>
    public static string Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            throw new ArgumentException($"An absolute path is required: '{path}'.");

        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var current = Path.GetPathRoot(full)!;

        foreach (var part in full[current.Length..].Split(Path.DirectorySeparatorChar,
                                                          StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);
            var info = new FileInfo(current);

            if (info.LinkTarget is null) continue;

            // A dangling link still points somewhere; resolve it lexically so containment checks see the target.
            var target = info.ResolveLinkTarget(true)?.FullName ??
                         Path.GetFullPath(info.LinkTarget, Path.GetDirectoryName(current)!);
            current = Normalize(target);
        }

        return Path.TrimEndingDirectorySeparator(current);
    }

    public static bool AreSame(string left, string right) { return string.Equals(left, right, Comparison); }

    /// <summary>True when <paramref name="path" /> is <paramref name="root" /> or below it. Both must be normalized.</summary>
    public static bool IsWithin(string path, string root)
    {
        root = Path.TrimEndingDirectorySeparator(root);

        return AreSame(path, root) || path.StartsWith(root + Path.DirectorySeparatorChar, Comparison);
    }

    /// <summary>Resolves a relative path below a root and rejects anything that would land outside it.</summary>
    public static string ResolveChild(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Contains(':') ||
            relative.Split('/', '\\').Any(p => p is ".." or "." or "" || p.EndsWith(' ') || p.EndsWith('.')))
            throw new InvalidDataException($"Invalid relative path: '{relative}'.");

        root = Normalize(root);
        var path = Normalize(Path.Combine(root, relative));

        return !AreSame(path, root) && IsWithin(path, root)
            ? path
            : throw new InvalidDataException($"'{relative}' resolves outside '{root}'.");
    }
}
