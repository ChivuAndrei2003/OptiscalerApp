using OptiscalerApp.Management;
using OptiscalerApp.Models;
using Xunit;

namespace Optiscaler.Tests;

public sealed class ReleaseAndLaunchTests
{
    [Theory]
    [InlineData("v0.9.5", "v0.9.4", true)]
    [InlineData("v0.9.4", "0.9.4.0", false)]
    [InlineData("v0.9.4", "0.9.5-pre4", false)]
    [InlineData("v0.10.0", "v0.9.9", true)]
    [InlineData("v0.9.5", "Bundled · local", false)]
    [InlineData(null, "v0.9.4", false)]
    public void ComparesReleaseTagsAndFileVersionsNumerically(string? available, string? installed, bool newer)
    {
        Assert.Equal(newer, ReleaseVersion.IsNewer(available, installed));
    }

    [Theory]
    [InlineData(GamePlatform.Steam, "1091500", "steam://rungameid/1091500")]
    [InlineData(GamePlatform.Steam, "not-a-number", null)]
    [InlineData(GamePlatform.Epic, "Fortnite", null)]
    public void OnlySteamGamesLaunchThroughTheirLauncher(GamePlatform platform, string id, string? uri)
    {
        var game = new GameRecord
        {
            Id = GameId.Create(platform, id, "/games/x"), Name = "x", Platform = platform, ExternalId = id
        };

        // Without an executable, non-Steam games have nothing to start.
        Assert.Equal(uri, GameLauncher.Resolve(game, null)?.Uri?.ToString());
    }

    [Theory]
    [InlineData("dxgi.dll", "WINEDLLOVERRIDES=\"dxgi=n,b\" %COMMAND%")]
    [InlineData("WinMM.dll", "WINEDLLOVERRIDES=\"winmm=n,b\" %COMMAND%")]
    public void LinuxLaunchOptionsOverrideTheProxy(string proxy, string expected)
    {
        Assert.Equal(expected, GameLauncher.LinuxLaunchOptions(proxy));
    }

    [Fact]
    public void RedactsTheHomeFolderAndUserName()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var text = DiagnosticsReport.Redact($"{Path.Combine(home, "Games", "x.exe")} by {Environment.UserName}");

        Assert.DoesNotContain(home, text);
        if (Environment.UserName.Length >= 3) Assert.DoesNotContain(Environment.UserName, text);
        Assert.Contains(Path.Combine("~", "Games", "x.exe"), text);
    }
}
