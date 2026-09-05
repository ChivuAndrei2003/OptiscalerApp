using Optiscaler.Core.Abstractions;
using Optiscaler.Core.Configuration;

namespace Optiscaler.Infrastructure.Persistence;

/// <summary>
/// Persists application preferences as a versioned JSON document.
/// </summary>
public sealed class JsonAppConfigurationRepository : IAppConfigurationRepository
{
    private readonly AtomicJsonFile<AppConfiguration> _store;

    public JsonAppConfigurationRepository(IAppPaths paths)
    {
        _store = new AtomicJsonFile<AppConfiguration>(
            paths.ConfigurationFilePath,
            OptiscalerJsonContext.Default.AppConfiguration);
    }

    /// <exception cref="InvalidDataException">
    /// The document uses an unsupported schema or contains invalid scan settings.
    /// </exception>
    public async Task<AppConfiguration> LoadAsync(CancellationToken cancellationToken = default)
    {
        var configuration = await _store.LoadAsync(cancellationToken).ConfigureAwait(false)
                            ?? new AppConfiguration();

        Validate(configuration);

        return configuration;
    }

    /// <summary>
    /// Saves configuration in the supported schema. Other versions require an explicit migration.
    /// </summary>
    public Task SaveAsync(AppConfiguration configuration, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        Validate(configuration);
        return _store.SaveAsync(configuration, cancellationToken);
    }

    private static void Validate(AppConfiguration configuration)
    {
        if (configuration.SchemaVersion != AppConfiguration.CurrentSchemaVersion)
            throw new InvalidDataException($"config.json uses unsupported schema {configuration.SchemaVersion}.");

        var sources = configuration.ScanSourceSettings;
        if (sources is null || sources.EnabledPlatforms is null ||
            sources.CustomFolders is null || sources.AllowedDriveRoots is null)
            throw new InvalidDataException("config.json contains invalid scan source settings.");
    }
}