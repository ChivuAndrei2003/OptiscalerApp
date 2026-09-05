using Optiscaler.Core.Analysis;
using Optiscaler.Core.Games;
using Optiscaler.Core.Scanning;

namespace Optiscaler.Core.Abstractions;

public interface IGameScanner
{
    GamePlatform Platform { get; }

    Task<ScanResult> ScanAsync(
        ScanContext context,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Performs read-only inspection of a discovered game installation.
/// </summary>
public interface IGameAnalyzer
{
    Task<GameAnalysis> AnalyzeAsync(
        GameId gameId,
        GameInstallation installation,
        CancellationToken cancellationToken = default);
}