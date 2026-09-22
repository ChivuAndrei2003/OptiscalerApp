using Microsoft.Extensions.DependencyInjection;
using OptiscalerApp.DependencyInjection;
using OptiscalerApp.Models;
using OptiscalerApp.Paths;
using OptiscalerApp.Persistence;
using OptiscalerApp.ViewModels;
using Xunit;

namespace Optiscaler.Tests;

public sealed class LibraryUiTests : IDisposable
{
    private readonly string _root = Path.Combine(OperatingSystem.IsMacOS() ? "/private/tmp" : Path.GetTempPath(),
                                                 "Optiscaler-library-ui-" + Guid.NewGuid().ToString("N"));

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
        using var provider = new ServiceCollection().AddOptiscalerServices()
            .AddSingleton<IAppPaths>(new AppPaths(Path.Combine(_root, "data")))
            .AddSingleton<ProfilesViewModel>().AddSingleton<MainWindowViewModel>().BuildServiceProvider();
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
        await provider.GetRequiredService<IAppConfigurationRepository>().SaveAppConfiguration_Async(new AppConfiguration
        {
            ScanSourceSettings = new ScanSourceSettings
            {
                EnabledPlatforms = [GamePlatform.Custom], CustomFolders = [gamesRoot]
            }
        }, ct);
        var vm = provider.GetRequiredService<MainWindowViewModel>();
        await vm.LoadGameLibrary_Async(ct);
        await vm.ScanGameLibrary_Async();
        await vm.ScanGameLibrary_Async();
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
        using var provider = new ServiceCollection().AddOptiscalerServices()
            .AddSingleton<IAppPaths>(new AppPaths(Path.Combine(_root, "data")))
            .AddSingleton<IGameCatalogRepository>(new FailingCatalogRepository())
            .AddSingleton<ProfilesViewModel>().AddSingleton<MainWindowViewModel>().BuildServiceProvider();
        await provider.GetRequiredService<IAppConfigurationRepository>().SaveAppConfiguration_Async(new AppConfiguration
        {
            ScanSourceSettings = new ScanSourceSettings
            {
                EnabledPlatforms = [GamePlatform.Custom], CustomFolders = [gamesRoot]
            }
        }, TestContext.Current.CancellationToken);
        var vm = provider.GetRequiredService<MainWindowViewModel>();
        await vm.LoadGameLibrary_Async(TestContext.Current.CancellationToken);
        await vm.ScanGameLibrary_Async();
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
        using var provider = new ServiceCollection().AddOptiscalerServices()
            .AddSingleton<IAppPaths>(new AppPaths(Path.Combine(_root, "data")))
            .AddSingleton<IGameCatalogRepository>(repository)
            .AddSingleton<ProfilesViewModel>().AddSingleton<MainWindowViewModel>().BuildServiceProvider();
        var vm = provider.GetRequiredService<MainWindowViewModel>();
        await vm.LoadGameLibrary_Async(TestContext.Current.CancellationToken);

        if (failSave)
        {
            await Assert.ThrowsAsync<IOException>(() => vm.SaveGameDetails_Async(original.Id, "Renamed", gameRoot,
                                                   exe));
            Assert.Same(original, Assert.Single(vm.Games));
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
            Assert.Same(saved, Assert.Single(vm.Games));
        }

        Assert.True(vm.CanAddGames);
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