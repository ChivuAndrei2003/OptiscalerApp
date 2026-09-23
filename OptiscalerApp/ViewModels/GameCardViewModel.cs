using OptiscalerApp.Management;
using OptiscalerApp.Models;

namespace OptiscalerApp.ViewModels;

public enum LibraryFilter
{
    All,
    Favorites,
    Tested,
    Managed,
    NeedsAttention,
    Updates,
    Hidden
}

/// <summary>One library card: the saved game plus the state found for it on this run.</summary>
public sealed class GameCardViewModel(
    GameRecord game,
    ManagedTarget? managed,
    CompatibilityEntry? compatibility,
    string? availableVersion)
{
    public GameRecord Game { get; } = game;

    public ManagedTarget? Managed { get; } = managed;

    public CompatibilityEntry? Compatibility { get; } = compatibility;

    public string Name => Game.Name;

    public string PlatformText => Game.Platform.ToString();

    public string? CoverImage => Game.CoverImage;

    public bool IsFavorite => Game.IsFavorite;

    public bool IsHidden => Game.IsHidden;

    public string FavoriteMenuText => IsFavorite ? "Remove from favorites" : "Add to favorites";

    public string HiddenMenuText => IsHidden ? "Show in library" : "Hide from library";

    public bool IsManaged => Managed is not null;

    public bool NeedsAttention => Managed is { Health: not ManagedHealth.Healthy };

    public bool HasUpdate => Managed is { Health: ManagedHealth.Healthy } &&
                             ReleaseVersion.IsNewer(availableVersion, Managed.Version);

    public bool IsTested => Compatibility is { Status: not CompatibilityStatus.NotWorking };

    public bool HasStatus => IsManaged || Compatibility is not null;

    /// <summary>The single most useful fact for the card, most urgent first.</summary>
    public string StatusText => Managed switch
    {
        { Health: ManagedHealth.Incomplete } => "Needs attention · interrupted change",
        { Health: ManagedHealth.FilesChanged } => "Needs attention · files changed",
        not null when HasUpdate => $"Update available · {availableVersion}",
        not null => "OptiScaler" + (Managed.Version is { } version ? $" · {version}" : " installed"),
        null => Compatibility?.Status switch
        {
            CompatibilityStatus.Working => "Tested on the wiki",
            CompatibilityStatus.WorkingOnSingleOs => "Tested on one OS",
            CompatibilityStatus.NotWorking => "Not working per wiki",
            _ => ""
        }
    };

    public bool IsWarning => NeedsAttention || (!IsManaged && Compatibility?.Status == CompatibilityStatus.NotWorking);

    public bool Matches(LibraryFilter filter)
    {
        return filter switch
        {
            LibraryFilter.Hidden => IsHidden,
            _ when IsHidden => false,
            LibraryFilter.Favorites => IsFavorite,
            LibraryFilter.Tested => IsTested,
            LibraryFilter.Managed => IsManaged,
            LibraryFilter.NeedsAttention => NeedsAttention,
            LibraryFilter.Updates => HasUpdate,
            _ => true
        };
    }
}
