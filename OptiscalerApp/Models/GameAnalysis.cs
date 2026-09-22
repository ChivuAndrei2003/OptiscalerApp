namespace OptiscalerApp.Models;

public enum ComponentKind
{
    Dlss,
    DlssFrameGeneration,
    Fsr,
    Xess,
    Optiscaler,
    Fakenvapi,
    InjectionProxy
}

/// <summary>Records one observable fact found while analyzing a game folder.</summary>
public sealed record GameAnalysisEvidence
{
    public required string Code { get; init; }

    public required string Message { get; init; }

    public string? Path { get; init; }
}

/// <summary>Describes a rendering component detected at a specific path, identified by filename only.</summary>
public sealed class DetectedComponent
{
    public string DisplayName => GetKindName(Kind) + (string.IsNullOrWhiteSpace(Version) ? "" : $" · {Version}");

    public static string GetKindName(ComponentKind kind)
    {
        return kind switch
        {
            ComponentKind.Dlss => "DLSS",
            ComponentKind.DlssFrameGeneration => "DLSS Frame Generation",
            ComponentKind.Fsr => "AMD FSR",
            ComponentKind.Xess => "Intel XeSS",
            ComponentKind.Optiscaler => "OptiScaler",
            ComponentKind.Fakenvapi => "Fakenvapi",
            _ => "Injection library"
        };
    }

    public ComponentKind Kind { get; set; }

    public string? Version { get; set; }

    public required string Path { get; set; }
}

public sealed class GameAnalysis
{
    public required GameId GameId { get; set; }

    /// <summary>OptiScaler or a proxy DLL is present, whether or not this app installed it.</summary>
    public bool HasOptiscalerFiles { get; set; }

    public List<DetectedComponent> Components { get; set; } = [];

    public List<GameAnalysisEvidence> Evidence { get; set; } = [];

    public DateTimeOffset AnalyzedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
