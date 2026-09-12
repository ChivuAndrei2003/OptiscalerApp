using OptiscalerApp.Models;

namespace OptiscalerApp.Scanning;

public interface IGameScanner
{
    GamePlatform Platform { get; }

    Task<ScanResult> ScanGames_Async(
        ScanContext context,
        CancellationToken cancellationToken = default);
}
