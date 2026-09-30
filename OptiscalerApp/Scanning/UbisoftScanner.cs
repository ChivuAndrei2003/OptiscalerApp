using OptiscalerApp.Models;

namespace OptiscalerApp.Scanning;

/// <summary>Ubisoft Connect registers each game as an uninstall entry named "Uplay Install {id}".</summary>
public sealed class UbisoftScanner(IEnumerable<RegistryGameEntry>? entries = null)
    : WindowsRegistryScanner(GamePlatform.Ubisoft, entries)
{
    private const string KeyPrefix = "Uplay Install ";

    protected override IReadOnlyList<string> RegistryPaths => [UninstallKey];

    protected override RegisteredGame? Read(RegistryGameEntry entry)
    {
        return entry.KeyName.StartsWith(KeyPrefix, StringComparison.OrdinalIgnoreCase)
            ? new RegisteredGame(entry.Get("DisplayName"), entry.KeyName[KeyPrefix.Length..].Trim(),
                                 entry.Get("InstallLocation"))
            : null;
    }
}
