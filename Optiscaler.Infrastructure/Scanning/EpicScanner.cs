using System.Text.Json;
using Optiscaler.Core.Abstractions;
using Optiscaler.Core.Games;
using Optiscaler.Core.Scanning;

namespace Optiscaler.Infrastructure.Scanning;

/// <summary>Reads Epic .item manifests; explicit roots also support portable libraries and tests.</summary>
public sealed class EpicScanner(IEnumerable<string>? manifestRoots = null) : IGameScanner
{
    public GamePlatform Platform => GamePlatform.Epic;

    public Task<ScanResult> ScanGames_Async(ScanContext context, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            var result = new ScanResult();

            if (!context.IsEnabled(Platform)) return result;

            var roots = manifestRoots ?? (OperatingSystem.IsWindows()
                ?
                [
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                                 "Epic/EpicGamesLauncher/Data/Manifests")
                ]
                : Array.Empty<string>());

            foreach (var root in roots.Concat(context.CustomFolders).Distinct())
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!Directory.Exists(root)) continue;

                try
                {
                    foreach (var file in Directory.EnumerateFiles(root, "*.item"))
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        try
                        {
                            using var document = JsonDocument.Parse(File.ReadAllText(file));
                            var item = document.RootElement;

                            if (item.ValueKind != JsonValueKind.Object)
                                throw new InvalidDataException("Expected an Epic manifest object.");

                            if (item.TryGetProperty("bIsIncompleteInstall", out var incomplete) &&
                                incomplete.ValueKind == JsonValueKind.True) continue;
                            if (item.TryGetProperty("bIsApplication", out var application) &&
                                application.ValueKind == JsonValueKind.False) continue;

                            ScanSource.AddDiscoveredGame(result, Platform, ScanSource.GetOptionalTextProperty(item, "DisplayName"),
                                           ScanSource.GetOptionalTextProperty(item, "AppName"),
                                           ScanSource.GetOptionalTextProperty(item, "InstallLocation"),
                                           ScanSource.GetOptionalTextProperty(item, "LaunchExecutable"));
                        }
                        catch (Exception ex) when (ScanSource.IsGameSourceReadError(ex))
                        {
                            ScanSource.AddScanWarning(result, Platform, file, ex);
                        }
                    }
                }
                catch (Exception ex) when (ScanSource.IsGameSourceReadError(ex))
                {
                    ScanSource.AddScanWarning(result, Platform, root, ex);
                }
            }

            return result;
        }, cancellationToken);
    }
}
