using Microsoft.Extensions.DependencyInjection;
using Optiscaler.Core.Abstractions;
using Optiscaler.Infrastructure.Paths;
using Optiscaler.Infrastructure.Persistence;
using Optiscaler.Infrastructure.Scanning;

namespace Optiscaler.Infrastructure.DependencyInjection;

public static class ServiceCollectionExtension
{
    public static IServiceCollection AddOptiscalerInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<IAppPaths, AppPaths>();
        services.AddSingleton<IGameCatalogRepository, JsonGameCatalogRepository>();
        services.AddSingleton<IAppConfigurationRepository, JsonAppConfigurationRepository>();

        //
        services.AddSingleton<GameDiscoveryCoordinator>();

        return services;
    }
}