namespace OptiscalerApp.Models;

/// <summary>
/// Stores the persistent configuration for automatic and custom game discovery sources.
/// </summary>
public sealed class ScanSourceSettings
{
    public HashSet<GamePlatform> EnabledPlatforms { get; set; } =
    [
        GamePlatform.Steam,
        GamePlatform.Epic,
        GamePlatform.Gog,
        GamePlatform.Xbox,
        GamePlatform.Ea,
        GamePlatform.BattleNet,
        GamePlatform.Ubisoft,
        GamePlatform.Lutris
    ];


    public List<string> CustomFolders { get; set; } = [];

    public List<string> AllowedDriveRoots { get; set; } = [];
}

public sealed class AppConfiguration
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public bool AutoScan { get; set; }

    /// <summary>Opens the game manager on the beta channel instead of stable releases.</summary>
    public bool PreferBetaReleases { get; set; }

    /// <summary>Proxy filename preselected when installing OptiScaler into a game.</summary>
    public string DefaultProxyDll { get; set; } = "dxgi.dll";

    public ScanSourceSettings ScanSourceSettings { get; set; } = new();
}