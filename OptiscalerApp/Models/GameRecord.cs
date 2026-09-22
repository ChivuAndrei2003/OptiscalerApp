namespace OptiscalerApp.Models;

public sealed class GameInstallation
{
    public required string RootPath { get; set; }

    public string? PrimaryExecutablePath { get; set; }
}

public sealed class GameRecord
{
    public required GameId Id { get; set; }

    public required string Name { get; set; }

    public required GamePlatform Platform { get; set; }

    public string? ExternalId { get; set; }

    public List<GameInstallation> Installations { get; set; } = [];

    public string? CoverImage { get; set; }
}

public sealed class GameCatalog
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    /// <summary>The complete set of persisted game records.</summary>
    public List<GameRecord> Games { get; set; } = [];
}