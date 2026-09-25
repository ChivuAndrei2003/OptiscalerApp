using System.Text;
using OptiscalerApp.Models;
using OptiscalerApp.Paths;
using OptiscalerApp.Persistence;

namespace OptiscalerApp.Management;

/// <summary>
///     Copies package files into a game folder after backing up whatever they replace. Each operation keeps a journal
///     (written before any game file changes) so it can be verified and restored, even after a crash or restart.
/// </summary>
public sealed class GameInstallationService(IAppPaths paths, PackageDownloadService packages) : IGameInstallationService
{
    public static readonly string[] ProxyNames =
        ["dxgi.dll", "winmm.dll", "d3d12.dll", "dbghelp.dll", "version.dll", "wininet.dll", "winhttp.dll"];

    public static readonly string[] NativeNames =
        ["nvngx_dlss.dll", "nvngx_dlssg.dll", "nvngx_dlssd.dll", "libxess.dll", "amd_fidelityfx_upscaler_dx12.dll"];

    // Guards against picking a whole drive as the package folder.
    private const int MaxPackageFiles = 2000;

    private readonly SemaphoreSlim _gate = new(1, 1);

    private string TransactionsDirectory => Path.Combine(paths.RootDirectory, "transactions");

    public Task<InstallPlan> PreviewInstallation_Async(string executablePath, string packageDirectory,
                                                       string proxyName, RenderProfile? profile,
                                                       CancellationToken cancellationToken = default,
                                                       bool keepCurrentSettings = false)
    {
        return PreviewPackageInstallation_Async(executablePath, packageDirectory, proxyName, profile, [], null,
                                                cancellationToken, keepCurrentSettings);
    }

    public async Task<InstallPlan> PreviewPackageInstallation_Async(
        string executablePath, string packageDirectory, string proxyName, RenderProfile? profile,
        IReadOnlyList<ComponentInstallSelection> components, IProgress<string>? progress = null,
        CancellationToken cancellationToken = default, bool keepCurrentSettings = false)
    {
        var plan = await PreviewPackageFiles_Async(executablePath, packageDirectory, proxyName, profile,
                                                   cancellationToken);
        var skipped = components.Where(selection => selection.KeepExisting)
            .SelectMany(selection => selection.Component.FileNames)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        plan = plan with
        {
            Files = plan.Files.Where(file => !skipped.Contains(Path.GetFileName(file.RelativePath))).ToList()
        };

        foreach (var selection in components)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (selection.KeepExisting) continue;

            if (selection.Release is { } release)
            {
                var sources = await packages.DownloadComponent_Async(selection.Component, release, progress,
                                                                     cancellationToken);
                plan = AddComponentFilesToPlan(plan, selection.Component, sources, release.Version);
            }
            else if (selection.LocalPath is { } localPath)
            {
                plan = AddComponentFilesToPlan(plan, selection.Component, [localPath], selection.LocalVersion);
            }
        }

