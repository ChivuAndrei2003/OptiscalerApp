using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Optiscaler.Core.Abstractions;
using Optiscaler.Core.Games;
using Optiscaler.Core.Configuration;
using Optiscaler.Core.Scanning;
using Optiscaler.Core.Management;
using Optiscaler.Infrastructure.Scanning;

namespace OptiscalerApp.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly IGameCatalogRepository _gameCatalogRepository;
    private GameCatalog _catalog = new();

    private readonly IAppConfigurationRepository _configurationRepository;
    private readonly GameDiscoveryCoordinator _discovery;
    public ProfilesViewModel Profiles { get; }
    private readonly IProfileRepository _profileRepository;
    public Task<ProfileCatalog> LoadProfileCatalog_Async() => _profileRepository.LoadProfileCatalog_Async();
    public IGameAnalyzer Analyzer { get; }
    public IGameInstallationService InstallationService { get; }
    public MainWindowViewModel(IGameCatalogRepository gameCatalogRepository,
        IAppConfigurationRepository configurationRepository, GameDiscoveryCoordinator discovery,
        ProfilesViewModel profiles, IGameAnalyzer analyzer, IGameInstallationService installationService, IProfileRepository profileRepository)
    {
        _gameCatalogRepository = gameCatalogRepository;
        _configurationRepository = configurationRepository;
        _discovery = discovery;
        Profiles = profiles;
        _profileRepository = profileRepository;
        Analyzer = analyzer;
        InstallationService = installationService;
    }

    public async Task ScanGameLibrary_Async()
    {
        if (!CanAddGames) return;
        IsBusy = true;
        StatusMessage = "Scanning game sources…";
        try
        {
            var settings = await _configurationRepository.LoadAppConfiguration_Async();
            var result = await _discovery.ScanGames_Async(ScanContext.FromSettings(settings.ScanSourceSettings));
            // Clone mutable records so a failed save cannot alter the currently published catalog.
            var games = _catalog.Games.Select(g => new GameRecord
            {
                Id = g.Id, Name = g.Name, Platform = g.Platform, ExternalId = g.ExternalId,
                Installations = g.Installations.ToList(), Preferences = g.Preferences, CoverImage = g.CoverImage
            }).ToList();
            var added = 0;
            foreach (var found in result.Games)
            {
                var id = GameId.Create(found.Platform, found.ExternalId, found.InstallPath);
                var game = games.FirstOrDefault(g => g.Id == id);
                if (game is null)
                {
                    game = new GameRecord { Id = id, Name = found.Name, Platform = found.Platform, ExternalId = found.ExternalId };
                    games.Add(game);
                    added++;
                }
                if (!game.Installations.Any(i => GameId.Create(GamePlatform.Manual, null, i.RootPath) ==
                                                GameId.Create(GamePlatform.Manual, null, found.InstallPath)))
                    game.Installations.Add(new GameInstallation { RootPath = found.InstallPath, PrimaryExecutablePath = found.ExecutablePath });
            }
            var catalog = new GameCatalog { Games = games };
            await _gameCatalogRepository.SaveGameCatalog_Async(catalog);
            _catalog = catalog;
            RefreshVisibleGames();
            StatusMessage = $"Scan complete: {added} new games. " + string.Join(" ", result.Diagnostics.Select(d => $"{d.Platform}: {d.Message}"));
        }
        catch (Exception ex) { StatusMessage = $"Could not scan games: {ex.Message}"; }
        finally { IsBusy = false; }
    }

    public Task<AppConfiguration> LoadConfiguration_Async() => _configurationRepository.LoadAppConfiguration_Async();
    public Task SaveConfiguration_Async(AppConfiguration configuration) => _configurationRepository.SaveAppConfiguration_Async(configuration);

    public ObservableCollection<GameRecord> Games { get; } = [];

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanAddGames))]
    private bool _isBusy;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanAddGames))] [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private bool _isLoaded;

    [ObservableProperty] private string _statusMessage = "Loading your library…";

    public bool CanAddGames => IsLoaded && !IsBusy;
    public bool IsEmpty => IsLoaded && Games.Count == 0;

    [ObservableProperty] private int _sortIndex;

    [ObservableProperty] private bool _sortDescending;

    [ObservableProperty] private string _searchText = string.Empty;

    partial void OnSortIndexChanged(int value)
    {
        RefreshVisibleGames();
    }

    partial void OnSortDescendingChanged(bool value)
    {
        RefreshVisibleGames();
    }

    partial void OnSearchTextChanged(string value)
    {
        RefreshVisibleGames();
    }

    public async Task LoadGameLibrary_Async(CancellationToken cancellationToken = default)
    {
        if (IsBusy || IsLoaded) return;

        IsBusy = true;

        try
        {
            _catalog = await _gameCatalogRepository.LoadGameCatalog_Async(cancellationToken);
            RefreshVisibleGames();
            IsLoaded = true;
            StatusMessage = $"{_catalog.Games.Count} games in your library.";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            StatusMessage = "Loading cancelled.";
        }
        catch (Exception exception) when (IsStorageError(exception))
        {
            StatusMessage = $"Could not load the library: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task AddManualGames_Async(
        IEnumerable<string> folders,
        CancellationToken cancellationToken = default)
    {
        if (!CanAddGames) return;

        IsBusy = true;

        try
        {
            var games = _catalog.Games.ToList();
            var knownPaths = games
                .SelectMany(game => game.Installations)
                .Select(installation => GameId.Create(GamePlatform.Manual, null, installation.RootPath))
                .ToHashSet();
            var added = 0;
            var skipped = 0;

            foreach (var folder in folders)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var path = GameId.NormalizeInstallPath(folder);
                var id = GameId.Create(GamePlatform.Manual, null, path);

                if (!Directory.Exists(path) || !knownPaths.Add(id))
                {
                    skipped++;

                    continue;
                }

                games.Add(new GameRecord
                {
                    Id = id,
                    Name = new DirectoryInfo(path).Name,
                    Platform = GamePlatform.Manual,
                    Installations = [new GameInstallation { RootPath = path }]
                });
                added++;
            }

            if (added > 0)
            {
                // Publish to the UI only after the new catalog has been saved successfully.
                var updatedCatalog = new GameCatalog { Games = games };
                await _gameCatalogRepository.SaveGameCatalog_Async(updatedCatalog, cancellationToken);
                _catalog = updatedCatalog;
                RefreshVisibleGames();
            }

            StatusMessage =
                $"Added {added} {(added == 1 ? "game" : "games")}. Skipped {skipped} duplicate or unavailable folders.";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            StatusMessage = "Adding games cancelled.";
        }
        catch (Exception exception) when (IsStorageError(exception) ||
                                          exception is ArgumentException or NotSupportedException)
        {
            StatusMessage = $"Could not add games: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void RefreshVisibleGames()
    {
        var filtered = _catalog.Games.Where(game =>
                                                game.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase));
        var sorted = SortIndex == 1
            ? filtered.OrderBy(game => game.Platform)
                .ThenBy(game => game.Name, StringComparer.OrdinalIgnoreCase)
            : filtered.OrderBy(game => game.Name, StringComparer.OrdinalIgnoreCase);

        Games.Clear();
        foreach (var game in SortDescending ? sorted.Reverse() : sorted)
            Games.Add(game);
        OnPropertyChanged(nameof(IsEmpty));
    }

    private static bool IsStorageError(Exception exception)
    {
        return exception is IOException or UnauthorizedAccessException or JsonException or InvalidDataException;
    }
}