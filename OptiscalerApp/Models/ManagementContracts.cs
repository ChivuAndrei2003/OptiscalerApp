namespace OptiscalerApp.Models;

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

    /// <summary>Additional OptiScaler.ini overrides keyed as <c>Section.Key</c>; omitted settings stay on auto.</summary>
    public IReadOnlyDictionary<string, string> Settings { get; init; } = new Dictionary<string, string>();

    public bool Equals(RenderProfile? other)
    {
        return other is not null && Id == other.Id && Name == other.Name && Description == other.Description &&
               Dx11Upscaler == other.Dx11Upscaler && Dx12Upscaler == other.Dx12Upscaler &&
               Sharpness == other.Sharpness && EnableLogging == other.EnableLogging &&
               Settings.Count == other.Settings.Count &&
               Settings.All(s => other.Settings.TryGetValue(s.Key, out var value) && value == s.Value);
    }

    public override int GetHashCode() { return HashCode.Combine(Id, Name, Dx11Upscaler, Dx12Upscaler, Settings.Count); }

    public override string ToString() { return Name; }
}

public sealed class ProfileCatalog
{
    public int SchemaVersion { get; set; } = 1;
    public List<RenderProfile> Profiles { get; set; } = [];
    public Guid? DefaultProfileId { get; set; }
}

public enum OperationKind
{
    InstallOptiscaler,
    ReplaceNativeDll,
    ApplyProfile
}

// Explicit values keep journals written by earlier builds readable.
public enum OperationState
{
    Applying = 1,
    Installed = 2,
    Restored = 4
}

/// <summary>One file the plan will write. The source is ignored when <see cref="GeneratedText" /> is set.</summary>
public sealed record PlannedFile(
    string SourcePath,
    string RelativePath,
    bool ReplacesExisting,
    string? GeneratedText = null);

/// <summary>A read-only preview of the files an operation will write.</summary>
public sealed record InstallPlan(
    string TargetDirectory,
    OperationKind Kind,
    string Description,
    IReadOnlyList<PlannedFile> Files);

/// <summary>A null <see cref="BeforeHash" /> means the destination did not exist before the operation.</summary>
public sealed class OperationFile
{
    public string RelativePath { get; set; } = "";
    public string? BeforeHash { get; set; }
    public string AfterHash { get; set; } = "";
}

/// <summary>Written before game files change, so an interrupted operation can still be restored.</summary>
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
