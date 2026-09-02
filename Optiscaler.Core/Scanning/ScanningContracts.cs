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
    public HashSet<GamePlatform> EnabledPlatforms { get; init; } = [];
    public List<string> CustomFolders { get; init; } = [];
    public List<string> AllowedDriveRoots { get; init; } = [];

    public bool IsEnabled(GamePlatform platform)
    {
        return EnabledPlatforms.Contains(platform);
    }
}

public sealed record ScanResult
{
    public List<DiscoveredGame> Games { get; init; } = [];

    public List<ScanDiagnostic> Diagnostics { get; init; } = [];
}