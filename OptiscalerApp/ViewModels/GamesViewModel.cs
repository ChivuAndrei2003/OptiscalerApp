using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OptiscalerApp.Management;
using OptiscalerApp.Models;
using OptiscalerApp.Paths;
using OptiscalerApp.Persistence;
using OptiscalerApp.Scanning;

namespace OptiscalerApp.ViewModels;

/// <summary>The game library: discovery, the cards shown, and opening a game to manage it.</summary>
public sealed partial class GamesViewModel : ViewModelBase
{
    private readonly IGameAnalyzer _analyzer;
    private readonly GameArtworkService _artwork;
    private readonly CompatibilityListService _compatibility;
    private readonly IAppConfigurationRepository _configurationRepository;
    private readonly IFileDialogs _dialogs;
    private readonly GameDiscoveryCoordinator _discovery;
    private readonly IGameCatalogRepository _gameCatalogRepository;
    private readonly Lazy<Task<IReadOnlyList<GpuInfo>>> _gpus;
    private readonly IGameInstallationService _installer;
    private readonly PackageDownloadService _packages;
    private readonly IProfileRepository _profileRepository;
    private readonly IShellActions _shell;

    // One card per game, rebuilt only when its inputs change; search, filter and sort just pick from these.
    private List<GameCardViewModel> _cards = [];
    private GameCatalog _catalog = new();
    private CompatibilityIndex _compatibilityIndex = CompatibilityIndex.Empty;
    private string? _latestRelease;
    private IReadOnlyList<ManagedTarget> _managedTargets = [];

