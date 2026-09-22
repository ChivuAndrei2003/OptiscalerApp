using OptiscalerApp.Models;
using OptiscalerApp.Paths;

namespace OptiscalerApp.Persistence;

/// <summary>
///     Persists the complete game catalog as a versioned JSON document.
/// </summary>
public sealed class JsonGameCatalogRepository : IGameCatalogRepository
{
    private readonly AtomicJsonFile<GameCatalog> _store;

    public JsonGameCatalogRepository(IAppPaths paths)
    {
        _store = new AtomicJsonFile<GameCatalog>(
                                                 paths.GamesFilePath,
                                                 OptiscalerJsonContext.Default.GameCatalog, ValidateGameCatalog);
    }

    /// <exception cref="InvalidDataException">
    ///     Neither the document nor its backup is a valid catalog in the supported schema.
    /// </exception>
    public async Task<GameCatalog> LoadGameCatalog_Async(CancellationToken cancellationToken = default)
    {
        return await _store.LoadJsonFile_Async(cancellationToken).ConfigureAwait(false) ?? new GameCatalog();
    }

    public Task SaveGameCatalog_Async(GameCatalog catalog, CancellationToken cancellationToken = default)
    {
        return _store.SaveJsonFile_Async(catalog, cancellationToken);
    }

    // JSON can contain null even where the model declares a non-nullable reference.
    private static void ValidateGameCatalog(GameCatalog catalog)
    {
        if (catalog.SchemaVersion != GameCatalog.CurrentSchemaVersion)
            throw new InvalidDataException($"games.json uses unsupported schema {catalog.SchemaVersion}.");

        if (catalog.Games is null || catalog.Games.Any(IsInvalidGameRecord))
            throw new InvalidDataException("games.json contains an invalid game or installation.");

        if (catalog.Games.Select(game => game.Id).Distinct().Count() != catalog.Games.Count)
            throw new InvalidDataException("games.json contains duplicate game IDs.");
    }

    private static bool IsInvalidGameRecord(GameRecord? game)
    {
        return game?.Id is null || string.IsNullOrWhiteSpace(game.Id.Value) ||
               string.IsNullOrWhiteSpace(game.Name) || !Enum.IsDefined(game.Platform) ||
               game.Installations is null || game.Installations.Any(IsInvalidGameInstallation);
    }

    private static bool IsInvalidGameInstallation(GameInstallation? installation)
    {
        return installation is null || string.IsNullOrWhiteSpace(installation.RootPath) ||
               !Path.IsPathFullyQualified(installation.RootPath);
    }
}
