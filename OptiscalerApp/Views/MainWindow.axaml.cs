using System;
using Avalonia.Controls;
using Avalonia.Input;
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
                await viewModel.LoadGameLibrary_Async();
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
        PageContent.Content = new ProfilesView();
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

    private void BtnScan_Click(object? sender, RoutedEventArgs e)
    {
        throw new NotImplementedException();
    }

    private void BtnAddManual_Click(object? sender, RoutedEventArgs e)
    {
        throw new NotImplementedException();
    }

    private void BtnBulkInstall_Click(object? sender, RoutedEventArgs e)
    {
        throw new NotImplementedException();
    }

    private void TxtSearch_LostFocus(object? sender, FocusChangedEventArgs e)
    {
        throw new NotImplementedException();
    }

    private void TxtSearch_GotFocus(object? sender, FocusChangedEventArgs e)
    {
        throw new NotImplementedException();
    }

    private void TxtSearch_TextChanged(object? sender, TextChangedEventArgs e)
    {
        throw new NotImplementedException();
    }

    private void BtnViewGrid_Click(object? sender, RoutedEventArgs e)
    {
        throw new NotImplementedException();
    }

    private void BtnViewList_Click(object? sender, RoutedEventArgs e)
    {
        throw new NotImplementedException();
    }

    private void BtnEditMode_Click(object? sender, RoutedEventArgs e)
    {
        throw new NotImplementedException();
    }

    private void BtnEditModeDone_Click(object? sender, RoutedEventArgs e)
    {
        throw new NotImplementedException();
    }

    private void AddGames_Click(object? sender, RoutedEventArgs e)
    {
        Console.WriteLine("Add Games is not implemented yet.");
    }

    private void ScanGames_Click(object? sender, RoutedEventArgs e)
    {
        Console.WriteLine("Scan Games is not implemented yet.");
    }
}