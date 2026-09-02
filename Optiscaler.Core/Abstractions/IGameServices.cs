using Optiscaler.Core.Analysis;
using Optiscaler.Core.Games;
using Optiscaler.Core.Scanning;

namespace Optiscaler.Core.Abstractions;

/// <summary>
/// Scans for games.
/// </summary>
public interface IGameScanner
{
    GamePlatform Platform { get; }

    Task<ScanResult> ScanAsync(
        ScanContext context,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Analyzes a game.
/// </summary>
public interface IGameAnalyzer
{
    Task<GameAnalysis> AnalyzeAsync(
        GameId gameId,
        GameInstallation installation,
        CancellationToken cancellationToken = default);
}