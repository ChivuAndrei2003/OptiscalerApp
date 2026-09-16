using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using OptiscalerApp.Development;
using OptiscalerApp.Management;
using OptiscalerApp.Models;
using OptiscalerApp.ViewModels;

namespace OptiscalerApp.Views;

public partial class ManageGameView : UserControl
{
    private readonly Dictionary<DownloadComponent, IReadOnlyList<PackageRelease>> _componentReleases = new();
    private readonly Dictionary<string, string> _packages = new();
    private bool _busy;
    private string _channel = "Stable";
    private Bitmap? _cover;
    private GameRecord? _game;
    private InstallPlan? _plan;
    private bool _refreshingPackage;
    private IReadOnlyList<PackageRelease> _releases = [];

    public ManageGameView()
    {
        InitializeComponent();
        SizeChanged += (_, _) => UpdateLayoutColumns();
        OptionsGrid.SizeChanged += (_, _) => UpdateOptionColumns();
        Unloaded += (_, _) =>
        {
            CoverImage.Source = null;
            _cover?.Dispose();
            _cover = null;
        };
        ProxyBox.ItemsSource = GameInstallationService.ProxyNames;
        ProxyBox.SelectedIndex = 0;
        foreach (var (box, _) in OptionalComponents) box.SelectionChanged += Component_OnSelectionChanged;
        RefreshPackageOptions();
        Loaded += async (_, _) => await RunOperation_Async(async () =>
        {
            if (ViewModel is not { } vm) return;

            // Read the full saved catalog independently of the Profiles page's search filter.
            var profiles = await vm.LoadProfileCatalog_Async();
            ProfileBox.ItemsSource = profiles.Profiles;
            ProfileBox.SelectedItem = profiles.Profiles.FirstOrDefault(p => p.Id == profiles.DefaultProfileId);
            LoadCover();
            if (DemoWorkspace.ActiveRoot is { } demoRoot) PackageBox.Text = Path.Combine(demoRoot, "Package");
            await AnalyzeGame_Async();
        });
    }

    public ManageGameView(GameRecord game) : this()
    {
        _game = game;
        GameNameText.Text = game.Name;
        GameNameBox.Text = game.Name;
        PlatformText.Text = game.Platform.ToString();
        InstallationBox.ItemsSource = game.Installations.Select((_, index) => game.Installations.Count == 1
                                                                    ? "Primary installation"
                                                                    : $"Installation {index + 1}").ToList();
        InstallationBox.SelectedIndex = game.Installations.Count > 0 ? 0 : -1;
        InstallationBox.IsVisible = game.Installations.Count > 1;
    }

    private (ComboBox Box, DownloadComponent Component)[] OptionalComponents =>
    [
        (FsrBox, DownloadComponent.Fsr),
        (FakeNvapiBox, DownloadComponent.FakeNvapi),
        (OptiPatcherBox, DownloadComponent.OptiPatcher),
        (NukemBox, DownloadComponent.Nukem)
    ];

    private MainWindowViewModel? ViewModel => DataContext as MainWindowViewModel;

    private string Executable => !string.IsNullOrWhiteSpace(ExecutableBox.Text)
        ? ExecutableBox.Text.Trim()
        : throw new InvalidOperationException("Select the game executable first.");

    private string TargetDirectory => Path.GetDirectoryName(Path.GetFullPath(Executable)) ??
                                      throw new InvalidOperationException("Invalid executable path.");

