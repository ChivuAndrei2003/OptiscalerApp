using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OptiscalerApp.Management;
using OptiscalerApp.Models;
using OptiscalerApp.Paths;
using OptiscalerApp.Persistence;

namespace OptiscalerApp.ViewModels;

/// <summary>Analyzes one game and previews, applies, verifies, and restores OptiScaler installations for it.</summary>
public sealed partial class ManageGameViewModel : ViewModelBase, IOperationHost
{
    public delegate Task<GameRecord> SaveGameDetails(GameId id, string name, string rootPath, string? executable);

    private readonly IGameAnalyzer _analyzer;
    private readonly CompatibilityListService _compatibility;
    private readonly Func<Task<IReadOnlyList<GpuInfo>>> _detectGpus;
    private readonly IGameInstallationService _installer;
    private readonly IProfileRepository _profiles;
    private readonly SaveGameDetails _saveGameDetails;

    [ObservableProperty] private bool _canRestore;

    [ObservableProperty] private bool _canUninstall;

    [ObservableProperty] private string _compatibilityText = "Not verified";

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasNoComponents))]
    private IReadOnlyList<DetectedComponent> _components = [];

    [ObservableProperty] private string? _coverImage;

    [ObservableProperty] private string _detailsText = "";

    [ObservableProperty] private string _editName;

    [ObservableProperty] private string _emptyComponentsText = "No rendering components detected.";

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanLaunch))]
    private string _executablePath = "";

    [ObservableProperty] private string _folderPath = "";
    private GameRecord _game;

    [ObservableProperty] private string _gameName;

    [ObservableProperty] private string _gpuText = "Detecting…";
    private IReadOnlyList<GpuInfo> _gpus = [];

    [ObservableProperty] private string _guidanceText = "Select your game executable, then verify the installation.";

    [ObservableProperty] private bool _hasCurrentIni;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CompatibilityPageUrl), nameof(HasCompatibilityPage),
                                 nameof(OptiPatcherText))]
    private bool _hasWikiList;

    [ObservableProperty] private string _inputsText = "None detected";

    [ObservableProperty] private string _installButtonText = "Preview install";

    [ObservableProperty] private string _installStateText = "Checking installation";

    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(BackCommand))]
    private bool _isBusy;

    [ObservableProperty] private bool _isDetailsExpanded;

    [ObservableProperty] private bool _isEditingDetails;

    [ObservableProperty] private bool _keepCurrentSettings = true;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsPreviewing))]
    private InstallPlan? _plan;

    [ObservableProperty] private string _previewText = "";

    [ObservableProperty] private IReadOnlyList<RenderProfile> _profileChoices = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RecommendationText), nameof(WarningsText), nameof(HasWarnings))]
    private InstallRecommendation? _recommendation;

    [ObservableProperty] private int _selectedInstallationIndex = -1;

    [ObservableProperty] private RenderProfile? _selectedProfile;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(LinuxLaunchOptions))]
    private string _selectedProxy = OptiscalerFiles.ProxyNames[0];

    [ObservableProperty] private string _status = "Inspecting your installation…";

    /// <summary>The game's row in the wiki list; null when it is not listed or the list is unavailable.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CompatibilityNotes), nameof(CompatibilityPageUrl), nameof(HasCompatibilityPage),
                                 nameof(CompatibilityPageLabel), nameof(OptiPatcherText))]
    private CompatibilityEntry? _wikiEntry;

    public ManageGameViewModel(GameRecord game, IGameAnalyzer analyzer, IGameInstallationService installer,
                               PackageDownloadService packages, IProfileRepository profiles,
                               SaveGameDetails saveGameDetails, CompatibilityListService compatibility,
                               Func<Task<IReadOnlyList<GpuInfo>>> detectGpus)
    {
        _game = game;
        _analyzer = analyzer;
        _installer = installer;
        _profiles = profiles;
        _saveGameDetails = saveGameDetails;
        _compatibility = compatibility;
        _detectGpus = detectGpus;
        _gameName = _editName = game.Name;
        _coverImage = game.CoverImage;
        Installations = game.Installations.Select((_, index) => game.Installations.Count == 1
                                                      ? "Primary installation"
                                                      : $"Installation {index + 1}").ToList();
        Package = new PackageSelectionViewModel(packages, this);
        SelectedInstallationIndex = game.Installations.Count > 0 ? 0 : -1;
    }

    /// <summary>File pickers; set by whoever opens the page.</summary>
    public IFileDialogs? Dialogs { get; init; }

    /// <summary>Clipboard, launching and folders; set by whoever opens the page.</summary>
    public IShellActions? Shell { get; init; }

    public PackageSelectionViewModel Package { get; }

    public string PlatformText => _game.Platform.DisplayName();

    public IReadOnlyList<string> Installations { get; }

    public bool HasMultipleInstallations => Installations.Count > 1;

    public bool IsPreviewing => Plan is not null;

    public bool HasNoComponents => Components.Count == 0;

    public string CompatibilityNotes => WikiEntry?.Notes ?? "";

    public string? CompatibilityPageUrl =>
        WikiEntry?.PageUrl ?? (HasWikiList ? CompatibilityListService.WikiUrl : null);

    public bool HasCompatibilityPage => CompatibilityPageUrl is not null;

    /// <summary>Only a game's own wiki page is an "entry"; otherwise the link opens the whole list.</summary>
    public string CompatibilityPageLabel =>
        WikiEntry?.PageUrl is not null ? "Open wiki entry ↗" : "Open compatibility list ↗";

    public string OptiPatcherText => WikiEntry switch
    {
        null => HasWikiList ? "Not listed" : "Not checked",
        { OptiPatcherSupported: true } => "Supported",
        _ => "Not supported"
    };

    public string RecommendationText => Recommendation is null
        ? "Verify the installation to get a recommendation."
        : string.Join("\n", Recommendation.Reasons.Select(reason => "• " + reason));

    public string WarningsText => string.Join("\n", Recommendation?.Warnings.Select(warning => "⚠ " + warning) ?? []);

    public bool HasWarnings => Recommendation?.Warnings.Count > 0;

    public bool CanLaunch => GameLauncher.Resolve(_game, ExecutablePath) is not null;

    /// <summary>Proton needs this in the game's launch options, or it ignores the OptiScaler DLL.</summary>
    public string LinuxLaunchOptions => GameLauncher.LinuxLaunchOptions(SelectedProxy);

    public bool ShowLinuxLaunchOptions => OperatingSystem.IsLinux();

    public IReadOnlyList<string> ProxyNames => OptiscalerFiles.ProxyNames;

    private bool CanGoBack => !IsBusy;

    private GameInstallation? SelectedInstallation => SelectedInstallationIndex >= 0
        ? _game.Installations[SelectedInstallationIndex]
        : null;

    private string Executable => !string.IsNullOrWhiteSpace(ExecutablePath)
        ? ExecutablePath.Trim()
        : throw new InvalidOperationException("Select the game executable first.");

    private string TargetDirectory => Path.GetDirectoryName(Path.GetFullPath(Executable)) ??
                                      throw new InvalidOperationException("Invalid executable path.");

    private IShellActions RequiredShell => Shell ?? throw new InvalidOperationException("The shell is unavailable.");

    public IProgress<string> Progress => new Progress<string>(message => Status = message);

    public IFileDialogs RequiredDialogs =>
        Dialogs ?? throw new InvalidOperationException("File dialogs are unavailable.");

    public async Task RunOperationAsync(Func<Task> operation)
    {
        if (IsBusy) return;

        IsBusy = true;
        Status = "Working…";

        try
        {
            await operation();
        }
        catch (Exception ex)
        {
            Status = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Raised when the user leaves the page.</summary>
    public event EventHandler? Closed;

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void Back()
    {
        Closed?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private Task Load()
    {
        return RunOperationAsync(async () =>
        {
            // Read the full saved catalog independently of the Profiles page's search filter.
            var catalog = await _profiles.LoadProfileCatalogAsync();
            ProfileChoices = catalog.Profiles;
            SelectedProfile = catalog.Profiles.FirstOrDefault(p => p.Id == catalog.DefaultProfileId);

            // Independent lookups; only the analysis needs all of them.
            var gpus = _detectGpus();
            var wiki = LoadCompatibilityAsync();

            // Most scanners cannot tell which executable is the game; the install guide's rules usually can.
            var detected = string.IsNullOrWhiteSpace(ExecutablePath) && SelectedInstallation is { } installation
                ? Task.Run(() => ExecutableResolver.FindBest(installation.RootPath, _game.Name))
                : Task.FromResult<ExecutableCandidate?>(null);
            await Task.WhenAll(gpus, wiki, detected);
            _gpus = gpus.Result;
            GpuText = InstallAdvisor.PickPrimaryGpu(_gpus)?.Name ?? "Not detected";
            if (detected.Result is { } best) ExecutablePath = best.Path;

            await AnalyzeAsync();
        });
    }

    [RelayCommand]
    private Task Verify()
    {
        return RunOperationAsync(AnalyzeAsync);
    }

    [RelayCommand]
    private Task DetectExecutable()
    {
        return RunOperationAsync(async () =>
        {
            if (SelectedInstallation is not { } installation) return;

            var candidates = await Task.Run(() => ExecutableResolver.FindCandidates(installation.RootPath,
                                                 _game.Name));

            if (candidates.Count == 0)
                throw new InvalidOperationException("No 64-bit game executable was found. Browse to it instead.");

            ExecutablePath = candidates[0].Path;
            await AnalyzeAsync();
            var relative = Path.GetRelativePath(installation.RootPath, candidates[0].Path);
            Status = $"Detected {relative}" + (candidates[0].Reasons.Count > 0
                ? $" ({string.Join(", ", candidates[0].Reasons)})."
                : ".");
            DetailsText = "Executable candidates, best first:\n" +
                          string.Join("\n", candidates.Take(8).Select(c =>
                                                                          $"{c.Score,4} · {Path.GetRelativePath(installation.RootPath, c.Path)}")) +
                          "\n\n" + DetailsText;
        });
    }

    [RelayCommand]
    private Task ApplyRecommended()
    {
        return RunOperationAsync(async () =>
        {
            if (Recommendation is null) await AnalyzeAsync();

            var recommendation = Recommendation ??
                                 throw new InvalidOperationException("Select the game executable first.");
            SelectedProxy = recommendation.Proxy;
            await Package.ApplyRecommendationAsync(recommendation);
            Status = "Recommended settings selected. Preview install to review the exact changes.";
        });
    }

    [RelayCommand]
    private Task PreviewInstall()
    {
        return RunOperationAsync(async () =>
        {
            // Validate the game before spending time downloading its package.
            SafeFiles.RequireX64PeFile(Executable, false);
            var package = await Package.EnsurePackageAsync();
            ShowPreview(await _installer.PreviewPackageInstallationAsync(
                                                                          Executable, package, SelectedProxy,
                                                                          SelectedProfile, Package.Selections, Progress,
                                                                          keepCurrentSettings: KeepCurrentSettings &&
                                                                          HasCurrentIni));
        });
    }

    [RelayCommand]
    private Task PreviewProfile()
    {
        return RunOperationAsync(async () =>
        {
            var profile = SelectedProfile ?? throw new InvalidOperationException("Select a saved profile first.");
            ShowPreview(await _installer.PreviewProfileApplicationAsync(Executable, profile));
        });
    }

    [RelayCommand]
    private Task PreviewNativeSwap()
    {
        return RunOperationAsync(async () =>
        {
            var target = PathUtil.Normalize(TargetDirectory);

            if (await RequiredDialogs.PickFileAsync("Select native DLL in this game", "*.dll") is not { } destination)
                return;

            if (!PathUtil.IsWithin(PathUtil.Normalize(destination), target))
                throw new InvalidOperationException("Select a DLL within the selected game's executable folder.");

            if (await RequiredDialogs.PickFileAsync("Select replacement DLL", "*.dll") is { } source)
                ShowPreview(await _installer.PreviewNativeDllSwapAsync(destination, source));
        });
    }

    [RelayCommand]
    private Task Apply()
    {
        return RunOperationAsync(async () =>
        {
            if (Plan is not { } plan) return;

            // Consume the preview even on failure; a retry must inspect the current file state again.
            Plan = null;
            await _installer.ExecuteInstallationPlanAsync(plan);
            await AnalyzeAsync();
            var verification = await _installer.VerifyInstallationAsync(plan.TargetDirectory);
            Status = verification.IsVerified
                ? "Changes applied and verified."
                : "Changes applied; verification needs attention.";
            if (verification.IsVerified && ShowLinuxLaunchOptions && plan.Kind == OperationKind.InstallOptiscaler)
                Status += $" On Linux, set the game's launch options to: {LinuxLaunchOptions}";
            if (verification.Issues.Count > 0) DetailsText += "\n" + string.Join("\n", verification.Issues);
        });
    }

    [RelayCommand]
    private void CancelPreview()
    {
        Plan = null;
        Status = "Preview cancelled.";
    }

    [RelayCommand]
    private Task Restore()
    {
        return RunOperationAsync(async () =>
        {
            await _installer.RestoreLatestOperationAsync(TargetDirectory);
            await AnalyzeAsync();
            Status = "Latest operation restored.";
        });
    }

    [RelayCommand]
    private Task Uninstall()
    {
        return RunOperationAsync(async () =>
        {
            await _installer.RestoreAllOperationsAsync(TargetDirectory);
            await AnalyzeAsync();
            Status = "OptiScaler uninstalled; the game's original files are back.";
        });
    }

    [RelayCommand]
    private Task ShowHistory()
    {
        return RunOperationAsync(async () =>
        {
            var target = PathUtil.Normalize(TargetDirectory);
            var history = (await _installer.GetOperationHistoryAsync())
                .Where(j => PathUtil.AreSame(j.TargetDirectory, target)).ToList();
            IsDetailsExpanded = true;
            Status = $"{history.Count} operations recorded for this folder.";
            DetailsText = history.Count == 0
                ? "No operations recorded for this folder."
                : string.Join("\n", history.Select(j => $"{j.CreatedAtUtc:g} : {j.Description} : {j.State}" +
                                                        (j.Version is { } version ? $" : {version}" : "")));
        });
    }

    [RelayCommand]
    private Task CopyDiagnostics()
    {
        return RunOperationAsync(async () =>
        {
            var report = await BuildDiagnosticsReportAsync();
            await RequiredShell.SetClipboardTextAsync(report);

            // Show exactly what was copied, so nothing leaves the machine unseen.
            DetailsText = report;
            IsDetailsExpanded = true;
            Status = "Diagnostic report copied. Personal folder names are replaced; review it before sharing.";
        });
    }

    [RelayCommand]
    private Task CopyLaunchOptions()
    {
        return RunOperationAsync(async () =>
        {
            await RequiredShell.SetClipboardTextAsync(LinuxLaunchOptions);
            Status = "Launch options copied. Paste them into the game's Steam properties.";
        });
    }

    [RelayCommand]
    private Task LaunchGame()
    {
        return RunOperationAsync(async () =>
        {
            var target = GameLauncher.Resolve(_game, ExecutablePath) ??
                         throw new InvalidOperationException("This game cannot be launched from here.");
            Status = await RequiredShell.OpenAsync(target)
                ? $"Launching {GameName}… Press Insert in game to open the OptiScaler overlay."
                : "Could not launch the game.";
        });
    }

    [RelayCommand]
    private Task OpenFolder()
    {
        return RunOperationAsync(async () =>
        {
            if (FolderPath.Length == 0) return;

            Status = await RequiredShell.OpenFolderAsync(FolderPath)
                ? "Game folder opened."
                : "Could not open the game folder.";
        });
    }

    [RelayCommand]
    private Task OpenCompatibilityPage()
    {
        return RunOperationAsync(async () =>
        {
            if (CompatibilityPageUrl is { } url &&
                !await RequiredShell.OpenAsync(new LaunchTarget(new Uri(url), null)))
                Status = "Could not open the wiki page.";
        });
    }

    [RelayCommand]
    private Task BrowseExecutable()
    {
        return RunOperationAsync(async () =>
        {
            if (await RequiredDialogs.PickFileAsync("Select game executable", "*.exe") is { } path)
                ExecutablePath = path;
        });
    }

    [RelayCommand]
    private void ClearProfile()
    {
        SelectedProfile = null;
    }

    [RelayCommand]
    private void ToggleEditDetails()
    {
        IsEditingDetails = !IsEditingDetails;
    }

    [RelayCommand]
    private Task SaveDetails()
    {
        return RunOperationAsync(async () =>
        {
            if (SelectedInstallation is not { } installation) return;

            _game = await _saveGameDetails(_game.Id, EditName, installation.RootPath, ExecutablePath.Trim());
            GameName = _game.Name;
            CoverImage = _game.CoverImage;
            IsEditingDetails = false;
            await LoadCompatibilityAsync();

            // A new name can match another wiki row; the compatibility text and recommendation depend on it.
            await AnalyzeAsync();
            Status = "Game details saved.";
        });
    }

    /// <summary>Everything a bug report needs, with the user's home folder and name replaced.</summary>
    public async Task<string> BuildDiagnosticsReportAsync()
    {
        string? target = null;

        try
        {
            if (!string.IsNullOrWhiteSpace(ExecutablePath)) target = PathUtil.Normalize(TargetDirectory);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            // Report without folder-specific details.
        }

        var ini = target is null ? null : Path.Combine(target, "OptiScaler.ini");

        return DiagnosticsReport.Build(new DiagnosticsInput
        {
            Game = _game,
            ExecutablePath = string.IsNullOrWhiteSpace(ExecutablePath) ? null : ExecutablePath.Trim(),
            Gpus = _gpus,
            Compatibility = WikiEntry,
            Components = Components,
            Verification = target is null ? null : await _installer.VerifyInstallationAsync(target),
            History = target is null
                ? []
                : (await _installer.GetOperationHistoryAsync())
                .Where(j => PathUtil.AreSame(j.TargetDirectory, target)).ToList(),
            CurrentIni = ini is not null && File.Exists(ini) ? await File.ReadAllTextAsync(ini) : null,
            LogTail = target is null
                ? null
                : await DiagnosticsReport.ReadLogTailAsync(Path.Combine(target, "OptiScaler.log"), 40)
        });
    }

    partial void OnSelectedInstallationIndexChanged(int value)
    {
        if (SelectedInstallation is not { } installation) return;

        ExecutablePath = installation.PrimaryExecutablePath ?? "";
        FolderPath = installation.RootPath;
        ResetAnalysis();
    }

    partial void OnExecutablePathChanged(string value)
    {
        ResetAnalysis();
    }

    partial void OnPlanChanged(InstallPlan? value)
    {
        if (value is null)
        {
            PreviewText = "";

            return;
        }

        var text = $"{value.Description}\nTarget: {value.TargetDirectory}\n\n" +
                   string.Join("\n",
                               value.Files.Select(f =>
                                                      $"{(f.ReplacesExisting ? "Replace" : "Create")} : {f.RelativePath}"));

        if (value.IniChanges.Count > 0)
            text += "\n\nOptiScaler.ini changes:\n" +
                    string.Join("\n", value.IniChanges.Take(40).Select(change => "  " + change)) +
                    (value.IniChanges.Count > 40 ? $"\n  … {value.IniChanges.Count - 40} more" : "");

        PreviewText = text;
    }

    private async Task LoadCompatibilityAsync()
    {
        var index = await _compatibility.GetIndexAsync();
        HasWikiList = index.Count > 0;
        WikiEntry = index.Find(GameName);
    }

    private void ShowPreview(InstallPlan plan)
    {
        Plan = plan;
        Status = "Review the preview, then apply or cancel.";
    }

    private async Task AnalyzeAsync()
    {
        if (SelectedInstallation is not { } original) return;

        var analysis = await _analyzer.AnalyzeGameAsync(_game.Id, new GameInstallation
        {
            RootPath = original.RootPath,
            PrimaryExecutablePath = string.IsNullOrWhiteSpace(ExecutablePath)
                ? original.PrimaryExecutablePath
                : ExecutablePath.Trim()
        });
        Components = analysis.Components.DistinctBy(c => (c.Kind, c.Version)).OrderBy(c => c.Kind).ToList();
        EmptyComponentsText = "No rendering components detected.";
        var inputs = analysis.Components
            .Where(c => c.Kind is ComponentKind.Dlss or ComponentKind.Fsr or ComponentKind.Xess)
            .Select(c => DetectedComponent.GetKindName(c.Kind)).Distinct().ToList();
        InputsText = inputs.Count == 0 ? "None detected" : string.Join(" · ", inputs);

        if (WikiEntry is { Inputs.Length: > 0 } listed) InputsText += $"\nWiki: {listed.Inputs}";

        var antiCheat = analysis.HasAntiCheat;
        CompatibilityText = antiCheat
            ? "Anti-cheat detected"
            : WikiEntry?.Status switch
            {
                CompatibilityStatus.Working => "Working (OptiScaler wiki)",
                CompatibilityStatus.WorkingOnSingleOs => "Working on one OS only (wiki)",
                CompatibilityStatus.NotWorking => "Not working (OptiScaler wiki)",
                CompatibilityStatus.Unconfirmed => "Unconfirmed on the wiki",
                _ => "Not in the wiki's tested list"
            };
        GuidanceText = antiCheat
            ? "Anti-cheat files were found. Rendering modifications should not be installed for this game."
            : string.IsNullOrWhiteSpace(ExecutablePath)
                ? "Select the actual game executable to check its installation and configure OptiScaler."
                : "Select Stable or Beta to fetch releases, or browse to a local package. Then review the planned changes.";
        InstallStateText = analysis.HasOptiscalerFiles
            ? "Untracked rendering files detected"
            : "OptiScaler not detected";
        InstallButtonText = "Preview install";
        CanUninstall = CanRestore = false;
        HasCurrentIni = false;
        Status = $"Analysis complete · {analysis.Components.Count} detected files.";
        DetailsText = string.Join("\n",
                                  analysis.Components.Select(c => $"{c.Kind}: {c.Path}")
                                      .Concat(analysis.Evidence.Select(e => e.Path is null
                                                                           ? e.Message
                                                                           : $"{e.Message} : {e.Path}")));

        if (string.IsNullOrWhiteSpace(ExecutablePath))
        {
            Recommend(analysis, antiCheat, []);

            return;
        }

        var target = PathUtil.Normalize(TargetDirectory);
        HasCurrentIni = File.Exists(Path.Combine(target, "OptiScaler.ini"));
        var result = await _installer.VerifyInstallationAsync(target);
        CanRestore = result.Journal is not null;
        CanUninstall = result.Operations.Any(j => j.Kind == OperationKind.InstallOptiscaler);

        if (result.Journal is not null)
        {
            InstallStateText = result.IsVerified ? "Managed files verified" : "Installation needs attention";
            InstallButtonText = "Preview update";
            if (!antiCheat)
                GuidanceText = result.IsVerified
                    ? "Your managed files match their saved hashes. Apply a profile or select a package to update this installation."
                    : "Review the analysis below. Restore an incomplete operation before making further changes.";
        }

        DetailsText += "\n" + (result.IsVerified ? "Installation verified." : string.Join("\n", result.Issues));

        // Proxies this app wrote, unchanged since, are ours to replace. Any other proxy DLL belongs to another mod,
        // including one written over a file this app installed earlier, e.g. ReShade copied over dxgi.dll.
        var managedFiles = result.Operations.SelectMany(j => j.Files).Select(f => f.RelativePath)
            .Except(result.ChangedFiles, StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var occupied = OptiscalerFiles.ProxyNames
            .Where(name => File.Exists(Path.Combine(target, name)) && !managedFiles.Contains(name)).ToList();
        Recommend(analysis, antiCheat, occupied);
    }

    private void Recommend(GameAnalysis analysis, bool antiCheat, IReadOnlyCollection<string> occupiedProxies)
    {
        Recommendation = InstallAdvisor.Recommend(new AdvisorInput
        {
            Gpu = InstallAdvisor.PickPrimaryGpu(_gpus),
            Compatibility = WikiEntry,
            Platform = _game.Platform,
            HasUpscalerInputs = analysis.Components.Any(c => c.Kind is ComponentKind.Dlss or ComponentKind.Fsr
                                                            or ComponentKind.Xess),
            HasDlssFrameGeneration = analysis.Components.Any(c => c.Kind == ComponentKind.DlssFrameGeneration),
            HasAntiCheat = antiCheat,
            OccupiedProxies = occupiedProxies
        });
    }

    private void ResetAnalysis()
    {
        Plan = null;
        InstallStateText = "Ready to verify";
        Components = [];
        EmptyComponentsText = "Verify to inspect rendering components.";
        CompatibilityText = "Not verified";
        InputsText = "Not checked";
        GuidanceText = "Verify the selected installation to refresh its status and detected components.";
        DetailsText = "";
        CanUninstall = CanRestore = false;
        HasCurrentIni = false;
        Recommendation = null;
        InstallButtonText = "Preview install";
        Status = "Installation selection changed. Verify to refresh its status.";
    }
}