using System.Security.Cryptography;
using System.Text;
using OptiscalerApp.Models;
using OptiscalerApp.Paths;
using OptiscalerApp.Persistence;

namespace OptiscalerApp.Management;

/// <summary>
///     Installs local packages using hashed previews and durable journals. No package code is executed.
///     Unfinished operations remain discoverable and can be restored after restarting the app.
/// </summary>
public sealed class GameInstallationService(IAppPaths paths, PackageDownloadService packages) : IGameInstallationService
{
    public static readonly string[] ProxyNames =
        ["dxgi.dll", "winmm.dll", "d3d12.dll", "dbghelp.dll", "version.dll", "wininet.dll", "winhttp.dll"];

    public static readonly string[] NativeNames =
        ["nvngx_dlss.dll", "nvngx_dlssg.dll", "nvngx_dlssd.dll", "libxess.dll", "amd_fidelityfx_upscaler_dx12.dll"];

    private readonly SemaphoreSlim _gate = new(1, 1);

    private string Transactions =>
        SafeFiles.NormalizeAndValidateAbsolutePath(Path.Combine(paths.RootDirectory, "transactions"));

    /// <summary>Builds a hash-pinned installation plan so package contents can be validated before any game files are changed.</summary>
    public Task<InstallPlan> PreviewInstallation_Async(string executablePath, string packageDirectory,
        string proxyName, RenderProfile? profile,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(async () =>
        {
            var target = GetValidatedExecutableDirectory(executablePath);
            var package = SafeFiles.NormalizeAndValidateAbsolutePath(packageDirectory);

            if (package == target || SafeFiles.IsPathWithinRoot(package, target) ||
                SafeFiles.IsPathWithinRoot(target, package))
                throw new InvalidDataException("The package folder must be separate from the game folder.");
            if (!ProxyNames.Contains(proxyName)) throw new InvalidDataException("Unsupported proxy filename.");

            SafeFiles.RequireX64PeFile(Path.Combine(package, "OptiScaler.dll"), true);

            if (!File.Exists(Path.Combine(package, "OptiScaler.ini")))
                throw new InvalidDataException(
                    "Choose the extracted package folder containing OptiScaler.dll and OptiScaler.ini.");

            var files = new List<PlannedFile>();
            long bytes = 0;

            foreach (var source in EnumeratePackageFiles(package, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                bytes += new FileInfo(source).Length;

                if (files.Count >= 2000 || bytes > 2L * 1024 * 1024 * 1024)
                    throw new InvalidDataException("Package exceeds the 2 GB / 2,000 file limit.");

                var relative = Path.GetRelativePath(package, source);
                if (relative.Equals("OptiScaler.dll", StringComparison.OrdinalIgnoreCase)) relative = proxyName;
                var text = relative.Equals("OptiScaler.ini", StringComparison.OrdinalIgnoreCase) && profile is not null
                    ? ProfileIni.ApplyProfileToIni(await File.ReadAllTextAsync(source, cancellationToken), profile)
                    : null;
                files.Add(await CreatePlannedFile_Async(source, target, relative, text, cancellationToken));
            }

            if (files.Select(f => f.RelativePath).Distinct(StringComparer.OrdinalIgnoreCase).Count() != files.Count)
                throw new InvalidDataException("The package contains conflicting destination filenames.");

            return new InstallPlan(Guid.NewGuid(), target, OperationKind.InstallOptiscaler,
                $"Local package : {proxyName}" + (profile is null ? "" : $" : {profile.Name}"),
                files.AsReadOnly(), DateTimeOffset.UtcNow);
        }, cancellationToken);
    }

    public async Task<InstallPlan> PreviewPackageInstallation_Async(
        string executablePath, string packageDirectory, string proxyName, RenderProfile? profile,
        IReadOnlyList<ComponentInstallSelection> components, IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var plan = await PreviewInstallation_Async(
            executablePath, packageDirectory, proxyName, profile, cancellationToken);
        var skipped = components.Where(selection => selection.KeepExisting)
            .SelectMany(selection => selection.Component.FileNames)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        plan = plan with
        {
            Files = plan.Files.Where(file => !skipped.Contains(Path.GetFileName(file.RelativePath)))
                .ToList().AsReadOnly()
        };

        foreach (var selection in components)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (selection.KeepExisting) continue;

            if (selection.Release is { } release)
            {
                var sources = await packages.DownloadComponent_Async(
                    selection.Component, release, progress, cancellationToken);
                plan = await AddComponentFilesToPlan_Async(
                    plan, selection.Component, sources, release.Version, cancellationToken);
            }
            else if (selection.LocalPath is { } localPath)
            {
                plan = await AddComponentFilesToPlan_Async(
                    plan, selection.Component, [localPath], selection.LocalVersion, cancellationToken);
            }
        }

        return plan;
    }

