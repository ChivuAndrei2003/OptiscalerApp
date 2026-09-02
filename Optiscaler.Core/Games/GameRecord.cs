namespace Optiscaler.Core.Games;

public sealed record GameInstallation
{
    public required string RootPath { get; init; }

    public string? PrimaryExecutablePath { get; init; }

    public List<string> ExecutableCandidates { get; init; } = [];
}

public sealed record GameUserPreferences
{
    public bool isHidden { get; init; }

    public bool isFavorite { get; init; }

    public int DisplayOrder { get; init; }
}

public sealed record GameRecord
{
    public required GameId Id { get; init; }

    public required string Name { get; init; }

    public required GamePlatform Platform { get; init; }

    public string? ExternalId { get; init; }

    public List<GameInstallation> Installations { get; init; } = [];

    public GameUserPreferences Preferences { get; init; } = new();

    public string? CoverImage { get; init; }
}

public sealed class GameCatalog
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public List<GameRecord> Games { get; set; } = [];
}