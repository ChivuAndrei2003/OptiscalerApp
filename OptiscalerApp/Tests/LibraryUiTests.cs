using Microsoft.Extensions.DependencyInjection;
using OptiscalerApp.Models;
using OptiscalerApp.Persistence;
using OptiscalerApp.ViewModels;
using Xunit;

namespace Optiscaler.Tests;

public sealed class LibraryUiTests : IDisposable
{
    private readonly string _root = TestData.TempRoot("Optiscaler-library-ui-");

    private string DataRoot => Path.Combine(_root, "data");

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    [Fact]
    public async Task RepeatedScansPersistNewGamesAndPreserveExistingNames()
    {
        var gamesRoot = Path.Combine(_root, "games");
        var first = Directory.CreateDirectory(Path.Combine(gamesRoot, "First")).FullName;
        Directory.CreateDirectory(Path.Combine(gamesRoot, "Second"));
        using var provider = TestData.LibraryServices(DataRoot);
        var ct = TestContext.Current.CancellationToken;
        var repository = provider.GetRequiredService<IGameCatalogRepository>();
        await repository.SaveGameCatalog_Async(new GameCatalog
        {
            Games =
            [
                new GameRecord
                {
                    Id = GameId.Create(GamePlatform.Custom, null, first),
                    Name = "My custom name",
                    Platform = GamePlatform.Custom,
                    Installations = [new GameInstallation { RootPath = first }]
                }
            ]
        }, ct);
        await SaveScanFolder_Async(provider, gamesRoot);
        var vm = provider.GetRequiredService<GamesViewModel>();
        await vm.LoadGameLibrary_Async(ct);
        await vm.ScanGamesCommand.ExecuteAsync(null);
        await vm.ScanGamesCommand.ExecuteAsync(null);
        Assert.Equal(2, vm.Games.Count);
        var saved = await repository.LoadGameCatalog_Async(ct);
        Assert.Equal(2, saved.Games.Count);
        var original = saved.Games.Single(g => g.Name == "My custom name");
        Assert.Single(original.Installations);
        Assert.True(vm.CanAddGames);
    }

