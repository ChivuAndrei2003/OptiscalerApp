using OptiscalerApp.Views;
using Xunit;

namespace Optiscaler.Tests;

public sealed class ControllerShortcutTests
{
    private const uint A = 1, View = 1 << 4, Menu = 1 << 6;

    private sealed class CountingKey : IMenuKey
    {
        public int Presses { get; private set; }

        public Task PressAsync()
        {
            Presses++;

            return Task.CompletedTask;
        }

        public void Dispose() { }
    }

    [Fact]
    public async Task TheChosenCombinationPressesTheMenuKeyOncePerPressAndOnlyInAGame()
    {
        var key = new CountingKey();
        var shortcut = new ControllerShortcut(() => key) { CanReadControllers = true };

        // Choosing starts with A still held on the Set button; the combination is then pressed one button at a time.
        var choosing = shortcut.CaptureAsync();
        foreach (var down in new[] { A, 0u, View, View | Menu, Menu, 0u }) shortcut.Update(down, true);
        var chosen = await choosing;

        Assert.Equal(View | Menu, chosen);
        Assert.Equal("View (Select) + Menu (Start)", shortcut.Describe(chosen));

        shortcut.Buttons = chosen;
        shortcut.Update(View | Menu, true);
        shortcut.Update(0, true);
        Assert.Equal(0, key.Presses);

        foreach (var down in new[] { View, View | Menu, View | Menu | A, View, 0u, View | Menu })
            shortcut.Update(down, false);
        Assert.Equal(2, key.Presses);
    }
}
