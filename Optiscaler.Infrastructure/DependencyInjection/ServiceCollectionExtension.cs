using Microsoft.Extensions.DependencyInjection;
using Optiscaler.Core.Abstractions;
using Optiscaler.Core.Management;
using Optiscaler.Core.Games;
using Optiscaler.Infrastructure.Management;
using Optiscaler.Infrastructure.Paths;
using Optiscaler.Infrastructure.Persistence;
using Optiscaler.Infrastructure.Scanning;

namespace Optiscaler.Infrastructure.DependencyInjection;

/// <summary>
/// Registers infrastructure services with the application's dependency-injection container.
/// </summary>
public static class ServiceCollectionExtension
{
    public static IServiceCollection AddOptiscalerInfrastructure(this IServiceCollection services)
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

        return services;
    }
}
