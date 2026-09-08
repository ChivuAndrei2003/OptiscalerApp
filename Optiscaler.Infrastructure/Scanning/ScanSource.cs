using System.Security;
using System.Text.Json;
using Optiscaler.Core.Games;
using Optiscaler.Core.Scanning;

namespace Optiscaler.Infrastructure.Scanning;

internal static class ScanSource
{
    internal static bool IsGameSourceReadError(Exception ex)
    {
        return ex is InvalidDataException or IOException or UnauthorizedAccessException or
            SecurityException or ArgumentException or NotSupportedException or JsonException
            or InvalidOperationException;
    }

    internal static void AddScanWarning(ScanResult result, GamePlatform platform, string source, Exception ex)
    {
        result.Diagnostics.Add(new ScanDiagnostic
        {
            Platform = platform, Severity = ScanDiagnosticSeverity.Warning,
            Code = "source.unreadable", Message = $"{source}: {ex.Message}"
        });
    }

    internal static string? GetOptionalTextProperty(JsonElement entry, string key)
    {
        return entry.ValueKind == JsonValueKind.Object && entry.TryGetProperty(key, out var value) &&
               value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    internal static void AddDiscoveredGame(ScanResult result, GamePlatform platform, string? name, string? id,
                                           string? path, string? executable = null)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(name))
            throw new InvalidDataException("Game name or installation path is missing.");

        path = ScanPaths.NormalizeAbsoluteGamePath(path);

        if (!Directory.Exists(path)) return; // Stale metadata from an uninstalled game.

        if (!string.IsNullOrWhiteSpace(executable))
        {
            executable = executable.Replace('\\', Path.DirectorySeparatorChar)
                .Replace('/', Path.DirectorySeparatorChar);
            executable =
                ScanPaths.NormalizeAbsoluteGamePath(Path.IsPathFullyQualified(executable)
                                                        ? executable
                                                        : Path.Combine(path, executable));
            if (!ScanPaths.IsPathWithinRoot(executable, path) || !File.Exists(executable)) executable = null;
        }

        result.Games.Add(new DiscoveredGame
        {
            Name = name.Trim(), Platform = platform, ExternalId = id,
            InstallPath = path, ExecutablePath = executable
        });
    }
}
