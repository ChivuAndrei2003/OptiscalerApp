using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OptiscalerApp.Development;
using OptiscalerApp.Management;
using OptiscalerApp.Models;
using OptiscalerApp.Paths;
using OptiscalerApp.Persistence;

namespace OptiscalerApp.ViewModels;

/// <summary>Analyzes one game and previews, applies, verifies, and restores OptiScaler installations for it.</summary>
public sealed partial class ManageGameViewModel : ViewModelBase
{
    public delegate Task<GameRecord> SaveGameDetails(GameId id, string name, string rootPath, string? executable);

    private static readonly VersionChoice FetchChoice = new("Fetch releases…", VersionAction.FetchReleases);
    private static readonly VersionChoice BrowseChoice = new("Choose local package…", VersionAction.BrowseLocal);

    private readonly IGameAnalyzer _analyzer;
    private readonly CompatibilityListService _compatibility;
    private readonly Dictionary<DownloadComponent, IReadOnlyList<PackageRelease>> _componentReleases = new();
    private readonly Func<Task<IReadOnlyList<GpuInfo>>> _detectGpus;
    private readonly IGameInstallationService _installer;
    private readonly Dictionary<ReleaseChannel, string> _packageByChannel = new();
    private readonly PackageDownloadService _packages;
    private readonly IProfileRepository _profiles;
    private readonly SaveGameDetails _saveGameDetails;
    private GameRecord _game;
    private IReadOnlyList<GpuInfo> _gpus = [];
    private IReadOnlyList<PackageRelease> _releases = [];

    [ObservableProperty] private bool _canRestore;

