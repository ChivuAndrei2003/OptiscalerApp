using Optiscaler.Core.Configuration;
using Optiscaler.Core.Games;

namespace Optiscaler.Core.Abstractions;

public interface IGameCatalogRepository
{
    Task<GameCatalog> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(GameCatalog catalog, CancellationToken cancellationToken = default);
}

public interface IAppConfigurationRepository
{
    Task<AppConfiguration> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(AppConfiguration configuration, CancellationToken cancellationToken = default);
}