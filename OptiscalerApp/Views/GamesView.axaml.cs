using System;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace OptiscalerApp.Views;

public partial class GamesView : UserControl
{
    public GamesView()
    {
        InitializeComponent();
    }

    private void AddGames_Click(object? sender, RoutedEventArgs e)
    {
        throw new NotImplementedException();
    }

    private void ScanGames_Click(object? sender, RoutedEventArgs e)
    {
        throw new NotImplementedException();
    }

    private void ManageGame_OnClick(object? sender, RoutedEventArgs e)
    {
        var gameName = (sender as Button)?.Tag as string ?? "Selected game";

        if (TopLevel.GetTopLevel(this) is MainWindow mainWindow)
        {
            mainWindow.ShowManageGame(gameName);
        }
    }
}
