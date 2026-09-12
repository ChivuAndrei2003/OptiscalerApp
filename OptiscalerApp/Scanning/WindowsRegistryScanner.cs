using System.Runtime.Versioning;
using Microsoft.Win32;
using OptiscalerApp.Models;

namespace OptiscalerApp.Scanning;

/// <summary>A registry entry separated from the operating-system handle for portable mapping tests.</summary>
public sealed record RegistryGameEntry(string KeyName, IReadOnlyDictionary<string, string> Values)
{
    public string? Get(string name)
    {
        return Values.TryGetValue(name, out var value) ? value : null;
    }
}

/// <summary>Reads installed-game keys in both registry views; never changes registry or launcher state.</summary>
public class WindowsRegistryScanner(GamePlatform platform, IEnumerable<RegistryGameEntry>? entries = null)
    : IGameScanner
{
    public GamePlatform Platform => platform;

    public Task<ScanResult> ScanGames_Async(ScanContext context, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            var result = new ScanResult();

            if (!context.IsEnabled(Platform)) return result;

            var source = entries ?? (OperatingSystem.IsWindows() ? ReadRegistry(result, cancellationToken) : []);

            foreach (var entry in source)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var name = entry.Get("DisplayName");
                    var path = entry.Get("InstallLocation");
                    var id = entry.KeyName;

                    switch (Platform)
                    {
                        case GamePlatform.Gog:
                            name = entry.Get("gameName");
                            path = entry.Get("path");
                            id = entry.Get("gameID") ?? id;

                            break;
                        case GamePlatform.Ea:
                            name ??= entry.KeyName;
                            path = entry.Get("Install Dir") ?? entry.Get("InstallDir");

                            break;
                        case GamePlatform.Ubisoft:
                            if (!entry.KeyName.StartsWith("Uplay Install ", StringComparison.OrdinalIgnoreCase))
                                continue;

                            id = entry.KeyName[14..].Trim();

                            break;
                        case GamePlatform.BattleNet:
                            if (entry.Get("Publisher")
                                    ?.Contains("Blizzard Entertainment", StringComparison.OrdinalIgnoreCase) != true ||
                                name is null ||
                                name.Contains("Battle.net", StringComparison.OrdinalIgnoreCase)) continue;

                            break;
                        default: throw new InvalidOperationException("Unsupported registry source.");
                    }

                    if (string.IsNullOrWhiteSpace(path)) continue;

                    ScanSource.AddDiscoveredGame(result, Platform, name, id, path);
                }
                catch (Exception ex) when (ScanSource.IsGameSourceReadError(ex))
                {
                    ScanSource.AddScanWarning(result, Platform, entry.KeyName, ex);
                }
            }

            return result;
        }, cancellationToken);
    }

    [SupportedOSPlatform("windows")]
    private List<RegistryGameEntry> ReadRegistry(ScanResult result, CancellationToken cancellationToken)
    {
        string[] paths = Platform switch
        {
            GamePlatform.Gog => [@"SOFTWARE\GOG.com\Games"],
            GamePlatform.Ea => [@"SOFTWARE\Electronic Arts\EA Games", @"SOFTWARE\EA Games"],
            _ => [@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"]
        };
        var entries = new List<RegistryGameEntry>();

        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
            foreach (var view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
                foreach (var path in paths)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        using var registry = RegistryKey.OpenBaseKey(hive, view);
                        using var parent = registry.OpenSubKey(path);

                        if (parent is null) continue;

                        foreach (var child in parent.GetSubKeyNames())
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            try
                            {
                                using var key = parent.OpenSubKey(child);

                                if (key is null) continue;

                                var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                                foreach (var name in new[]
                                         {
                                             "DisplayName", "InstallLocation", "Publisher", "gameName", "path",
                                             "gameID", "Install Dir", "InstallDir"
                                         })
                                    if (key.GetValue(name) is string value)
                                        values[name] = value;
                                entries.Add(new RegistryGameEntry(child, values));
                            }
                            catch (Exception ex) when (ScanSource.IsGameSourceReadError(ex))
                            {
                                ScanSource.AddScanWarning(result, Platform, child, ex);
                            }
                        }
                    }
                    catch (Exception ex) when (ScanSource.IsGameSourceReadError(ex))
                    {
                        ScanSource.AddScanWarning(result, Platform, $"{hive}/{view}/{path}", ex);
                    }
                }

        return entries;
    }
}
