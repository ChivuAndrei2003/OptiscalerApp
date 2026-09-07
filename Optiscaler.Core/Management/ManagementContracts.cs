namespace Optiscaler.Core.Management;

/// <summary>Typed overrides applied to a package's original INI without discarding other settings.</summary>
public sealed record RenderProfile
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = "Default";
    public string Description { get; init; } = "";
    public string Dx11Upscaler { get; init; } = "auto";
    public string Dx12Upscaler { get; init; } = "auto";
    public decimal? Sharpness { get; init; }
    public bool EnableLogging { get; init; }

    public override string ToString()
    {
        return Name;
    }
}

public sealed class ProfileCatalog
{
    public int SchemaVersion { get; set; } = 1;
    public List<RenderProfile> Profiles { get; set; } = [];
}

public interface IProfileRepository
{
    Task<ProfileCatalog> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(ProfileCatalog catalog, CancellationToken cancellationToken = default);
}

public enum OperationKind
{
    InstallOptiscaler,
    ReplaceNativeDll,
    ApplyProfile
}

public enum OperationState
{
    Prepared,
    Applying,
    Installed,
    Restoring,
    Restored
}

/// <summary>A hashed file change. A null original hash means the destination did not exist.</summary>
public sealed record PlannedFile(
    string SourcePath,
    string RelativePath,
    string? BeforeHash,
    string AfterHash,
    string? GeneratedText = null,
    string? SourceHash = null);

/// <summary>A read-only preview. Execution rechecks all hashes before changing game files.</summary>
public sealed record InstallPlan(
    Guid Id,
    string TargetDirectory,
    OperationKind Kind,
    string Description,
    IReadOnlyList<PlannedFile> Files,
    DateTimeOffset CreatedAtUtc);

public sealed class OperationFile
{
    public string RelativePath { get; set; } = "";
    public string? BeforeHash { get; set; }
    public string AfterHash { get; set; } = "";
}

/// <summary>Durable write-ahead journal, retained with original bytes for recovery and auditing.</summary>
public sealed class OperationJournal
{
    public int SchemaVersion { get; set; } = 1;
    public Guid Id { get; set; }
    public string TargetDirectory { get; set; } = "";
    public string Description { get; set; } = "";
    public OperationKind Kind { get; set; }
    public OperationState State { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public List<OperationFile> Files { get; set; } = [];
}

public sealed record VerificationResult(OperationJournal? Journal, IReadOnlyList<string> Issues)
{
    public bool IsVerified => Journal?.State == OperationState.Installed && Issues.Count == 0;
}

public interface IGameInstallationService
{
    Task<InstallPlan> PreviewInstallAsync(string executablePath, string packageDirectory, string proxyName,
                                          RenderProfile? profile, CancellationToken cancellationToken = default);

    Task<InstallPlan> PreviewNativeSwapAsync(string destinationDll, string sourceDll,
                                             CancellationToken cancellationToken = default);

    Task<InstallPlan> PreviewProfileAsync(string executablePath, RenderProfile profile,
                                          CancellationToken cancellationToken = default);

    Task ExecuteAsync(InstallPlan plan, CancellationToken cancellationToken = default);
    Task<VerificationResult> VerifyAsync(string targetDirectory, CancellationToken cancellationToken = default);
    Task RestoreAsync(string targetDirectory, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OperationJournal>> GetHistoryAsync(CancellationToken cancellationToken = default);
}