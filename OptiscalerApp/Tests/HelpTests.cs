using OptiscalerApp.Management;
using OptiscalerApp.ViewModels;
using Xunit;

namespace Optiscaler.Tests;

public sealed class HelpTests
{
    [Fact]
    public void HelpExplainsEveryProfileOptionAndSearchNarrowsIt()
    {
        var vm = new HelpViewModel();
        var topics = vm.Sections.SelectMany(s => s.Topics).ToList();

        Assert.All(ProfileSettings.All, setting => Assert.Contains(topics, t =>
                       t.Title == setting.Label && t.Text.Contains($"[{setting.Section}] {setting.Key}")));
        Assert.All(topics, t => Assert.False(string.IsNullOrWhiteSpace(t.Text)));

        vm.SearchText = " nukem ";
        var found = vm.Sections.SelectMany(s => s.Topics).ToList();
        Assert.Contains(found, t => t.Title == "NukemFG");
        Assert.All(found, t => Assert.Contains("nukem", t.Title + t.Text, StringComparison.OrdinalIgnoreCase));

        // A section's title brings all of its topics along.
        vm.SearchText = "maintenance";
        Assert.Contains(vm.Sections.SelectMany(s => s.Topics), t => t.Text == HelpText.NativeSwap);

        vm.SearchText = "no such option";
        Assert.True(vm.HasNoMatches);

        vm.SearchText = "";
        Assert.Equal(topics.Count, vm.Sections.Sum(s => s.Topics.Count));
    }
}
