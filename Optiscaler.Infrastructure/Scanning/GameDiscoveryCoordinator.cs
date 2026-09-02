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

    public async Task<ScanResult> ScanAsync(
        ScanContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var activeScanners = _scanners
            .Where(scanner => context.IsEnabled(scanner.Platform))
            .ToList();


        var tasks = activeScanners
            .Select(scanner => RunScannerSafelyAsync(scanner, context, cancellationToken))
            .ToList();

        var sourceResults = await Task.WhenAll(tasks).ConfigureAwait(false);
        var diagnostics = sourceResults.SelectMany(result => result.Diagnostics).ToList();


        var pathComparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

        var uniqueByIdentity = new Dictionary<string, DiscoveredGame>(pathComparer);

        foreach (var game in sourceResults.SelectMany(result => result.Games))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var canonicalPath = CanonicalizePath(game.InstallPath);
            var identityKey = !string.IsNullOrWhiteSpace(game.ExternalId)
                ? $"{game.Platform}:id:{game.ExternalId.Trim()}"
                : $"{game.Platform}:path:{canonicalPath}";


            uniqueByIdentity.TryAdd(identityKey, game with { InstallPath = canonicalPath });
        }

        return new ScanResult
        {
            Games = uniqueByIdentity.Values
                .OrderBy(game => game.Platform)
                .ThenBy(game => game.Name, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            Diagnostics = diagnostics
        };
    }

    private static async Task<ScanResult> RunScannerSafelyAsync(
        IGameScanner scanner,
        ScanContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            return await scanner.ScanAsync(context, cancellationToken).ConfigureAwait(false);
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

    private static string CanonicalizePath(string path)
    {
        try
        {
            return Path.GetFullPath(path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return path.Trim();
        }
    }
}