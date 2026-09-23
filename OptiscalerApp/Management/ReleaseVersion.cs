using System.Text.RegularExpressions;

namespace OptiscalerApp.Management;

/// <summary>Compares release tags ("v0.9.4", "0.9.5-pre4") and DLL file versions ("0.9.4.0") by their numbers.</summary>
public static class ReleaseVersion
{
    private static readonly Regex Numbers = new(@"\d+(\.\d+){1,3}", RegexOptions.CultureInvariant);

    public static Version? Parse(string? label)
    {
        if (label is null || Numbers.Match(label) is not { Success: true } match) return null;

        var version = Version.Parse(match.Value);

        // Treat 0.9.4 and 0.9.4.0 as the same release.
        return new Version(version.Major, version.Minor, Math.Max(version.Build, 0), Math.Max(version.Revision, 0));
    }

    /// <summary>True only when both labels are readable and the available release is newer.</summary>
    public static bool IsNewer(string? available, string? installed)
    {
        return Parse(available) is { } latest && Parse(installed) is { } current && latest > current;
    }
}
