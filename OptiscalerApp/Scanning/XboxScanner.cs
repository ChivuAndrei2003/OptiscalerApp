using System.Xml.Linq;
using OptiscalerApp.Models;

namespace OptiscalerApp.Scanning;

/// <summary>Finds accessible XboxGames installs; protected WindowsApps folders are left to Windows.</summary>
public sealed class XboxScanner(IEnumerable<string>? libraryRoots = null) : IGameScanner
{
    public GamePlatform Platform => GamePlatform.Xbox;

    public Task<ScanResult> ScanGames_Async(ScanContext context, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            var result = new ScanResult();

            if (!context.IsEnabled(Platform)) return result;

            var roots = libraryRoots ?? (OperatingSystem.IsWindows()
                ? DriveInfo.GetDrives()
                    .Where(d => d.IsReady && d.DriveType == DriveType.Fixed)
                    .Select(d => Path.Combine(d.RootDirectory.FullName, "XboxGames"))
                : []);

            foreach (var root in roots.Concat(context.CustomFolders))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!Directory.Exists(root)) continue;

                try
                {
                    foreach (var game in Directory.EnumerateDirectories(root))
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        try
                        {
                            var content = Directory.Exists(Path.Combine(game, "Content"))
                                ? Path.Combine(game, "Content")
                                : game;
                            var config = Path.Combine(content, "MicrosoftGame.config");

                            if (!File.Exists(config)) continue;

                            var doc = XDocument.Load(config);
                            var name = doc.Descendants("ShellVisuals").FirstOrDefault()?.Attribute("DefaultDisplayName")
                                ?.Value;
                            var id = doc.Descendants("Identity").FirstOrDefault()?.Attribute("Name")?.Value;
                            var exe = doc.Descendants("Executable").FirstOrDefault()?.Attribute("Name")?.Value;
                            ScanSource.AddDiscoveredGame(result, Platform,
                                           string.IsNullOrWhiteSpace(name) ? Path.GetFileName(game) : name, id, content,
                                           exe);
                        }
                        catch (Exception ex) when (ScanSource.IsGameSourceReadError(ex) || ex is System.Xml.XmlException)
                        {
                            ScanSource.AddScanWarning(result, Platform, game, ex);
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
