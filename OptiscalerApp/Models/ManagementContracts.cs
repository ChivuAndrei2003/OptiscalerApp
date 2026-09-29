namespace OptiscalerApp.Models;

/// <summary>
///     OptiScaler.ini overrides keyed as <c>Section.Key</c> (see <c>ProfileSettings</c>) and applied to a package's INI
///     without discarding other lines. An omitted key keeps OptiScaler's own default.
/// </summary>
public sealed record RenderProfile
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = "Default";
    public string Description { get; init; } = "";
    public IReadOnlyDictionary<string, string> Settings { get; init; } = new Dictionary<string, string>();

    // Records compare dictionaries by reference; profiles are equal when their settings have the same content.
    public bool Equals(RenderProfile? other)
    {
        return other is not null && Id == other.Id && Name == other.Name && Description == other.Description &&
               Settings.Count == other.Settings.Count &&
               Settings.All(s => other.Settings.TryGetValue(s.Key, out var value) && value == s.Value);
    }

    public override int GetHashCode() { return HashCode.Combine(Id, Name, Settings.Count); }

    public override string ToString() { return Name; }
}

/// <summary>One INI key whose effective value differs between two versions of a file.</summary>
public sealed record IniChange(string Section, string Key, string? Before, string After)
{
    public override string ToString() { return $"[{Section}] {Key}: {Before ?? "(not set)"} → {After}"; }
}

public sealed class ProfileCatalog
{
    /// <summary>Version 2 keeps every override in <see cref="RenderProfile.Settings" />; version 1 is upgraded on load.</summary>
    public const int CurrentSchemaVersion = 2;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
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
    IReadOnlyList<PlannedFile> Files)
{
    /// <summary>The OptiScaler release tag or file version this plan installs, when known.</summary>
    public string? Version { get; init; }

    /// <summary>OptiScaler.ini keys whose value changes compared with the file currently in the game folder.</summary>
    public IReadOnlyList<IniChange> IniChanges { get; init; } = [];
}

/// <summary>A null <see cref="BeforeHash" /> means the destination did not exist before the operation.</summary>
public sealed class OperationFile
{
    public string RelativePath { get; set; } = "";
    public string? BeforeHash { get; set; }
    public string AfterHash { get; set; } = "";

    /// <summary>Size of the written file, for a quick check without hashing. Absent in older journals.</summary>
    public long? AfterLength { get; set; }
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

    /// <summary>The installed OptiScaler version, when the operation installed one.</summary>
    public string? Version { get; set; }
}

public enum ManagedHealth
{
    Healthy,
    Incomplete,
    FilesChanged
}

/// <summary>The latest unrestored operation in a folder, checked by file size so the whole library stays cheap.</summary>
public sealed record ManagedTarget(string TargetDirectory, OperationJournal Journal, ManagedHealth Health)
{
    /// <summary>The OptiScaler version from the newest active operation that installed one.</summary>
    public string? Version { get; init; }
}

public sealed record VerificationResult(OperationJournal? Journal, IReadOnlyList<string> Issues)
{
    /// <summary>Every unrestored operation in the folder, newest first; <see cref="Journal" /> is the first.</summary>
    public IReadOnlyList<OperationJournal> Operations { get; init; } = [];

    /// <summary>Managed files that no longer match what this app wrote; something else may own them now.</summary>
    public IReadOnlyList<string> ChangedFiles { get; init; } = [];

    public bool IsVerified => Journal?.State == OperationState.Installed && Issues.Count == 0;
}
