using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Optiscaler.Core.Games;
using OptiscalerApp.ViewModels;

namespace OptiscalerApp.Views;

public partial class MainWindow : Window
{
    //private bool _sidebarExpanded;

    private enum AppPage
    {
        Games,
        Profiles,
        Settings,
        GameManager
    }

    public MainWindow()
    {
        InitializeComponent();
        ShowGames();
        Opened += async (_, _) =>
        {
            if (DataContext is MainWindowViewModel viewModel)
            {
                await viewModel.LoadGameLibrary_Async();
                try { if ((await viewModel.LoadConfiguration_Async()).AutoScan) await viewModel.ScanGameLibrary_Async(); }
                catch (Exception ex) { viewModel.StatusMessage = $"Could not load settings: {ex.Message}"; }
            }
        };
    }

    public void ShowManageGame(GameRecord game)
    {
        PageContent.Content = new ManageGameView(game);
        SetActivePage(AppPage.GameManager);
    }

    public void ShowGames()
    {
        PageContent.Content = new GamesView();
        SetActivePage(AppPage.Games);
    }

    // private void Button_OnClick(object? sender, RoutedEventArgs e)
    // {
    //     Console.WriteLine("hello world");
    // }

    // private void ToggleSidebar_Click(object? sender, RoutedEventArgs e)
    // {
    //     _sidebarExpanded = !_sidebarExpanded;
    //
    //     Sidebar.Width = _sidebarExpanded ? 220 : 70;
    // }

    private void SettingsButton_OnClick(object? sender, RoutedEventArgs e)
    {
        PageContent.Content = new SettingsView();
        SetActivePage(AppPage.Settings);
    }

    private void ProfilesButton_OnClick(object? sender, RoutedEventArgs e)
    {
        PageContent.Content = new ProfilesView { DataContext = (DataContext as MainWindowViewModel)?.Profiles };
        SetActivePage(AppPage.Profiles);
    }

    private void GamesButton_OnClick(object? sender, RoutedEventArgs e)
    {
        ShowGames();
    }

    private void ToggleSidebar_Click(object? sender, RoutedEventArgs e)
    {
        MainSplitView.IsPaneOpen = !MainSplitView.IsPaneOpen;
    }

    private void SetActivePage(AppPage page)
    {
        GamesNavButton.Classes.Set("active", page is AppPage.Games or AppPage.GameManager);
        ProfilesNavButton.Classes.Set("active", page == AppPage.Profiles);
        SettingsNavButton.Classes.Set("active", page == AppPage.Settings);
        ToolbarHost.IsVisible = page == AppPage.Games;
    }

}
