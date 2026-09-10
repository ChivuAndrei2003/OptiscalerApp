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
                                                 OptiscalerJsonContext.Default.GameCatalog, ValidateGameCatalog);
    }

    /// <exception cref="InvalidDataException">
    /// The document uses an unsupported schema or contains invalid catalog data.
    /// </exception>
    public async Task<GameCatalog> LoadGameCatalog_Async(CancellationToken cancellationToken = default)
    {
        var catalog = await _store.LoadJsonFile_Async(cancellationToken).ConfigureAwait(false) ?? new GameCatalog();

        ValidateGameCatalog(catalog);

        return catalog;
    }

    private static void ValidateGameCatalog(GameCatalog catalog)
    {
        if (catalog.SchemaVersion != GameCatalog.CurrentSchemaVersion)
            throw new InvalidDataException(
                                           $"games.json uses unsupported schema {catalog.SchemaVersion}.");

        var games = AsPotentiallyNullJsonValue(catalog.Games);

        if (games is null || games.Any(IsInvalidGameRecord))
            throw new InvalidDataException("games.json contains an invalid game or installation.");

        if (games.Select(game => game.Id).Distinct().Count() != games.Count)
            throw new InvalidDataException("games.json contains duplicate game IDs.");
    }

    private static bool IsInvalidGameRecord(GameRecord deserializedGame)
    {
        var game = AsPotentiallyNullJsonValue(deserializedGame);

        if (game is null) return true;

        var id = AsPotentiallyNullJsonValue(game.Id);
        var preferences = AsPotentiallyNullJsonValue(game.Preferences);
        var installations = AsPotentiallyNullJsonValue(game.Installations);

        return id is null || string.IsNullOrWhiteSpace(id.Value) ||
               string.IsNullOrWhiteSpace(game.Name) || !Enum.IsDefined(game.Platform) ||
               preferences is null || installations is null ||
               installations.Any(IsInvalidGameInstallation);
    }

    private static bool IsInvalidGameInstallation(GameInstallation deserializedInstallation)
    {
        var installation = AsPotentiallyNullJsonValue(deserializedInstallation);

        return installation is null || string.IsNullOrWhiteSpace(installation.RootPath) ||
               !Path.IsPathFullyQualified(installation.RootPath) ||
               AsPotentiallyNullJsonValue(installation.ExecutableCandidates) is null;
    }

    // JSON can contain null even when the domain model declares a reference as non-nullable.
    private static T? AsPotentiallyNullJsonValue<T>(T value) where T : class
    {
        return value;
    }

    /// <summary>
    /// Saves a catalog in the supported schema. Other versions require an explicit migration.
    /// </summary>
    public Task SaveGameCatalog_Async(GameCatalog catalog, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ValidateGameCatalog(catalog);

        return _store.SaveJsonFile_Async(catalog, cancellationToken);
    }
}
