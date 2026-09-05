using Optiscaler.Core.Configuration;
using Optiscaler.Core.Games;

namespace Optiscaler.Core.Scanning;

public sealed record DiscoveredGame
{
    public required string Name { get; init; }

    public required GamePlatform Platform { get; init; }

    public string? ExternalId { get; init; }

    public required string InstallPath { get; init; }

    public string? ExecutablePath { get; init; }
}

public enum ScanDiagnosticSeverity
{
    Information,
    Warning,
    Error
}

public sealed record ScanDiagnostic
{
    public required GamePlatform Platform { get; init; }

    public required ScanDiagnosticSeverity Severity { get; init; }

    public required string Code { get; init; }

    public string? Message { get; init; }
}

public sealed record ScanContext
{
    public required IReadOnlySet<GamePlatform> EnabledPlatforms { get; init; }

    public IReadOnlyList<string> CustomFolders { get; init; } = [];

    public IReadOnlyList<string> AllowedDriveRoots { get; init; } = [];

    public bool IsEnabled(GamePlatform platform)
    {
        return EnabledPlatforms.Contains(platform);
    }

    public static ScanContext FromSettings(ScanSourceSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        // Defensive copies isolate an in-flight scan from settings changes made by the UI.
        return new ScanContext
        {
            EnabledPlatforms = settings.EnabledPlatforms.ToHashSet(),
            CustomFolders = settings.CustomFolders.ToList(),
            AllowedDriveRoots = settings.AllowedDriveRoots.ToList()
        };
    }
}

/// <summary>
/// Contains the combined games and diagnostics produced by one or more scanners.
/// </summary>
public sealed class ScanResult
{
    public List<DiscoveredGame> Games { get; set; } = [];

    public List<ScanDiagnostic> Diagnostics { get; set; } = [];
}