        // The INI is written last, once the final file list shows whether plugins need loading.
        return await ComposeIni_Async(plan, profile, keepCurrentSettings, cancellationToken);
    }

    /// <summary>Lists the package files to copy; OptiScaler.ini is copied as is until <see cref="ComposeIni_Async" />.</summary>
    private Task<InstallPlan> PreviewPackageFiles_Async(string executablePath, string packageDirectory,
                                                        string proxyName, RenderProfile? profile,
                                                        CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            var target = GetExecutableDirectory(executablePath);
            var package = PathUtil.Normalize(packageDirectory);

            if (PathUtil.IsWithin(package, target) || PathUtil.IsWithin(target, package))
                throw new InvalidDataException("The package folder must be separate from the game folder.");
            if (!ProxyNames.Contains(proxyName)) throw new InvalidDataException("Unsupported proxy filename.");

            var dll = Path.Combine(package, "OptiScaler.dll");
            SafeFiles.RequireX64PeFile(dll, true);

            if (!File.Exists(Path.Combine(package, "OptiScaler.ini")))
                throw new InvalidDataException(
                                               "Choose the extracted package folder containing OptiScaler.dll and OptiScaler.ini.");

            var files = new List<PlannedFile>();
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint
            };

            foreach (var source in Directory.EnumerateFiles(package, "*", options))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (files.Count >= MaxPackageFiles)
                    throw new InvalidDataException($"Package has more than {MaxPackageFiles} files.");

                var relative = Path.GetRelativePath(package, source);
                if (relative.Equals("OptiScaler.dll", StringComparison.OrdinalIgnoreCase)) relative = proxyName;
                files.Add(PlanFile(source, target, relative, null));
            }

            if (files.Select(f => f.RelativePath).Distinct(StringComparer.OrdinalIgnoreCase).Count() != files.Count)
                throw new InvalidDataException("The package contains conflicting destination filenames.");

            return new InstallPlan(target, OperationKind.InstallOptiscaler,
                                   $"Local package : {proxyName}" + (profile is null ? "" : $" : {profile.Name}"),
                                   files)
            {
                Version = packages.GetPackageVersion(package) ?? SafeFiles.ReadFileVersion(dll)
            };
        }, cancellationToken);
    }

    /// <summary>
    ///     Builds OptiScaler.ini in one pass: package defaults, then the user's current settings when kept, then the
    ///     profile, which is the most explicit. When settings are kept, only what the profile actually sets replaces them.
    /// </summary>
    private static async Task<InstallPlan> ComposeIni_Async(InstallPlan plan, RenderProfile? profile, bool keep,
                                                            CancellationToken cancellationToken)
    {
        var files = plan.Files.ToList();
        var index = files.FindIndex(file => file.RelativePath.Equals("OptiScaler.ini",
                                                                     StringComparison.OrdinalIgnoreCase));

        if (index < 0) return plan;

        var packaged = await File.ReadAllTextAsync(files[index].SourcePath, cancellationToken);
        var current = await ReadCurrentIni_Async(plan.TargetDirectory, cancellationToken);
        var carried = 0;
        keep &= current is not null;
        var ini = keep ? ProfileIni.CarryOverSettings(packaged, current!, out carried) : packaged;
        if (profile is not null) ini = ProfileIni.ApplyProfileToIni(ini, profile, keep);

        // OptiScaler ignores plugins/*.asi unless LoadAsiPlugins is set. A kept OptiPatcher must keep loading after an
        // update; other ASI files in plugins/ may belong to Ultimate ASI Loader, which already loads them.
        if (files.Any(file => file.RelativePath.EndsWith(".asi", StringComparison.OrdinalIgnoreCase)) ||
            File.Exists(Path.Combine(plan.TargetDirectory, "plugins", "OptiPatcher.asi")))
            ini = ProfileIni.SetIniValue(ini, "Plugins", "LoadAsiPlugins", "true");

        files[index] = files[index] with { GeneratedText = ini == packaged ? null : ini };

        return plan with
        {
            Files = files,
            Description = plan.Description + (carried > 0 ? $" · kept {carried} current settings" : ""),
            IniChanges = ProfileIni.CompareIni(current ?? packaged, ini)
        };
    }

    public Task<InstallPlan> PreviewNativeDllSwap_Async(string destinationDll, string sourceDll,
                                                        CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            var destination = PathUtil.Normalize(destinationDll);
            var source = PathUtil.Normalize(sourceDll);
            var name = Path.GetFileName(destination);

            if (!NativeNames.Contains(name, StringComparer.OrdinalIgnoreCase) ||
                !Path.GetFileName(source).Equals(name, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Choose matching, supported native DLL filenames.");
            if (PathUtil.AreSame(source, destination))
                throw new InvalidDataException("Source and destination must be different files.");

            SafeFiles.RequireX64PeFile(destination, true);
            SafeFiles.RequireX64PeFile(source, true);
            var target = Path.GetDirectoryName(destination)!;

            return new InstallPlan(target, OperationKind.ReplaceNativeDll, $"Replace {name}",
                                   [PlanFile(source, target, name, null)]);
        }, cancellationToken);
    }

    public Task<InstallPlan> PreviewProfileApplication_Async(string executablePath, RenderProfile profile,
                                                             CancellationToken cancellationToken = default)
    {
        return Task.Run(async () =>
        {
            var target = GetExecutableDirectory(executablePath);
            var source = PathUtil.ResolveChild(target, "OptiScaler.ini");
            var current = await File.ReadAllTextAsync(source, cancellationToken);
            var text = ProfileIni.ApplyProfileToIni(current, profile);

            return new InstallPlan(target, OperationKind.ApplyProfile, $"Profile : {profile.Name}",
                                   [PlanFile(source, target, "OptiScaler.ini", text)])
            {
                IniChanges = ProfileIni.CompareIni(current, text)
            };
        }, cancellationToken);
    }

    public static InstallPlan AddComponentFilesToPlan(InstallPlan plan, DownloadComponent component,
                                                      IReadOnlyList<string> sources, string version)
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
            .Where(file => !component.FileNames.Contains(Path.GetFileName(file.RelativePath),
                                                         StringComparer.OrdinalIgnoreCase))
            .ToList();

        foreach (var source in sources)
        {
            if (!source.EndsWith(".ini", StringComparison.OrdinalIgnoreCase)) SafeFiles.RequireX64PeFile(source, true);
            var relative = component.Destination(Path.GetFileName(source));
            files.Add(PlanFile(source, plan.TargetDirectory, relative, null));
        }

        return plan with { Files = files, Description = plan.Description + $" · {component.Name} {version}" };
    }

    public async Task ExecuteInstallationPlan_Async(InstallPlan plan, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var target = await ValidatePlan_Async(plan, cancellationToken);
            var journal = new OperationJournal
            {
                Id = Guid.NewGuid(),
                TargetDirectory = target,
                Description = plan.Description,
                Kind = plan.Kind,
                State = OperationState.Applying,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                Version = plan.Version
            };
            var directory = GetJournalDirectory(journal.Id);

            try
            {
                // Back up originals and record the expected result before the first game file changes.
                foreach (var file in plan.Files)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var destination = PathUtil.ResolveChild(target, file.RelativePath);
                    var before = await SafeFiles.ComputeFileHash_Async(destination, cancellationToken);

                    if (before is not null)
                        await SafeFiles.CopyFileAtomically_Async(
                                                                 destination,
                                                                 GetBackupPath(journal.Id, file.RelativePath),
                                                                 cancellationToken);

                    var after = file.GeneratedText is { } text
                        ? SafeFiles.ComputeTextHash(text)
                        : await SafeFiles.ComputeFileHash_Async(file.SourcePath, cancellationToken) ??
                          throw new FileNotFoundException("Package file is missing.", file.SourcePath);
                    journal.Files.Add(new OperationFile
                    {
                        RelativePath = file.RelativePath,
                        BeforeHash = before,
                        AfterHash = after,
                        AfterLength = file.GeneratedText is { } written
                            ? Encoding.UTF8.GetByteCount(written)
                            : new FileInfo(file.SourcePath).Length
                    });
                }

                await CreateJournalStore(journal.Id).SaveJsonFile_Async(journal, cancellationToken);
            }
            catch
            {
                // Nothing in the game folder has changed yet.
                if (Directory.Exists(directory)) Directory.Delete(directory, true);

                throw;
            }

            try
            {
                foreach (var file in plan.Files)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var destination = PathUtil.ResolveChild(target, file.RelativePath);

                    if (file.GeneratedText is { } text)
                        await SafeFiles.WriteTextAtomically_Async(destination, text, cancellationToken);
                    else
                        await SafeFiles.CopyFileAtomically_Async(file.SourcePath, destination, cancellationToken);
                }
            }
            catch
            {
                // Roll back so a failed install never leaves a half-modified game. If the rollback also fails,
                // the journal stays in Applying and the user can restore it later.
                try
                {
                    await RestoreFiles_Async(journal, CancellationToken.None);
                    journal.State = OperationState.Restored;
                    await CreateJournalStore(journal.Id).SaveJsonFile_Async(journal, CancellationToken.None);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
                {
                }

                throw;
            }

            journal.State = OperationState.Installed;
            await CreateJournalStore(journal.Id).SaveJsonFile_Async(journal, CancellationToken.None);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<VerificationResult> VerifyInstallation_Async(string targetDirectory,
                                                                   CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var target = PathUtil.Normalize(targetDirectory);
            var active = ActiveOperations(await LoadHistory_Async(cancellationToken))
                             .FirstOrDefault(operations => PathUtil.AreSame(operations.Key, target))?.ToList() ??
                         [];
            var issues = new List<string>();
            var changed = new List<string>();

            foreach (var operation in active.Where(o => o.State != OperationState.Installed))
                issues.Add("Incomplete operation. Use Restore to recover original files.");

            foreach (var file in LatestExpectations(active))
                if (await SafeFiles.ComputeFileHash_Async(PathUtil.ResolveChild(target, file.RelativePath),
                                                          cancellationToken) != file.AfterHash)
                {
                    changed.Add(file.RelativePath);
                    issues.Add($"Changed or missing: {file.RelativePath}");
                }

            foreach (var operation in active)
            foreach (var file in operation.Files.Where(f => f.BeforeHash is not null))
                if (await SafeFiles.ComputeFileHash_Async(GetBackupPath(operation.Id, file.RelativePath),
                                                          cancellationToken) != file.BeforeHash)
                    issues.Add($"Backup changed or missing: {file.RelativePath} ({operation.Id})");

            return new VerificationResult(active.FirstOrDefault(), issues)
            {
                Operations = active, ChangedFiles = changed
            };
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RestoreLatestOperation_Async(string targetDirectory,
                                                   CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var target = PathUtil.Normalize(targetDirectory);
            var journal = (await LoadHistory_Async(cancellationToken))
                          .FirstOrDefault(j => PathUtil.AreSame(j.TargetDirectory, target) &&
                                               j.State != OperationState.Restored)
                          ?? throw new InvalidOperationException(
                                                                 "There is no managed operation to restore in this folder.");

            await RestoreFiles_Async(journal, cancellationToken);
            journal.State = OperationState.Restored;
            await CreateJournalStore(journal.Id).SaveJsonFile_Async(journal, CancellationToken.None);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<OperationJournal>> GetOperationHistory_Async(
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            return await LoadHistory_Async(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task<IReadOnlyList<ManagedTarget>> GetManagedTargets_Async(CancellationToken cancellationToken = default)
    {
        // Off the UI thread: the library calls this on startup and whenever it is shown.
        return Task.Run(async () =>
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                var targets = new List<ManagedTarget>();

                foreach (var operations in ActiveOperations(await LoadHistory_Async(cancellationToken)))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var newestFirst = operations.ToList();
                    var health = newestFirst.Any(j => j.State != OperationState.Installed)
                        ? ManagedHealth.Incomplete
                        : LatestExpectations(newestFirst).All(file => MatchesQuickly(operations.Key, file))
                            ? ManagedHealth.Healthy
                            : ManagedHealth.FilesChanged;

                    targets.Add(new ManagedTarget(operations.Key, newestFirst[0], health)
                    {
                        Version = newestFirst.FirstOrDefault(j => j.Version is not null)?.Version
                    });
                }

                return (IReadOnlyList<ManagedTarget>)targets;
            }
            finally
            {
                _gate.Release();
            }
        }, cancellationToken);
    }

    /// <summary>Unrestored operations per folder, newest first: what is installed there now.</summary>
    private static IEnumerable<IGrouping<string, OperationJournal>> ActiveOperations(
        IEnumerable<OperationJournal> newestFirst)
    {
        return newestFirst.Where(j => j.State != OperationState.Restored)
            .GroupBy(j => j.TargetDirectory, PathUtil.Comparer);
    }

    /// <summary>A later operation's expectation for a file replaces an earlier one's.</summary>
    private static IEnumerable<OperationFile> LatestExpectations(IEnumerable<OperationJournal> newestFirst)
    {
        return newestFirst.SelectMany(j => j.Files).DistinctBy(f => f.RelativePath, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     A size check catches game updates and file verification, which replace or delete files, without hashing
    ///     every managed game on startup. Manage Game still verifies full hashes.
    /// </summary>
    private static bool MatchesQuickly(string target, OperationFile file)
    {
        try
        {
            var info = new FileInfo(PathUtil.ResolveChild(target, file.RelativePath));

            return info.Exists && (file.AfterLength is null || info.Length == file.AfterLength);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return false;
        }
    }

    private static async Task<string?> ReadCurrentIni_Async(string target, CancellationToken cancellationToken)
    {
        var path = PathUtil.ResolveChild(target, "OptiScaler.ini");

        return File.Exists(path) ? await File.ReadAllTextAsync(path, cancellationToken) : null;
    }

    /// <summary>Puts original files back. Checks every file first so a later game update or user edit is never lost.</summary>
    private async Task RestoreFiles_Async(OperationJournal journal, CancellationToken cancellationToken)
    {
        var pending = new List<(OperationFile File, string Destination)>();

        foreach (var file in journal.Files)
        {
            var destination = PathUtil.ResolveChild(journal.TargetDirectory, file.RelativePath);
            var current = await SafeFiles.ComputeFileHash_Async(destination, cancellationToken);

            if (current != file.BeforeHash && current != file.AfterHash)
                throw new IOException(
                                      $"Restore blocked: {file.RelativePath} has changed. Preserve your changes and resolve the conflict first.");

            if (file.BeforeHash is not null &&
                await SafeFiles.ComputeFileHash_Async(GetBackupPath(journal.Id, file.RelativePath),
                                                      cancellationToken) != file.BeforeHash)
                throw new IOException($"Restore blocked: the backup of {file.RelativePath} is changed or missing.");

            if (current != file.BeforeHash) pending.Add((file, destination));
        }

        foreach (var (file, destination) in pending)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (file.BeforeHash is null)
                File.Delete(destination);
            else
                await SafeFiles.CopyFileAtomically_Async(GetBackupPath(journal.Id, file.RelativePath), destination,
                                                         cancellationToken);
        }
    }

    private async Task<string> ValidatePlan_Async(InstallPlan plan, CancellationToken cancellationToken)
    {
        var target = PathUtil.Normalize(plan.TargetDirectory);

        if (!Directory.Exists(target) || plan.Files.Count == 0)
            throw new InvalidDataException("Invalid or empty installation plan.");
        if (plan.Files.Select(file => file.RelativePath).Distinct(StringComparer.OrdinalIgnoreCase).Count() !=
            plan.Files.Count)
            throw new InvalidDataException("Duplicate plan destinations.");

        var dataRoot = PathUtil.Normalize(paths.RootDirectory);

        if (PathUtil.IsWithin(target, dataRoot) || PathUtil.IsWithin(dataRoot, target))
            throw new InvalidDataException("The installation target cannot overlap application data and backups.");

        // Rejects traversal, and links that would redirect a write outside the game folder.
        foreach (var file in plan.Files) PathUtil.ResolveChild(target, file.RelativePath);

        if ((await LoadHistory_Async(cancellationToken)).Any(j => PathUtil.AreSame(j.TargetDirectory, target) &&
                                                                  j.State == OperationState.Applying))
            throw new InvalidOperationException("Restore the incomplete operation before installing again.");

        return target;
    }

    /// <summary>Returns journals newest first.</summary>
    private async Task<List<OperationJournal>> LoadHistory_Async(CancellationToken cancellationToken)
    {
        var journals = new List<OperationJournal>();

        if (!Directory.Exists(TransactionsDirectory)) return journals;

        foreach (var directory in Directory.EnumerateDirectories(TransactionsDirectory))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!Guid.TryParseExact(Path.GetFileName(directory), "N", out var id)) continue;

            var journal = await CreateJournalStore(id).LoadJsonFile_Async(cancellationToken);

            if (journal is null) continue;

            // Validated here rather than in the store, so a corrupt journal is reported instead of silently
            // replaced by its backup, which may describe an earlier state.
            ValidateJournal(journal, id);
            journals.Add(journal);
        }

        return journals.OrderByDescending(j => j.CreatedAtUtc).ToList();
    }

    private static void ValidateJournal(OperationJournal journal, Guid id)
    {
        // Persisted JSON can contain null despite the model's non-nullable declarations.
        if (journal.SchemaVersion != 1 || journal.Id != id || journal.Files is null || journal.Files.Count == 0 ||
            journal.Files.Any(file => file is null || !IsSha256(file.AfterHash) ||
                                      (file.BeforeHash is not null && !IsSha256(file.BeforeHash))) ||
            !Enum.IsDefined(journal.State) || !Enum.IsDefined(journal.Kind) ||
            string.IsNullOrWhiteSpace(journal.TargetDirectory) || !Path.IsPathFullyQualified(journal.TargetDirectory))
            throw new InvalidDataException($"Invalid operation journal: {id}");
    }

    private static bool IsSha256(string? hash) { return hash is { Length: 64 } && hash.All(Uri.IsHexDigit); }

    private string GetJournalDirectory(Guid id) { return Path.Combine(TransactionsDirectory, id.ToString("N")); }

    private string GetBackupPath(Guid id, string relativePath)
    {
        return PathUtil.ResolveChild(Path.Combine(GetJournalDirectory(id), "original"), relativePath);
    }

    private AtomicJsonFile<OperationJournal> CreateJournalStore(Guid id)
    {
        return new AtomicJsonFile<OperationJournal>(Path.Combine(GetJournalDirectory(id), "journal.json"),
                                                    OptiscalerJsonContext.Default.OperationJournal);
    }

    private static string GetExecutableDirectory(string executablePath)
    {
        var executable = PathUtil.Normalize(executablePath);
        SafeFiles.RequireX64PeFile(executable, false);

        return Path.GetDirectoryName(executable)!;
    }

    private static PlannedFile PlanFile(string source, string target, string relative, string? text)
    {
        return new PlannedFile(source, relative, File.Exists(PathUtil.ResolveChild(target, relative)), text);
    }
}
