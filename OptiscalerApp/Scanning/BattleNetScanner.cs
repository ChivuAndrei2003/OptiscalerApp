using OptiscalerApp.Models;

namespace OptiscalerApp.Scanning;

public sealed class BattleNetScanner(IEnumerable<RegistryGameEntry>? entries = null)
    : WindowsRegistryScanner(GamePlatform.BattleNet, entries);