    public static async Task<InstallPlan> AddComponentFilesToPlan_Async(InstallPlan plan, DownloadComponent component,
        IReadOnlyList<string> sources, string version,
        CancellationToken cancellationToken = default)
    {
        var names = sources.Select(Path.GetFileName).ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (names.Any(name => !component.FileNames.Contains(name, StringComparer.OrdinalIgnoreCase)))
            throw new InvalidDataException(
                $"Select a supported {component.Name} file: {string.Join(", ", component.FileNames)}.");

        if (component == DownloadComponent.Fsr &&
            !names.Contains("amd_fidelityfx_upscaler_dx12.dll") && !names.Contains("amdxcffx64.dll"))
            throw new InvalidDataException(
                "The FSR release must include an upscaler binary, not only the driver companion.");

        if (!names.Any(name => Path.GetExtension(name)?.ToLowerInvariant() is ".dll" or ".asi"))
            throw new InvalidDataException($"{component.Name} does not contain its expected binary.");

        if (names.Count != sources.Count)
            throw new InvalidDataException(
                "Release contains multiple variants of the same component. Use a local package to choose a variant.");

        var files = plan.Files
            .Where(file => !component.FileNames.Contains(Path.GetFileName(file.RelativePath), StringComparer.OrdinalIgnoreCase))
            .ToList();

        foreach (var source in sources)
        {
            SafeFiles.NormalizeAndValidateAbsolutePath(source);
            if (!source.EndsWith(".ini", StringComparison.OrdinalIgnoreCase)) SafeFiles.RequireX64PeFile(source, true);
            var relative = component.Destination(Path.GetFileName(source));
            files.Add(await CreatePlannedFile_Async(source, plan.TargetDirectory, relative, null, cancellationToken));
        }

        return plan with
        {
            Files = files.AsReadOnly(),
            Description = plan.Description + $" · {component.Name} {version}"
        };
    }

