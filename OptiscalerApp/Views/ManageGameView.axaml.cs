using System.ComponentModel;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using OptiscalerApp.Management;
using OptiscalerApp.ViewModels;

namespace OptiscalerApp.Views;

/// <summary>Hosts <see cref="ManageGameViewModel" />; owns only layout, pickers, and navigation.</summary>
public partial class ManageGameView : UserControl, IFileDialogs, IShellActions
{
    private ManageGameViewModel? _viewModel;

    public ManageGameView()
    {
        InitializeComponent();
        SizeChanged += (_, _) => UpdateLayoutColumns();
        OptionsGrid.SizeChanged += (_, _) => UpdateOptionColumns();
        Loaded += async (_, _) =>
        {
            if (_viewModel is not null) await _viewModel.LoadCommand.ExecuteAsync(null);
        };
    }

    public async Task<string?> PickFile_Async(string title, string pattern)
    {
        if (TopLevel.GetTopLevel(this) is not { } top) return null;

        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType(title) { Patterns = [pattern] }]
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

    public async Task<string?> PickFolder_Async(string title)
    {
        if (TopLevel.GetTopLevel(this) is not { } top) return null;

        var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title });

        try
        {
            return folders.FirstOrDefault()?.TryGetLocalPath();
        }
        finally
        {
            foreach (var folder in folders) folder.Dispose();
        }
    }

    public async Task SetClipboardText_Async(string text)
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard)
            throw new InvalidOperationException("The clipboard is unavailable.");

        await clipboard.SetTextAsync(text);
    }

    public async Task<bool> Open_Async(LaunchTarget target)
    {
        if (target.Uri is { } uri)
            return TopLevel.GetTopLevel(this) is { } top && await top.Launcher.LaunchUriAsync(uri);

        if (target.Executable is not { } executable) return false;

        // Games often load data relative to their own folder.
        using var process = Process.Start(new ProcessStartInfo(executable)
        {
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(executable)
        });

        return process is not null;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= ViewModel_OnPropertyChanged;
            _viewModel.Dialogs = null;
            _viewModel.Shell = null;
        }

        _viewModel = DataContext as ManageGameViewModel;

        if (_viewModel is null) return;

        _viewModel.Dialogs = this;
        _viewModel.Shell = this;
        _viewModel.PropertyChanged += ViewModel_OnPropertyChanged;
    }

    private void ViewModel_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ManageGameViewModel.IsPreviewing) && _viewModel?.IsPreviewing == true)
            Dispatcher.UIThread.Post(() => PreviewPanel.BringIntoView(), DispatcherPriority.Loaded);
        else if (e.PropertyName == nameof(ManageGameViewModel.IsEditingDetails) &&
                 _viewModel?.IsEditingDetails == true)
            GameNameBox.Focus();
    }

    private void BackToGames_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel?.IsBusy != true && TopLevel.GetTopLevel(this) is MainWindow window) window.ShowGames();
    }

    private async void OpenFolder_OnClick_Async(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is not { FolderPath.Length: > 0 } vm || TopLevel.GetTopLevel(this) is not { } top) return;

        try
        {
            using var folder = await top.StorageProvider.TryGetFolderFromPathAsync(vm.FolderPath);

            vm.Status = folder is not null && await top.Launcher.LaunchFileAsync(folder)
                ? "Game folder opened."
                : "Could not open the game folder.";
        }
        catch (Exception ex)
        {
            vm.Status = $"Could not open the game folder: {ex.Message}";
        }
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
}
