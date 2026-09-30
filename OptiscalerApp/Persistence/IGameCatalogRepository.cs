using OptiscalerApp.Models;

namespace OptiscalerApp.Persistence;

public interface IGameCatalogRepository
{
    Task<GameCatalog> LoadGameCatalogAsync(CancellationToken cancellationToken = default);

    Task SaveGameCatalogAsync(GameCatalog catalog, CancellationToken cancellationToken = default);
}