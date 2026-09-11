using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Optiscaler.Core.Configuration;
using Optiscaler.Core.Games;
using OptiscalerApp.ViewModels;

namespace OptiscalerApp.Views;

public partial class SettingsView : UserControl
{
    private AppConfiguration? _configuration;
    public SettingsView()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            if (DataContext is not MainWindowViewModel vm) return;
            try
            {
                _configuration = await vm.LoadConfiguration_Async();
                AutoScanBox.IsChecked = _configuration.AutoScan;
                CustomFoldersBox.Text = string.Join(Environment.NewLine, _configuration.ScanSourceSettings.CustomFolders);
                AllowedRootsBox.Text = string.Join(Environment.NewLine, _configuration.ScanSourceSettings.AllowedDriveRoots);
                PlatformsPanel.Children.Clear();
                foreach (var platform in Enum.GetValues<GamePlatform>().Where(p => p != GamePlatform.Manual))
                    PlatformsPanel.Children.Add(new CheckBox { Content = platform, Tag = platform, Margin = new Thickness(0, 0, 16, 8), IsChecked = _configuration.ScanSourceSettings.EnabledPlatforms.Contains(platform) });
                SettingsPanel.IsEnabled = true;
                StatusText.Text = "Choose the platforms and folders to scan.";
            }
            catch (Exception ex) { StatusText.Text = $"Could not load settings: {ex.Message}"; }
        };
    }
    private static List<string> ReadPaths(string? text) => (text ?? "").Split(['\r', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Distinct().ToList();
    private async void SaveSettings_OnClick_Async(object? sender, RoutedEventArgs e)
    {
        if (_configuration is not { } previous || DataContext is not MainWindowViewModel vm) return;
        SettingsPanel.IsEnabled = false;
        try
        {
            var updated = new AppConfiguration
            {
                Language = previous.Language, AnimationEnabled = previous.AnimationEnabled, PreferGridView = previous.PreferGridView,
                AutoScan = AutoScanBox.IsChecked == true,
                ScanSourceSettings = new ScanSourceSettings
                {
                    EnabledPlatforms = PlatformsPanel.Children.OfType<CheckBox>().Where(c => c.IsChecked == true).Select(c => (GamePlatform)c.Tag!).ToHashSet(),
                    CustomFolders = ReadPaths(CustomFoldersBox.Text), AllowedDriveRoots = ReadPaths(AllowedRootsBox.Text)
                }
            };
            await vm.SaveConfiguration_Async(updated);
            _configuration = updated;
            StatusText.Text = "Settings saved.";
        }
        catch (Exception ex) { StatusText.Text = $"Could not save settings: {ex.Message}"; }
        finally { SettingsPanel.IsEnabled = true; }
    }
}
