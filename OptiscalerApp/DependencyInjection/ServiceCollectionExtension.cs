using Microsoft.Extensions.DependencyInjection;
using OptiscalerApp.Models;
using OptiscalerApp.Management;
using OptiscalerApp.Paths;
using OptiscalerApp.Persistence;
using OptiscalerApp.Scanning;
using OptiscalerApp.ViewModels;
using OptiscalerApp.Views;

namespace OptiscalerApp.DependencyInjection;

/// <summary>
/// Registers application services with the application's dependency-injection container.
/// </summary>
public static class ServiceCollectionExtension
{
    public static IServiceCollection AddOptiscalerServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // These services are singletons because they represent one process-wide data root and
        // contain synchronization used to serialize access to shared persistence files.
        services.AddSingleton<IAppPaths, AppPaths>();
        services.AddSingleton<IGameCatalogRepository, JsonGameCatalogRepository>();
        services.AddSingleton<IAppConfigurationRepository, JsonAppConfigurationRepository>();

        // Factories keep optional test roots unset; DI would otherwise inject an empty IEnumerable<string>.
        services.AddSingleton<IGameScanner>(_ => new SteamScanner());
        services.AddSingleton<IGameScanner>(_ => new EpicScanner());
        services.AddSingleton<IGameScanner>(_ => new HeroicScanner(GamePlatform.Epic));
        services.AddSingleton<IGameScanner>(_ => new HeroicScanner(GamePlatform.Gog));
        services.AddSingleton<IGameScanner>(_ => new GogScanner());
        services.AddSingleton<IGameScanner>(_ => new EaScanner());
        services.AddSingleton<IGameScanner>(_ => new UbisoftScanner());
        services.AddSingleton<IGameScanner>(_ => new BattleNetScanner());
        services.AddSingleton<IGameScanner>(_ => new XboxScanner());
        services.AddSingleton<IGameScanner>(_ => new LutrisScanner());
        services.AddSingleton<IGameScanner, CustomFolderScanner>();
        services.AddSingleton<GameDiscoveryCoordinator>();
        services.AddSingleton<IGameAnalyzer, GameAnalyzer>();
        services.AddSingleton<IProfileRepository, JsonProfileRepository>();
        services.AddSingleton<IGameInstallationService, GameInstallationService>();
        services.AddSingleton(_ => PackageDownloadService.CreateClient());
        services.AddSingleton<PackageDownloadService>();
        services.AddSingleton<GameArtworkService>();
        services.AddSingleton<CompatibilityListService>();

        return services;
    }

    /// <summary>Registers the pages and the window services they use for pickers, the clipboard and launching.</summary>
    public static IServiceCollection AddOptiscalerViewModels(this IServiceCollection services)
    {
        services.AddSingleton<TopLevelServices>();
        services.AddSingleton<IFileDialogs>(provider => provider.GetRequiredService<TopLevelServices>());
        services.AddSingleton<IShellActions>(provider => provider.GetRequiredService<TopLevelServices>());
        services.AddSingleton<GamesViewModel>();
        services.AddSingleton<ProfilesViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<MainWindowViewModel>();

        return services;
    }
}