using Optiscaler.Core.Configuration;
using Optiscaler.Core.Games;

namespace Optiscaler.Core.Abstractions;

public interface IGameCatalogRepository
{
    Task<GameCatalog> LoadGameCatalog_Async(CancellationToken cancellationToken = default);

    Task SaveGameCatalog_Async(GameCatalog catalog, CancellationToken cancellationToken = default);
}

public interface IAppConfigurationRepository
{
    Task<AppConfiguration> LoadAppConfiguration_Async(CancellationToken cancellationToken = default);

    Task SaveAppConfiguration_Async(AppConfiguration configuration, CancellationToken cancellationToken = default);
}
