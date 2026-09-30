using OptiscalerApp.Models;

namespace OptiscalerApp.Scanning;

public sealed class GogScanner(IEnumerable<RegistryGameEntry>? entries = null)
    : WindowsRegistryScanner(GamePlatform.Gog, entries)
{
    protected override IReadOnlyList<string> RegistryPaths => [@"SOFTWARE\GOG.com\Games"];

    protected override RegisteredGame Read(RegistryGameEntry entry)
    {
        return new RegisteredGame(entry.Get("gameName"), entry.Get("gameID") ?? entry.KeyName, entry.Get("path"));
    }
}
