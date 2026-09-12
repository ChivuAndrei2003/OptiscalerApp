using OptiscalerApp.Models;

namespace OptiscalerApp.Scanning;

public sealed class EaScanner(IEnumerable<RegistryGameEntry>? entries = null)
    : WindowsRegistryScanner(GamePlatform.Ea, entries);
