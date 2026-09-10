using Optiscaler.Core.Abstractions;
using Optiscaler.Core.Games;
using Optiscaler.Core.Scanning;

namespace Optiscaler.Infrastructure.Scanning;

public sealed class GameDiscoveryCoordinator
{
    private readonly IReadOnlyList<IGameScanner> _scanners;

    public GameDiscoveryCoordinator(IEnumerable<IGameScanner> scanners)
    {
        _scanners = scanners.ToList();
    }

    public async Task<ScanResult> ScanGames_Async(
        ScanContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        var activeScanners = _scanners
            .Where(scanner => context.IsEnabled(scanner.Platform))
            .ToList();

        var tasks = activeScanners
            .Select(scanner => RunScannerSafely_Async(scanner, context, cancellationToken))
            .ToList();

        var sourceResults = await Task.WhenAll(tasks).ConfigureAwait(false);
        var diagnostics = sourceResults.SelectMany(result => result.Diagnostics).ToList();

        cancellationToken.ThrowIfCancellationRequested();
        // Apply the same root policy to every launcher, including paths reached through directory links.
        var allowedRoots = new List<string>();

        foreach (var root in context.AllowedDriveRoots)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                allowedRoots.Add(ScanPaths.NormalizeAbsoluteGamePath(root));
            }
            catch (Exception exception) when (exception is ArgumentException or IOException or
                                                  UnauthorizedAccessException or System.Security.SecurityException
                                                  or NotSupportedException)
            {
                diagnostics.Add(new ScanDiagnostic
                {
                    Platform = GamePlatform.Manual,
                    Severity = ScanDiagnosticSeverity.Warning,
                    Code = "scan.invalid_allowed_root",
                    Message = $"{root}: {exception.Message}"
                });
            }
        }

        var seen = new HashSet<(GameId Game, GameId Installation)>();
        var games = new List<DiscoveredGame>();

        if (activeScanners.Count == 0)
            diagnostics.Add(new ScanDiagnostic
            {
                Platform = GamePlatform.Manual,
                Severity = ScanDiagnosticSeverity.Information,
                Code = "scan.no_sources",
                Message = "No scanners are registered for the enabled platforms."
            });

        foreach (var game in sourceResults.SelectMany(result => result.Games))
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var path = ScanPaths.NormalizeAbsoluteGamePath(game.InstallPath);

                // Invalid configured roots must not accidentally turn a restricted scan into an unrestricted one.
                if (context.AllowedDriveRoots.Count > 0 &&
                    !allowedRoots.Any(root => ScanPaths.IsPathWithinRoot(path, root)))
                    continue;

                var gameId = GameId.Create(game.Platform, game.ExternalId, path);
                var installationId = GameId.Create(game.Platform, null, path);

                if (seen.Add((gameId, installationId)))
                    games.Add(game with { InstallPath = path });
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or IOException
                                                  or UnauthorizedAccessException or System.Security.SecurityException)
            {
                diagnostics.Add(new ScanDiagnostic
                {
                    Platform = game.Platform,
                    Severity = ScanDiagnosticSeverity.Warning,
                    Code = "scan.invalid_path",
                    Message = $"{game.Name}: {exception.Message}"
                });
            }
        }

        return new ScanResult
        {
            Games = games
                .OrderBy(game => game.Platform)
                .ThenBy(game => game.Name, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            Diagnostics = diagnostics
        };
    }

    private static async Task<ScanResult> RunScannerSafely_Async(
        IGameScanner scanner,
        ScanContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            return await scanner.ScanGames_Async(context, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new ScanResult
            {
                Diagnostics =
                [
                    new ScanDiagnostic
                    {
                        Platform = scanner.Platform,
                        Severity = ScanDiagnosticSeverity.Error,
                        Code = "scanner.failed",
                        Message = exception.Message
                    }
                ]
            };
        }
    }
}
