using Optiscaler.Core.Abstractions;
using Optiscaler.Core.Games;

namespace Optiscaler.Infrastructure.Persistence;

/// <summary>
/// Persists the complete game catalog as a versioned JSON document.
/// </summary>
public sealed class JsonGameCatalogRepository : IGameCatalogRepository
{
    private readonly AtomicJsonFile<GameCatalog> _store;

    public JsonGameCatalogRepository(IAppPaths paths)
    {
        _store = new AtomicJsonFile<GameCatalog>(
            paths.GamesFilePath,
            OptiscalerJsonContext.Default.GameCatalog);
    }

    /// <exception cref="InvalidDataException">
    /// The document uses an unsupported schema or contains invalid catalog data.
    /// </exception>
    public async Task<GameCatalog> LoadAsync(CancellationToken cancellationToken = default)
    {
        var catalog = await _store.LoadAsync(cancellationToken).ConfigureAwait(false) ?? new GameCatalog();

        Validate(catalog);

        return catalog;
    }

    private static void Validate(GameCatalog catalog)
    {
        if (catalog.SchemaVersion != GameCatalog.CurrentSchemaVersion)
            throw new InvalidDataException(
                $"games.json uses unsupported schema {catalog.SchemaVersion}.");

        if (catalog.Games is null || catalog.Games.Any(game =>
                game is null || game.Id is null ||
                string.IsNullOrWhiteSpace(game.Id.Value) ||
                string.IsNullOrWhiteSpace(game.Name) ||
                game.Preferences is null ||
                game.Installations is null ||
                game.Installations.Any(installation =>
                    installation is null ||
                    string
                        .IsNullOrWhiteSpace(installation
                            .RootPath) ||
                    installation
                            .ExecutableCandidates
                        is
                        null)))
            throw new InvalidDataException("games.json contains an invalid game or installation.");
    }

    /// <summary>
    /// Saves a catalog in the supported schema. Other versions require an explicit migration.
    /// </summary>
    public Task SaveAsync(GameCatalog catalog, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        Validate(catalog);
        return _store.SaveAsync(catalog, cancellationToken);
    }
}