    /// <summary>Builds a validated plan for replacing a supported native DLL with a matching x64 binary.</summary>
    public Task<InstallPlan> PreviewNativeDllSwap_Async(string destinationDll, string sourceDll,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(async () =>
        {
            var destination = SafeFiles.NormalizeAndValidateAbsolutePath(destinationDll);
            var source = SafeFiles.NormalizeAndValidateAbsolutePath(sourceDll);
            var name = Path.GetFileName(destination);

            if (!NativeNames.Contains(name, StringComparer.OrdinalIgnoreCase) ||
                !Path.GetFileName(source).Equals(name, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Choose matching, supported native DLL filenames.");
            if (source.Equals(destination, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Source and destination must be different files.");

            SafeFiles.RequireX64PeFile(destination, true);
            SafeFiles.RequireX64PeFile(source, true);
            var target = Path.GetDirectoryName(destination)!;
            var file = await CreatePlannedFile_Async(source, target, name, null, cancellationToken);

            return new InstallPlan(Guid.NewGuid(), target, OperationKind.ReplaceNativeDll, $"Replace {name}",
                Array.AsReadOnly(new[] { file }), DateTimeOffset.UtcNow);
        }, cancellationToken);
    }

    /// <summary>Previews the exact configuration change so the profile can be verified before it is written.</summary>
    public Task<InstallPlan> PreviewProfileApplication_Async(string executablePath, RenderProfile profile,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(async () =>
        {
            var target = GetValidatedExecutableDirectory(executablePath);
            var source = SafeFiles.ResolveSafeChildPath(target, "OptiScaler.ini");
            var text = ProfileIni.ApplyProfileToIni(await File.ReadAllTextAsync(source, cancellationToken), profile);
            var file = await CreatePlannedFile_Async(source, target, "OptiScaler.ini", text, cancellationToken);

            return new InstallPlan(Guid.NewGuid(), target, OperationKind.ApplyProfile, $"Profile : {profile.Name}",
                Array.AsReadOnly(new[] { file }), DateTimeOffset.UtcNow);
        }, cancellationToken);
    }

    /// <summary>Applies a preview once using staged files, verified backups, and a durable journal for safe recovery.</summary>
    public async Task ExecuteInstallationPlan_Async(InstallPlan plan,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var processLock = AcquireOperationLock();
            var target = await ValidatePlanForExecution_Async(plan, cancellationToken);
            var journal = await PrepareInstallation_Async(plan, target, cancellationToken);
            var store = CreateJournalStore(plan.Id);

            await store.SaveJsonFile_Async(journal, cancellationToken);
            journal.State = OperationState.Applying;
            await store.SaveJsonFile_Async(journal, cancellationToken);

            await ApplyStagedFiles_Async(journal, cancellationToken);

            journal.State = OperationState.Installed;
            await store.SaveJsonFile_Async(journal, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<string> ValidatePlanForExecution_Async(InstallPlan plan, CancellationToken cancellationToken)
    {
        var target = SafeFiles.NormalizeAndValidateAbsolutePath(plan.TargetDirectory);
        if (!Directory.Exists(target) || plan.Files.Count == 0 || plan.Id == Guid.Empty)
            throw new InvalidDataException("Invalid or empty installation plan.");
        if (plan.Files.Select(file => file.RelativePath).Distinct(StringComparer.OrdinalIgnoreCase).Count() != plan.Files.Count)
            throw new InvalidDataException("Duplicate plan destinations.");

        var dataRoot = SafeFiles.NormalizeAndValidateAbsolutePath(paths.RootDirectory);
        if (AreSameTargetPaths(target, dataRoot) || SafeFiles.IsPathWithinRoot(target, dataRoot) ||
            SafeFiles.IsPathWithinRoot(dataRoot, target))
            throw new InvalidDataException("The installation target cannot overlap application data and backups.");

        var destinations = plan.Files.Select(file => SafeFiles.ResolveSafeChildPath(target, file.RelativePath))
            .ToHashSet(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        var history = await LoadAndValidateOperationHistory_Async(cancellationToken);
        foreach (var operation in history)
        {
            if (operation.State == OperationState.Restored) continue;

            if (AreSameTargetPaths(operation.TargetDirectory, target))
            {
                if (operation.State is OperationState.Prepared or OperationState.Applying or OperationState.Restoring)
                    throw new InvalidOperationException("Restore the incomplete operation before installing again.");
                continue;
            }

            foreach (var file in operation.Files)
            {
                var existingPath = SafeFiles.ResolveSafeChildPath(operation.TargetDirectory, file.RelativePath);
                if (destinations.Contains(existingPath))
                    throw new InvalidOperationException(
                        "Files overlap an operation in another folder. Restore that operation first.");
            }
        }

        if (Directory.Exists(ResolveJournalDirectory(plan.Id)))
            throw new InvalidOperationException("This preview has already been used.");
        return target;
    }

    private async Task<OperationJournal> PrepareInstallation_Async(
        InstallPlan plan, string target, CancellationToken cancellationToken)
    {
        var directory = ResolveJournalDirectory(plan.Id);
        var journal = new OperationJournal
        {
            Id = plan.Id,
            TargetDirectory = target,
            Description = plan.Description,
            Kind = plan.Kind,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            State = OperationState.Prepared,
            Files = plan.Files.Select(file => new OperationFile
            {
                RelativePath = file.RelativePath,
                BeforeHash = file.BeforeHash,
                AfterHash = file.AfterHash
            }).ToList()
        };

        // Stage every new file and back up every original before publishing a recoverable journal.
        foreach (var file in plan.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var destination = SafeFiles.ResolveSafeChildPath(target, file.RelativePath);
            await RequireExpectedFileHash_Async(file.SourcePath, file.SourceHash ?? file.AfterHash, cancellationToken);
            await RequireExpectedFileHash_Async(destination, file.BeforeHash, cancellationToken);

            var stagedPath = SafeFiles.ResolveSafeChildPath(Path.Combine(directory, "staged"), file.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(stagedPath)!);
            if (file.GeneratedText is null)
                await SafeFiles.CopyFile_Async(file.SourcePath, stagedPath, cancellationToken);
            else
                await File.WriteAllTextAsync(stagedPath, file.GeneratedText, new UTF8Encoding(false), cancellationToken);
            await RequireExpectedFileHash_Async(stagedPath, file.AfterHash, cancellationToken);

            if (file.BeforeHash is null) continue;

            var backupPath = SafeFiles.ResolveSafeChildPath(Path.Combine(directory, "original"), file.RelativePath);
            await SafeFiles.CopyFile_Async(destination, backupPath, cancellationToken);
            await RequireExpectedFileHash_Async(backupPath, file.BeforeHash, cancellationToken);
        }

        return journal;
    }

    private async Task ApplyStagedFiles_Async(OperationJournal journal, CancellationToken cancellationToken)
    {
        var stagedDirectory = Path.Combine(ResolveJournalDirectory(journal.Id), "staged");
        foreach (var file in journal.Files)
        {
            var destination = SafeFiles.ResolveSafeChildPath(journal.TargetDirectory, file.RelativePath);
            var stagedPath = SafeFiles.ResolveSafeChildPath(stagedDirectory, file.RelativePath);
            await RequireExpectedFileHash_Async(destination, file.BeforeHash, cancellationToken);
            await SafeFiles.ReplaceFileAtomically_Async(stagedPath, destination, cancellationToken);
            await RequireExpectedFileHash_Async(destination, file.AfterHash, cancellationToken);
        }
    }

    /// <summary>Compares installed files and backups with active journals to report changes or incomplete operations.</summary>
    public async Task<VerificationResult> VerifyInstallation_Async(
        string targetDirectory, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            using var processLock = AcquireOperationLock();
            var target = SafeFiles.NormalizeAndValidateAbsolutePath(targetDirectory);
            var active = (await LoadAndValidateOperationHistory_Async(cancellationToken))
                .Where(j => AreSameTargetPaths(j.TargetDirectory, target) && j.State != OperationState.Restored)
                .ToList();
            var journal = active.FirstOrDefault();
            var issues = new List<string>();
            var checkedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var operation in active)
            {
                if (operation.State != OperationState.Installed)
                    issues.Add("Incomplete operation. Use Restore to recover original files.");

                foreach (var file in operation.Files)
                {
                    // Newer operations override expectations for the same destination, but do not
                    // hide the remaining files or the original backups of earlier operations.
                    var destination = SafeFiles.ResolveSafeChildPath(target, file.RelativePath);
                    if (checkedFiles.Add(file.RelativePath) &&
                        await SafeFiles.ComputeFileHash_Async(destination, cancellationToken) != file.AfterHash)
                        issues.Add($"Changed or missing: {file.RelativePath}");

                    if (file.BeforeHash is null) continue;

                    var backupDirectory = Path.Combine(ResolveJournalDirectory(operation.Id), "original");
                    var backupPath = SafeFiles.ResolveSafeChildPath(backupDirectory, file.RelativePath);
                    if (await SafeFiles.ComputeFileHash_Async(backupPath, cancellationToken) != file.BeforeHash)
                        issues.Add($"Backup changed or missing: {file.RelativePath} ({operation.Id})");
                }
            }

            return new VerificationResult(journal, issues.AsReadOnly());
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    ///     Restores the latest managed operation after confirming that no later game or user changes would be
    ///     overwritten.
    /// </summary>
    public async Task RestoreLatestOperation_Async(string targetDirectory,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            using var processLock = AcquireOperationLock();
            var target = SafeFiles.NormalizeAndValidateAbsolutePath(targetDirectory);
            var journal = await GetLatestActiveOperation_Async(target, cancellationToken)
                ?? throw new InvalidOperationException("There is no managed operation to restore in this folder.");
            var backupDirectory = Path.Combine(ResolveJournalDirectory(journal.Id), "original");
            var store = CreateJournalStore(journal.Id);

            // Validate the whole set first. Never overwrite a game update or a user's later edit.
            foreach (var file in journal.Files)
            {
                var destination = SafeFiles.ResolveSafeChildPath(journal.TargetDirectory, file.RelativePath);
                var current = await SafeFiles.ComputeFileHash_Async(destination, cancellationToken);
                if (current != file.BeforeHash && current != file.AfterHash)
                    throw new IOException(
                        $"Restore blocked: {file.RelativePath} has changed. Preserve your changes and resolve the conflict first.");

                if (file.BeforeHash is null) continue;

                var backupPath = SafeFiles.ResolveSafeChildPath(backupDirectory, file.RelativePath);
                await RequireExpectedFileHash_Async(backupPath, file.BeforeHash, cancellationToken);
            }

            journal.State = OperationState.Restoring;
            await store.SaveJsonFile_Async(journal, cancellationToken);

            foreach (var file in journal.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var destination = SafeFiles.ResolveSafeChildPath(journal.TargetDirectory, file.RelativePath);
                var current = await SafeFiles.ComputeFileHash_Async(destination, cancellationToken);
                if (current == file.BeforeHash) continue;

                await RequireExpectedFileHash_Async(destination, file.AfterHash, cancellationToken);
                if (file.BeforeHash is null)
                {
                    File.Delete(destination);
                }
                else
                {
                    var backupPath = SafeFiles.ResolveSafeChildPath(backupDirectory, file.RelativePath);
                    await SafeFiles.ReplaceFileAtomically_Async(backupPath, destination, cancellationToken);
                }
                await RequireExpectedFileHash_Async(destination, file.BeforeHash, cancellationToken);
            }

            journal.State = OperationState.Restored;
            await store.SaveJsonFile_Async(journal, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Returns validated operation journals under a lock so callers receive a consistent history snapshot.</summary>
    public async Task<IReadOnlyList<OperationJournal>> GetOperationHistory_Async(
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            using var processLock = AcquireOperationLock();

            return await LoadAndValidateOperationHistory_Async(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Loads and validates persisted journals to reject corrupt or unsafe recovery data.</summary>
    private async Task<List<OperationJournal>> LoadAndValidateOperationHistory_Async(CancellationToken ct)
    {
        var journals = new List<OperationJournal>();

        if (!Directory.Exists(Transactions)) return journals;

        foreach (var directory in Directory.EnumerateDirectories(Transactions))
        {
            ct.ThrowIfCancellationRequested();

            if (!Guid.TryParseExact(Path.GetFileName(directory), "N", out var id)) continue;

            var journal = await CreateJournalStore(id).LoadJsonFile_Async(ct);

            if (journal is null) continue; // Abandoned staging never touched game files.

            // Persisted JSON can contain null despite the model's non-nullable declarations.
            if (journal.SchemaVersion != 1 || journal.Id != id || journal.Files is null ||
                journal.Files.Count == 0 || journal.Files.Any(file => file is null) ||
                !Enum.IsDefined(journal.State) ||
                !Enum.IsDefined(journal.Kind) ||
                journal.Files.Select(f => f.RelativePath).Distinct(StringComparer.OrdinalIgnoreCase).Count() !=
                journal.Files.Count)
                throw new InvalidDataException($"Invalid operation journal: {id}");

            SafeFiles.NormalizeAndValidateAbsolutePath(journal.TargetDirectory);

            foreach (var file in journal.Files)
            {
                SafeFiles.ResolveSafeChildPath(journal.TargetDirectory, file.RelativePath);

                if (!IsValidSha256Hash(file.AfterHash) ||
                    (file.BeforeHash is not null && !IsValidSha256Hash(file.BeforeHash)))
                    throw new InvalidDataException("Invalid operation file hash.");
            }

            journals.Add(journal);
        }

        return journals.OrderByDescending(j => j.CreatedAtUtc).ToList();
    }

    /// <summary>Finds the newest unrestored operation for a target so recovery proceeds in reverse order.</summary>
    private async Task<OperationJournal?> GetLatestActiveOperation_Async(string target, CancellationToken ct)
    {
        var history = await LoadAndValidateOperationHistory_Async(ct);
        return history.FirstOrDefault(operation =>
            AreSameTargetPaths(operation.TargetDirectory, target) && operation.State != OperationState.Restored);
    }

    /// <summary>Compares target paths using the case-sensitivity rules of the current operating system.</summary>
    private static bool AreSameTargetPaths(string left, string right)
    {
        return string.Equals(left, right,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    /// <summary>Resolves a transaction folder through the shared safe-path checks.</summary>
    private string ResolveJournalDirectory(Guid id)
    {
        return SafeFiles.ResolveSafeChildPath(Transactions, id.ToString("N"));
    }

    /// <summary>Creates atomic journal storage so interrupted writes do not destroy recovery metadata.</summary>
    private AtomicJsonFile<OperationJournal> CreateJournalStore(Guid id)
    {
        var journalPath = SafeFiles.ResolveSafeChildPath(ResolveJournalDirectory(id), "journal.json");
        return new AtomicJsonFile<OperationJournal>(journalPath, OptiscalerJsonContext.Default.OperationJournal);
    }

    /// <summary>Acquires a process-wide file lock to prevent concurrent operations from modifying the same state.</summary>
    private FileStream AcquireOperationLock()
    {
        Directory.CreateDirectory(Transactions);

        return new FileStream(SafeFiles.ResolveSafeChildPath(Transactions, "operations.lock"), FileMode.OpenOrCreate,
            FileAccess.ReadWrite, FileShare.None);
    }

    /// <summary>Accepts only complete SHA-256 hexadecimal hashes before trusting persisted journal values.</summary>
    private static bool IsValidSha256Hash(string? hash)
    {
        return hash is not null && hash.Length == 64 && hash.All(Uri.IsHexDigit);
    }

    /// <summary>Validates a game executable as x64 PE and returns its normalized containing directory.</summary>
    private static string GetValidatedExecutableDirectory(string path)
    {
        SafeFiles.RequireX64PeFile(path, false);

        return Path.GetDirectoryName(SafeFiles.NormalizeAndValidateAbsolutePath(path))!;
    }

    /// <summary>Requires the current file hash to match the preview so changed inputs are never applied silently.</summary>
    private static async Task RequireExpectedFileHash_Async(string path, string? expected, CancellationToken ct)
    {
        if (await SafeFiles.ComputeFileHash_Async(path, ct) != expected)
            throw new IOException($"File changed since preview or backup verification: {path}. Create a new preview.");
    }

    /// <summary>Captures the before, source, and resulting hashes needed to execute and later verify one file change.</summary>
    private static async Task<PlannedFile> CreatePlannedFile_Async(string source, string target, string relative,
        string? text, CancellationToken ct)
    {
        var before = await SafeFiles.ComputeFileHash_Async(SafeFiles.ResolveSafeChildPath(target, relative), ct);
        var sourceHash = await SafeFiles.ComputeFileHash_Async(source, ct);
        var after = text is null ? sourceHash : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

        return new PlannedFile(source, relative, before,
            after ?? throw new FileNotFoundException("Package file is missing.", source), text,
            sourceHash);
    }

    /// <summary>Enumerates a bounded package tree through safe paths to reject links and runaway directory layouts.</summary>
    private static IEnumerable<string> EnumeratePackageFiles(string root, CancellationToken cancellationToken)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        var directoryCount = 0;

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (++directoryCount > 4000) throw new InvalidDataException("Package exceeds the 4,000 directory limit.");

            var directory = pending.Pop();

            foreach (var file in Directory.EnumerateFiles(directory))
                yield return SafeFiles.NormalizeAndValidateAbsolutePath(file);

            foreach (var child in Directory.EnumerateDirectories(directory))
                pending.Push(SafeFiles.NormalizeAndValidateAbsolutePath(child));
        }
    }
}
