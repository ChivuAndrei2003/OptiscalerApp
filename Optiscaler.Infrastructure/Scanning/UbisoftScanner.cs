using Optiscaler.Core.Games;

namespace Optiscaler.Infrastructure.Scanning;

public sealed class UbisoftScanner(IEnumerable<RegistryGameEntry>? entries = null)
    : WindowsRegistryScanner(GamePlatform.Ubisoft, entries);
