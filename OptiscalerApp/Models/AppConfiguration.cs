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

    public ScanSourceSettings ScanSourceSettings { get; set; } = new();
}