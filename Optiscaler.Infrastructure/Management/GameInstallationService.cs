using System.Security.Cryptography;
using System.Text;
using Optiscaler.Core.Abstractions;
using Optiscaler.Core.Management;
using Optiscaler.Infrastructure.Persistence;

namespace Optiscaler.Infrastructure.Management;

/// <summary>
/// Installs local packages using hashed previews and durable journals. No package code is executed.
/// Unfinished operations remain discoverable and can be restored after restarting the app.
/// </summary>
///
public sealed class GameInstallationService(IAppPaths paths) : IGameInstallationService
{
    public static readonly string[] ProxyNames =
        ["dxgi.dll", "winmm.dll", "d3d12.dll", "dbghelp.dll", "version.dll", "wininet.dll", "winhttp.dll"];

    public static readonly string[] NativeNames =
        ["nvngx_dlss.dll", "nvngx_dlssg.dll", "nvngx_dlssd.dll", "libxess.dll", "amd_fidelityfx_upscaler_dx12.dll"];

    private readonly SemaphoreSlim _gate = new(1, 1);
    private string Transactions => SafeFiles.Absolute(Path.Combine(paths.RootDirectory, "transactions"));

    public Task<InstallPlan> PreviewInstallAsync(string executablePath, string packageDirectory, string proxyName,
                                                 RenderProfile? profile, CancellationToken cancellationToken = default)
    {
        return Task.Run(async () =>
        {
            var target = ExecutableDirectory(executablePath);
            var package = SafeFiles.Absolute(packageDirectory);

            if (package == target || SafeFiles.IsWithin(package, target) || SafeFiles.IsWithin(target, package))
                throw new InvalidDataException("The package folder must be separate from the game folder.");
            if (!ProxyNames.Contains(proxyName)) throw new InvalidDataException("Unsupported proxy filename.");

            SafeFiles.RequireX64Pe(Path.Combine(package, "OptiScaler.dll"), true);

            if (!File.Exists(Path.Combine(package, "OptiScaler.ini")))
                throw new
                    InvalidDataException("Choose the extracted package folder containing OptiScaler.dll and OptiScaler.ini.");

            var files = new List<PlannedFile>();
            long bytes = 0;

            foreach (var source in EnumeratePackage(package, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                bytes += new FileInfo(source).Length;

                if (files.Count >= 2000 || bytes > 2L * 1024 * 1024 * 1024)
                    throw new InvalidDataException("Package exceeds the 2 GB / 2,000 file limit.");

                var relative = Path.GetRelativePath(package, source);
                if (relative.Equals("OptiScaler.dll", StringComparison.OrdinalIgnoreCase)) relative = proxyName;
                var text = relative.Equals("OptiScaler.ini", StringComparison.OrdinalIgnoreCase) && profile is not null
                    ? ProfileIni.Apply(await File.ReadAllTextAsync(source, cancellationToken), profile)
                    : null;
                files.Add(await PlanFileAsync(source, target, relative, text, cancellationToken));
            }

            if (files.Select(f => f.RelativePath).Distinct(StringComparer.OrdinalIgnoreCase).Count() != files.Count)
                throw new InvalidDataException("The package contains conflicting destination filenames.");

            return new InstallPlan(Guid.NewGuid(), target, OperationKind.InstallOptiscaler,
                                   $"Local package : {proxyName}" + (profile is null ? "" : $" : {profile.Name}"),
                                   files.AsReadOnly(), DateTimeOffset.UtcNow);
        }, cancellationToken);
    }

    public Task<InstallPlan> PreviewNativeSwapAsync(string destinationDll, string sourceDll,
                                                    CancellationToken cancellationToken = default)
    {
        return Task.Run(async () =>
        {
            var destination = SafeFiles.Absolute(destinationDll);
            var source = SafeFiles.Absolute(sourceDll);
            var name = Path.GetFileName(destination);

            if (!NativeNames.Contains(name, StringComparer.OrdinalIgnoreCase) ||
                !Path.GetFileName(source).Equals(name, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Choose matching, supported native DLL filenames.");
            if (source.Equals(destination, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Source and destination must be different files.");

            SafeFiles.RequireX64Pe(destination, true);
            SafeFiles.RequireX64Pe(source, true);
            var target = Path.GetDirectoryName(destination)!;
            var file = await PlanFileAsync(source, target, name, null, cancellationToken);

            return new InstallPlan(Guid.NewGuid(), target, OperationKind.ReplaceNativeDll, $"Replace {name}",
                                   Array.AsReadOnly(new[] { file }), DateTimeOffset.UtcNow);
        }, cancellationToken);
    }

    public Task<InstallPlan> PreviewProfileAsync(string executablePath, RenderProfile profile,
                                                 CancellationToken cancellationToken = default)
    {
        return Task.Run(async () =>
        {
            var target = ExecutableDirectory(executablePath);
            var source = SafeFiles.Child(target, "OptiScaler.ini");
            var text = ProfileIni.Apply(await File.ReadAllTextAsync(source, cancellationToken), profile);
            var file = await PlanFileAsync(source, target, "OptiScaler.ini", text, cancellationToken);

            return new InstallPlan(Guid.NewGuid(), target, OperationKind.ApplyProfile, $"Profile : {profile.Name}",
                                   Array.AsReadOnly(new[] { file }), DateTimeOffset.UtcNow);
        }, cancellationToken);
    }

    public async Task ExecuteAsync(InstallPlan plan, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            using var processLock = AcquireLock();
            var target = SafeFiles.Absolute(plan.TargetDirectory);

            if (!Directory.Exists(target) || plan.Files.Count == 0 || plan.Id == Guid.Empty)
                throw new InvalidDataException("Invalid or empty installation plan.");
            if (plan.Files.Select(f => f.RelativePath).Distinct(StringComparer.OrdinalIgnoreCase).Count() !=
                plan.Files.Count)
                throw new InvalidDataException("Duplicate plan destinations.");

            var dataRoot = SafeFiles.Absolute(paths.RootDirectory);

            if (SameTarget(target, dataRoot) || SafeFiles.IsWithin(target, dataRoot) ||
                SafeFiles.IsWithin(dataRoot, target))
                throw new InvalidDataException("The installation target cannot overlap application data and backups.");

            var active = (await HistoryCoreAsync(cancellationToken)).Where(j => j.State != OperationState.Restored)
                .ToList();

            if (active.Any(j => !SameTarget(j.TargetDirectory, target) && j.Files.Any(old => plan.Files.Any(next =>
                               SameTarget(SafeFiles.Child(j.TargetDirectory, old.RelativePath),
                                          SafeFiles.Child(target, next.RelativePath))))))
                throw new
                    InvalidOperationException("Files overlap an operation in another folder. Restore that operation first.");
            if (active.Any(j => SameTarget(j.TargetDirectory, target) &&
                                j.State is OperationState.Prepared or OperationState.Applying
                                    or OperationState.Restoring))
                throw new InvalidOperationException("Restore the incomplete operation before installing again.");

            var directory = JournalDirectory(plan.Id);

            if (Directory.Exists(directory)) throw new InvalidOperationException("This preview has already been used.");

            var journal = new OperationJournal
            {
                Id = plan.Id, TargetDirectory = target, Description = plan.Description, Kind = plan.Kind,
                CreatedAtUtc = DateTimeOffset.UtcNow, State = OperationState.Prepared,
                Files = plan.Files.Select(f => new OperationFile
                {
                    RelativePath = f.RelativePath, BeforeHash = f.BeforeHash, AfterHash = f.AfterHash
                }).ToList()
            };

            // Stage every new file and backup every original before publishing a recoverable journal.
            foreach (var file in plan.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var destination = SafeFiles.Child(target, file.RelativePath);
                await RequireHashAsync(file.SourcePath, file.SourceHash ?? file.AfterHash, cancellationToken);
                await RequireHashAsync(destination, file.BeforeHash, cancellationToken);
                var staged = SafeFiles.Child(Path.Combine(directory, "staged"), file.RelativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(staged)!);
                if (file.GeneratedText is null) await SafeFiles.CopyAsync(file.SourcePath, staged, cancellationToken);
                else
                    await File.WriteAllTextAsync(staged, file.GeneratedText, new UTF8Encoding(false),
                                                 cancellationToken);
                await RequireHashAsync(staged, file.AfterHash, cancellationToken);

                if (file.BeforeHash is not null)
                {
                    var backup = SafeFiles.Child(Path.Combine(directory, "original"), file.RelativePath);
                    await SafeFiles.CopyAsync(destination, backup, cancellationToken);
                    await RequireHashAsync(backup, file.BeforeHash, cancellationToken);
                }
            }

            await Store(plan.Id).SaveAsync(journal, cancellationToken);
            journal.State = OperationState.Applying;
            await Store(plan.Id).SaveAsync(journal, cancellationToken);

            foreach (var file in journal.Files)
            {
                var destination = SafeFiles.Child(target, file.RelativePath);
                await RequireHashAsync(destination, file.BeforeHash, cancellationToken);
                await SafeFiles.ReplaceAsync(SafeFiles.Child(Path.Combine(directory, "staged"), file.RelativePath),
                                             destination, cancellationToken);
                await RequireHashAsync(destination, file.AfterHash, cancellationToken);
            }

            journal.State = OperationState.Installed;
            await Store(plan.Id).SaveAsync(journal, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<VerificationResult> VerifyAsync(string targetDirectory,
                                                      CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            using var processLock = AcquireLock();
            var target = SafeFiles.Absolute(targetDirectory);
            var active = (await HistoryCoreAsync(cancellationToken))
                .Where(j => SameTarget(j.TargetDirectory, target) && j.State != OperationState.Restored).ToList();
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
                    if (checkedFiles.Add(file.RelativePath) &&
                        await SafeFiles.HashAsync(SafeFiles.Child(target, file.RelativePath), cancellationToken) !=
                        file.AfterHash)
                        issues.Add($"Changed or missing: {file.RelativePath}");
                    if (file.BeforeHash is not null && await SafeFiles.HashAsync(
                         SafeFiles.Child(Path.Combine(JournalDirectory(operation.Id), "original"),
                                         file.RelativePath), cancellationToken) != file.BeforeHash)
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

    public async Task RestoreAsync(string targetDirectory, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            using var processLock = AcquireLock();
            var journal = await LatestAsync(SafeFiles.Absolute(targetDirectory), cancellationToken)
                          ?? throw new
                              InvalidOperationException("There is no managed operation to restore in this folder.");
            var directory = JournalDirectory(journal.Id);

            // Validate the whole set first. Never overwrite a game update or a user's later edit.
            foreach (var file in journal.Files)
            {
                var current = await SafeFiles.HashAsync(SafeFiles.Child(journal.TargetDirectory, file.RelativePath),
                                                        cancellationToken);

                if (current != file.BeforeHash && current != file.AfterHash)
                    throw new
                        IOException($"Restore blocked: {file.RelativePath} has changed. Preserve your changes and resolve the conflict first.");

                if (file.BeforeHash is not null)
                    await RequireHashAsync(SafeFiles.Child(Path.Combine(directory, "original"), file.RelativePath),
                                           file.BeforeHash, cancellationToken);
            }

            journal.State = OperationState.Restoring;
            await Store(journal.Id).SaveAsync(journal, cancellationToken);

            foreach (var file in journal.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var destination = SafeFiles.Child(journal.TargetDirectory, file.RelativePath);
                var current = await SafeFiles.HashAsync(destination, cancellationToken);

                if (current == file.BeforeHash) continue;

                await RequireHashAsync(destination, file.AfterHash, cancellationToken);
                if (file.BeforeHash is null) File.Delete(destination);
                else
                    await SafeFiles.ReplaceAsync(SafeFiles.Child(Path.Combine(directory, "original"),
                                                                 file.RelativePath), destination, cancellationToken);
                await RequireHashAsync(destination, file.BeforeHash, cancellationToken);
            }

            journal.State = OperationState.Restored;
            await Store(journal.Id).SaveAsync(journal, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<OperationJournal>> GetHistoryAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            using var processLock = AcquireLock();

            return await HistoryCoreAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<List<OperationJournal>> HistoryCoreAsync(CancellationToken ct)
    {
        var journals = new List<OperationJournal>();

        if (!Directory.Exists(Transactions)) return journals;

        foreach (var directory in Directory.EnumerateDirectories(Transactions))
        {
            ct.ThrowIfCancellationRequested();

            if (!Guid.TryParseExact(Path.GetFileName(directory), "N", out var id)) continue;

            var journal = await Store(id).LoadAsync(ct);

            if (journal is null) continue; // Abandoned staging never touched game files.

            if (journal.SchemaVersion != 1 || journal.Id != id ||
                journal.Files.Count == 0 || journal.Files.Any(_ => false) || !Enum.IsDefined(journal.State) ||
                !Enum.IsDefined(journal.Kind) ||
                journal.Files.Select(f => f.RelativePath).Distinct(StringComparer.OrdinalIgnoreCase).Count() !=
                journal.Files.Count)
                throw new InvalidDataException($"Invalid operation journal: {id}");

            SafeFiles.Absolute(journal.TargetDirectory);

            foreach (var file in journal.Files)
            {
                SafeFiles.Child(journal.TargetDirectory, file.RelativePath);

                if (!ValidHash(file.AfterHash) || (file.BeforeHash is not null && !ValidHash(file.BeforeHash)))
                    throw new InvalidDataException("Invalid operation file hash.");
            }

            journals.Add(journal);
        }

        return journals.OrderByDescending(j => j.CreatedAtUtc).ToList();
    }

    private async Task<OperationJournal?> LatestAsync(string target, CancellationToken ct)
    {
        return (await HistoryCoreAsync(ct)).FirstOrDefault(j => SameTarget(j.TargetDirectory, target) &&
                                                                j.State != OperationState.Restored);
    }

    private static bool SameTarget(string left, string right)
    {
        return string.Equals(left, right,
                             OperatingSystem.IsWindows()
                                 ? StringComparison.OrdinalIgnoreCase
                                 : StringComparison.Ordinal);
    }

    private string JournalDirectory(Guid id)
    {
        return SafeFiles.Child(Transactions, id.ToString("N"));
    }

    private AtomicJsonFile<OperationJournal> Store(Guid id)
    {
        return new AtomicJsonFile<OperationJournal>(SafeFiles.Child(JournalDirectory(id), "journal.json"),
                                                    OptiscalerJsonContext.Default.OperationJournal);
    }

    private FileStream AcquireLock()
    {
        Directory.CreateDirectory(Transactions);

        return new FileStream(SafeFiles.Child(Transactions, "operations.lock"), FileMode.OpenOrCreate,
                              FileAccess.ReadWrite, FileShare.None);
    }

    private static bool ValidHash(string? hash)
    {
        return hash is not null && hash.Length == 64 && hash.All(Uri.IsHexDigit);
    }

    private static string ExecutableDirectory(string path)
    {
        SafeFiles.RequireX64Pe(path, false);

        return Path.GetDirectoryName(SafeFiles.Absolute(path))!;
    }

    private static async Task RequireHashAsync(string path, string? expected, CancellationToken ct)
    {
        if (await SafeFiles.HashAsync(path, ct) != expected)
            throw new IOException($"File changed since preview or backup verification: {path}. Create a new preview.");
    }

    private static async Task<PlannedFile> PlanFileAsync(string source, string target, string relative, string? text,
                                                         CancellationToken ct)
    {
        var before = await SafeFiles.HashAsync(SafeFiles.Child(target, relative), ct);
        var sourceHash = await SafeFiles.HashAsync(source, ct);
        var after = text is null ? sourceHash : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

        return new PlannedFile(source, relative, before,
                               after ?? throw new FileNotFoundException("Package file is missing.", source), text,
                               sourceHash);
    }

    private static IEnumerable<string> EnumeratePackage(string root, CancellationToken cancellationToken)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        var directoryCount = 0;

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (++directoryCount > 4000) throw new InvalidDataException("Package exceeds the 4,000 directory limit.");

            var directory = pending.Pop();

            foreach (var file in Directory.EnumerateFiles(directory)) yield return SafeFiles.Absolute(file);

            foreach (var child in Directory.EnumerateDirectories(directory)) pending.Push(SafeFiles.Absolute(child));
        }
    }
}