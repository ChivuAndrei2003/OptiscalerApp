using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Optiscaler.Core.Games;
using Optiscaler.Core.Management;
using Optiscaler.Infrastructure.Management;
using OptiscalerApp.ViewModels;

namespace OptiscalerApp.Views;

public partial class ManageGameView : UserControl
{
    private GameRecord? _game;
    private InstallPlan? _plan;
    private bool _busy;
    private MainWindowViewModel? ViewModel => DataContext as MainWindowViewModel;
    public ManageGameView()
    {
        InitializeComponent();
        ProxyBox.ItemsSource = GameInstallationService.ProxyNames;
        ProxyBox.SelectedIndex = 0;
        Loaded += async (_, _) => await RunOperation_Async(async () =>
        {
            if (ViewModel is not { } vm) return;
            // Read the full saved catalog independently of the Profiles page's search filter.
            var profiles = await vm.LoadProfileCatalog_Async();
            ProfileBox.ItemsSource = profiles.Profiles;
            ProfileBox.SelectedItem = profiles.Profiles.FirstOrDefault(p => p.Id == profiles.DefaultProfileId);
            await AnalyzeGame_Async();
        });
    }
    public ManageGameView(GameRecord game) : this()
    {
        _game = game;
        GameNameText.Text = game.Name;
        InstallationBox.ItemsSource = game.Installations.Select(i => i.RootPath).ToList();
        InstallationBox.SelectedIndex = game.Installations.Count > 0 ? 0 : -1;
    }
    private void Installation_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_game is null || InstallationBox.SelectedIndex < 0) return;
        ExecutableBox.Text = _game.Installations[InstallationBox.SelectedIndex].PrimaryExecutablePath ?? "";
        DetailsText.Text = "";
        StatusText.Text = "Choose an executable to manage this installation.";
    }
    private void BackToGames_OnClick(object? sender, RoutedEventArgs e)
    { if (!_busy && TopLevel.GetTopLevel(this) is MainWindow window) window.ShowGames(); }
    private void ClearProfile_OnClick(object? sender, RoutedEventArgs e) => ProfileBox.SelectedIndex = -1;
    private string Executable => !string.IsNullOrWhiteSpace(ExecutableBox.Text) ? ExecutableBox.Text.Trim() : throw new InvalidOperationException("Select the game executable first.");
    private string TargetDirectory => Path.GetDirectoryName(Path.GetFullPath(Executable)) ?? throw new InvalidOperationException("Invalid executable path.");
    private async Task RunOperation_Async(Func<Task> operation)
    {
        if (_busy) return;
        _busy = true;
        OperationPanel.IsEnabled = false;
        try { await operation(); }
        catch (Exception ex) { StatusText.Text = ex.Message; }
        finally { _busy = false; OperationPanel.IsEnabled = true; }
    }
    private async Task<string?> PickFile_Async(string title, string pattern)
    {
        if (TopLevel.GetTopLevel(this) is not { } top) return null;
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        { Title = title, AllowMultiple = false, FileTypeFilter = [new FilePickerFileType(title) { Patterns = [pattern] }] });
        try { return files.FirstOrDefault()?.TryGetLocalPath(); }
        finally { foreach (var file in files) file.Dispose(); }
    }
    private async void BrowseExecutable_OnClick_Async(object? sender, RoutedEventArgs e) => await RunOperation_Async(async () =>
    { if (await PickFile_Async("Select game executable", "*.exe") is { } path) ExecutableBox.Text = path; });
    private async void BrowsePackage_OnClick_Async(object? sender, RoutedEventArgs e) => await RunOperation_Async(async () =>
    {
        if (TopLevel.GetTopLevel(this) is not { } top) return;
        var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Select extracted OptiScaler package" });
        try { if (folders.FirstOrDefault()?.TryGetLocalPath() is { } path) PackageBox.Text = path; }
        finally { foreach (var folder in folders) folder.Dispose(); }
    });
    private void ShowPreview(InstallPlan plan)
    {
        _plan = plan;
        PreviewText.Text = $"{plan.Description}\nTarget: {plan.TargetDirectory}\n\n" + string.Join("\n", plan.Files.Select(f => $"{(f.BeforeHash is null ? "Create" : "Replace")} : {f.RelativePath}"));
        PreviewPanel.IsVisible = true;
        InputPanel.IsEnabled = false;
        StatusText.Text = "Review the preview, then apply or cancel.";
        Dispatcher.UIThread.Post(() => PreviewPanel.BringIntoView(), DispatcherPriority.Loaded);
    }
    private async void PreviewInstall_OnClick_Async(object? sender, RoutedEventArgs e) => await RunOperation_Async(async () =>
    {
        if (ViewModel is { } vm) ShowPreview(await vm.InstallationService.PreviewInstallation_Async(Executable, PackageBox.Text?.Trim() ?? "", ProxyBox.SelectedItem as string ?? "dxgi.dll", ProfileBox.SelectedItem as RenderProfile));
    });
    private async void PreviewProfile_OnClick_Async(object? sender, RoutedEventArgs e) => await RunOperation_Async(async () =>
    {
        if (ProfileBox.SelectedItem is not RenderProfile profile) throw new InvalidOperationException("Select a saved profile first.");
        if (ViewModel is { } vm) ShowPreview(await vm.InstallationService.PreviewProfileApplication_Async(Executable, profile));
    });
    private async void PreviewNative_OnClick_Async(object? sender, RoutedEventArgs e) => await RunOperation_Async(async () =>
    {
        var target = TargetDirectory;
        var destination = await PickFile_Async("Select native DLL in this game", "*.dll");
        if (destination is null) return;
        var relative = Path.GetRelativePath(target, destination);
        if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar))
            throw new InvalidOperationException("Select a DLL within the selected game's executable folder.");
        var source = await PickFile_Async("Select replacement DLL", "*.dll");
        if (source is not null && ViewModel is { } vm) ShowPreview(await vm.InstallationService.PreviewNativeDllSwap_Async(destination, source));
    });
    private async void Apply_OnClick_Async(object? sender, RoutedEventArgs e) => await RunOperation_Async(async () =>
    {
        if (_plan is not { } plan || ViewModel is not { } vm) return;
        // Consume the preview even on failure; a retry must inspect the current file state again.
        ClearPreview();
        await vm.InstallationService.ExecuteInstallationPlan_Async(plan);
        var verification = await vm.InstallationService.VerifyInstallation_Async(plan.TargetDirectory);
        StatusText.Text = verification.IsVerified ? "Changes applied and verified." : "Changes applied; verification needs attention.";
        DetailsText.Text = string.Join("\n", verification.Issues);
    });
    private void ClearPreview() { _plan = null; PreviewPanel.IsVisible = false; InputPanel.IsEnabled = true; }
    private void CancelPreview_OnClick(object? sender, RoutedEventArgs e) { ClearPreview(); StatusText.Text = "Preview cancelled."; }
    private async void Analyze_OnClick_Async(object? sender, RoutedEventArgs e) => await RunOperation_Async(AnalyzeGame_Async);
    private async Task AnalyzeGame_Async()
    {
        if (_game is null || ViewModel is not { } vm || InstallationBox.SelectedIndex < 0) return;
        var original = _game.Installations[InstallationBox.SelectedIndex];
        var analysis = await vm.Analyzer.AnalyzeGame_Async(_game.Id, new GameInstallation
        { RootPath = original.RootPath, PrimaryExecutablePath = string.IsNullOrWhiteSpace(ExecutableBox.Text) ? original.PrimaryExecutablePath : ExecutableBox.Text.Trim() });
        StatusText.Text = $"Installation: {analysis.InstallState}. Detected {analysis.Components.Count} components.";
        DetailsText.Text = string.Join("\n", analysis.Components.Select(c => $"{c.Kind}: {c.Path}").Concat(analysis.Evidence.Select(e => e.Path is null ? e.Message : $"{e.Message} : {e.Path}")));
        if (!string.IsNullOrWhiteSpace(ExecutableBox.Text))
        {
            var result = await vm.InstallationService.VerifyInstallation_Async(TargetDirectory);
            if (result.IsVerified) StatusText.Text = $"Installation verified. Detected {analysis.Components.Count} components.";
            DetailsText.Text += "\n" + (result.IsVerified ? "Installation verified." : string.Join("\n", result.Issues));
        }
    }
    private async void Restore_OnClick_Async(object? sender, RoutedEventArgs e) => await RunOperation_Async(async () =>
    {
        if (ViewModel is not { } vm) return;
        await vm.InstallationService.RestoreLatestOperation_Async(TargetDirectory);
        await AnalyzeGame_Async();
        StatusText.Text = "Latest operation restored.";
    });
    private async void History_OnClick_Async(object? sender, RoutedEventArgs e) => await RunOperation_Async(async () =>
    {
        if (ViewModel is not { } vm) return;
        var target = TargetDirectory;
        var history = (await vm.InstallationService.GetOperationHistory_Async()).Where(j => string.Equals(j.TargetDirectory, target, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)).ToList();
        DetailsText.Text = history.Count == 0 ? "No operations recorded for this folder." : string.Join("\n", history.Select(j => $"{j.CreatedAtUtc:g} : {j.Description} : {j.State}"));
    });
}
