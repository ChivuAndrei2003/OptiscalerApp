using Optiscaler.Core.Configuration;
using Optiscaler.Core.Games;

namespace Optiscaler.Core.Abstractions;

/// <summary>
/// Repository for the game catalog.
/// </summary>
public interface IGameCatalogRepository
{
    Task<GameCatalog> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(GameCatalog catalog, CancellationToken cancellationToken = default);
}

/// <summary>
/// Repository for the application configuration.
/// </summary>
public interface IAppConfigurationRepository
{
    Task<AppConfiguration> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(AppConfiguration configuration, CancellationToken cancellationToken = default);
}