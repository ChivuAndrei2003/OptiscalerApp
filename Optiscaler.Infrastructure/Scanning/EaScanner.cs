using Optiscaler.Core.Games;

namespace Optiscaler.Infrastructure.Scanning;

public sealed class EaScanner(IEnumerable<RegistryGameEntry>? entries = null)
    : WindowsRegistryScanner(GamePlatform.Ea, entries);
