using Optiscaler.Core.Games;

namespace Optiscaler.Infrastructure.Scanning;

public sealed class BattleNetScanner(IEnumerable<RegistryGameEntry>? entries = null)
    : WindowsRegistryScanner(GamePlatform.BattleNet, entries);
