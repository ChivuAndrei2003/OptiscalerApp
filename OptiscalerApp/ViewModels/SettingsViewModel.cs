using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OptiscalerApp.Management;
using OptiscalerApp.Models;
using OptiscalerApp.Paths;
using OptiscalerApp.Persistence;

namespace OptiscalerApp.ViewModels;

/// <summary>A launcher that discovery can scan, switched on or off in Settings.</summary>
public sealed partial class PlatformOption(GamePlatform platform) : ObservableObject
{
    [ObservableProperty] private bool _isEnabled;

    public GamePlatform Platform { get; } = platform;

    public string Name => Platform.DisplayName();
}

public sealed partial class SettingsViewModel(
    IAppConfigurationRepository repository,
    PackageDownloadService packages,
    IAppPaths paths,
    IShellActions shell) : ViewModelBase
{
    [ObservableProperty] private string _allowedRoots = "";

    [ObservableProperty] private bool _autoScan;

    [ObservableProperty] private string _cacheSizeText = "Calculating…";

    [ObservableProperty] private bool _canClearCache;

    [ObservableProperty] private string _customFolders = "";

    [ObservableProperty] private bool _isLoaded;

    [ObservableProperty] private bool _preferBetaReleases;

    [ObservableProperty] private string _selectedProxy = OptiscalerFiles.ProxyNames[0];

    [ObservableProperty] private string _statusText = "";

    public string DataDirectory => paths.RootDirectory;

    public IReadOnlyList<string> ProxyNames => OptiscalerFiles.ProxyNames;

    public IReadOnlyList<string> Channels { get; } = ["Stable", "Beta (pre-releases)"];

    public int ChannelIndex
    {
        get => PreferBetaReleases ? 1 : 0;
        set => PreferBetaReleases = value == 1;
    }

    public IReadOnlyList<PlatformOption> Platforms { get; } =
        Enum.GetValues<GamePlatform>().Where(p => p != GamePlatform.Manual).Select(p => new PlatformOption(p)).ToList();

    partial void OnPreferBetaReleasesChanged(bool value) { OnPropertyChanged(nameof(ChannelIndex)); }

    [RelayCommand]
    private async Task Load()
    {
        try
        {
            var configuration = await repository.LoadAppConfigurationAsync();
            var sources = configuration.ScanSourceSettings;
            AutoScan = configuration.AutoScan;
            CustomFolders = string.Join(Environment.NewLine, sources.CustomFolders);
            AllowedRoots = string.Join(Environment.NewLine, sources.AllowedDriveRoots);
            foreach (var option in Platforms) option.IsEnabled = sources.EnabledPlatforms.Contains(option.Platform);
            PreferBetaReleases = configuration.PreferBetaReleases;
            SelectedProxy = configuration.DefaultProxyDll;
            IsLoaded = true;
            StatusText = "Choose the platforms and folders to scan, and the defaults used for new installs.";
        }
        catch (Exception ex) when (IsStorageError(ex))
        {
            StatusText = $"Could not load settings: {ex.Message}";
        }

        await RefreshCacheSizeAsync();
    }

    [RelayCommand]
    private async Task Save()
    {
        if (!IsLoaded) return;

        IsLoaded = false;

        try
        {
            await repository.SaveAppConfigurationAsync(new AppConfiguration
            {
                AutoScan = AutoScan,
                PreferBetaReleases = PreferBetaReleases,
                DefaultProxyDll = SelectedProxy,

                ScanSourceSettings = new ScanSourceSettings
                {
                    EnabledPlatforms = Platforms.Where(p => p.IsEnabled).Select(p => p.Platform).ToHashSet(),
                    CustomFolders = ReadPaths(CustomFolders),
                    AllowedDriveRoots = ReadPaths(AllowedRoots)
                }
            });
            StatusText = "Settings saved.";
        }
        catch (Exception ex) when (IsStorageError(ex))
        {
            StatusText = $"Could not save settings: {ex.Message}";
        }
        finally
        {
            IsLoaded = true;
        }
    }

    [RelayCommand]
    private async Task OpenDataFolder()
    {
        try
        {
            Directory.CreateDirectory(DataDirectory);
            if (!await shell.OpenFolderAsync(DataDirectory)) StatusText = "The data folder could not be opened.";
        }
        catch (Exception ex) when (IsStorageError(ex) || ex is InvalidOperationException)
        {
            StatusText = $"Could not open the data folder: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ClearCache()
    {
        CanClearCache = false;

        try
        {
            // Release lists are forgotten too, so the next fetch is fresh.
            packages.ClearReleaseLists();
            var skipped = await Task.Run(packages.ClearCache);
            StatusText = skipped == 0
                ? "Downloaded packages cleared."
                : $"Downloaded packages cleared; {skipped} in use were kept.";
        }
        catch (Exception ex) when (IsStorageError(ex))
        {
            StatusText = $"Could not clear downloads: {ex.Message}";
        }

        await RefreshCacheSizeAsync();
    }

    private async Task RefreshCacheSizeAsync()
    {
        try
        {
            var size = await Task.Run(packages.GetCacheSize);
            CacheSizeText = size == 0 ? "No packages downloaded." : $"{FormatSize(size)} in use.";
            CanClearCache = size > 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CacheSizeText = $"Could not read the download cache: {ex.Message}";
            CanClearCache = true;
        }
    }

    private static List<string> ReadPaths(string text)
    {
        return text.Split(['\r', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Distinct().ToList();
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
