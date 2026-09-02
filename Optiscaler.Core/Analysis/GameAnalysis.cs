namespace Optiscaler.Core.Analysis;

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
    OptiPatcer,
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

public sealed record AnalysisEvidence
{
    public required string Code { get; init; }
    public required string Message { get; init; }
    public string? Path { get; init; }
    public EvidenceConfidence Confidence { get; init; }
}

public sealed record DetectedComponent
{
    public ComponentKind Kind { get; init; }
    public string? Version { get; init; }
    public required string Path { get; init; }
    public ComponentOrigin Origin { get; init; }
    public List<AnalysisEvidence> Evidences { get; init; } = [];
}

public sealed record GameAnalysis
{
    public required string GameId { get; init; }
    public InstallState InstallState { get; init; }
    public List<DetectedComponent> Components { get; init; } = [];
    public List<AnalysisEvidence> Evidence { get; init; } = [];
    public DateTimeOffset AnalyzedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}