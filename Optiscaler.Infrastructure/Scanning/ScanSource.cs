using System.Security;
using System.Text.Json;
using Optiscaler.Core.Games;
using Optiscaler.Core.Scanning;

namespace Optiscaler.Infrastructure.Scanning;

internal static class ScanSource
{
    internal static bool IsReadError(Exception ex) => ex is InvalidDataException or IOException or UnauthorizedAccessException or
        SecurityException or ArgumentException or NotSupportedException or JsonException or InvalidOperationException;

    internal static void Warn(ScanResult result, GamePlatform platform, string source, Exception ex) =>
        result.Diagnostics.Add(new ScanDiagnostic { Platform = platform, Severity = ScanDiagnosticSeverity.Warning,
            Code = "source.unreadable", Message = $"{source}: {ex.Message}" });

    internal static string? Text(JsonElement entry, string key) =>
        entry.ValueKind == JsonValueKind.Object && entry.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() : null;

    internal static void Add(ScanResult result, GamePlatform platform, string? name, string? id, string? path, string? executable = null)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(name))
            throw new InvalidDataException("Game name or installation path is missing.");
        
        path = ScanPaths.NormalizeAbsolutePath(path);
        if (!Directory.Exists(path)) return; // Stale metadata from an uninstalled game.
        if (!string.IsNullOrWhiteSpace(executable))
        {
            executable = executable.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
            executable = ScanPaths.NormalizeAbsolutePath(Path.IsPathFullyQualified(executable) ? executable : Path.Combine(path, executable));
            if (!ScanPaths.IsWithin(executable, path) || !File.Exists(executable)) executable = null;
        }
        result.Games.Add(new DiscoveredGame { Name = name.Trim(), Platform = platform, ExternalId = id,
            InstallPath = path, ExecutablePath = executable });
    }
}
