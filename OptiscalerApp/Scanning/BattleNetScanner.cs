using OptiscalerApp.Models;

namespace OptiscalerApp.Scanning;

/// <summary>Blizzard games are uninstall entries published by Blizzard, other than the Battle.net launcher itself.</summary>
public sealed class BattleNetScanner(IEnumerable<RegistryGameEntry>? entries = null)
    : WindowsRegistryScanner(GamePlatform.BattleNet, entries)
{
    protected override IReadOnlyList<string> RegistryPaths => [UninstallKey];

    protected override RegisteredGame? Read(RegistryGameEntry entry)
    {
        var name = entry.Get("DisplayName");

        return entry.Get("Publisher")?.Contains("Blizzard Entertainment", StringComparison.OrdinalIgnoreCase) == true &&
               name is not null && !name.Contains("Battle.net", StringComparison.OrdinalIgnoreCase)
            ? new RegisteredGame(name, entry.KeyName, entry.Get("InstallLocation"))
            : null;
    }
}
