using OptiscalerApp.Models;

namespace OptiscalerApp.Management;

/// <summary>Performs read-only inspection of a discovered game installation.</summary>
public interface IGameAnalyzer
{
    Task<GameAnalysis> AnalyzeGameAsync(string rootPath, CancellationToken cancellationToken = default);
}