using OptiscalerApp.Models;

namespace OptiscalerApp.Scanning;

public sealed class GogScanner(IEnumerable<RegistryGameEntry>? entries = null)
    : WindowsRegistryScanner(GamePlatform.Gog, entries);
