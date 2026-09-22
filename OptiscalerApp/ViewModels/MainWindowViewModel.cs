using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using OptiscalerApp.Management;
using OptiscalerApp.Models;
using OptiscalerApp.Persistence;
using OptiscalerApp.Scanning;

namespace OptiscalerApp.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly IGameAnalyzer _analyzer;
    private readonly GameArtworkService _artwork;
    private readonly IAppConfigurationRepository _configurationRepository;
    private readonly GameDiscoveryCoordinator _discovery;
    private readonly IGameCatalogRepository _gameCatalogRepository;
    private readonly IGameInstallationService _installer;
    private readonly PackageDownloadService _packages;
    private readonly IProfileRepository _profileRepository;
    private GameCatalog _catalog = new();

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanAddGames))]
    private bool _isBusy;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanAddGames))] [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private bool _isLoaded;

    [ObservableProperty] private string _searchText = string.Empty;

    [ObservableProperty] private bool _sortDescending;

    [ObservableProperty] private int _sortIndex;

    [ObservableProperty] private string _statusMessage = "Loading your library…";

    public MainWindowViewModel(IGameCatalogRepository gameCatalogRepository,
                               IAppConfigurationRepository configurationRepository, GameDiscoveryCoordinator discovery,
                               ProfilesViewModel profiles, IGameAnalyzer analyzer,
                               IGameInstallationService installationService, IProfileRepository profileRepository,
                               PackageDownloadService packages, GameArtworkService artwork)
    {
        _gameCatalogRepository = gameCatalogRepository;
        _configurationRepository = configurationRepository;
        _discovery = discovery;
        Profiles = profiles;
        _profileRepository = profileRepository;
        _analyzer = analyzer;
        _installer = installationService;
        _packages = packages;
        _artwork = artwork;
    }

    public ProfilesViewModel Profiles { get; }

    public ObservableCollection<GameRecord> Games { get; } = [];

    public bool CanAddGames => IsLoaded && !IsBusy;
    public bool IsEmpty => IsLoaded && Games.Count == 0;

    public ManageGameViewModel CreateManageGameViewModel(GameRecord game)
    {
        return new ManageGameViewModel(game, _analyzer, _installer, _packages, _profileRepository,
                                       SaveGameDetails_Async);
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
                Id = g.Id,
                Name = g.Name,
                Platform = g.Platform,
                ExternalId = g.ExternalId,
                Installations = g.Installations.ToList(),
                CoverImage = g.CoverImage
            }).ToList();
            var added = 0;

            foreach (var found in result.Games)
            {
                var id = GameId.Create(found.Platform, found.ExternalId, found.InstallPath);
                var game = games.FirstOrDefault(g => g.Id == id);

                if (game is null)
                {
                    game = new GameRecord
                    {
                        Id = id, Name = found.Name, Platform = found.Platform, ExternalId = found.ExternalId
                    };
                    games.Add(game);
                    added++;
                }

                if (game.Installations.All(i => GameId.Create(GamePlatform.Manual, null, i.RootPath) !=
                                                GameId.Create(GamePlatform.Manual, null, found.InstallPath)))
                    game.Installations.Add(new GameInstallation
                    {
                        RootPath = found.InstallPath,
                        PrimaryExecutablePath = found.ExecutablePath
                    });
            }

            await _artwork.PopulateArtwork_Async(games);
            var catalog = new GameCatalog { Games = games };
            await _gameCatalogRepository.SaveGameCatalog_Async(catalog);
            _catalog = catalog;
            RefreshVisibleGames();
            StatusMessage = $"Scan complete: {added} new games. " +
                            string.Join(" ", result.Diagnostics.Select(d => $"{d.Platform}: {d.Message}"));
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not scan games: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public Task<AppConfiguration> LoadConfiguration_Async()
    {
        return _configurationRepository.LoadAppConfiguration_Async();
    }

    public Task SaveConfiguration_Async(AppConfiguration configuration)
    {
        return _configurationRepository.SaveAppConfiguration_Async(configuration);
    }

    public async Task<GameRecord> SaveGameDetails_Async(GameId id, string name, string rootPath, string? executable)
    {
        if (IsBusy || !IsLoaded) throw new InvalidOperationException("Wait for the library to finish loading.");
        if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("Enter a game name.");
        if (!string.IsNullOrWhiteSpace(executable) && (!File.Exists(executable) ||
                                                       !Path.GetExtension(executable)
                                                           .Equals(".exe", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Select an existing game executable.");

        var original = _catalog.Games.Single(g => g.Id == id);
        var updated = new GameRecord
        {
            Id = original.Id,
            Name = name.Trim(),
            Platform = original.Platform,
            ExternalId = original.ExternalId,
            CoverImage = original.CoverImage,
            Installations = original.Installations.Select(i => new GameInstallation
            {
                RootPath = i.RootPath,
                PrimaryExecutablePath =
                    i.RootPath == rootPath
                        ? executable
                        : i.PrimaryExecutablePath
            }).ToList()
        };
        IsBusy = true;

        try
        {
            await _artwork.PopulateArtwork_Async([updated]);
            var catalog = new GameCatalog { Games = _catalog.Games.Select(g => g.Id == id ? updated : g).ToList() };
            await _gameCatalogRepository.SaveGameCatalog_Async(catalog);
            _catalog = catalog;
            RefreshVisibleGames();

            return updated;
        }
        finally
        {
            IsBusy = false;
        }
    }

    partial void OnSortIndexChanged(int value) { RefreshVisibleGames(); }

    partial void OnSortDescendingChanged(bool value) { RefreshVisibleGames(); }

    partial void OnSearchTextChanged(string value) { RefreshVisibleGames(); }

    public async Task LoadGameLibrary_Async(CancellationToken cancellationToken = default)
    {
        if (IsBusy || IsLoaded) return;

        IsBusy = true;

        try
        {
            _catalog = await _gameCatalogRepository.LoadGameCatalog_Async(cancellationToken);
            if (await _artwork.PopulateArtwork_Async(_catalog.Games, cancellationToken))
                await _gameCatalogRepository.SaveGameCatalog_Async(_catalog, cancellationToken);
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
                await _artwork.PopulateArtwork_Async(games.Except(_catalog.Games), cancellationToken);
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
        foreach (var game in SortDescending ? sorted.Reverse() : sorted) Games.Add(game);
        OnPropertyChanged(nameof(IsEmpty));
    }

    private static bool IsStorageError(Exception exception)
    {
        return exception is IOException or UnauthorizedAccessException or JsonException or InvalidDataException;
    }
}