    [ObservableProperty] private int _filterIndex;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanAddGames))]
    private bool _isBusy;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanAddGames))] [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private bool _isLoaded;

    [ObservableProperty] private string _librarySummary = "";

    [ObservableProperty] private string _searchText = string.Empty;

    [ObservableProperty] private bool _sortDescending;

    [ObservableProperty] private int _sortIndex;

    [ObservableProperty] private string _statusMessage = "Loading your library…";

    public GamesViewModel(IGameCatalogRepository gameCatalogRepository,
                          IAppConfigurationRepository configurationRepository, GameDiscoveryCoordinator discovery,
                          IGameAnalyzer analyzer, IGameInstallationService installationService,
                          IProfileRepository profileRepository, PackageDownloadService packages,
                          GameArtworkService artwork, CompatibilityListService compatibility, IFileDialogs dialogs,
                          IShellActions shell)
    {
        _gameCatalogRepository = gameCatalogRepository;
        _configurationRepository = configurationRepository;
        _discovery = discovery;
        _profileRepository = profileRepository;
        _analyzer = analyzer;
        _installer = installationService;
        _packages = packages;
        _artwork = artwork;
        _compatibility = compatibility;
        _dialogs = dialogs;
        _shell = shell;

        // Detected once per run: DXGI and lspci are slow enough to notice when opening every game.
        _gpus = new Lazy<Task<IReadOnlyList<GpuInfo>>>(() => Task.Run(async () =>
        {
            try
            {
                return await GpuDetectService.DetectGpus_Async();
            }
            catch (Exception)
            {
                // GPU detection only improves recommendations; the app works without it.
                return (IReadOnlyList<GpuInfo>)[];
            }
        }));
    }

    public ObservableCollection<GameCardViewModel> Games { get; } = [];

    public bool CanAddGames => IsLoaded && !IsBusy;
    public bool IsEmpty => IsLoaded && Games.Count == 0;

    public LibraryFilter Filter =>
        Enum.IsDefined((LibraryFilter)FilterIndex) ? (LibraryFilter)FilterIndex : LibraryFilter.All;

    /// <summary>Raised with the page for a game the user chose to manage.</summary>
    public event Action<ManageGameViewModel>? ManageRequested;

    [RelayCommand]
    private async Task OpenGame(GameCardViewModel card)
    {
        var configuration = new AppConfiguration();

        try
        {
            configuration = await _configurationRepository.LoadAppConfiguration_Async();
        }
        catch (Exception ex) when (IsStorageError(ex))
        {
            // The game opens with the built-in defaults; Settings reports the damaged file.
        }

        ManageRequested?.Invoke(new ManageGameViewModel(card.Game, _analyzer, _installer, _packages,
                                                        _profileRepository, SaveGameDetails_Async, _compatibility,
                                                        () => _gpus.Value)
        {
            Package = { Channel = configuration.PreferBetaReleases ? ReleaseChannel.Beta : ReleaseChannel.Stable },
            SelectedProxy = configuration.DefaultProxyDll,
            Dialogs = _dialogs,
            Shell = _shell
        });
    }

    /// <summary>Scans on startup when the user asked for it in Settings.</summary>
    public async Task ScanIfAutomatic_Async()
    {
        try
        {
            if (!(await _configurationRepository.LoadAppConfiguration_Async()).AutoScan) return;
        }
        catch (Exception ex) when (IsStorageError(ex))
        {
            StatusMessage = $"Could not load settings: {ex.Message}";

            return;
        }

        await ScanGames();
    }

    [RelayCommand]
    private async Task ScanGames()
    {
        if (!CanAddGames) return;

        IsBusy = true;
        StatusMessage = "Scanning game sources…";

        try
        {
            var settings = await _configurationRepository.LoadAppConfiguration_Async();
            var result = await _discovery.ScanGames_Async(ScanContext.FromSettings(settings.ScanSourceSettings));

            // Clone mutable records so a failed save cannot alter the currently published catalog.
            var games = _catalog.Games.Select(g => g.Clone()).ToList();
            var owners = new Dictionary<GameId, GameRecord>();
            var added = 0;

            foreach (var game in games)
            foreach (var installation in game.Installations)
                owners.TryAdd(FolderId(installation.RootPath), game);

            foreach (var found in result.Games)
            {
                var id = GameId.Create(found.Platform, found.ExternalId, found.InstallPath);
                var folder = FolderId(found.InstallPath);

                // A folder is listed once, e.g. not again after it was added by hand. Only launcher entries may
                // share one, as Steam games that install into the same folder do.
                if (owners.TryGetValue(folder, out var owner) &&
                    (IsFolderOnly(owner.Platform) || IsFolderOnly(found.Platform)))
                    continue;

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

                if (game.Installations.All(i => FolderId(i.RootPath) != folder))
                    game.Installations.Add(new GameInstallation
                    {
                        RootPath = found.InstallPath,
                        PrimaryExecutablePath = found.ExecutablePath
                    });

                owners.TryAdd(folder, game);
            }

            await _artwork.PopulateArtwork_Async(games);
            var catalog = new GameCatalog { Games = games };
            await _gameCatalogRepository.SaveGameCatalog_Async(catalog);
            _catalog = catalog;
            RebuildCards();
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

    public async Task<GameRecord> SaveGameDetails_Async(GameId id, string name, string rootPath, string? executable)
    {
        if (IsBusy || !IsLoaded) throw new InvalidOperationException("Wait for the library to finish loading.");
        if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("Enter a game name.");

        // An empty box clears the executable instead of saving an empty path.
        executable = string.IsNullOrWhiteSpace(executable) ? null : executable;

        if (executable is not null && (!File.Exists(executable) ||
                                       !Path.GetExtension(executable)
                                           .Equals(".exe", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Select an existing game executable.");

        var original = _catalog.Games.Single(g => g.Id == id);
        var updated = original.Clone();
        updated.Name = name.Trim();
        updated.Installations = original.Installations.Select(i => new GameInstallation
        {
            RootPath = i.RootPath,
            PrimaryExecutablePath = i.RootPath == rootPath ? executable : i.PrimaryExecutablePath
        }).ToList();
        IsBusy = true;

        try
        {
            await _artwork.PopulateArtwork_Async([updated]);
            var catalog = new GameCatalog { Games = _catalog.Games.Select(g => g.Id == id ? updated : g).ToList() };
            await _gameCatalogRepository.SaveGameCatalog_Async(catalog);
            _catalog = catalog;
            RebuildCards();

            return updated;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private Task ToggleFavorite(GameCardViewModel card)
    {
        return UpdateGame_Async(card.Game.Id, game =>
        {
            game.IsFavorite = !game.IsFavorite;

            return game.IsFavorite ? $"{game.Name} added to favorites." : $"{game.Name} removed from favorites.";
        });
    }

    [RelayCommand]
    private Task ToggleHidden(GameCardViewModel card)
    {
        return UpdateGame_Async(card.Game.Id, game =>
        {
            game.IsHidden = !game.IsHidden;

            return game.IsHidden
                ? $"{game.Name} is hidden. Choose the Hidden filter to show it again."
                : $"{game.Name} is visible again.";
        });
    }

    /// <summary>Forgets a game without touching its files; operations stay recorded for its folder.</summary>
    [RelayCommand]
    private Task RemoveGame(GameCardViewModel card)
    {
        return ChangeCatalog_Async(games =>
        {
            var game = games.Single(g => g.Id == card.Game.Id);
            games.Remove(game);

            return $"Removed {game.Name} from the library. Its files were not changed; " +
                   "a scan may add it again, so hide it instead to keep it out.";
        });
    }

    /// <summary>Re-reads managed installations and the saved wiki list, updating cards only when either changed.</summary>
    public async Task RefreshLibraryStatus_Async()
    {
        try
        {
            var targets = _installer.GetManagedTargets_Async();
            var index = _compatibility.GetCachedIndex_Async();
            await Task.WhenAll(targets, index);

            if (SameTargets(targets.Result, _managedTargets) && ReferenceEquals(index.Result, _compatibilityIndex))
                return;

            _managedTargets = targets.Result;
            _compatibilityIndex = index.Result;
            RebuildCards();
        }
        catch (Exception ex) when (IsStorageError(ex))
        {
            StatusMessage = $"Could not read installation history: {ex.Message}";
        }
    }

    /// <summary>Downloads the wiki list when it is stale; the library is usable while this runs.</summary>
    public async Task RefreshCompatibility_Async()
    {
        var index = await _compatibility.GetIndex_Async();

        if (ReferenceEquals(index, _compatibilityIndex)) return;

        _compatibilityIndex = index;
        RebuildCards();
    }

    /// <summary>Compares every managed game with the latest stable OptiScaler release and refreshes the wiki list.</summary>
    [RelayCommand]
    private async Task CheckForUpdates()
    {
        if (!CanAddGames) return;

        IsBusy = true;
        StatusMessage = "Checking OptiScaler releases and the compatibility list…";

        try
        {
            var index = _compatibility.GetIndex_Async(true);
            var targets = _installer.GetManagedTargets_Async();
            var releases = _packages.GetReleases_Async(false);
            await Task.WhenAll(index, targets, releases);
            _compatibilityIndex = index.Result;
            _managedTargets = targets.Result;
            _latestRelease = releases.Result.FirstOrDefault()?.Version;
            RebuildCards();
            var updates = _cards.Count(card => card.HasUpdate);
            StatusMessage = _latestRelease is null
                ? "No stable OptiScaler release was found."
                : $"Latest OptiScaler: {_latestRelease}. " +
                  (updates == 0 ? "Every managed game is up to date. " : $"{updates} managed games can be updated. ") +
                  $"Compatibility list: {_compatibilityIndex.Count} tested games.";
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException || IsStorageError(ex))
        {
            StatusMessage = $"Could not check for updates: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    partial void OnSortIndexChanged(int value) { RefreshVisibleGames(); }

    partial void OnSortDescendingChanged(bool value) { RefreshVisibleGames(); }

    partial void OnSearchTextChanged(string value) { RefreshVisibleGames(); }

    partial void OnFilterIndexChanged(int value) { RefreshVisibleGames(); }

    public async Task LoadGameLibrary_Async(CancellationToken cancellationToken = default)
    {
        if (IsBusy || IsLoaded) return;

        IsBusy = true;

        try
        {
            _catalog = await _gameCatalogRepository.LoadGameCatalog_Async(cancellationToken);
            if (await _artwork.PopulateArtwork_Async(_catalog.Games, cancellationToken))
                await _gameCatalogRepository.SaveGameCatalog_Async(_catalog, cancellationToken);

            try
            {
                // Local only; the wiki list is refreshed later so it never delays the library.
                var targets = _installer.GetManagedTargets_Async(cancellationToken);
                var index = _compatibility.GetCachedIndex_Async(cancellationToken);
                await Task.WhenAll(targets, index);
                _managedTargets = targets.Result;
                _compatibilityIndex = index.Result;
            }
            catch (Exception exception) when (IsStorageError(exception))
            {
                // A damaged journal must not hide the library; Manage Game reports it per folder.
            }

            RebuildCards();
            IsLoaded = true;
            StatusMessage = $"{_catalog.Games.Count} games in your library.";

            // Started now so opening the first game does not wait for DXGI or lspci.
            _ = _gpus.Value;
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

    [RelayCommand]
    private async Task AddGames()
    {
        if (!CanAddGames) return;

        try
        {
            if (await _dialogs.PickFolders_Async("Select game folders") is { Count: > 0 } folders)
                await AddManualGames_Async(folders);
        }
        catch (InvalidOperationException ex)
        {
            StatusMessage = $"Could not open the selected folders: {ex.Message}";
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
                .Select(installation => FolderId(installation.RootPath))
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
                RebuildCards();
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

    private Task UpdateGame_Async(GameId id, Func<GameRecord, string> change)
    {
        return ChangeCatalog_Async(games =>
        {
            var index = games.FindIndex(g => g.Id == id);

            if (index < 0) throw new InvalidOperationException("The game is no longer in the library.");

            var copy = games[index].Clone();
            games[index] = copy;

            return change(copy);
        });
    }

    /// <summary>Applies a change to a copy of the catalog and publishes it only once it is saved.</summary>
    private async Task ChangeCatalog_Async(Func<List<GameRecord>, string> change)
    {
        if (!CanAddGames) return;

        IsBusy = true;

        try
        {
            var games = _catalog.Games.ToList();
            var message = change(games);
            var catalog = new GameCatalog { Games = games };
            await _gameCatalogRepository.SaveGameCatalog_Async(catalog);
            _catalog = catalog;
            RebuildCards();
            StatusMessage = message;
        }
        catch (Exception exception) when (IsStorageError(exception) || exception is InvalidOperationException)
        {
            StatusMessage = $"Could not update the library: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void RebuildCards()
    {
        _cards = _catalog.Games.Select(game => new GameCardViewModel(game, FindManagedTarget(game),
                                                                     _compatibilityIndex.Find(game.Name),
                                                                     _latestRelease)).ToList();
        RefreshVisibleGames();
    }

    private void RefreshVisibleGames()
    {
        var filtered = _cards.Where(card => card.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase) &&
                                            card.Matches(Filter));
        var sorted = SortIndex == 1
            ? filtered.OrderBy(card => card.Game.Platform)
                .ThenBy(card => card.Name, StringComparer.OrdinalIgnoreCase)
            : filtered.OrderBy(card => card.Name, StringComparer.OrdinalIgnoreCase);

        Games.Clear();

        // Favorites stay on top in either direction.
        foreach (var card in (SortDescending ? sorted.Reverse() : sorted).OrderByDescending(c => c.IsFavorite))
            Games.Add(card);

        var visible = _cards.Where(card => !card.IsHidden).ToList();
        var attention = visible.Count(card => card.NeedsAttention);
        LibrarySummary = $"{Games.Count} shown · {visible.Count(card => card.IsManaged)} managed" +
                         (attention > 0 ? $" · {attention} need attention" : "") +
                         (_cards.Count > visible.Count ? $" · {_cards.Count - visible.Count} hidden" : "");
        OnPropertyChanged(nameof(IsEmpty));
    }

    /// <summary>A game can have several installations; the one that needs attention is the one to show.</summary>
    private ManagedTarget? FindManagedTarget(GameRecord game)
    {
        if (_managedTargets.Count == 0) return null;

        var roots = new List<string>();

        foreach (var installation in game.Installations)
            try
            {
                roots.Add(PathUtil.Normalize(installation.RootPath));
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
            {
                // An unreachable installation cannot contain a managed folder.
            }

        return _managedTargets.Where(t => roots.Any(root => PathUtil.IsWithin(t.TargetDirectory, root)))
            .OrderByDescending(t => t.Health != ManagedHealth.Healthy)
            .ThenByDescending(t => t.Journal.CreatedAtUtc)
            .FirstOrDefault();
    }

    /// <summary>Identifies an installation folder whichever launcher reported it.</summary>
    private static GameId FolderId(string path) { return GameId.Create(GamePlatform.Manual, null, path); }

    /// <summary>Games known only by their folder, added by hand or found in a custom scan folder.</summary>
    private static bool IsFolderOnly(GamePlatform platform)
    {
        return platform is GamePlatform.Manual or GamePlatform.Custom;
    }

    private static bool SameTargets(IReadOnlyList<ManagedTarget> left, IReadOnlyList<ManagedTarget> right)
    {
        return left.Select(t => (t.TargetDirectory, t.Health, t.Version, t.Journal.Id))
            .SequenceEqual(right.Select(t => (t.TargetDirectory, t.Health, t.Version, t.Journal.Id)));
    }
}
