using OptiscalerApp.Models;

namespace OptiscalerApp.Management;

/// <summary>Performs read-only inspection of a discovered game installation.</summary>
public interface IGameAnalyzer
{
    Task<GameAnalysis> AnalyzeGameAsync(
        GameId gameId,
        GameInstallation installation,
        CancellationToken cancellationToken = default);
}