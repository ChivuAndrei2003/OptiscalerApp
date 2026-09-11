using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Optiscaler.Core.Games;
using OptiscalerApp.ViewModels;

namespace OptiscalerApp.Views;

public partial class GamesView : UserControl
{
    public GamesView()
    {
        InitializeComponent();
    }

    private async void AddGames_Click_Async(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel || !viewModel.CanAddGames)
            return;

        var topLevel = TopLevel.GetTopLevel(this);

        if (topLevel is null) return;

        try
        {
            var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Select game folders",
                AllowMultiple = true
            });

            try
            {
                if (folders.Count == 0) return;

                var paths = folders.Select(folder => folder.TryGetLocalPath()).OfType<string>().ToArray();

                if (paths.Length != folders.Count)
                {
                    viewModel.StatusMessage = "Select folders available on this computer.";

                    return;
                }

                await viewModel.AddManualGames_Async(paths);
            }
            finally
            {
                foreach (var folder in folders) folder.Dispose();
            }
        }
        catch (Exception exception)
        {
            viewModel.StatusMessage = $"Could not open the selected folders: {exception.Message}";
        }
    }

    private async void ScanGames_OnClick_Async(object? sender, RoutedEventArgs e)
    { if (DataContext is MainWindowViewModel vm) await vm.ScanGameLibrary_Async(); }

    private void ManageGame_OnClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.DataContext is GameRecord game &&
            TopLevel.GetTopLevel(this) is MainWindow mainWindow)
            mainWindow.ShowManageGame(game);
    }
}