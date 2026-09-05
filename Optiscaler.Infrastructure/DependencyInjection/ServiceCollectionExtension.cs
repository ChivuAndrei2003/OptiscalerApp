using Microsoft.Extensions.DependencyInjection;
using Optiscaler.Core.Abstractions;
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

        services.AddSingleton<GameDiscoveryCoordinator>();

        return services;
    }
}