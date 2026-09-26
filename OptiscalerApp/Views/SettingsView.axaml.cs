using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using OptiscalerApp.Management;
using OptiscalerApp.Models;
using OptiscalerApp.ViewModels;

namespace OptiscalerApp.Views;

public partial class SettingsView : UserControl
{
    private AppConfiguration? _configuration;

    public SettingsView()
    {
        InitializeComponent();
        ProxyBox.ItemsSource = GameInstallationService.ProxyNames;
        Loaded += async (_, _) =>
        {
            if (DataContext is not MainWindowViewModel vm) return;

            DataFolderText.Text = vm.DataDirectory;

            try
            {
                _configuration = await vm.LoadConfiguration_Async();
                AutoScanBox.IsChecked = _configuration.AutoScan;
                CustomFoldersBox.Text =
                    string.Join(Environment.NewLine, _configuration.ScanSourceSettings.CustomFolders);
                AllowedRootsBox.Text =
                    string.Join(Environment.NewLine, _configuration.ScanSourceSettings.AllowedDriveRoots);
                PlatformsPanel.Children.Clear();
                foreach (var platform in Enum.GetValues<GamePlatform>().Where(p => p != GamePlatform.Manual))
                    PlatformsPanel.Children.Add(new CheckBox
                    {
                        Content = platform,
                        Tag = platform,
                        Margin = new Thickness(0, 0, 16, 8),
                        IsChecked = _configuration.ScanSourceSettings.EnabledPlatforms
                            .Contains(platform)
                    });
                ChannelBox.SelectedIndex = _configuration.PreferBetaReleases ? 1 : 0;
                ProxyBox.SelectedItem = _configuration.DefaultProxyDll;
                SettingsPanel.IsEnabled = true;
                StatusText.Text = "Choose the platforms and folders to scan, and the defaults used for new installs.";
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Could not load settings: {ex.Message}";
            }

            await RefreshCacheSize_Async(vm);
        };
    }

    private static List<string> ReadPaths(string? text)
    {
        return (text ?? "").Split(['\r', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Distinct().ToList();
    }

    private async void SaveSettings_OnClick_Async(object? sender, RoutedEventArgs e)
    {
        if (_configuration is null || DataContext is not MainWindowViewModel vm) return;

        SettingsPanel.IsEnabled = false;

        try
        {
            var updated = new AppConfiguration
            {
                AutoScan = AutoScanBox.IsChecked == true,
                PreferBetaReleases = ChannelBox.SelectedIndex == 1,
                DefaultProxyDll = ProxyBox.SelectedItem as string ?? GameInstallationService.ProxyNames[0],
                ScanSourceSettings = new ScanSourceSettings
                {
                    EnabledPlatforms = PlatformsPanel.Children.OfType<CheckBox>().Where(c => c.IsChecked == true)
                        .Select(c => (GamePlatform)c.Tag!).ToHashSet(),
                    CustomFolders = ReadPaths(CustomFoldersBox.Text),
                    AllowedDriveRoots = ReadPaths(AllowedRootsBox.Text)
                }
            };
            await vm.SaveConfiguration_Async(updated);
            _configuration = updated;
            StatusText.Text = "Settings saved.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Could not save settings: {ex.Message}";
        }
        finally
        {
            SettingsPanel.IsEnabled = true;
        }
    }

    private async void OpenDataFolder_OnClick_Async(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm || TopLevel.GetTopLevel(this) is not { } top) return;

        try
        {
            Directory.CreateDirectory(vm.DataDirectory);
            if (!await top.Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(vm.DataDirectory)))
                StatusText.Text = "The data folder could not be opened.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Could not open the data folder: {ex.Message}";
        }
    }

    private async void ClearCache_OnClick_Async(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;

        ClearCacheButton.IsEnabled = false;

        try
        {
            var skipped = await vm.ClearPackageCache_Async();
            StatusText.Text = skipped == 0
                ? "Downloaded packages cleared."
                : $"Downloaded packages cleared; {skipped} in use were kept.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Could not clear downloads: {ex.Message}";
        }

        await RefreshCacheSize_Async(vm);
    }

    private async Task RefreshCacheSize_Async(MainWindowViewModel vm)
    {
        try
        {
            var size = await vm.GetPackageCacheSize_Async();
            CacheSizeText.Text = size == 0 ? "No packages downloaded." : $"{FormatSize(size)} in use.";
            ClearCacheButton.IsEnabled = size > 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CacheSizeText.Text = $"Could not read the download cache: {ex.Message}";
            ClearCacheButton.IsEnabled = true;
        }
    }

    private static string FormatSize(long bytes)
    {
        return bytes switch
        {
            >= 1L << 30 => $"{bytes / (double)(1L << 30):0.0} GB",
            >= 1L << 20 => $"{bytes / (double)(1L << 20):0.0} MB",
            _ => $"{Math.Max(1, bytes / 1024)} KB"
        };
    }
}
