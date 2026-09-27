using System.Text.RegularExpressions;

namespace OptiscalerApp.Management;

/// <summary>Compares release tags ("v0.9.4", "0.9.5-pre4") and DLL file versions ("0.9.4.0") by their numbers.</summary>
public static class ReleaseVersion
{
    private static readonly Regex Numbers = new(@"\d+(\.\d+){1,3}", RegexOptions.CultureInvariant);

    private static Version? Parse(string? label)
    {
        // TryParse: a component too large for Int32, e.g. a date-like tag, is unreadable rather than an error.
        if (label is null || Numbers.Match(label) is not { Success: true } match ||
            !Version.TryParse(match.Value, out var version))
            return null;

        // Treat 0.9.4 and 0.9.4.0 as the same release.
        return new Version(version.Major, version.Minor, Math.Max(version.Build, 0), Math.Max(version.Revision, 0));
    }

    /// <summary>True only when both labels are readable and the available release is newer.</summary>
    public static bool IsNewer(string? available, string? installed)
    {
        return Parse(available) is { } latest && Parse(installed) is { } current && latest > current;
    }
}
