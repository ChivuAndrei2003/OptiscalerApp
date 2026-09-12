using OptiscalerApp.Models;

namespace OptiscalerApp.Scanning;

public sealed class CustomFolderScanner : IGameScanner
{
    public GamePlatform Platform  =>GamePlatform.Custom;

    public Task<ScanResult> ScanGames_Async(ScanContext context, CancellationToken cancellationToken = default)
    {

        return Task.Run(() =>
        {
            var result=new ScanResult();

            if(!context.IsEnabled(Platform)) return result;

            foreach (var root in context.CustomFolders)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    if (!Directory.Exists(root)) continue;

                    foreach (var directory in Directory.EnumerateDirectories(root))
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) continue;

                        result.Games.Add(new DiscoveredGame
                        {
                            Name=Path.GetFileName(directory),InstallPath=directory,Platform=Platform
                        });
                    }
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
                {
                    result.Diagnostics.Add(new ScanDiagnostic
                    {
                        Platform = Platform,Severity=ScanDiagnosticSeverity.Warning,
                        Code="custom.unreadable",Message=  e.Message
                    });
                }
            }
            return result;
        },cancellationToken);
    }
}
