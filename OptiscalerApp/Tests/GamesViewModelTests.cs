using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OptiscalerApp.Management;
using OptiscalerApp.Models;
using OptiscalerApp.Persistence;
using OptiscalerApp.Scanning;
using OptiscalerApp.ViewModels;
using Xunit;

namespace Optiscaler.Tests;

/// <summary>The library page: scanning, editing, filtering and the status shown on each card.</summary>
public sealed class GamesViewModelTests : TempFolderTest
{
    private ServiceProvider Provider(HttpMessageHandler? http = null, Action<IServiceCollection>? configure = null)
    {
        return TestData.LibraryServices(Path.Combine(Root, "data"), http, configure);
    }

    private GameRecord Game(string name, GamePlatform platform = GamePlatform.Manual)
    {
        return TestData.Game(Folder("games", name), name, platform);
    }

    private Task ScanCustomFolder(IServiceProvider provider)
    {
        return provider.GetRequiredService<IAppConfigurationRepository>().SaveAppConfigurationAsync(
            new AppConfiguration
            {
                ScanSourceSettings = new ScanSourceSettings
                {
                    EnabledPlatforms = [GamePlatform.Custom], CustomFolders = [Path.Combine(Root, "games")]
                }
            }, Ct);
    }

    [Fact]
    public async Task RescanningAddsNewGamesWithoutDuplicatingOrRenamingKnownOnes()
    {
        using var provider = Provider();
        await ScanCustomFolder(provider);

        // One game was added by hand; the other was found by an earlier scan and renamed since.
        var vm = await provider.LoadLibraryAsync(TestData.Game(Folder("games", "First"), "Added by hand"),
                                                 TestData.Game(Folder("games", "Second"), "My custom name",
                                                               GamePlatform.Custom));
        Folder("games", "Third");

        await vm.ScanGamesCommand.ExecuteAsync(null);
        await vm.ScanGamesCommand.ExecuteAsync(null);

        Assert.Equal(["Added by hand", "My custom name", "Third"], vm.Games.Select(c => c.Name).Order());
        var saved = await provider.GetRequiredService<IGameCatalogRepository>().LoadGameCatalogAsync(Ct);
        Assert.Equal(3, saved.Games.Count);
        Assert.All(saved.Games, game => Assert.Single(game.Installations));
    }

    [Fact]
    public async Task FailedLibraryWriteDoesNotPublishDiscoveredGames()
    {
        Folder("games", "New game");
        using var provider = Provider(configure: services =>
                                          services.AddSingleton<IGameCatalogRepository>(
                                              new FakeCatalog(new GameCatalog(), true)));
        await ScanCustomFolder(provider);
        var vm = await provider.LoadLibraryAsync();

        await vm.ScanGamesCommand.ExecuteAsync(null);

        Assert.Empty(vm.Games);
        Assert.Contains("disk full", vm.StatusMessage);
        Assert.True(vm.CanAddGames);
    }

    [Fact]
    public async Task RescanningAMovedGameDropsItsMissingFolder()
    {
        var moved = Folder("new-library", "Game");
        var missing = Path.Combine(Root, "old-library", "Game");
        using var provider = Provider(configure: services =>
        {
            services.RemoveAll<IGameScanner>();
            services.AddSingleton<IGameScanner>(new MovedGameScanner(moved));
        });
        await provider.GetRequiredService<IAppConfigurationRepository>().SaveAppConfigurationAsync(
            new AppConfiguration
            {
                ScanSourceSettings = new ScanSourceSettings { EnabledPlatforms = [GamePlatform.Steam] }
            }, Ct);
        var vm = await provider.LoadLibraryAsync(TestData.Game(missing, "Game", GamePlatform.Steam, "42"));

        await vm.ScanGamesCommand.ExecuteAsync(null);

        Assert.Equal(moved, Assert.Single(Assert.Single(vm.Games).Game.Installations).RootPath);
    }

    [Fact]
    public async Task AddGamesUsesThePickedFolder()
    {
        var folder = Folder("games", "Picked");
        using var provider = Provider(configure: services =>
                                          services.AddSingleton<IFileDialogs>(new FakeDialogs(folder)));
        var vm = await provider.LoadLibraryAsync();

        await vm.AddGamesCommand.ExecuteAsync(null);

        Assert.Equal("Picked", Assert.Single(vm.Games).Name);
    }

