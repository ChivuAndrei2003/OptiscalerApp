using System.Text.Json;
using OptiscalerApp.Models;

namespace OptiscalerApp.Scanning;

/// <summary>Reads one store's Heroic/Legendary metadata so Epic and GOG toggles remain independent.</summary>
public sealed class HeroicScanner : IGameScanner
{
    private readonly IReadOnlyList<string>? _metadataFiles;
    public GamePlatform Platform { get; }

    public HeroicScanner(GamePlatform platform, IEnumerable<string>? metadataFiles = null)
    {
        if (platform is not (GamePlatform.Epic or GamePlatform.Gog))
            throw new ArgumentOutOfRangeException(nameof(platform));

        Platform = platform;
        _metadataFiles = metadataFiles?.ToArray();
    }

    public Task<ScanResult> ScanGames_Async(ScanContext context, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            var result = new ScanResult();

            if (!context.IsEnabled(Platform)) return result;

            foreach (var file in (_metadataFiles ?? DefaultFiles()).Distinct())
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!File.Exists(file)) continue;

                try
                {
                    using var document = JsonDocument.Parse(File.ReadAllText(file));
                    var root = document.RootElement;

                    if (root.ValueKind != JsonValueKind.Object)
                        throw new InvalidDataException("Expected installed game metadata.");

                    if (Platform == GamePlatform.Gog)
                    {
                        if (!root.TryGetProperty("installed", out var installed) ||
                            installed.ValueKind != JsonValueKind.Array)
                            throw new InvalidDataException("Expected a GOG installed array.");

                        foreach (var item in installed.EnumerateArray()) ReadEntry(item, null);
                    }
                    else
                    {
                        foreach (var entry in root.EnumerateObject())
                            ReadEntry(entry.Value, entry.Name);
                    }
                }
                catch (Exception ex) when (ScanSource.IsGameSourceReadError(ex))
                {
                    ScanSource.AddScanWarning(result, Platform, file, ex);
                }

                void ReadEntry(JsonElement entry, string? fallbackId)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        if (entry.ValueKind != JsonValueKind.Object)
                            throw new InvalidDataException("Expected a game object.");

                        if (entry.TryGetProperty("is_dlc", out var dlc) && dlc.ValueKind == JsonValueKind.True) return;

                        var path = ScanSource.GetOptionalTextProperty(entry, "install_path");
                        var name = ScanSource.GetOptionalTextProperty(entry, "title") ??
                                   (path is null ? null : Path.GetFileName(Path.TrimEndingDirectorySeparator(path)));
                        var id = ScanSource.GetOptionalTextProperty(entry, "app_name") ?? ScanSource.GetOptionalTextProperty(entry, "appName") ?? fallbackId;
                        ScanSource.AddDiscoveredGame(result, Platform, name, id, path, ScanSource.GetOptionalTextProperty(entry, "executable"));
                    }
                    catch (Exception ex) when (ScanSource.IsGameSourceReadError(ex))
                    {
                        ScanSource.AddScanWarning(result, Platform, file, ex);
                    }
                }
            }

            return result;
        }, cancellationToken);
    }

    private IReadOnlyList<string> DefaultFiles()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux()) return [];

        var relative = Platform == GamePlatform.Epic
            ? "legendaryConfig/legendary/installed.json"
            : "gog_store/installed.json";
        var files = LauncherLocations
            .HeroicRoots(LauncherLocations.Home, LauncherLocations.ConfigHome, OperatingSystem.IsWindows())
            .Select(root => Path.Combine(root, relative)).ToList();

        if (Platform == GamePlatform.Epic)
        {
            files.Add(Path.Combine(LauncherLocations.ConfigHome, "legendary/installed.json"));
            files.Add(Path.Combine(LauncherLocations.Home, ".config/legendary/installed.json"));
        }

        return files;
    }
}
