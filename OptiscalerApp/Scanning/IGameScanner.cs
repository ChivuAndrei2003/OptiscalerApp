using OptiscalerApp.Models;

namespace OptiscalerApp.Scanning;

public interface IGameScanner
{
    GamePlatform Platform { get; }

    Task<ScanResult> ScanGamesAsync(ScanContext context, CancellationToken cancellationToken = default);
}