    [Fact]
    public async Task FailedLibraryWriteDoesNotPublishDiscoveredGames()
    {
        var gamesRoot = Directory.CreateDirectory(Path.Combine(_root, "games")).FullName;
        Directory.CreateDirectory(Path.Combine(gamesRoot, "New game"));
        using var provider = TestData.LibraryServices(DataRoot, configure: services =>
                                                          services.AddSingleton<IGameCatalogRepository>(
                                                              new FailingCatalogRepository()));
        await SaveScanFolder_Async(provider, gamesRoot);
        var vm = provider.GetRequiredService<GamesViewModel>();
        await vm.LoadGameLibrary_Async(TestContext.Current.CancellationToken);
        await vm.ScanGamesCommand.ExecuteAsync(null);
        Assert.Empty(vm.Games);
        Assert.Contains("disk full", vm.StatusMessage);
        Assert.True(vm.CanAddGames);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SavingGameDetailsPublishesOnlyAfterPersistence(bool failSave)
    {
        var gameRoot = Directory.CreateDirectory(Path.Combine(_root, "game")).FullName;
        var exe = Path.Combine(gameRoot, "game.exe");
        InstallationTests.WritePe(exe, false);
        var original = new GameRecord
        {
            Id = GameId.Create(GamePlatform.Manual, null, gameRoot),
            Name = "Original",
            Platform = GamePlatform.Manual,
            CoverImage = "cover.png",
            Installations =
                [new GameInstallation { RootPath = gameRoot }, new GameInstallation { RootPath = gameRoot + "-other" }]
        };
        var repository = new EditableCatalogRepository(new GameCatalog { Games = [original] }, failSave);
        using var provider = TestData.LibraryServices(DataRoot, configure: services =>
                                                          services.AddSingleton<IGameCatalogRepository>(repository));
        var vm = provider.GetRequiredService<GamesViewModel>();
        await vm.LoadGameLibrary_Async(TestContext.Current.CancellationToken);

        if (failSave)
        {
            await Assert.ThrowsAsync<IOException>(() => vm.SaveGameDetails_Async(original.Id, "Renamed", gameRoot,
                                                   exe));
            Assert.Same(original, Assert.Single(vm.Games).Game);
            Assert.Equal("Original", original.Name);
            Assert.Null(original.Installations[0].PrimaryExecutablePath);
        }
        else
        {
            await vm.SaveGameDetails_Async(original.Id, " Renamed ", gameRoot, exe);
            var saved = Assert.Single((await repository.LoadGameCatalog_Async(TestContext.Current.CancellationToken))
                                      .Games);
            Assert.Equal("Renamed", saved.Name);
            Assert.Equal(exe, saved.Installations[0].PrimaryExecutablePath);
            Assert.Null(saved.Installations[1].PrimaryExecutablePath);
            Assert.Equal("cover.png", saved.CoverImage);
            Assert.Equal("Original", original.Name);
            Assert.Same(saved, Assert.Single(vm.Games).Game);

            // Clearing the executable box forgets the executable rather than saving an empty path.
            await vm.SaveGameDetails_Async(original.Id, "Renamed", gameRoot, " ");
            Assert.Null((await repository.LoadGameCatalog_Async(TestContext.Current.CancellationToken)).Games[0]
                        .Installations[0].PrimaryExecutablePath);
        }

        Assert.True(vm.CanAddGames);
    }

    [Fact]
    public async Task ScanningDoesNotListAFolderTwice()
    {
        var gamesRoot = Path.Combine(_root, "games");
        var first = Directory.CreateDirectory(Path.Combine(gamesRoot, "First")).FullName;
        Directory.CreateDirectory(Path.Combine(gamesRoot, "Second"));
        using var provider = TestData.LibraryServices(DataRoot);
        var ct = TestContext.Current.CancellationToken;
        await provider.GetRequiredService<IGameCatalogRepository>().SaveGameCatalog_Async(new GameCatalog
        {
            Games =
            [
                new GameRecord
                {
                    Id = GameId.Create(GamePlatform.Manual, null, first),
                    Name = "Added by hand",
                    Platform = GamePlatform.Manual,
                    Installations = [new GameInstallation { RootPath = first }]
                }
            ]
        }, ct);
        await SaveScanFolder_Async(provider, gamesRoot);
        var vm = provider.GetRequiredService<GamesViewModel>();
        await vm.LoadGameLibrary_Async(ct);
        await vm.ScanGamesCommand.ExecuteAsync(null);
        Assert.Equal(["Added by hand", "Second"], vm.Games.Select(card => card.Name).Order());
    }

    [Fact]
    public async Task AddGamesUsesThePickedFolder()
    {
        var folder = Directory.CreateDirectory(Path.Combine(_root, "games", "Picked")).FullName;
        using var provider = TestData.LibraryServices(DataRoot, configure: services =>
                                                          services.AddSingleton<IFileDialogs>(new FakeDialogs(folder)));
        var vm = provider.GetRequiredService<GamesViewModel>();
        await vm.LoadGameLibrary_Async(TestContext.Current.CancellationToken);

        await vm.AddGamesCommand.ExecuteAsync(null);

        Assert.Equal("Picked", Assert.Single(vm.Games).Name);
        Assert.StartsWith("Added 1 game.", vm.StatusMessage);
    }

    private static Task SaveScanFolder_Async(IServiceProvider provider, string folder)
    {
        return provider.GetRequiredService<IAppConfigurationRepository>().SaveAppConfiguration_Async(
            new AppConfiguration
            {
                ScanSourceSettings = new ScanSourceSettings
                {
                    EnabledPlatforms = [GamePlatform.Custom], CustomFolders = [folder]
                }
            }, TestContext.Current.CancellationToken);
    }

    private sealed class EditableCatalogRepository(GameCatalog catalog, bool failSave) : IGameCatalogRepository
    {
        public Task<GameCatalog> LoadGameCatalog_Async(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(catalog);
        }

        public Task SaveGameCatalog_Async(GameCatalog updated, CancellationToken cancellationToken = default)
        {
            if (failSave) throw new IOException("disk full");

            catalog = updated;

            return Task.CompletedTask;
        }
    }

    private sealed class FailingCatalogRepository : IGameCatalogRepository
    {
        public Task<GameCatalog> LoadGameCatalog_Async(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new GameCatalog());
        }

        public Task SaveGameCatalog_Async(GameCatalog catalog, CancellationToken cancellationToken = default)
        {
            throw new IOException("disk full");
        }
    }
}
