using System.Net;
using Microsoft.Extensions.DependencyInjection;
using OptiscalerApp.DependencyInjection;
using OptiscalerApp.Management;
using OptiscalerApp.Models;
using OptiscalerApp.Paths;
using OptiscalerApp.Persistence;
using OptiscalerApp.ViewModels;
using Xunit;

namespace Optiscaler.Tests;

public sealed class LibraryFeatureTests : IDisposable
{
    private readonly string _root = Path.Combine(OperatingSystem.IsMacOS() ? "/private/tmp" : Path.GetTempPath(),
                                                 "Optiscaler-library-features-" + Guid.NewGuid().ToString("N"));

    private CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private ServiceProvider Provider(HttpMessageHandler? http = null)
    {
        return new ServiceCollection().AddOptiscalerServices()
            .AddSingleton<IAppPaths>(new AppPaths(Path.Combine(_root, "data")))
            .AddSingleton(new HttpClient(http ?? StubHttpHandler.Offline()))
            .AddSingleton<ProfilesViewModel>().AddSingleton<MainWindowViewModel>().BuildServiceProvider();
    }

    private async Task<(MainWindowViewModel Vm, GameRecord[] Games)> Library(ServiceProvider provider,
                                                                             params string[] names)
    {
        var games = names.Select(name =>
        {
            var folder = Directory.CreateDirectory(Path.Combine(_root, "games", name)).FullName;

            return new GameRecord
            {
                Id = GameId.Create(GamePlatform.Manual, null, folder),
                Name = name,
                Platform = GamePlatform.Manual,
                Installations = [new GameInstallation { RootPath = folder }]
            };
        }).ToArray();
        await provider.GetRequiredService<IGameCatalogRepository>()
            .SaveGameCatalog_Async(new GameCatalog { Games = games.ToList() }, Ct);
        var vm = provider.GetRequiredService<MainWindowViewModel>();
        await vm.LoadGameLibrary_Async(Ct);

        return (vm, games);
    }

    [Fact]
    public async Task FavoritesStayOnTopAndHiddenGamesOnlyShowInTheirFilter()
    {
        using var provider = Provider();
        var (vm, games) = await Library(provider, "Alpha", "Beta", "Gamma");

        await vm.SetFavorite_Async(games[2].Id, true);
        await vm.SetHidden_Async(games[1].Id, true);
        Assert.Equal(["Gamma", "Alpha"], vm.Games.Select(c => c.Name));
        Assert.Contains("1 hidden", vm.LibrarySummary);

        vm.FilterIndex = (int)LibraryFilter.Favorites;
        Assert.Equal("Gamma", Assert.Single(vm.Games).Name);
        vm.FilterIndex = (int)LibraryFilter.Hidden;
        Assert.Equal("Beta", Assert.Single(vm.Games).Name);

        // Both flags survive a restart.
        using var restarted = Provider();
        var reloaded = restarted.GetRequiredService<MainWindowViewModel>();
        await reloaded.LoadGameLibrary_Async(Ct);
        Assert.Equal(["Gamma", "Alpha"], reloaded.Games.Select(c => c.Name));
    }

    [Fact]
    public async Task RemovingAGameKeepsItsFiles()
    {
        using var provider = Provider();
        var (vm, games) = await Library(provider, "Alpha", "Beta");

        await vm.RemoveGame_Async(games[0].Id);

        Assert.Equal("Beta", Assert.Single(vm.Games).Name);
        Assert.True(Directory.Exists(games[0].Installations[0].RootPath));
        Assert.Contains("files were not changed", vm.StatusMessage);
        var saved = await provider.GetRequiredService<IGameCatalogRepository>().LoadGameCatalog_Async(Ct);
        Assert.Equal("Beta", Assert.Single(saved.Games).Name);
    }

    [Fact]
    public async Task CardsShowManagedGamesThatNeedAttentionAndAvailableUpdates()
    {
        var release = """
                      [{"tag_name":"v0.9.5","draft":false,"prerelease":false,
                        "assets":[{"name":"Optiscaler_0.9.5.7z","browser_download_url":"https://github.com/x/y.7z"}]}]
                      """;
        using var provider = Provider(new StubHttpHandler(request =>
                                                              new HttpResponseMessage(request.RequestUri!.Host ==
                                                                  "api.github.com"
                                                                      ? HttpStatusCode.OK
                                                                      : HttpStatusCode.NotFound)
                                                              {
                                                                  Content = new StringContent(release)
                                                              }));
        var (vm, games) = await Library(provider, "Managed", "Plain");
        var folder = games[0].Installations[0].RootPath;
        var exe = Path.Combine(folder, "Binaries", "game.exe");
        var package = Directory.CreateDirectory(Path.Combine(_root, "package")).FullName;
        Directory.CreateDirectory(Path.GetDirectoryName(exe)!);
        InstallationTests.WritePe(exe, false);
        InstallationTests.WritePe(Path.Combine(package, "OptiScaler.dll"), true);
        await File.WriteAllTextAsync(Path.Combine(package, "OptiScaler.ini"), "[Upscalers]\n", Ct);
        var installer = provider.GetRequiredService<IGameInstallationService>();
        var plan = await installer.PreviewInstallation_Async(exe, package, "dxgi.dll", null, Ct);
        await installer.ExecuteInstallationPlan_Async(plan with { Version = "v0.9.4" }, Ct);

        await vm.RefreshLibraryStatus_Async();
        var managed = vm.Games.Single(c => c.Name == "Managed");
        Assert.Equal("OptiScaler · v0.9.4", managed.StatusText);
        Assert.False(vm.Games.Single(c => c.Name == "Plain").HasStatus);

        await vm.CheckForUpdates_Async();
        Assert.Contains("1 managed games can be updated", vm.StatusMessage);
        vm.FilterIndex = (int)LibraryFilter.Updates;
        Assert.Equal("Update available · v0.9.5", Assert.Single(vm.Games).StatusText);

        // A launcher's "verify files" removes the proxy DLL.
        File.Delete(Path.Combine(folder, "Binaries", "dxgi.dll"));
        await vm.RefreshLibraryStatus_Async();
        vm.FilterIndex = (int)LibraryFilter.NeedsAttention;
        var card = Assert.Single(vm.Games);
        Assert.True(card.IsWarning);
        Assert.Equal("Needs attention · files changed", card.StatusText);
        Assert.Contains("1 need attention", vm.LibrarySummary);
    }

    [Fact]
    public async Task CachedWikiEntriesMarkTestedGames()
    {
        using (var online = Provider(StubHttpHandler.Text(CompatibilityListTests.Wiki)))
        {
            await online.GetRequiredService<CompatibilityListService>().GetIndex_Async(cancellationToken: Ct);
        }

        // Loading the library never downloads; it reads what an earlier refresh saved.
        var offline = StubHttpHandler.Offline();
        using var provider = Provider(offline);
        var (vm, _) = await Library(provider, "Cyberpunk 2077", "EA Sports WRC", "Unknown");

        vm.FilterIndex = (int)LibraryFilter.Tested;
        Assert.Equal("Tested on the wiki", Assert.Single(vm.Games).StatusText);
        vm.FilterIndex = (int)LibraryFilter.All;
        Assert.True(vm.Games.Single(c => c.Name == "EA Sports WRC").IsWarning);
        Assert.Equal(0, offline.Requests);
    }
}
