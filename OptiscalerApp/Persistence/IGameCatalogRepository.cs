using OptiscalerApp.Models;

namespace OptiscalerApp.Persistence;

public interface IGameCatalogRepository
{
    Task<GameCatalog> LoadGameCatalog_Async(CancellationToken cancellationToken = default);

    Task SaveGameCatalog_Async(GameCatalog catalog, CancellationToken cancellationToken = default);
}
