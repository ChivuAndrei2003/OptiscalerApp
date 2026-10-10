using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OptiscalerApp.Management;

namespace OptiscalerApp.ViewModels;

/// <summary>The page that runs package actions: one operation at a time, reported in its status line.</summary>
public interface IOperationHost
{
    bool IsBusy { get; }

    string Status { set; }

    IFileDialogs RequiredDialogs { get; }

    IProgress<string> Progress { get; }

    Task RunOperationAsync(Func<Task> operation);
}

/// <summary>
///     Which OptiScaler package an install uses, per release channel, and which version of each optional component
///     (FSR, FakeNvapi, OptiPatcher, NukemFG) goes with it.
/// </summary>
public sealed partial class PackageSelectionViewModel : ObservableObject
{
    private static readonly VersionChoice FetchChoice = new("Fetch releases…", VersionAction.FetchReleases);
    private static readonly VersionChoice BrowseChoice = new("Choose local package…", VersionAction.BrowseLocal);

    private readonly Dictionary<DownloadComponent, IReadOnlyList<PackageRelease>> _componentReleases = new();
    private readonly IOperationHost _host;
    private readonly Dictionary<ReleaseChannel, string> _packageByChannel = new();
    private readonly PackageDownloadService _packages;
    private readonly Dictionary<ReleaseChannel, IReadOnlyList<PackageRelease>> _releasesByChannel = new();
    private IReadOnlyList<string> _componentFailures = [];
    private bool _componentReleasesLoaded;
    private PackageContents _package = PackageContents.None;
    private Task _packageRead = Task.CompletedTask;
    private IReadOnlyList<PackageRelease> _releases = [];

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsStableChannel), nameof(IsBetaChannel))]
    private ReleaseChannel _channel = ReleaseChannel.Stable;

    [ObservableProperty] private string _extrasText = "Choose a local package to see its bundled components.";

    [ObservableProperty] private string _packageInfo = "";

    [ObservableProperty] private string _packagePath = "";

    [ObservableProperty] private VersionChoice? _selectedVersion;

    [ObservableProperty] private IReadOnlyList<VersionChoice> _versionChoices = [FetchChoice, BrowseChoice];

    [ObservableProperty] private string _versionPlaceholder = "Fetch releases…";

    public PackageSelectionViewModel(PackageDownloadService packages, IOperationHost host)
    {
        _packages = packages;
        _host = host;
        ComponentOptions =
        [
            Fsr = new ComponentOption(DownloadComponent.Fsr),
            FakeNvapi = new ComponentOption(DownloadComponent.FakeNvapi),
            OptiPatcher = new ComponentOption(DownloadComponent.OptiPatcher),
            Nukem = new ComponentOption(DownloadComponent.Nukem)
        ];
        foreach (var option in ComponentOptions) option.BrowseRequested += o => _ = BrowseComponentAsync(o);
        ShowPackage();
    }

    public bool IsStableChannel => Channel == ReleaseChannel.Stable;

    public bool IsBetaChannel => Channel == ReleaseChannel.Beta;

    public ComponentOption Fsr { get; }

    public ComponentOption FakeNvapi { get; }

    public ComponentOption OptiPatcher { get; }

    public ComponentOption Nukem { get; }

    private IReadOnlyList<ComponentOption> ComponentOptions { get; }

    public IReadOnlyList<ComponentInstallSelection> Selections => ComponentOptions.Select(o => o.ToSelection()).ToList();

    /// <summary>The chosen package folder; without one, the channel's newest release is downloaded.</summary>
    public async Task<string> EnsurePackageAsync()
    {
        if (string.IsNullOrWhiteSpace(PackagePath))
        {
            if (!_releasesByChannel.ContainsKey(Channel)) await FetchReleasesAsync();
            var release = _releases.FirstOrDefault() ??
                          throw new InvalidOperationException("No release is available. Choose a local package.");
            await DownloadPackageAsync(release);
        }

        return PackagePath.Trim();
    }

    /// <summary>
    ///     Loads both channels and the component lists when the page opens, so choosing a channel does not have to
    ///     wait for GitHub. Failures stay silent here; fetching by hand reports them.
    /// </summary>
    public async Task PreloadReleasesAsync()
    {
        var stable = TryGetReleasesAsync(_packages.GetReleasesAsync(false));
        var beta = TryGetReleasesAsync(_packages.GetReleasesAsync(true));
        var failures = await FetchComponentReleasesAsync();
        if (await stable is { } stableReleases) _releasesByChannel[ReleaseChannel.Stable] = stableReleases;
        if (await beta is { } betaReleases) _releasesByChannel[ReleaseChannel.Beta] = betaReleases;
        _componentReleasesLoaded |= failures.Count == 0;
        _releases = _releasesByChannel.GetValueOrDefault(Channel, _releases);
        ShowPackage();
        ShowComponentNotes();
    }

    /// <summary>Selects the advised component versions; nothing changes on disk until the install is applied.</summary>
    public async Task ApplyRecommendationAsync(InstallRecommendation recommendation)
    {
        Advise(FakeNvapi, recommendation.FakeNvapi);
        Advise(Nukem, recommendation.Nukem);

        if (recommendation.OptiPatcher != ComponentAdvice.Install)
        {
            Advise(OptiPatcher, recommendation.OptiPatcher);

            return;
        }

        // OptiScaler releases do not bundle OptiPatcher, so it has to come from its own releases.
        if (!_componentReleases.ContainsKey(DownloadComponent.OptiPatcher))
        {
            _componentReleases[DownloadComponent.OptiPatcher] =
                await _packages.GetComponentReleasesAsync(DownloadComponent.OptiPatcher);
            ShowPackage();
        }

        OptiPatcher.Selected = OptiPatcher.Choices.FirstOrDefault(c => c.Source == ComponentSource.Release)
                               ?? OptiPatcher.Selected;
    }

    [RelayCommand]
    private async Task SelectChannel(ReleaseChannel channel)
    {
        if (_host.IsBusy) return;

        Channel = channel;
        _releases = _releasesByChannel.GetValueOrDefault(channel, []);
        var package = _packageByChannel.GetValueOrDefault(channel, "");

        // A different folder is read and shown by the path change; otherwise only the release list changed.
        if (package == PackagePath)
        {
            ShowPackage();
        }
        else
        {
            PackagePath = package;
            await _packageRead;
        }

        // Each channel is fetched once; switching back and forth reuses the list until Refresh versions.
        if (_releasesByChannel.ContainsKey(channel))
        {
            _host.Status = DescribeReleases();
            ShowComponentNotes();
        }
        else
        {
            await _host.RunOperationAsync(FetchReleasesAsync);
        }
    }

    [RelayCommand]
    private Task RefreshVersions()
    {
        return _host.RunOperationAsync(() =>
        {
            _packages.ClearReleaseLists();
            _releasesByChannel.Clear();
            _componentReleasesLoaded = false;

            return FetchReleasesAsync();
        });
    }

    [RelayCommand]
    private Task BrowsePackage() { return _host.RunOperationAsync(BrowsePackageAsync); }

    partial void OnPackagePathChanged(string value)
    {
        _packageByChannel[Channel] = value.Trim();
        _packageRead = ReadPackageAsync();
    }

    partial void OnSelectedVersionChanged(VersionChoice? value)
    {
        if (value is { Action: not VersionAction.UseCurrent }) _ = HandleVersionChoiceAsync(value);
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

    private async Task HandleVersionChoiceAsync(VersionChoice choice)
    {
        // Let the combo box finish its selection before the list it shows is replaced.
        await Task.Yield();
        await _host.RunOperationAsync(choice.Action switch
        {
            VersionAction.Download => () => DownloadPackageAsync(choice.Release!),
            VersionAction.FetchReleases => FetchReleasesAsync,
            _ => BrowsePackageAsync
        });

        // Actions are not a real selection; fall back to the package that is actually chosen.
        await _packageRead;
        if (SelectedVersion == choice) ShowPackage();
    }

    private async Task BrowseComponentAsync(ComponentOption option)
    {
        await Task.Yield();

        // "Choose local file…" is an action, not a choice; cancelling the picker keeps the earlier choice.
        option.Selected = option.SelectedBeforeBrowse ?? ComponentChoice.Bundle;
        await _host.RunOperationAsync(async () =>
        {
            var component = option.Component;

            if (await _host.RequiredDialogs.PickFileAsync($"Select {component.Name} binary",
                                                           component == DownloadComponent.OptiPatcher
                                                               ? "*.asi"
                                                               : "*.dll")
                is not { } path)
                return;

            if (!component.FileNames.Contains(Path.GetFileName(path), StringComparer.OrdinalIgnoreCase))
                throw new InvalidDataException("Expected " + string.Join(" or ", component.FileNames));

            SafeFiles.RequireX64PeFile(path, true);
            var local = new ComponentChoice($"Local · {ReadVersion(path)}", ComponentSource.Local, LocalPath: path);
            option.Choices = [..option.Choices.Where(c => c.Source != ComponentSource.Local).SkipLast(1), local,
                              ComponentChoice.BrowseLocal];
            option.Selected = local;
            _host.Status = $"Local {component.Name} selected. Preview install to review changes.";
        });
    }

    private async Task BrowsePackageAsync()
    {
        if (await _host.RequiredDialogs.PickFolderAsync("Select extracted OptiScaler package") is not { } path) return;

        PackagePath = path;
        await _packageRead;
    }

    private async Task DownloadPackageAsync(PackageRelease release)
    {
        PackagePath = await _packages.DownloadPackageAsync(release, _host.Progress);
        await _packageRead;
        PackageInfo = $"{Channel} · {release.Version} · {release.AssetName}";
        _host.Status = "Package downloaded. Review the components, then click Install to preview changes.";
    }

    private async Task FetchReleasesAsync()
    {
        var releases = _packages.GetReleasesAsync(Channel == ReleaseChannel.Beta);

        // Component releases do not depend on the OptiScaler channel, so they are fetched only once, alongside it.
        if (!_componentReleasesLoaded)
        {
            _componentFailures = await FetchComponentReleasesAsync();
            _componentReleasesLoaded = _componentFailures.Count == 0;
        }

        _releases = _releasesByChannel[Channel] = await releases;
        ShowPackage();
        SelectedVersion = null;
        _host.Status = DescribeReleases();
        ShowComponentNotes();
    }

    /// <summary>Asks for every component's releases at once; returns the names of those that failed.</summary>
    private async Task<IReadOnlyList<string>> FetchComponentReleasesAsync()
    {
        var lists = await Task.WhenAll(ComponentOptions.Select(o => TryGetReleasesAsync(
                                                                   _packages.GetComponentReleasesAsync(o.Component))));
        var failures = new List<string>();

        foreach (var (option, list) in ComponentOptions.Zip(lists))
            if (list is null) failures.Add(option.Component.Name);
            else _componentReleases[option.Component] = list;

        return failures;
    }

    private static async Task<IReadOnlyList<PackageRelease>?> TryGetReleasesAsync(
        Task<IReadOnlyList<PackageRelease>> request)
    {
        try
        {
            return await request;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidDataException)
        {
            return null;
        }
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

    /// <summary>Reads the chosen package folder off the UI thread, then shows it.</summary>
    private async Task ReadPackageAsync()
    {
        var folder = PackagePath.Trim();
        var contents = Path.IsPathFullyQualified(folder)
            ? await Task.Run(() => ReadPackage(folder))
            : PackageContents.None;

        // A slower read of a folder that is no longer the chosen one must not replace the current one.
        if (folder != PackagePath.Trim()) return;

        _package = contents;
        ShowPackage();
    }

    private PackageContents ReadPackage(string folder)
    {
        try
        {
            var dll = Path.Combine(folder, "OptiScaler.dll");

            if (!File.Exists(dll) || !File.Exists(Path.Combine(folder, "OptiScaler.ini"))) return PackageContents.None;

            // Bounded, and unreadable folders are skipped: a picked folder can hold far more than a package.
            var files = Directory.EnumerateFiles(folder, "*", new EnumerationOptions { RecurseSubdirectories = true })
                .Take(GameInstallationService.MaxPackageFiles)
                .Where(p => !p.EndsWith(".ini", StringComparison.OrdinalIgnoreCase)).ToList();
            var bundled = new Dictionary<DownloadComponent, string>();

            foreach (var option in ComponentOptions)
                if (files.FirstOrDefault(p => option.Component.FileNames.Contains(Path.GetFileName(p),
                                                  StringComparer.OrdinalIgnoreCase)) is { } file)
                    bundled[option.Component] = ReadVersion(file);

            return new PackageContents(folder, _packages.GetPackageVersion(folder) ?? ReadVersion(dll), bundled);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return PackageContents.None;
        }
    }

    /// <summary>Rebuilds the version and component lists from the package and the releases already in memory.</summary>
    private void ShowPackage()
    {
        var current = _package.Version is null ? null : new VersionChoice(_package.Version, VersionAction.UseCurrent);
        VersionChoices = [..current is null ? [] : new[] { current },
                          .._releases.Select(r => new VersionChoice(r.Version, VersionAction.Download, r)),
                          FetchChoice, BrowseChoice];
        SelectedVersion = current;
        VersionPlaceholder = _releases.Count > 0 ? "Select release to download" : "Fetch releases…";

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
            option.Tip = _package.Bundled.TryGetValue(option.Component, out var bundled)
                ? $"Bundled: {bundled}. Choose a release to override it."
                : "Choose a release, use the package bundle, or keep existing files.";
        }

        ExtrasText = _package.Version is not null
            ? "Choose component versions or use the package bundle. Downloads are staged before you review changes."
            : "Fetch a release to download OptiScaler and its bundled components.";
        PackageInfo = _package.Version is not null
            ? $"{Channel} · {_package.Folder}"
            : "Install downloads the latest release. You can also select a version or browse a local package.";
    }

    internal static string ReadVersion(string path) { return SafeFiles.ReadFileVersion(path) ?? "Bundled · local"; }
}

/// <summary>What a package folder holds; <see cref="Version" /> is null when it is not an OptiScaler package.</summary>
internal sealed record PackageContents(
    string Folder,
    string? Version,
    IReadOnlyDictionary<DownloadComponent, string> Bundled)
{
    public static readonly PackageContents None = new("", null, new Dictionary<DownloadComponent, string>());
}
