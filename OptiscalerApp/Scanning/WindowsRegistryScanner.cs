using System.Runtime.Versioning;
using Microsoft.Win32;
using OptiscalerApp.Models;

namespace OptiscalerApp.Scanning;

/// <summary>A registry entry separated from the operating-system handle for portable mapping tests.</summary>
public sealed record RegistryGameEntry(string KeyName, IReadOnlyDictionary<string, string> Values)
{
    public string? Get(string name) { return Values.TryGetValue(name, out var value) ? value : null; }
}

/// <summary>What a launcher's registry key says about one installed game.</summary>
public sealed record RegisteredGame(string? Name, string? Id, string? InstallPath);

/// <summary>
///     Reads installed-game keys in both registry views; never changes registry or launcher state. Each launcher
///     names the keys it stores games under and how to read one of them.
/// </summary>
public abstract class WindowsRegistryScanner(GamePlatform platform, IEnumerable<RegistryGameEntry>? entries)
    : IGameScanner
{
    protected const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    public GamePlatform Platform => platform;

    protected abstract IReadOnlyList<string> RegistryPaths { get; }

    /// <summary>The game an entry describes, or null when the entry is not one of this launcher's games.</summary>
    protected abstract RegisteredGame? Read(RegistryGameEntry entry);

    public Task<ScanResult> ScanGamesAsync(ScanContext context, CancellationToken cancellationToken = default)
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
                    if (Read(entry) is not { } game || string.IsNullOrWhiteSpace(game.InstallPath)) continue;

                    ScanSource.AddDiscoveredGame(result, Platform, game.Name, game.Id, game.InstallPath);
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
        var entries = new List<RegistryGameEntry>();

        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
            foreach (var view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
                foreach (var path in RegistryPaths)
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