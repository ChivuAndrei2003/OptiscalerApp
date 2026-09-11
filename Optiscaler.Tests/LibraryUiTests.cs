using Microsoft.Extensions.DependencyInjection;
using Optiscaler.Core.Abstractions;
using Optiscaler.Core.Configuration;
using Optiscaler.Core.Games;
using Optiscaler.Infrastructure.DependencyInjection;
using Optiscaler.Infrastructure.Paths;
using OptiscalerApp.ViewModels;
using Xunit;

namespace Optiscaler.Tests;

public sealed class LibraryUiTests : IDisposable
{
    private readonly string _root = Path.Combine(OperatingSystem.IsMacOS() ? "/private/tmp" : Path.GetTempPath(), "Optiscaler-library-ui-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    [Fact]
    public async Task RepeatedScansPersistNewGamesAndPreserveExistingPreferences()
    {
        var gamesRoot = Path.Combine(_root, "games");
        var first = Directory.CreateDirectory(Path.Combine(gamesRoot, "First")).FullName;
        Directory.CreateDirectory(Path.Combine(gamesRoot, "Second"));
        using var provider = new ServiceCollection().AddOptiscalerInfrastructure()
            .AddSingleton<IAppPaths>(new AppPaths(Path.Combine(_root, "data")))
            .AddSingleton<ProfilesViewModel>().AddSingleton<MainWindowViewModel>().BuildServiceProvider();
        var ct = TestContext.Current.CancellationToken;
        var repository = provider.GetRequiredService<IGameCatalogRepository>();
        await repository.SaveGameCatalog_Async(new GameCatalog
        {
            Games = [new GameRecord
            {
                Id = GameId.Create(GamePlatform.Custom, null, first), Name = "My custom name", Platform = GamePlatform.Custom,
                Preferences = new GameUserPreferences { IsFavorite = true },
                Installations = [new GameInstallation { RootPath = first }]
            }]
        }, ct);
        await provider.GetRequiredService<IAppConfigurationRepository>().SaveAppConfiguration_Async(new AppConfiguration
        {
            ScanSourceSettings = new ScanSourceSettings { EnabledPlatforms = [GamePlatform.Custom], CustomFolders = [gamesRoot] }
        }, ct);
        var vm = provider.GetRequiredService<MainWindowViewModel>();
        await vm.LoadGameLibrary_Async(ct);
        await vm.ScanGameLibrary_Async();
        await vm.ScanGameLibrary_Async();
        Assert.Equal(2, vm.Games.Count);
        var saved = await repository.LoadGameCatalog_Async(ct);
        Assert.Equal(2, saved.Games.Count);
        var original = saved.Games.Single(g => g.Name == "My custom name");
        Assert.True(original.Preferences.IsFavorite);
        Assert.Single(original.Installations);
        Assert.True(vm.CanAddGames);
    }

    [Fact]
    public async Task FailedLibraryWriteDoesNotPublishDiscoveredGames()
    {
        var gamesRoot = Directory.CreateDirectory(Path.Combine(_root, "games")).FullName;
        Directory.CreateDirectory(Path.Combine(gamesRoot, "New game"));
        using var provider = new ServiceCollection().AddOptiscalerInfrastructure()
            .AddSingleton<IAppPaths>(new AppPaths(Path.Combine(_root, "data")))
            .AddSingleton<IGameCatalogRepository>(new FailingCatalogRepository())
            .AddSingleton<ProfilesViewModel>().AddSingleton<MainWindowViewModel>().BuildServiceProvider();
        await provider.GetRequiredService<IAppConfigurationRepository>().SaveAppConfiguration_Async(new AppConfiguration
        {
            ScanSourceSettings = new ScanSourceSettings { EnabledPlatforms = [GamePlatform.Custom], CustomFolders = [gamesRoot] }
        }, TestContext.Current.CancellationToken);
        var vm = provider.GetRequiredService<MainWindowViewModel>();
        await vm.LoadGameLibrary_Async(TestContext.Current.CancellationToken);
        await vm.ScanGameLibrary_Async();
        Assert.Empty(vm.Games);
        Assert.Contains("disk full", vm.StatusMessage);
        Assert.True(vm.CanAddGames);
    }

    private sealed class FailingCatalogRepository : IGameCatalogRepository
    {
        public Task<GameCatalog> LoadGameCatalog_Async(CancellationToken cancellationToken = default) => Task.FromResult(new GameCatalog());
        public Task SaveGameCatalog_Async(GameCatalog catalog, CancellationToken cancellationToken = default) => throw new IOException("disk full");
    }
}
