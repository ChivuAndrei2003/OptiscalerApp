using OptiscalerApp.Management;
using OptiscalerApp.Paths;
using OptiscalerApp.Models;

namespace OptiscalerApp.Persistence;

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
                                                      OptiscalerJsonContext.Default.AppConfiguration,
                                                      ValidateAppConfiguration);
    }

    /// <exception cref="InvalidDataException">
    /// Neither the document nor its backup is a valid configuration in the supported schema.
    /// </exception>
    public async Task<AppConfiguration> LoadAppConfiguration_Async(CancellationToken cancellationToken = default)
    {
        return await _store.LoadJsonFile_Async(cancellationToken).ConfigureAwait(false) ?? new AppConfiguration();
    }

    public Task SaveAppConfiguration_Async(AppConfiguration configuration,
                                           CancellationToken cancellationToken = default)
    {
        return _store.SaveJsonFile_Async(configuration, cancellationToken);
    }

    private static void ValidateAppConfiguration(AppConfiguration configuration)
    {
        if (configuration.SchemaVersion != AppConfiguration.CurrentSchemaVersion)
            throw new InvalidDataException($"config.json uses unsupported schema {configuration.SchemaVersion}.");

        var sources = configuration.ScanSourceSettings;

        if (sources is null || sources.EnabledPlatforms is null ||
            sources.CustomFolders is null || sources.AllowedDriveRoots is null)
            throw new InvalidDataException("config.json contains invalid scan source settings.");

        if (sources.EnabledPlatforms.Any(platform => !Enum.IsDefined(platform)) ||
            sources.CustomFolders.Concat(sources.AllowedDriveRoots)
                .Any(path => string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)))
            throw new InvalidDataException("Scan sources must use known platforms and absolute paths.");

        if (!GameInstallationService.ProxyNames.Contains(configuration.DefaultProxyDll))
            throw new InvalidDataException("config.json contains an unsupported default proxy DLL.");
    }
}