using Optiscaler.Core.Abstractions;
using Optiscaler.Core.Configuration;

namespace Optiscaler.Infrastructure.Persistence;

public sealed class JsonAppConfigurationRepository : IAppConfigurationRepository
{
    private readonly AtomicJsonFile<AppConfiguration> _store;

    public JsonAppConfigurationRepository(IAppPaths paths)
    {
        _store = new AtomicJsonFile<AppConfiguration>(
            paths.ConfigurationFilePath,
            OptiscalerJsonContext.Default.AppConfiguration);
    }

    public async Task<AppConfiguration> LoadAsync(CancellationToken cancellationToken = default)
    {
        var configuration = await _store.LoadAsync(cancellationToken).ConfigureAwait(false)
                            ?? new AppConfiguration();
        if (configuration.SchemaVersion > AppConfiguration.CurrentSchemaVersion)
            throw new InvalidDataException($"config.json uses a newer schema :{configuration.SchemaVersion}");

        return configuration;
    }

    public Task SaveAsync(AppConfiguration configuration, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        configuration.SchemaVersion = AppConfiguration.CurrentSchemaVersion;
        return _store.SaveAsync(configuration, cancellationToken);
    }
}