    [Fact]
    public async Task SavingGameDetailsUpdatesTheSavedGameAndItsCard()
    {
        var original = Game("Original");
        original.CoverImage = "cover.png";
        var folder = original.Installations[0].RootPath;
        var exe = Path.Combine(folder, "game.exe");
        TestData.WritePe(exe, false);
        using var provider = Provider();
        var vm = await provider.LoadLibraryAsync(original);
        var repository = provider.GetRequiredService<IGameCatalogRepository>();

        await vm.SaveGameDetailsAsync(original.Id, " Renamed ", folder, exe);

        var saved = Assert.Single((await repository.LoadGameCatalogAsync(Ct)).Games);
        Assert.Equal("Renamed", saved.Name);
        Assert.Equal(exe, saved.Installations[0].PrimaryExecutablePath);
        Assert.Equal("cover.png", saved.CoverImage);
        Assert.Equal("Renamed", Assert.Single(vm.Games).Name);

        // Clearing the executable box forgets the executable rather than saving an empty path.
        await vm.SaveGameDetailsAsync(original.Id, "Renamed", folder, " ");
        Assert.Null((await repository.LoadGameCatalogAsync(Ct)).Games[0].Installations[0].PrimaryExecutablePath);
    }

    [Fact]
    public async Task AFailedSaveOfGameDetailsLeavesTheGameUnchanged()
    {
        var original = Game("Original");
        var folder = original.Installations[0].RootPath;
        var exe = Path.Combine(folder, "game.exe");
        TestData.WritePe(exe, false);
        using var provider = Provider(configure: services =>
                                          services.AddSingleton<IGameCatalogRepository>(
                                              new FakeCatalog(new GameCatalog { Games = [original] }, true)));
        var vm = await provider.LoadLibraryAsync();

        await Assert.ThrowsAsync<IOException>(() => vm.SaveGameDetailsAsync(original.Id, "Renamed", folder, exe));

        Assert.Same(original, Assert.Single(vm.Games).Game);
        Assert.Equal("Original", original.Name);
        Assert.Null(original.Installations[0].PrimaryExecutablePath);
        Assert.True(vm.CanAddGames);
    }

    [Fact]
    public async Task RemovingAGameKeepsItsFiles()
    {
        using var provider = Provider();
        var alpha = Game("Alpha");
        var vm = await provider.LoadLibraryAsync(alpha, Game("Beta"));

        await vm.RemoveGameCommand.ExecuteAsync(Card(vm, "Alpha"));

        Assert.Equal("Beta", Assert.Single(vm.Games).Name);
        Assert.True(Directory.Exists(alpha.Installations[0].RootPath));
        var saved = await provider.GetRequiredService<IGameCatalogRepository>().LoadGameCatalogAsync(Ct);
        Assert.Equal("Beta", Assert.Single(saved.Games).Name);
    }

    [Fact]
    public async Task FavoritesStayOnTopAndHiddenGamesOnlyShowInTheirFilter()
    {
        using var provider = Provider();
        var vm = await provider.LoadLibraryAsync(Game("Alpha"), Game("Beta"), Game("Gamma"));

        await vm.ToggleFavoriteCommand.ExecuteAsync(Card(vm, "Gamma"));
        await vm.ToggleHiddenCommand.ExecuteAsync(Card(vm, "Beta"));
        Assert.Equal(["Gamma", "Alpha"], vm.Games.Select(c => c.Name));

        vm.FilterIndex = (int)LibraryFilter.Favorites;
        Assert.Equal("Gamma", Assert.Single(vm.Games).Name);
        vm.FilterIndex = (int)LibraryFilter.Hidden;
        Assert.Equal("Beta", Assert.Single(vm.Games).Name);

        // Both flags survive a restart.
        using var restarted = Provider();
        var reloaded = await restarted.LoadLibraryAsync();
        Assert.Equal(["Gamma", "Alpha"], reloaded.Games.Select(c => c.Name));
    }

