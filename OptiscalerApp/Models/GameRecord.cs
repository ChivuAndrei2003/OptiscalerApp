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

    public bool IsFavorite { get; set; }

    /// <summary>Hidden games stay in the catalog, so a rescan does not add them back.</summary>
    public bool IsHidden { get; set; }

    /// <summary>A copy whose fields and installation list can change without touching the published record.</summary>
    public GameRecord Clone()
    {
        return new GameRecord
        {
            Id = Id,
            Name = Name,
            Platform = Platform,
            ExternalId = ExternalId,
            Installations = Installations.ToList(),
            CoverImage = CoverImage,
            IsFavorite = IsFavorite,
            IsHidden = IsHidden
        };
    }
}

public sealed class GameCatalog
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    /// <summary>The complete set of persisted game records.</summary>
    public List<GameRecord> Games { get; set; } = [];
}