    [ObservableProperty] private bool _canUninstall;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsStableChannel), nameof(IsBetaChannel))]
    private ReleaseChannel _channel = ReleaseChannel.Stable;

    [ObservableProperty] private string _compatibilityText = "Not verified";

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasNoComponents))]
    private IReadOnlyList<DetectedComponent> _components = [];

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasCover))]
    private string? _coverImage;

    [ObservableProperty] private string _detailsText = "";

    [ObservableProperty] private string _editName;

    [ObservableProperty] private string _emptyComponentsText = "No rendering components detected.";

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanLaunch))]
    private string _executablePath = "";

    [ObservableProperty] private string _extrasText = "Choose a local package to see its bundled components.";

    [ObservableProperty] private string _folderPath = "";

    [ObservableProperty] private string _gameName;

    [ObservableProperty] private string _gpuText = "Detecting…";

    [ObservableProperty] private string _guidanceText = "Select your game executable, then verify the installation.";

    [ObservableProperty] private bool _hasCurrentIni;

    [ObservableProperty] private string _inputsText = "None detected";

    [ObservableProperty] private string _installButtonText = "Preview install";

    [ObservableProperty] private string _installStateText = "Checking installation";

    [ObservableProperty] private bool _isBusy;

    [ObservableProperty] private bool _isDetailsExpanded;

    [ObservableProperty] private bool _isEditingDetails;

    [ObservableProperty] private bool _keepCurrentSettings = true;

    [ObservableProperty] private string _packageInfo = "";

    [ObservableProperty] private string _packagePath = "";

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsPreviewing))]
    private InstallPlan? _plan;

    [ObservableProperty] private string _previewText = "";

    [ObservableProperty] private IReadOnlyList<RenderProfile> _profileChoices = [];

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(RecommendationText), nameof(WarningsText), nameof(HasWarnings))]
    private InstallRecommendation? _recommendation;

    [ObservableProperty] private int _selectedInstallationIndex = -1;

    [ObservableProperty] private RenderProfile? _selectedProfile;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(LinuxLaunchOptions))]
    private string _selectedProxy = GameInstallationService.ProxyNames[0];

    [ObservableProperty] private VersionChoice? _selectedVersion;

    [ObservableProperty] private string _status = "Inspecting your installation…";

    [ObservableProperty] private IReadOnlyList<VersionChoice> _versionChoices = [FetchChoice, BrowseChoice];

    [ObservableProperty] private string _versionPlaceholder = "Fetch releases…";

    /// <summary>The game's row in the wiki list; null when it is not listed or the list is unavailable.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CompatibilityNotes), nameof(CompatibilityPageUrl), nameof(HasCompatibilityPage),
                              nameof(OptiPatcherText))]
    private CompatibilityEntry? _wikiEntry;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CompatibilityPageUrl), nameof(HasCompatibilityPage),
                                                  nameof(OptiPatcherText))]
    private bool _hasWikiList;

    public ManageGameViewModel(GameRecord game, IGameAnalyzer analyzer, IGameInstallationService installer,
                               PackageDownloadService packages, IProfileRepository profiles,
                               SaveGameDetails saveGameDetails, CompatibilityListService compatibility,
                               Func<Task<IReadOnlyList<GpuInfo>>> detectGpus)
    {
        _game = game;
        _analyzer = analyzer;
        _installer = installer;
        _packages = packages;
        _profiles = profiles;
        _saveGameDetails = saveGameDetails;
        _compatibility = compatibility;
        _detectGpus = detectGpus;
        _gameName = _editName = game.Name;
        _coverImage = game.CoverImage;
        Installations = game.Installations.Select((_, index) => game.Installations.Count == 1
                                                      ? "Primary installation"
                                                      : $"Installation {index + 1}").ToList();
        ComponentOptions =
        [
            Fsr = new ComponentOption(DownloadComponent.Fsr),
            FakeNvapi = new ComponentOption(DownloadComponent.FakeNvapi),
            OptiPatcher = new ComponentOption(DownloadComponent.OptiPatcher),
            Nukem = new ComponentOption(DownloadComponent.Nukem)
        ];
        foreach (var option in ComponentOptions) option.BrowseRequested += o => _ = BrowseComponent_Async(o);
        RefreshPackage();
        SelectedInstallationIndex = game.Installations.Count > 0 ? 0 : -1;
    }

    /// <summary>Set by the view before any command that picks files runs.</summary>
    public IFileDialogs? Dialogs { get; set; }

    /// <summary>Set by the view before any command that copies text or launches something runs.</summary>
    public IShellActions? Shell { get; set; }

    public string PlatformText => _game.Platform.ToString();

    public IReadOnlyList<string> Installations { get; }

    public bool HasMultipleInstallations => Installations.Count > 1;

    public bool HasCover => File.Exists(CoverImage);

    public bool IsPreviewing => Plan is not null;

    public bool HasNoComponents => Components.Count == 0;

    public bool IsStableChannel => Channel == ReleaseChannel.Stable;

    public bool IsBetaChannel => Channel == ReleaseChannel.Beta;

    public string CompatibilityNotes => WikiEntry?.Notes ?? "";

    public string? CompatibilityPageUrl => WikiEntry?.PageUrl ?? (HasWikiList ? CompatibilityListService.WikiUrl : null);

    public bool HasCompatibilityPage => CompatibilityPageUrl is not null;

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

    public IReadOnlyList<string> ProxyNames => GameInstallationService.ProxyNames;

    public ComponentOption Fsr { get; }

    public ComponentOption FakeNvapi { get; }

    public ComponentOption OptiPatcher { get; }

    public ComponentOption Nukem { get; }

    private IReadOnlyList<ComponentOption> ComponentOptions { get; }

    private GameInstallation? SelectedInstallation => SelectedInstallationIndex >= 0
        ? _game.Installations[SelectedInstallationIndex]
        : null;

    private string Executable => !string.IsNullOrWhiteSpace(ExecutablePath)
        ? ExecutablePath.Trim()
        : throw new InvalidOperationException("Select the game executable first.");

    private string TargetDirectory => Path.GetDirectoryName(Path.GetFullPath(Executable)) ??
                                      throw new InvalidOperationException("Invalid executable path.");

    private IProgress<string> Progress => new Progress<string>(message => Status = message);

    private IFileDialogs RequiredDialogs =>
        Dialogs ?? throw new InvalidOperationException("File dialogs are unavailable.");

    private IShellActions RequiredShell => Shell ?? throw new InvalidOperationException("The shell is unavailable.");

    [RelayCommand]
    private Task Load()
    {
        return RunOperation_Async(async () =>
        {
            // Read the full saved catalog independently of the Profiles page's search filter.
            var catalog = await _profiles.LoadProfileCatalog_Async();
            ProfileChoices = catalog.Profiles;
            SelectedProfile = catalog.Profiles.FirstOrDefault(p => p.Id == catalog.DefaultProfileId);
            if (DemoWorkspace.ActiveRoot is { } demoRoot) PackagePath = Path.Combine(demoRoot, "Package");

            // Independent lookups; only the analysis needs all of them.
            var gpus = _detectGpus();
            var wiki = LoadCompatibility_Async();

            // Most scanners cannot tell which executable is the game; the install guide's rules usually can.
            var detected = string.IsNullOrWhiteSpace(ExecutablePath) && SelectedInstallation is { } installation
                ? Task.Run(() => ExecutableResolver.FindBest(installation.RootPath, _game.Name))
                : Task.FromResult<ExecutableCandidate?>(null);
            await Task.WhenAll(gpus, wiki, detected);
            _gpus = gpus.Result;
            GpuText = InstallAdvisor.PickPrimaryGpu(_gpus)?.Name ?? "Not detected";
            if (detected.Result is { } best) ExecutablePath = best.Path;

            await Analyze_Async();
        });
    }

    [RelayCommand]
    private Task Verify() { return RunOperation_Async(Analyze_Async); }

    [RelayCommand]
    private Task DetectExecutable()
    {
        return RunOperation_Async(async () =>
        {
            if (SelectedInstallation is not { } installation) return;

            var candidates = await Task.Run(() => ExecutableResolver.FindCandidates(installation.RootPath,
                                                                                   _game.Name));

            if (candidates.Count == 0)
                throw new InvalidOperationException("No 64-bit game executable was found. Browse to it instead.");

            ExecutablePath = candidates[0].Path;
            await Analyze_Async();
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
        return RunOperation_Async(async () =>
        {
            if (Recommendation is null) await Analyze_Async();

            var recommendation = Recommendation ??
                                 throw new InvalidOperationException("Select the game executable first.");
            SelectedProxy = recommendation.Proxy;
            Advise(FakeNvapi, recommendation.FakeNvapi);
            Advise(Nukem, recommendation.Nukem);

            if (recommendation.OptiPatcher == ComponentAdvice.Install)
            {
                // OptiScaler releases do not bundle OptiPatcher, so it has to come from its own releases.
                if (!_componentReleases.ContainsKey(DownloadComponent.OptiPatcher))
                {
                    _componentReleases[DownloadComponent.OptiPatcher] =
                        await _packages.GetComponentReleases_Async(DownloadComponent.OptiPatcher);
                    RefreshPackage();
                }

                OptiPatcher.Selected = OptiPatcher.Choices.FirstOrDefault(c => c.Source == ComponentSource.Release)
                                       ?? OptiPatcher.Selected;
            }
            else
            {
                Advise(OptiPatcher, recommendation.OptiPatcher);
            }

            Status = "Recommended settings selected. Preview install to review the exact changes.";
        });
    }

    [RelayCommand]
    private Task PreviewInstall()
    {
        return RunOperation_Async(async () =>
        {
            // Validate the game before spending time downloading its package.
            SafeFiles.RequireX64PeFile(Executable, false);

            if (string.IsNullOrWhiteSpace(PackagePath))
            {
                var release = (await _packages.GetReleases_Async(Channel == ReleaseChannel.Beta)).FirstOrDefault() ??
                              throw new InvalidOperationException("No release is available. Choose a local package.");
                await DownloadPackage_Async(release);
            }

            var plan = await _installer.PreviewPackageInstallation_Async(
                                                                         Executable, PackagePath.Trim(), SelectedProxy,
                                                                         SelectedProfile,
                                                                         ComponentOptions
                                                                             .Select(o => o.ToSelection()).ToList(),
                                                                         Progress,
                                                                         keepCurrentSettings: KeepCurrentSettings &&
                                                                             HasCurrentIni);
            ShowPreview(plan);
        });
    }

    [RelayCommand]
    private Task PreviewProfile()
    {
        return RunOperation_Async(async () =>
        {
            var profile = SelectedProfile ?? throw new InvalidOperationException("Select a saved profile first.");
            ShowPreview(await _installer.PreviewProfileApplication_Async(Executable, profile));
        });
    }

    [RelayCommand]
    private Task PreviewNativeSwap()
    {
        return RunOperation_Async(async () =>
        {
            var target = PathUtil.Normalize(TargetDirectory);

            if (await RequiredDialogs.PickFile_Async("Select native DLL in this game", "*.dll") is not { } destination)
                return;

            if (!PathUtil.IsWithin(PathUtil.Normalize(destination), target))
                throw new InvalidOperationException("Select a DLL within the selected game's executable folder.");

            if (await RequiredDialogs.PickFile_Async("Select replacement DLL", "*.dll") is { } source)
                ShowPreview(await _installer.PreviewNativeDllSwap_Async(destination, source));
        });
    }

    [RelayCommand]
    private Task Apply()
    {
        return RunOperation_Async(async () =>
        {
            if (Plan is not { } plan) return;

            // Consume the preview even on failure; a retry must inspect the current file state again.
            Plan = null;
            await _installer.ExecuteInstallationPlan_Async(plan);
            await Analyze_Async();
            var verification = await _installer.VerifyInstallation_Async(plan.TargetDirectory);
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
        return RunOperation_Async(async () =>
        {
            await _installer.RestoreLatestOperation_Async(TargetDirectory);
            await Analyze_Async();
            Status = "Latest operation restored.";
        });
    }

    [RelayCommand]
    private Task ShowHistory()
    {
        return RunOperation_Async(async () =>
        {
            var target = PathUtil.Normalize(TargetDirectory);
            var history = (await _installer.GetOperationHistory_Async())
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
        return RunOperation_Async(async () =>
        {
            var report = await BuildDiagnosticsReport_Async();
            await RequiredShell.SetClipboardText_Async(report);

            // Show exactly what was copied, so nothing leaves the machine unseen.
            DetailsText = report;
            IsDetailsExpanded = true;
            Status = "Diagnostic report copied. Personal folder names are replaced; review it before sharing.";
        });
    }

    [RelayCommand]
    private Task CopyLaunchOptions()
    {
        return RunOperation_Async(async () =>
        {
            await RequiredShell.SetClipboardText_Async(LinuxLaunchOptions);
            Status = "Launch options copied. Paste them into the game's Steam properties.";
        });
    }

    [RelayCommand]
    private Task LaunchGame()
    {
        return RunOperation_Async(async () =>
        {
            var target = GameLauncher.Resolve(_game, ExecutablePath) ??
                         throw new InvalidOperationException("This game cannot be launched from here.");
            Status = await RequiredShell.Open_Async(target)
                ? $"Launching {GameName}… Press Insert in game to open the OptiScaler overlay."
                : "Could not launch the game.";
        });
    }

    [RelayCommand]
    private Task OpenCompatibilityPage()
    {
        return RunOperation_Async(async () =>
        {
            if (CompatibilityPageUrl is { } url &&
                !await RequiredShell.Open_Async(new LaunchTarget(new Uri(url), null)))
                Status = "Could not open the wiki page.";
        });
    }

    [RelayCommand]
    private async Task SelectChannel(ReleaseChannel channel)
    {
        if (IsBusy) return;

        Channel = channel;
        _releases = [];
        PackagePath = _packageByChannel.GetValueOrDefault(channel, "");
        RefreshPackage();
        await RunOperation_Async(FetchReleases_Async);
    }

    [RelayCommand]
    private Task RefreshVersions() { return RunOperation_Async(FetchReleases_Async); }

    [RelayCommand]
    private Task BrowseExecutable()
    {
        return RunOperation_Async(async () =>
        {
            if (await RequiredDialogs.PickFile_Async("Select game executable", "*.exe") is { } path)
                ExecutablePath = path;
        });
    }

    [RelayCommand]
    private Task BrowsePackage() { return RunOperation_Async(BrowsePackage_Async); }

    [RelayCommand]
    private void ClearProfile() { SelectedProfile = null; }

    [RelayCommand]
    private void ToggleEditDetails() { IsEditingDetails = !IsEditingDetails; }

    [RelayCommand]
    private Task SaveDetails()
    {
        return RunOperation_Async(async () =>
        {
            if (SelectedInstallation is not { } installation) return;

            _game = await _saveGameDetails(_game.Id, EditName, installation.RootPath, ExecutablePath.Trim());
            GameName = _game.Name;
            CoverImage = _game.CoverImage;
            IsEditingDetails = false;
            Status = "Game details saved.";
            await LoadCompatibility_Async();
        });
    }

    /// <summary>Everything a bug report needs, with the user's home folder and name replaced.</summary>
    public async Task<string> BuildDiagnosticsReport_Async()
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
            Verification = target is null ? null : await _installer.VerifyInstallation_Async(target),
            History = target is null
                ? []
                : (await _installer.GetOperationHistory_Async())
                .Where(j => PathUtil.AreSame(j.TargetDirectory, target)).ToList(),
            CurrentIni = ini is not null && File.Exists(ini) ? await File.ReadAllTextAsync(ini) : null,
            LogTail = target is null
                ? null
                : await DiagnosticsReport.ReadLogTail_Async(Path.Combine(target, "OptiScaler.log"), 40)
        });
    }

    partial void OnSelectedInstallationIndexChanged(int value)
    {
        if (SelectedInstallation is not { } installation) return;

        ExecutablePath = installation.PrimaryExecutablePath ?? "";
        FolderPath = installation.RootPath;
        ResetAnalysis();
    }

    partial void OnExecutablePathChanged(string value) { ResetAnalysis(); }

    partial void OnPackagePathChanged(string value)
    {
        _packageByChannel[Channel] = value.Trim();
        RefreshPackage();
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
                               value.Files.Select(f => $"{(f.ReplacesExisting ? "Replace" : "Create")} : {f.RelativePath}"));

        if (value.IniChanges.Count > 0)
            text += "\n\nOptiScaler.ini changes:\n" +
                    string.Join("\n", value.IniChanges.Take(40).Select(change => "  " + change)) +
                    (value.IniChanges.Count > 40 ? $"\n  … {value.IniChanges.Count - 40} more" : "");

        PreviewText = text;
    }

    partial void OnSelectedVersionChanged(VersionChoice? value)
    {
        if (value is { Action: not VersionAction.UseCurrent }) _ = HandleVersionChoice_Async(value);
    }

    private static void Advise(ComponentOption option, ComponentAdvice advice)
    {
        // OptiScaler 0.9+ bundles FakeNvapi and NukemFG, so "install" means using the bundled copy.
        option.Selected = advice switch
        {
            ComponentAdvice.Install => ComponentChoice.Bundle,
            ComponentAdvice.Skip => ComponentChoice.KeepExisting,
            _ => option.Selected
        };
    }

    private async Task LoadCompatibility_Async()
    {
        var index = await _compatibility.GetIndex_Async();
        HasWikiList = index.Count > 0;
        WikiEntry = index.Find(GameName);
    }

    private async Task HandleVersionChoice_Async(VersionChoice choice)
    {
        // Let the combo box finish its selection before the list it shows is replaced.
        await Task.Yield();
        await RunOperation_Async(choice.Action switch
        {
            VersionAction.Download => () => DownloadPackage_Async(choice.Release!),
            VersionAction.FetchReleases => FetchReleases_Async,
            _ => BrowsePackage_Async
        });

        // Actions are not a real selection; fall back to the package that is actually chosen.
        if (SelectedVersion == choice) RefreshPackage();
    }

    private async Task BrowseComponent_Async(ComponentOption option)
    {
        await Task.Yield();
        option.Selected = ComponentChoice.Bundle;
        await RunOperation_Async(async () =>
        {
            var component = option.Component;

            if (await RequiredDialogs.PickFile_Async($"Select {component.Name} binary",
                                                     component == DownloadComponent.OptiPatcher ? "*.asi" : "*.dll")
                is not { } path)
                return;

            if (!component.FileNames.Contains(Path.GetFileName(path), StringComparer.OrdinalIgnoreCase))
                throw new InvalidDataException("Expected " + string.Join(" or ", component.FileNames));

            SafeFiles.RequireX64PeFile(path, true);
            var local = new ComponentChoice($"Local · {ReadVersion(path)}", ComponentSource.Local, LocalPath: path);
            option.Choices = [..option.Choices.Where(c => c.Source != ComponentSource.Local).SkipLast(1), local,
                              ComponentChoice.BrowseLocal];
            option.Selected = local;
            Status = $"Local {component.Name} selected. Preview install to review changes.";
        });
    }

    private async Task BrowsePackage_Async()
    {
        if (await RequiredDialogs.PickFolder_Async("Select extracted OptiScaler package") is { } path)
            PackagePath = path;
    }

    private async Task DownloadPackage_Async(PackageRelease release)
    {
        PackagePath = await _packages.DownloadPackage_Async(release, Progress);
        PackageInfo = $"{Channel} · {release.Version} · {release.AssetName}";
        Status = "Package downloaded. Review the components, then click Install to preview changes.";
    }

    private async Task FetchReleases_Async()
    {
        _releases = await _packages.GetReleases_Async(Channel == ReleaseChannel.Beta);
        var failures = new List<string>();

        foreach (var option in ComponentOptions)
            try
            {
                _componentReleases[option.Component] = await _packages.GetComponentReleases_Async(option.Component);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                failures.Add(option.Component.Name);
            }

        RefreshPackage();
        SelectedVersion = null;
        Status = _releases.Count == 0
            ? "No downloadable releases found. A local package can still be used."
            : "Select a release to download its complete bundle.";
        if (_componentReleases.TryGetValue(DownloadComponent.Nukem, out var nukem) && nukem.Count == 0)
            ExtrasText =
                "NukemFG has no downloadable binary releases. Use a bundled copy or choose a local DLL; other components can use the versions below.";
        if (failures.Count > 0)
            ExtrasText = "Could not refresh: " + string.Join(", ", failures) +
                         ". Retry with Refresh versions; bundled choices remain available.";
    }

    /// <summary>Rebuilds the version and component lists for the current package folder.</summary>
    private void RefreshPackage()
    {
        var folder = PackagePath.Trim();
        var dll = Path.Combine(folder, "OptiScaler.dll");
        var valid = Path.IsPathFullyQualified(folder) && File.Exists(dll) &&
                    File.Exists(Path.Combine(folder, "OptiScaler.ini"));
        var current = valid
            ? new VersionChoice(_packages.GetPackageVersion(folder) ?? ReadVersion(dll), VersionAction.UseCurrent)
            : null;
        VersionChoices = [..current is null ? [] : new[] { current },
                          .._releases.Select(r => new VersionChoice(r.Version, VersionAction.Download, r)),
                          FetchChoice, BrowseChoice];
        SelectedVersion = current;
        VersionPlaceholder = _releases.Count > 0 ? "Select release to download" : "Fetch releases…";

        var bundledFiles = valid
            ? Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
                .Where(p => !p.EndsWith(".ini", StringComparison.OrdinalIgnoreCase)).ToList()
            : [];

        foreach (var option in ComponentOptions)
        {
            var selected = option.Selected;
            List<ComponentChoice> choices =
            [
                ComponentChoice.Bundle, ComponentChoice.KeepExisting,
                .._componentReleases.GetValueOrDefault(option.Component, [])
                    .Select(r => new ComponentChoice(r.Version, ComponentSource.Release, r)),
                ..option.Choices.Where(c => c.Source == ComponentSource.Local),
                ComponentChoice.BrowseLocal
            ];
            option.Choices = choices;
            option.Selected = selected is not null && choices.Contains(selected) ? selected : choices[0];
            var bundled = bundledFiles.FirstOrDefault(p => option.Component.FileNames.Contains(Path.GetFileName(p),
                                                          StringComparer.OrdinalIgnoreCase));
            option.Tip = bundled is null
                ? "Choose a release, use the package bundle, or keep existing files."
                : $"Bundled: {ReadVersion(bundled)}. Choose a release to override it.";
        }

        ExtrasText = valid
            ? "Choose component versions or use the package bundle. Downloads are staged before you review changes."
            : "Fetch a release to download OptiScaler and its bundled components.";
        PackageInfo = valid
            ? $"{Channel} · {folder}"
            : "Install downloads the latest release. You can also select a version or browse a local package.";
    }

    private void ShowPreview(InstallPlan plan)
    {
        Plan = plan;
        Status = "Review the preview, then apply or cancel.";
    }

    private async Task Analyze_Async()
    {
        if (SelectedInstallation is not { } original) return;

        var analysis = await _analyzer.AnalyzeGame_Async(_game.Id, new GameInstallation
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

        var antiCheat = analysis.Evidence.Any(e => e.Code == "game.anticheat");
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
        var result = await _installer.VerifyInstallation_Async(target);
        CanRestore = result.Journal is not null;
        CanUninstall = result.Journal is { Kind: OperationKind.InstallOptiscaler, State: OperationState.Installed };

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

        // Proxies this app wrote are ours to replace; any other proxy DLL belongs to another mod.
        var managedFiles = result.Operations.SelectMany(j => j.Files).Select(f => f.RelativePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var occupied = GameInstallationService.ProxyNames
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

    private async Task RunOperation_Async(Func<Task> operation)
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

    internal static string ReadVersion(string path) { return SafeFiles.ReadFileVersion(path) ?? "Bundled · local"; }
}
