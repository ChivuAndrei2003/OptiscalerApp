using OptiscalerApp.Models;

namespace OptiscalerApp.Scanning;

public sealed class UbisoftScanner(IEnumerable<RegistryGameEntry>? entries = null)
    : WindowsRegistryScanner(GamePlatform.Ubisoft, entries);
