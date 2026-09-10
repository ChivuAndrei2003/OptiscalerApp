using Optiscaler.Core.Games;

namespace Optiscaler.Infrastructure.Scanning;

public sealed class GogScanner(IEnumerable<RegistryGameEntry>? entries = null)
    : WindowsRegistryScanner(GamePlatform.Gog, entries);
