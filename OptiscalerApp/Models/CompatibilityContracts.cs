namespace OptiscalerApp.Models;

public enum CompatibilityStatus
{
    Unconfirmed,
    Working,
    WorkingOnSingleOs,
    NotWorking
}

/// <summary>One row of the community-maintained OptiScaler wiki compatibility tables.</summary>
public sealed record CompatibilityEntry
{
    public required string GameName { get; init; }

    public CompatibilityStatus Status { get; init; }

    public string Inputs { get; init; } = "";

    public bool OptiPatcherSupported { get; init; }

    public string Notes { get; init; } = "";

    /// <summary>The detailed wiki page, when the row links to one.</summary>
    public string? PageUrl { get; init; }

    /// <summary>Injection filenames the notes mention, e.g. "Install as winmm.dll for the Xbox version".</summary>
    public List<string> MentionedProxies { get; init; } = [];
}

public sealed class CompatibilityCatalog
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public DateTimeOffset FetchedAtUtc { get; set; }

    public List<CompatibilityEntry> Entries { get; set; } = [];
}
