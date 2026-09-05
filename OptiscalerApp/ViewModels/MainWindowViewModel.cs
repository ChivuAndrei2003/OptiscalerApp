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

namespace OptiscalerApp.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly IGameCatalogRepository _gameCatalogRepository;
    private GameCatalog _catalog = new();

    public MainWindowViewModel(IGameCatalogRepository gameCatalogRepository)
    {
        _gameCatalogRepository = gameCatalogRepository;
    }

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
        RefreshGames();
    }

    partial void OnSortDescendingChanged(bool value)
    {
        RefreshGames();
    }

    partial void OnSearchTextChanged(string value)
    {
        RefreshGames();
    }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (IsBusy || IsLoaded) return;

        IsBusy = true;
        try
        {
            _catalog = await _gameCatalogRepository.LoadAsync(cancellationToken);
            RefreshGames();
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

    public async Task AddManualGamesAsync(
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
                await _gameCatalogRepository.SaveAsync(updatedCatalog, cancellationToken);
                _catalog = updatedCatalog;
                RefreshGames();
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

    private void RefreshGames()
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