using OptiscalerApp.Management;
using OptiscalerApp.Models;
using OptiscalerApp.Paths;
using Xunit;

namespace Optiscaler.Tests;

public sealed class CompatibilityListTests : IDisposable
{
    // Shaped like the real wiki page: a commented row template, the main table, then a table without OptiPatcher.
    internal const string Wiki = """
                                 > **This is NOT a complete list of games supported by OptiScaler.**

                                 <!--
                                 TEMPLATE FOR NEW ENTRIES
                                 | GAME NAME | ✅/❌/➖ | DLSS/FSR3.1/XeSS | ✨ | Notes go here | [#](url) |
                                 -->

                                 | Game | Compatibility | Upscaler <br>Inputs | OptiPatcher <br>Support | Notes | Images |
                                 | ---- | :-----------: | :-----------------: | :---: | ----- | :----: |
                                 | [Cyberpunk 2077](Cyberpunk-2077) | ✅ | DLSS, FSR3.1, XeSS | ✨ | Use **FSR4** for best results.<br>Works great. | [1](https://example.com/1) |
                                 | Aphelion | ✅ | DLSS, FSR3.1 |  | Install as `winmm.dll` for Xbox version. | [1](https://example.com/2) |
                                 | Amid Evil | ✅ | DLSS |  | Linux: use `dxgi.dll`, hudfix=true. |  |
                                 | Dying Light 2 | ✅ | DLSS, FSR2 |  |  |  |
                                 | EA Sports WRC | ❌ |  |  | EA Anti-cheat blocks unknown .dlls |  |
                                 | Starfield | 💥 | DLSS, FSR3 | ✨ | Works on Windows only. |  |

                                 ## Upscaler mods support

                                 | Game | Compatibility | Upscaler <br>Inputs | Notes  | Images |
                                 | ---- | :-----------: | :-----------------: | ------ | :----: |
                                 | [Dishonored 2](Dishonored-2) | ✅ | DLSS | Requires Luma Framework mod | |
                                 | [Cyberpunk 2077](Luma) | ✅ | DLSS | Duplicate row from a mod table | |
                                 """;

    private readonly string _root = Path.Combine(Path.GetTempPath(), "Optiscaler-compat-" + Guid.NewGuid().ToString("N"));

    private CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    [Fact]
    public void ParsesEveryTableAndCleansMarkdown()
    {
        var entries = CompatibilityListParser.Parse(Wiki);

        Assert.Equal(8, entries.Count);
        Assert.DoesNotContain(entries, e => e.GameName == "GAME NAME");
        var cyberpunk = entries[0];
        Assert.Equal("Cyberpunk 2077", cyberpunk.GameName);
        Assert.Equal(CompatibilityStatus.Working, cyberpunk.Status);
        Assert.Equal("DLSS, FSR3.1, XeSS", cyberpunk.Inputs);
        Assert.True(cyberpunk.OptiPatcherSupported);
        Assert.Equal("Use FSR4 for best results. Works great.", cyberpunk.Notes);
        Assert.Equal("https://github.com/optiscaler/OptiScaler/wiki/Cyberpunk-2077", cyberpunk.PageUrl);
        Assert.Equal(["winmm.dll"], entries.Single(e => e.GameName == "Aphelion").MentionedProxies);
        Assert.Equal(CompatibilityStatus.NotWorking, entries.Single(e => e.GameName == "EA Sports WRC").Status);
        Assert.Equal(CompatibilityStatus.WorkingOnSingleOs, entries.Single(e => e.GameName == "Starfield").Status);
        Assert.False(entries.Single(e => e.GameName == "Dishonored 2").OptiPatcherSupported);
    }

    [Theory]
    [InlineData("Cyberpunk 2077™", "Cyberpunk 2077")]
    [InlineData("CYBERPUNK 2077", "Cyberpunk 2077")]
    [InlineData("Cyberpunk 2077: Ultimate Edition", "Cyberpunk 2077")]
    [InlineData("Dying Light 2 Stay Human", null)]
    [InlineData("Dying Light", null)]
    [InlineData("Dying Light 2 - Definitive Edition", "Dying Light 2")]
    [InlineData("Unknown Game", null)]
    public void MatchesNamesConservatively(string name, string? expected)
    {
        var index = new CompatibilityIndex(new CompatibilityCatalog { Entries = CompatibilityListParser.Parse(Wiki) });

        Assert.Equal(expected, index.Find(name)?.GameName);
    }

    [Fact]
    public void MainTableWinsOverModTableDuplicates()
    {
        var index = new CompatibilityIndex(new CompatibilityCatalog { Entries = CompatibilityListParser.Parse(Wiki) });

        Assert.Equal("DLSS, FSR3.1, XeSS", index.Find("Cyberpunk 2077")!.Inputs);
    }

    [Fact]
    public async Task DownloadsOnceThenServesTheCacheOffline()
    {
        var paths = new AppPaths(_root);
        var online = StubHttpHandler.Text(Wiki);
        using (var client = new HttpClient(online))
        {
            var service = new CompatibilityListService(paths, client);
            Assert.Equal(8, (await service.GetIndex_Async(cancellationToken: Ct)).Count);

            // Fresh data is not downloaded again.
            await service.GetIndex_Async(cancellationToken: Ct);
            Assert.Equal(1, online.Requests);
        }

        var offline = StubHttpHandler.Offline();
        using var offlineClient = new HttpClient(offline);
        var restarted = new CompatibilityListService(paths, offlineClient);
        Assert.NotNull((await restarted.GetCachedIndex_Async(Ct)).Find("Aphelion"));
        Assert.Equal(0, offline.Requests);

        // A forced refresh that fails keeps the saved list.
        Assert.Equal(8, (await restarted.GetIndex_Async(true, Ct)).Count);
        Assert.Equal(1, offline.Requests);
    }

    [Fact]
    public async Task OfflineWithoutCacheReturnsAnEmptyListAndDoesNotRetryImmediately()
    {
        var offline = StubHttpHandler.Offline();
        using var client = new HttpClient(offline);
        var service = new CompatibilityListService(new AppPaths(_root), client);

        Assert.Equal(0, (await service.GetIndex_Async(cancellationToken: Ct)).Count);
        Assert.Null((await service.GetIndex_Async(cancellationToken: Ct)).Find("Aphelion"));
        Assert.Equal(1, offline.Requests);
    }

    [Fact]
    public async Task UnrecognizedPageKeepsThePreviousList()
    {
        var paths = new AppPaths(_root);
        using (var client = new HttpClient(StubHttpHandler.Text(Wiki)))
        {
            await new CompatibilityListService(paths, client).GetIndex_Async(cancellationToken: Ct);
        }

        using var changed = new HttpClient(StubHttpHandler.Text("# The page moved"));
        Assert.Equal(8, (await new CompatibilityListService(paths, changed).GetIndex_Async(true, Ct)).Count);
    }
}