    private void Installation_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_game is null || InstallationBox.SelectedIndex < 0) return;

        ExecutableBox.Text = _game.Installations[InstallationBox.SelectedIndex].PrimaryExecutablePath ?? "";
        FolderText.Text = _game.Installations[InstallationBox.SelectedIndex].RootPath;
        ToolTip.SetTip(FolderText, FolderText.Text);
        ResetAnalysis();
    }

    private void BackToGames_OnClick(object? sender, RoutedEventArgs e)
    {
        if (!_busy && TopLevel.GetTopLevel(this) is MainWindow window) window.ShowGames();
    }

    private void ClearProfile_OnClick(object? sender, RoutedEventArgs e) { ProfileBox.SelectedIndex = -1; }

    private async Task RunOperation_Async(Func<Task> operation)
    {
        if (_busy) return;

        _busy = true;
        OperationPanel.IsEnabled = false;
        StatusText.Text = "Working…";

        try
        {
            await operation();
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
        finally
        {
            _busy = false;
            OperationPanel.IsEnabled = true;
        }
    }

    private async Task<string?> PickFile_Async(string title, string pattern)
    {
        if (TopLevel.GetTopLevel(this) is not { } top) return null;

        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType(title) { Patterns = [pattern] }
            ]
        });

        try
        {
            return files.FirstOrDefault()?.TryGetLocalPath();
        }
        finally
        {
            foreach (var file in files) file.Dispose();
        }
    }

    private async void BrowseExecutable_OnClick_Async(object? sender, RoutedEventArgs e)
    {
        await RunOperation_Async(async () =>
        {
            if (await PickFile_Async("Select game executable", "*.exe") is { } path) ExecutableBox.Text = path;
        });
    }

    private async void BrowsePackage_OnClick_Async(object? sender, RoutedEventArgs e)
    {
        await RunOperation_Async(async () =>
        {
            if (TopLevel.GetTopLevel(this) is not { } top) return;

            var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title =
                    "Select extracted OptiScaler package"
            });

            try
            {
                if (folders.FirstOrDefault()?.TryGetLocalPath() is { } path) PackageBox.Text = path;
            }
            finally
            {
                foreach (var folder in folders) folder.Dispose();
            }
        });
    }

    private void ShowPreview(InstallPlan plan)
    {
        _plan = plan;
        PreviewText.Text = $"{plan.Description}\nTarget: {plan.TargetDirectory}\n\n" +
                           string.Join("\n",
                                       plan.Files.Select(f =>
                                                             $"{(f.BeforeHash is null ? "Create" : "Replace")} : {f.RelativePath}"));
        PreviewPanel.IsVisible = true;
        InputPanel.IsEnabled = false;
        IdentityPanel.IsEnabled = false;
        MaintenancePanel.IsEnabled = false;
        VerifyButton.IsEnabled = false;
        StatusText.Text = "Review the preview, then apply or cancel.";
        Dispatcher.UIThread.Post(() => PreviewPanel.BringIntoView(), DispatcherPriority.Loaded);
    }

    private async void PreviewInstall_OnClick_Async(object? sender, RoutedEventArgs e)
    {
        await RunOperation_Async(async () =>
        {
            if (ViewModel is not { } vm) return;

            // Validate the game before spending time downloading its package.
            SafeFiles.RequireX64PeFile(Executable, false);

            if (string.IsNullOrWhiteSpace(PackageBox.Text))
            {
                var releases = await vm.Packages.GetReleases_Async(_channel == "Beta");
                var release = releases.FirstOrDefault() ??
                              throw new InvalidOperationException("No release is available. Choose a local package.");
                PackageBox.Text = await vm.Packages.DownloadPackage_Async(release,
                                                                          new Progress<string>(message =>
                                                                              StatusText.Text = message));
                PackageInfoText.Text = $"{_channel} · {release.Version} · {release.AssetName}";
            }

            var selections = OptionalComponents.Select(item => item.Box.SelectedItem switch
            {
                PackageRelease release => new ComponentInstallSelection(item.Component, release),
                LocalComponent local => new ComponentInstallSelection(
                                                                      item.Component, LocalPath: local.Path,
                                                                      LocalVersion: ReadVersion(local.Path)),
                "Keep existing" => new ComponentInstallSelection(item.Component, KeepExisting: true),
                _ => new ComponentInstallSelection(item.Component)
            }).ToList();
            var plan = await vm.InstallationService.PreviewPackageInstallation_Async(
             Executable, PackageBox.Text.Trim(), ProxyBox.SelectedItem as string ?? "dxgi.dll",
             ProfileBox.SelectedItem as RenderProfile, selections,
             new Progress<string>(message => StatusText.Text = message));

            ShowPreview(plan);
        });
    }

    private async void PreviewProfile_OnClick_Async(object? sender, RoutedEventArgs e)
    {
        await RunOperation_Async(async () =>
        {
            if (ProfileBox.SelectedItem is not RenderProfile profile)
                throw new InvalidOperationException("Select a saved profile first.");

            if (ViewModel is { } vm)
                ShowPreview(await vm.InstallationService.PreviewProfileApplication_Async(Executable, profile));
        });
    }

    private async void PreviewNative_OnClick_Async(object? sender, RoutedEventArgs e)
    {
        await RunOperation_Async(async () =>
        {
            var target = TargetDirectory;
            var destination = await PickFile_Async("Select native DLL in this game", "*.dll");

            if (destination is null) return;

            var relative = Path.GetRelativePath(target, destination);

            if (Path.IsPathRooted(relative) || relative == ".." ||
                relative.StartsWith(".." + Path.DirectorySeparatorChar))
                throw new InvalidOperationException("Select a DLL within the selected game's executable folder.");

            var source = await PickFile_Async("Select replacement DLL", "*.dll");
            if (source is not null && ViewModel is { } vm)
                ShowPreview(await vm.InstallationService.PreviewNativeDllSwap_Async(destination, source));
        });
    }

    private async void Apply_OnClick_Async(object? sender, RoutedEventArgs e)
    {
        await RunOperation_Async(async () =>
        {
            if (_plan is not { } plan || ViewModel is not { } vm) return;

            // Consume the preview even on failure; a retry must inspect the current file state again.
            ClearPreview();
            await vm.InstallationService.ExecuteInstallationPlan_Async(plan);
            await AnalyzeGame_Async();
            var verification = await vm.InstallationService.VerifyInstallation_Async(plan.TargetDirectory);
            StatusText.Text = verification.IsVerified
                ? "Changes applied and verified."
                : "Changes applied; verification needs attention.";
            if (verification.Issues.Count > 0) DetailsText.Text += "\n" + string.Join("\n", verification.Issues);
        });
    }

    private void ClearPreview()
    {
        _plan = null;
        PreviewPanel.IsVisible = false;
        InputPanel.IsEnabled = IdentityPanel.IsEnabled = MaintenancePanel.IsEnabled = VerifyButton.IsEnabled = true;
    }

    private void CancelPreview_OnClick(object? sender, RoutedEventArgs e)
    {
        ClearPreview();
        StatusText.Text = "Preview cancelled.";
    }

    private async void Analyze_OnClick_Async(object? sender, RoutedEventArgs e)
    {
        await RunOperation_Async(AnalyzeGame_Async);
    }

    private async Task AnalyzeGame_Async()
    {
        if (_game is null || ViewModel is not { } vm || InstallationBox.SelectedIndex < 0) return;

        var original = _game.Installations[InstallationBox.SelectedIndex];
        var analysis = await vm.Analyzer.AnalyzeGame_Async(_game.Id, new GameInstallation
        {
            RootPath = original.RootPath,
            PrimaryExecutablePath = string.IsNullOrWhiteSpace(ExecutableBox.Text)
                ? original.PrimaryExecutablePath
                : ExecutableBox.Text.Trim()
        });
        ComponentsList.ItemsSource = analysis.Components
            .DistinctBy(c => (c.Kind, c.Version)).OrderBy(c => c.Kind).ToList();
        EmptyComponentsText.IsVisible = analysis.Components.Count == 0;
        EmptyComponentsText.Text = "No rendering components detected.";
        var inputs = analysis.Components
            .Where(c => c.Kind is ComponentKind.Dlss or ComponentKind.Fsr or ComponentKind.Xess)
            .Select(c => DetectedComponent.GetKindName(c.Kind)).Distinct().ToList();
        InputsText.Text = inputs.Count == 0 ? "None detected" : string.Join(" · ", inputs);
        var antiCheat = analysis.Evidence.Any(e => e.Code == "game.anticheat");
        CompatibilityText.Text = antiCheat ? "Anti-cheat detected" : "Not verified";
        GuidanceText.Text = antiCheat
            ? "Anti-cheat files were found. Rendering modifications should not be installed for this game."
            : string.IsNullOrWhiteSpace(ExecutableBox.Text)
                ? "Select the actual game executable to check its installation and configure OptiScaler."
                : "Select Stable or Beta to fetch releases, or browse to a local package. Then review the planned changes.";
        InstallStateText.Text = analysis.InstallState == InstallState.NotInstalled
            ? "OptiScaler not detected"
            : "Untracked rendering files detected";
        InstallButton.Content = "Preview install";
        UninstallButton.IsEnabled = RestoreButton.IsEnabled = false;
        StatusText.Text = $"Analysis complete · {analysis.Components.Count} detected files.";
        DetailsText.Text =
            string.Join("\n",
                        analysis.Components.Select(c => $"{c.Kind}: {c.Path}")
                            .Concat(analysis.Evidence.Select(e => e.Path is null
                                                                 ? e.Message
                                                                 : $"{e.Message} : {e.Path}")));

        if (!string.IsNullOrWhiteSpace(ExecutableBox.Text))
        {
            var result = await vm.InstallationService.VerifyInstallation_Async(TargetDirectory);
            RestoreButton.IsEnabled = result.Journal is { State: not OperationState.Restored };
            UninstallButton.IsEnabled = result.Journal is
            {
                Kind: OperationKind.InstallOptiscaler, State: OperationState.Installed
            };

            if (result.Journal is { State: not OperationState.Restored })
            {
                InstallStateText.Text = result.IsVerified ? "Managed files verified" : "Installation needs attention";
                InstallButton.Content = "Preview update";
                if (!antiCheat)
                    GuidanceText.Text = result.IsVerified
                        ? "Your managed files match their saved hashes. Apply a profile or select a package to update this installation."
                        : "Review the analysis below. Restore an incomplete operation before making further changes.";
            }

            DetailsText.Text +=
                "\n" + (result.IsVerified ? "Installation verified." : string.Join("\n", result.Issues));
        }
    }

    private async void Restore_OnClick_Async(object? sender, RoutedEventArgs e)
    {
        await RunOperation_Async(async () =>
        {
            if (ViewModel is not { } vm) return;

            await vm.InstallationService.RestoreLatestOperation_Async(TargetDirectory);
            await AnalyzeGame_Async();
            StatusText.Text = "Latest operation restored.";
        });
    }

    private async void History_OnClick_Async(object? sender, RoutedEventArgs e)
    {
        await RunOperation_Async(async () =>
        {
            if (ViewModel is not { } vm) return;

            var target = TargetDirectory;
            var history = (await vm.InstallationService.GetOperationHistory_Async())
                .Where(j => string.Equals(j.TargetDirectory, target,
                                          OperatingSystem.IsWindows()
                                              ? StringComparison.OrdinalIgnoreCase
                                              : StringComparison.Ordinal)).ToList();
            DetailsPanel.IsExpanded = true;
            StatusText.Text = $"{history.Count} operations recorded for this folder.";
            DetailsText.Text = history.Count == 0
                ? "No operations recorded for this folder."
                : string.Join("\n", history.Select(j => $"{j.CreatedAtUtc:g} : {j.Description} : {j.State}"));
        });
    }

    private void UpdateLayoutColumns()
    {
        var narrow = Bounds.Width < 760;
        var wide = Bounds.Width >= 1180;
        OperationPanel.ColumnDefinitions = new ColumnDefinitions(narrow ? "*" : wide ? "180,*,190" : "190,*");
        OperationPanel.RowDefinitions = new RowDefinitions(narrow ? "Auto,Auto,Auto" : "Auto,Auto");
        Grid.SetColumn(ManagementPanel, narrow ? 0 : 1);
        Grid.SetRow(ManagementPanel, narrow ? 1 : 0);
        Grid.SetColumn(GuidancePanel, wide ? 2 : 0);
        Grid.SetRow(GuidancePanel, narrow ? 2 : wide ? 0 : 1);
        Grid.SetColumnSpan(GuidancePanel, !narrow && !wide ? 2 : 1);
        var compactIdentity = narrow && Bounds.Width >= 520;
        IdentityPanel.ColumnDefinitions = new ColumnDefinitions(compactIdentity ? "140,*" : "*");
        IdentityPanel.RowDefinitions = new RowDefinitions(compactIdentity ? "Auto" : "Auto,Auto");
        Grid.SetColumn(IdentityDetails, compactIdentity ? 1 : 0);
        Grid.SetRow(IdentityDetails, compactIdentity ? 0 : 1);
        CoverPanel.Height = narrow ? 180 : wide ? 300 : 270;
        UpdateOptionColumns();
    }

    private void UpdateOptionColumns()
    {
        var columns = OptionsGrid.Bounds.Width >= 660 ? 3 : OptionsGrid.Bounds.Width >= 400 ? 2 : 1;

        if (OptionsGrid.ColumnDefinitions.Count == columns) return;

        OptionsGrid.ColumnDefinitions = new ColumnDefinitions(string.Join(",", Enumerable.Repeat("*", columns)));
        OptionsGrid.RowDefinitions =
            new RowDefinitions(string.Join(",",
                                           Enumerable.Repeat("Auto",
                                                             (OptionsGrid.Children.Count + columns - 1) / columns)));

        for (var i = 0; i < OptionsGrid.Children.Count; i++)
        {
            Grid.SetColumn(OptionsGrid.Children[i], i % columns);
            Grid.SetRow(OptionsGrid.Children[i], i / columns);
        }
    }

    private void LoadCover()
    {
        CoverImage.Source = null;
        _cover?.Dispose();
        _cover = null;
        CoverFallback.IsVisible = true;

        if (_game?.CoverImage is not { } path || !File.Exists(path)) return;

        try
        {
            using var stream = File.OpenRead(path);
            _cover = Bitmap.DecodeToWidth(stream, 480);
            CoverImage.Source = _cover;
            CoverFallback.IsVisible = false;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            CoverFallback.IsVisible = true;
        }
    }

    private void ResetAnalysis()
    {
        if (InstallStateText is null) return;

        ClearPreview();
        InstallStateText.Text = "Ready to verify";
        ComponentsList.ItemsSource = null;
        EmptyComponentsText.IsVisible = true;
        EmptyComponentsText.Text = "Verify to inspect rendering components.";
        CompatibilityText.Text = "Not verified";
        InputsText.Text = "Not checked";
        GuidanceText.Text = "Verify the selected installation to refresh its status and detected components.";
        DetailsText.Text = "";
        UninstallButton.IsEnabled = RestoreButton.IsEnabled = false;
        InstallButton.Content = "Preview install";
        StatusText.Text = "Installation selection changed. Verify to refresh its status.";
    }

    private void Executable_OnTextChanged(object? sender, TextChangedEventArgs e) { ResetAnalysis(); }

    private void Package_OnTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (PackageInfoText is null) return;

        _packages[_channel] = PackageBox.Text?.Trim() ?? "";
        RefreshPackageOptions();
    }

    private void RefreshPackageOptions()
    {
        _refreshingPackage = true;

        try
        {
            StableButton.Classes.Set("primary", _channel == "Stable");
            BetaButton.Classes.Set("primary", _channel == "Beta");
            var folder = PackageBox.Text?.Trim() ?? "";
            var dll = Path.Combine(folder, "OptiScaler.dll");
            var valid = File.Exists(dll) && File.Exists(Path.Combine(folder, "OptiScaler.ini"));
            VersionBox.ItemsSource =
                valid
                    ? new object[] { ReadVersion(dll), "Fetch releases…", "Choose local package…" }
                    : new object[] { "Fetch releases…", "Choose local package…" };
            VersionBox.SelectedIndex = valid ? 0 : -1;
            VersionBox.PlaceholderText = "Fetch releases…";

            foreach (var (box, component) in OptionalComponents)
            {
                var selected = box.SelectedItem;
                var bundled = valid
                    ? Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
                        .FirstOrDefault(p => component.FileNames.Contains(Path.GetFileName(p),
                                                                          StringComparer.OrdinalIgnoreCase)
                                             && !p.EndsWith(".ini", StringComparison.OrdinalIgnoreCase))
                    : null;
                var choices = new List<object> { "Use package bundle", "Keep existing" };
                choices.AddRange(_componentReleases.GetValueOrDefault(component, []));
                if (selected is LocalComponent) choices.Add(selected);
                choices.Add("Choose local file…");
                box.ItemsSource = choices;
                box.SelectedItem = selected is not null && choices.Contains(selected) ? selected : choices[0];
                box.IsEnabled = true;
                ToolTip.SetTip(box, bundled is null
                                   ? "Choose a release, use the package bundle, or keep existing files."
                                   : $"Bundled: {ReadVersion(bundled)}. Choose a release to override it.");
            }

            ExtrasText.Text = valid
                ? "Choose component versions or use the package bundle. Downloads are staged before you review changes."
                : "Fetch a release to download OptiScaler and its bundled components.";
            PackageInfoText.Text =
                valid
                    ? $"{_channel} · {folder}"
                    : "Install downloads the latest release. You can also select a version or browse a local package.";
        }
        finally
        {
            _refreshingPackage = false;
        }
    }

    private static string ReadVersion(string path)
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

    private async void Channel_OnClick(object? sender, RoutedEventArgs e)
    {
        _channel = ReferenceEquals(sender, BetaButton) ? "Beta" : "Stable";
        PackageBox.Text = _packages.GetValueOrDefault(_channel, "");
        RefreshPackageOptions();
        await FetchReleases_Async();
    }

    private async void Version_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_refreshingPackage || _busy || ViewModel is not { } vm) return;

        if (VersionBox.SelectedItem as string == "Choose local package…")
            BrowsePackage_OnClick_Async(sender, e);
        else if (VersionBox.SelectedItem as string == "Fetch releases…")
            await FetchReleases_Async();
        else if (VersionBox.SelectedItem is PackageRelease release)
            await RunOperation_Async(async () =>
            {
                PackageBox.Text = await vm.Packages.DownloadPackage_Async(release,
                                                                          new Progress<string>(message =>
                                                                              StatusText.Text = message));
                PackageInfoText.Text = $"{_channel} · {release.Version} · {release.AssetName}";
                StatusText.Text = "Package downloaded. Review the components, then click Install to preview changes.";
            });
    }

    private Task FetchReleases_Async()
    {
        return RunOperation_Async(async () =>
        {
            if (ViewModel is not { } vm) return;

            _releases = await vm.Packages.GetReleases_Async(_channel == "Beta");
            _refreshingPackage = true;

            try
            {
                VersionBox.ItemsSource = _releases
                    .Concat(new object[] { "Fetch releases…", "Choose local package…" }).ToList();
                VersionBox.SelectedIndex = -1;
                VersionBox.PlaceholderText = "Select release to download";
            }
            finally
            {
                _refreshingPackage = false;
            }

            await FetchComponentReleases_Async();
            StatusText.Text = _releases.Count == 0
                ? "No downloadable releases found. A local package can still be used."
                : "Select a release to download its complete bundle.";
        });
    }

    private async void Component_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_refreshingPackage || _busy || sender is not ComboBox box ||
            box.SelectedItem as string != "Choose local file…")
            return;

        var component = OptionalComponents.First(c => c.Box == box).Component;
        box.SelectedIndex = 0;
        await RunOperation_Async(async () =>
        {
            if (await PickFile_Async($"Select {component.Name} binary",
                                     component == DownloadComponent.OptiPatcher ? "*.asi" : "*.dll") is not { } path)
                return;

            if (!component.FileNames.Contains(Path.GetFileName(path), StringComparer.OrdinalIgnoreCase))
                throw new InvalidDataException("Expected " + string.Join(" or ", component.FileNames));

            SafeFiles.RequireX64PeFile(path, true);
            var local = new LocalComponent(path);
            var choices = box.ItemsSource!.Cast<object>().ToList();
            choices.Insert(2, local);
            box.ItemsSource = choices;
            box.SelectedItem = local;
            StatusText.Text = $"Local {component.Name} selected. Preview install to review changes.";
        });
    }

    private async void RefreshVersions_OnClick_Async(object? sender, RoutedEventArgs e) { await FetchReleases_Async(); }

    private async Task FetchComponentReleases_Async()
    {
        if (ViewModel is not { } vm) return;

        var failures = new List<string>();

        foreach (var (_, component) in OptionalComponents)
            try
            {
                _componentReleases[component] = await vm.Packages.GetComponentReleases_Async(component);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                failures.Add(component.Name);
            }

        // Refresh only component selectors, preserving the OptiScaler release list.
        var versions = VersionBox.ItemsSource;
        var selected = VersionBox.SelectedItem;
        RefreshPackageOptions();
        _refreshingPackage = true;
        VersionBox.ItemsSource = versions;
        VersionBox.SelectedItem = selected;
        VersionBox.PlaceholderText = "Select release to download";
        _refreshingPackage = false;
        if (_componentReleases.TryGetValue(DownloadComponent.Nukem, out var nukem) && nukem.Count == 0)
            ExtrasText.Text =
                "NukemFG has no downloadable binary releases. Use a bundled copy or choose a local DLL; other components can use the versions below.";
        if (failures.Count > 0)
            ExtrasText.Text = "Could not refresh: " + string.Join(", ", failures) +
                              ". Retry with Refresh versions; bundled choices remain available.";
    }

    private void EditGame_OnClick(object? sender, RoutedEventArgs e)
    {
        EditGamePanel.IsVisible = !EditGamePanel.IsVisible;
        if (EditGamePanel.IsVisible) GameNameBox.Focus();
    }

    private async void SaveGame_OnClick_Async(object? sender, RoutedEventArgs e)
    {
        await RunOperation_Async(async () =>
        {
            if (_game is null || ViewModel is not { } vm || InstallationBox.SelectedIndex < 0) return;

            _game = await vm.SaveGameDetails_Async(_game.Id, GameNameBox.Text ?? "",
                                                   _game.Installations[InstallationBox.SelectedIndex].RootPath,
                                                   ExecutableBox.Text?.Trim());
            GameNameText.Text = _game.Name;
            LoadCover();
            EditGamePanel.IsVisible = false;
            StatusText.Text = "Game details saved.";
        });
    }

    private async void OpenFolder_OnClick_Async(object? sender, RoutedEventArgs e)
    {
        await RunOperation_Async(async () =>
        {
            if (_game is null || InstallationBox.SelectedIndex < 0 || TopLevel.GetTopLevel(this) is not { } top) return;

            var path = _game.Installations[InstallationBox.SelectedIndex].RootPath;
            using var folder = await top.StorageProvider.TryGetFolderFromPathAsync(path);

            if (folder is null || !await top.Launcher.LaunchFileAsync(folder))
                throw new IOException("Could not open the game folder.");

            StatusText.Text = "Game folder opened.";
        });
    }

    private sealed record LocalComponent(string Path)
    {
        public override string ToString() { return $"Local · {ReadVersion(Path)}"; }
    }
}