    [Fact]
    public async Task CardsShowManagedGamesThatNeedAttentionAndAvailableUpdates()
    {
        const string release = """
                               [{"tag_name":"v0.9.5","draft":false,"prerelease":false,
                                 "assets":[{"name":"Optiscaler_0.9.5.7z","browser_download_url":"https://github.com/x/y.7z"}]}]
                               """;
        using var provider = Provider(new StubHttpHandler(request => request.RequestUri!.Host == "api.github.com"
                                          ? new HttpResponseMessage(HttpStatusCode.OK)
                                          {
                                              Content = new StringContent(release)
                                          }
                                          : new HttpResponseMessage(HttpStatusCode.NotFound)));
        var managed = Game("Managed");
        var vm = await provider.LoadLibraryAsync(managed, Game("Plain"));
        var binaries = Folder("games", "Managed", "Binaries");
        var exe = Path.Combine(binaries, "game.exe");
        TestData.WritePe(exe, false);
        var package = TestData.WritePackage(Path.Combine(Root, "package"), "[Upscalers]\n");
        var installer = provider.GetRequiredService<IGameInstallationService>();
        var plan = await installer.PreviewInstallationAsync(exe, package, "dxgi.dll", null, Ct);
        await installer.ExecuteInstallationPlanAsync(plan with { Version = "v0.9.4" }, Ct);

        await vm.RefreshLibraryStatusAsync();
        Assert.Equal("OptiScaler · v0.9.4", Card(vm, "Managed").StatusText);
        Assert.False(Card(vm, "Plain").HasStatus);

        await vm.CheckForUpdatesCommand.ExecuteAsync(null);
        vm.FilterIndex = (int)LibraryFilter.Updates;
        Assert.Equal("Update available · v0.9.5", Assert.Single(vm.Games).StatusText);

        // A launcher's "verify files" removes the proxy DLL.
        File.Delete(Path.Combine(binaries, "dxgi.dll"));
        await vm.RefreshLibraryStatusAsync();
        vm.FilterIndex = (int)LibraryFilter.NeedsAttention;
        var card = Assert.Single(vm.Games);
        Assert.True(card.IsWarning);
        Assert.Equal("Needs attention · files changed", card.StatusText);
    }

    [Fact]
    public async Task CachedWikiEntriesMarkTestedGames()
    {
        using (var online = Provider(StubHttpHandler.Text(CompatibilityListTests.Wiki)))
        {
            await online.GetRequiredService<CompatibilityListService>().GetIndexAsync(cancellationToken: Ct);
        }

        // Loading the library never downloads; it reads what an earlier refresh saved.
        var offline = StubHttpHandler.Offline();
        using var provider = Provider(offline);
        var vm = await provider.LoadLibraryAsync(Game("Cyberpunk 2077"), Game("EA Sports WRC"), Game("Unknown"));

        vm.FilterIndex = (int)LibraryFilter.Tested;
        Assert.Equal("Tested on the wiki", Assert.Single(vm.Games).StatusText);
        vm.FilterIndex = (int)LibraryFilter.All;
        Assert.True(Card(vm, "EA Sports WRC").IsWarning);
        Assert.Equal(0, offline.Requests);
    }

    [Fact]
    public void UnconfirmedWikiRowsAreLabelledButNotTested()
    {
        var entry = new CompatibilityEntry { GameName = "Maybe", Status = CompatibilityStatus.Unconfirmed };
        var card = new GameCardViewModel(TestData.Game("/games/maybe", "Maybe"), null, entry, null);

        Assert.False(card.Matches(LibraryFilter.Tested));
        Assert.Equal("Unconfirmed on the wiki", card.StatusText);
    }

    private static GameCardViewModel Card(GamesViewModel vm, string name)
    {
        return vm.Games.Single(card => card.Name == name);
    }

    private sealed class MovedGameScanner(string folder) : IGameScanner
    {
        public GamePlatform Platform => GamePlatform.Steam;

        public Task<ScanResult> ScanGamesAsync(ScanContext context, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ScanResult
            {
                Games =
                [
                    new DiscoveredGame { Name = "Game", Platform = Platform, ExternalId = "42", InstallPath = folder }
                ]
            });
        }
    }

    /// <summary>An in-memory library whose saves can be made to fail like a full disk.</summary>
    private sealed class FakeCatalog(GameCatalog catalog, bool failSave) : IGameCatalogRepository
    {
        public Task<GameCatalog> LoadGameCatalogAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(catalog);
        }

        public Task SaveGameCatalogAsync(GameCatalog updated, CancellationToken cancellationToken = default)
        {
            if (failSave) throw new IOException("disk full");

            catalog = updated;

            return Task.CompletedTask;
        }
    }
}
