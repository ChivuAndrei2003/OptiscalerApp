using Optiscaler.Core.Abstractions;
using Optiscaler.Core.Games;

namespace Optiscaler.Infrastructure.Persistence;

public sealed class JsonGameCatalogRepository : IGameCatalogRepository
{
    private readonly AtomicJsonFIle<GameCatalog> _store;

    public JsonGameCatalogRepository(IAppPaths paths)
    {
        _store = new AtomicJsonFIle<GameCatalog>(
            paths.GamesFilePath,
            OptiscalerJsonContext.Default.GameCatalog);
    }

    public async Task<GameCatalog> LoadAsync(CancellationToken cancellationToken = default)
    {
        var catalog = await _store.LoadAsync(cancellationToken).ConfigureAwait(false) ?? new GameCatalog();

        if (catalog.SchemaVersion > GameCatalog.CurrentSchemaVersion)
            throw new InvalidDataException($"games.json uses schema {catalog.SchemaVersion}," +
                                           $" but this version of the application only supports up to {GameCatalog.CurrentSchemaVersion}.");

        return catalog;
    }

    public Task SaveAsync(GameCatalog catalog, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        catalog.SchemaVersion = GameCatalog.CurrentSchemaVersion;
        return _store.SaveAsync(catalog, cancellationToken);
    }
}