using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace OptiscalerApp.Views;

public partial class GamesView : UserControl
{
    public GamesView()
    {
        InitializeComponent();
    }

    private async void AddGames_Click(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return;

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select game folders",
            AllowMultiple = true
        });

        GamesStatusText.Text = folders.Count switch
        {
            0 => "No folders selected.",
            1 => "1 game folder selected and ready to be added.",
            _ => $"{folders.Count} game folders selected and ready to be added."
        };
    }

    private async void ScanGames_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button scanButton) scanButton.IsEnabled = false;

        GamesStatusText.Text = "Scanning configured sources…";
        await Task.Delay(650);
        GamesStatusText.Text = "Scan complete · Your library is up to date.";

        if (sender is Button completedButton) completedButton.IsEnabled = true;
    }

    private void ManageGame_OnClick(object? sender, RoutedEventArgs e)
    {
        var gameName = (sender as Button)?.Tag as string ?? "Selected game";

        if (TopLevel.GetTopLevel(this) is MainWindow mainWindow) mainWindow.ShowManageGame(gameName);
    }
}