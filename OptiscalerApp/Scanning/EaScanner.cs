using OptiscalerApp.Models;

namespace OptiscalerApp.Scanning;

public sealed class EaScanner(IEnumerable<RegistryGameEntry>? entries = null)
    : WindowsRegistryScanner(GamePlatform.Ea, entries)
{
    protected override IReadOnlyList<string> RegistryPaths =>
        [@"SOFTWARE\Electronic Arts\EA Games", @"SOFTWARE\EA Games"];

    protected override RegisteredGame Read(RegistryGameEntry entry)
    {
        return new RegisteredGame(entry.Get("DisplayName") ?? entry.KeyName, entry.KeyName,
                                  entry.Get("Install Dir") ?? entry.Get("InstallDir"));
    }
}
