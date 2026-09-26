using System.Diagnostics;
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
    private readonly Dictionary<DownloadComponent, IReadOnlyList<PackageRelease>> _componentReleases = new();
    private readonly IGameInstallationService _installer;
    private readonly Dictionary<ReleaseChannel, string> _packageByChannel = new();
    private readonly Dictionary<ReleaseChannel, IReadOnlyList<PackageRelease>> _releasesByChannel = new();
    private readonly PackageDownloadService _packages;
    private readonly IProfileRepository _profiles;
    private readonly SaveGameDetails _saveGameDetails;
    private IReadOnlyList<string> _componentFailures = [];
    private bool _componentReleasesLoaded;
    private GameRecord _game;
    private IReadOnlyList<PackageRelease> _releases = [];

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

    [ObservableProperty] private string _executablePath = "";

    [ObservableProperty] private string _extrasText = "Choose a local package to see its bundled components.";

    [ObservableProperty] private string _folderPath = "";

    [ObservableProperty] private string _gameName;

    [ObservableProperty] private string _guidanceText = "Select your game executable, then verify the installation.";

    [ObservableProperty] private string _inputsText = "None detected";

    [ObservableProperty] private string _installButtonText = "Preview install";

    [ObservableProperty] private string _installStateText = "Checking installation";

    [ObservableProperty] private bool _isBusy;

    [ObservableProperty] private bool _isDetailsExpanded;

    [ObservableProperty] private bool _isEditingDetails;

    [ObservableProperty] private bool _canRestore;

    [ObservableProperty] private bool _canUninstall;

    [ObservableProperty] private string _packageInfo = "";

    [ObservableProperty] private string _packagePath = "";

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsPreviewing))]
    private InstallPlan? _plan;

    [ObservableProperty] private string _previewText = "";

    [ObservableProperty] private IReadOnlyList<RenderProfile> _profileChoices = [];

    [ObservableProperty] private int _selectedInstallationIndex = -1;

    [ObservableProperty] private RenderProfile? _selectedProfile;

    [ObservableProperty] private string _selectedProxy = GameInstallationService.ProxyNames[0];

    [ObservableProperty] private VersionChoice? _selectedVersion;

    [ObservableProperty] private string _status = "Inspecting your installation…";

    [ObservableProperty] private IReadOnlyList<VersionChoice> _versionChoices = [FetchChoice, BrowseChoice];

    [ObservableProperty] private string _versionPlaceholder = "Fetch releases…";

    public ManageGameViewModel(GameRecord game, IGameAnalyzer analyzer, IGameInstallationService installer,
                               PackageDownloadService packages, IProfileRepository profiles,
                               SaveGameDetails saveGameDetails)
    {
        _game = game;
        _analyzer = analyzer;
        _installer = installer;
        _packages = packages;
        _profiles = profiles;
        _saveGameDetails = saveGameDetails;
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

    public string PlatformText => _game.Platform.ToString();

    public IReadOnlyList<string> Installations { get; }

    public bool HasMultipleInstallations => Installations.Count > 1;

    public bool HasCover => File.Exists(CoverImage);

    public bool IsPreviewing => Plan is not null;

    public bool HasNoComponents => Components.Count == 0;

    public bool IsStableChannel => Channel == ReleaseChannel.Stable;

    public bool IsBetaChannel => Channel == ReleaseChannel.Beta;

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
            await Analyze_Async();
        });
    }

    [RelayCommand]
    private Task Verify() { return RunOperation_Async(Analyze_Async); }

    [RelayCommand]
    private Task PreviewInstall()
    {
        return RunOperation_Async(async () =>
        {
            // Validate the game before spending time downloading its package.
            SafeFiles.RequireX64PeFile(Executable, false);

            if (string.IsNullOrWhiteSpace(PackagePath))
            {
                if (!_releasesByChannel.ContainsKey(Channel)) await FetchReleases_Async();
                var release = _releases.FirstOrDefault() ??
                              throw new InvalidOperationException("No release is available. Choose a local package.");
                await DownloadPackage_Async(release);
            }

            ShowPreview(await _installer.PreviewPackageInstallation_Async(
                                                                          Executable, PackagePath.Trim(), SelectedProxy,
                                                                          SelectedProfile,
                                                                          ComponentOptions
                                                                              .Select(o => o.ToSelection()).ToList(),
                                                                          Progress));
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
                : string.Join("\n", history.Select(j => $"{j.CreatedAtUtc:g} : {j.Description} : {j.State}"));
        });
    }

    [RelayCommand]
    private async Task SelectChannel(ReleaseChannel channel)
    {
        if (IsBusy) return;

        Channel = channel;
        _releases = _releasesByChannel.GetValueOrDefault(channel, []);
        PackagePath = _packageByChannel.GetValueOrDefault(channel, "");
        RefreshPackage();

        // Each channel is fetched once; switching back and forth reuses the list until Refresh versions.
        if (_releasesByChannel.ContainsKey(channel))
        {
            Status = DescribeReleases();
            ShowComponentNotes();
        }
        else
        {
            await RunOperation_Async(FetchReleases_Async);
        }
    }

    [RelayCommand]
    private Task RefreshVersions()
    {
        return RunOperation_Async(() =>
        {
            _packages.ClearReleaseLists();
            _releasesByChannel.Clear();
            _componentReleasesLoaded = false;

            return FetchReleases_Async();
        });
    }

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
        PreviewText = value is null
            ? ""
            : $"{value.Description}\nTarget: {value.TargetDirectory}\n\n" +
              string.Join("\n",
                          value.Files.Select(f => $"{(f.ReplacesExisting ? "Replace" : "Create")} : {f.RelativePath}"));
    }

    partial void OnSelectedVersionChanged(VersionChoice? value)
    {
        if (value is { Action: not VersionAction.UseCurrent }) _ = HandleVersionChoice_Async(value);
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
        _releases = _releasesByChannel[Channel] = await _packages.GetReleases_Async(Channel == ReleaseChannel.Beta);

        // Component releases do not depend on the OptiScaler channel, so they are fetched only once.
        if (!_componentReleasesLoaded)
        {
            var failures = new List<string>();

            foreach (var option in ComponentOptions)
                try
                {
                    _componentReleases[option.Component] =
                        await _packages.GetComponentReleases_Async(option.Component);
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
                {
                    failures.Add(option.Component.Name);
                }

            _componentFailures = failures;
            _componentReleasesLoaded = failures.Count == 0;
        }

        RefreshPackage();
        SelectedVersion = null;
        Status = DescribeReleases();
        ShowComponentNotes();
    }

    /// <summary>Replaces the package hint with component problems whenever a channel's releases are shown.</summary>
    private void ShowComponentNotes()
    {
        if (_componentReleases.TryGetValue(DownloadComponent.Nukem, out var nukem) && nukem.Count == 0)
            ExtrasText =
                "NukemFG has no downloadable binary releases. Use a bundled copy or choose a local DLL; other components can use the versions below.";
        if (_componentFailures.Count > 0)
            ExtrasText = "Could not refresh: " + string.Join(", ", _componentFailures) +
                         ". Retry with Refresh versions; bundled choices remain available.";
    }

    private string DescribeReleases()
    {
        return _releases.Count == 0
            ? "No downloadable releases found. A local package can still be used."
            : "Select a release to download its complete bundle.";
    }

    /// <summary>Rebuilds the version and component lists for the current package folder.</summary>
    private void RefreshPackage()
    {
        var folder = PackagePath.Trim();
        var dll = Path.Combine(folder, "OptiScaler.dll");
        var valid = Path.IsPathFullyQualified(folder) && File.Exists(dll) &&
                    File.Exists(Path.Combine(folder, "OptiScaler.ini"));
        var current = valid ? new VersionChoice(ReadVersion(dll), VersionAction.UseCurrent) : null;
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
        var antiCheat = analysis.Evidence.Any(e => e.Code == "game.anticheat");
        CompatibilityText = antiCheat ? "Anti-cheat detected" : "Not verified";
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
        Status = $"Analysis complete · {analysis.Components.Count} detected files.";
        DetailsText = string.Join("\n",
                                  analysis.Components.Select(c => $"{c.Kind}: {c.Path}")
                                      .Concat(analysis.Evidence.Select(e => e.Path is null
                                                                           ? e.Message
                                                                           : $"{e.Message} : {e.Path}")));

        if (string.IsNullOrWhiteSpace(ExecutablePath)) return;

        var result = await _installer.VerifyInstallation_Async(TargetDirectory);
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

    internal static string ReadVersion(string path)
    {
        try
        {
            var version = FileVersionInfo.GetVersionInfo(path).FileVersion;

            return string.IsNullOrWhiteSpace(version) ? "Bundled · local" : version;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            return "Bundled · local";
        }
    }
}
