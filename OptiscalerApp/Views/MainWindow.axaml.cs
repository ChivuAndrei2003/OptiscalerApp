using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace OptiscalerApp.Views;

public partial class MainWindow : Window
{
    //private bool _sidebarExpanded;

    public MainWindow()
    {
        InitializeComponent();
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
    }

    private void FavoritesButton_OnClick(object? sender, RoutedEventArgs e)
    {
        PageContent.Content = new FavoritesView();
    }

    private void GamesButton_OnClick(object? sender, RoutedEventArgs e)
    {
        PageContent.Content = new GamesView();
    }

    private void ToggleSidebar_Click(object? sender, RoutedEventArgs e)
    {
        MainSplitView.IsPaneOpen = !MainSplitView.IsPaneOpen;
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
}