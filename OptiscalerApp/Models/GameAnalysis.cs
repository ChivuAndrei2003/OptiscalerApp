namespace OptiscalerApp.Models;

public enum ComponentKind
{
    Dlss,
    DlssFrameGeneration,
    Fsr,
    Xess,
    Optiscaler,
    Fakenvapi,
    NukemFrameGeneration,
    Fsr4Extra,
    OptiPatcher,
    InjectionProxy
}

public enum ComponentOrigin
{
    Native,
    ManagedByOptiscalerApp,
    Untracked,
    Unknown
}

public enum EvidenceConfidence
{
    Low,
    Medium,
    High
}

public enum InstallState
{
    NotInstalled,
    InstalledAndVerified,
    InstalledButChanged,
    IncompleteOperation,
    UntrackedInstallation,
    RestoreAvailable,
    NeedsAttention
}

/// <summary>
/// Records one observable fact used by the analyzer to support a conclusion.
/// </summary>
public sealed record GameAnalysisEvidence
{
    public required string Code { get; init; }

    public required string Message { get; init; }

    public string? Path { get; init; }

    public EvidenceConfidence Confidence { get; init; }
}

/// <summary>
/// Describes a rendering component detected at a specific path.
/// </summary>
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
            ComponentKind.NukemFrameGeneration => "NukemFG",
            ComponentKind.Fsr4Extra => "FSR 4",
            ComponentKind.OptiPatcher => "OptiPatcher",
            _ => "Injection library"
        };
    }

    public ComponentKind Kind { get; set; }

    public string? Version { get; set; }

    public required string Path { get; set; }

    public ComponentOrigin Origin { get; set; }

    public List<GameAnalysisEvidence> Evidence { get; set; } = [];
}

public sealed class GameAnalysis
{
    public required GameId GameId { get; set; }

    public InstallState InstallState { get; set; }

    public List<DetectedComponent> Components { get; set; } = [];

    public List<GameAnalysisEvidence> Evidence { get; set; } = [];

    public DateTimeOffset AnalyzedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}