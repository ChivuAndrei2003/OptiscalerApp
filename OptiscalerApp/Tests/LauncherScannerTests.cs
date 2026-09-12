using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using OptiscalerApp.Models;
using OptiscalerApp.DependencyInjection;
using OptiscalerApp.Paths;
using OptiscalerApp.Persistence;
using OptiscalerApp.Scanning;
using Xunit;

namespace Optiscaler.Tests;

public sealed class LauncherScannerTests : IDisposable
{
    private readonly string _root = Path.Combine(OperatingSystem.IsMacOS() ? "/private/tmp" : Path.GetTempPath(),
                                                 "Optiscaler-launchers-" + Guid.NewGuid().ToString("N"));

    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ScanContext Context(params GamePlatform[] platforms)
    {
        return new ScanContext { EnabledPlatforms = platforms.ToHashSet() };
    }

    public LauncherScannerTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        Directory.Delete(_root, true);
    }

    private string Folder(string name)
    {
        var path = Path.Combine(_root, name);
        Directory.CreateDirectory(path);

        return path;
    }

    private async Task<string> Json(string name, object data)
    {
        var file = Path.Combine(_root, name);
        await File.WriteAllTextAsync(file, JsonSerializer.Serialize(data), Ct);

        return file;
    }

    [Fact]
    public async Task HeroicAndLegendaryKeepValidEntriesAfterMalformedEntryAndSkipDlcAndMissingGames()
    {
        var game = Folder("Game with spaces");
        await File.WriteAllTextAsync(Path.Combine(game, "game.exe"), "fixture", Ct);
        var file = await Json("installed.json", new Dictionary<string, object>
        {
            ["broken"] = new { title = "Broken", install_path = "relative/path" },
            ["alpha"] = new { title = "Alpha", install_path = game, executable = "game.exe", platform = "Windows" },
            ["dlc"] = new { title = "DLC", install_path = game, is_dlc = true },
            ["missing"] = new { title = "Missing", install_path = Path.Combine(_root, "absent") }
        });
        var result = await new HeroicScanner(GamePlatform.Epic, [file]).ScanGames_Async(Context(GamePlatform.Epic), Ct);
        var found = Assert.Single(result.Games);
        Assert.Equal("alpha", found.ExternalId);
        Assert.Equal(Path.Combine(game, "game.exe"), found.ExecutablePath);
        Assert.Single(result.Diagnostics);
    }

    [Fact]
    public async Task GogHeroicReadsInstalledArrayAndDoesNotEscapeExecutableRoot()
    {
        var game = Folder("GOG Game");
        await File.WriteAllTextAsync(Path.Combine(_root, "outside.exe"), "fixture", Ct);
        var file = await Json("gog.json", new
        {
            installed = new[]
            {
                new
                {
                    title = "GOG", appName = "42", install_path = game,
                    executable = "../outside.exe", platform = "linux"
                }
            }
        });
        var scanner = new HeroicScanner(GamePlatform.Gog, [file]);
        var result = await scanner.ScanGames_Async(Context(GamePlatform.Gog), Ct);
        Assert.Equal("42", Assert.Single(result.Games).ExternalId);
        Assert.Null(result.Games[0].ExecutablePath);
        Assert.Empty((await scanner.ScanGames_Async(Context(GamePlatform.Epic), Ct)).Games);
    }

    [Fact]
    public async Task BrokenHeroicFileDoesNotHideNextMetadataFile()
    {
        var broken = Path.Combine(_root, "broken.json");
        await File.WriteAllTextAsync(broken, "{", Ct);
        var good = await Json("good.json",
                              new
                              {
                                  installed = new[]
                                      { new { title = "Game", app_name = "1", install_path = Folder("game") } }
                              });
        var result = await new HeroicScanner(GamePlatform.Gog, [broken, good]).ScanGames_Async(Context(GamePlatform.Gog), Ct);
        Assert.Single(result.Games);
        Assert.Single(result.Diagnostics);
    }

    [Fact]
    public async Task LutrisReadsQuotedYamlAndMapsWineCDriveWithoutLosingHealthyGames()
    {
        var prefix = Folder("prefix");
        var game = Folder("prefix/drive_c/Games/Example");
        await File.WriteAllTextAsync(Path.Combine(game, "game.exe"), "fixture", Ct);
        var configs = Folder("lutris/games");
        await File.WriteAllTextAsync(Path.Combine(configs, "example-123.yml"),
                                     $"name: 'Example: Game'\ngame:\n  exe: 'C:\\Games\\Example\\game.exe'\n  prefix: '{prefix}'\nsystem:\n  env: {{ KEY: value }}\n",
                                     Ct);
        await File.WriteAllTextAsync(Path.Combine(configs, "broken.yml"), "game: [unclosed", Ct);
        var result = await new LutrisScanner([configs]).ScanGames_Async(Context(GamePlatform.Lutris), Ct);
        var found = Assert.Single(result.Games);
        Assert.Equal("Example: Game", found.Name);
        Assert.Equal(game, found.InstallPath);
        Assert.Equal(Path.Combine(game, "game.exe"), found.ExecutablePath);
        Assert.Single(result.Diagnostics);
    }

    [Fact]
    public async Task LutrisSupportsRelativeExeWithWorkingDirectoryAndSkipsUnresolvedRelativeExe()
    {
        var game = Folder("native");
        await File.WriteAllTextAsync(Path.Combine(game, "game"), "fixture", Ct);
        var configs = Folder("configs");
        await File.WriteAllTextAsync(Path.Combine(configs, "native-123.yaml"),
                                     $"game:\n  exe: game\n  working_dir: '{game}'\n", Ct);
        await File.WriteAllTextAsync(Path.Combine(configs, "bad.yml"), "game:\n  exe: missing.exe\n", Ct);
        var result = await new LutrisScanner([configs]).ScanGames_Async(Context(GamePlatform.Lutris), Ct);
        Assert.Equal("native", Assert.Single(result.Games).Name);
        Assert.Single(result.Diagnostics);
    }

    [Theory]
    [InlineData(GamePlatform.Gog, "42", "gameName", "path")]
    [InlineData(GamePlatform.Ea, "Game", "DisplayName", "Install Dir")]
    [InlineData(GamePlatform.Ubisoft, "Uplay Install 42", "DisplayName", "InstallLocation")]
    [InlineData(GamePlatform.BattleNet, "Game", "DisplayName", "InstallLocation")]
    public async Task MapsWindowsLauncherRegistryAndDeduplicatesRegistryViews(
        GamePlatform platform, string key, string nameKey, string pathKey)
    {
        var values = new Dictionary<string, string>
            { [nameKey] = "Example", [pathKey] = Folder("Game"), ["Publisher"] = "Blizzard Entertainment" };
        var entry = new RegistryGameEntry(key, values);
        var scanner = new WindowsRegistryScanner(platform, [entry, entry]);
        var result = await new GameDiscoveryCoordinator([scanner]).ScanGames_Async(Context(platform), Ct);
        Assert.Equal("Example", Assert.Single(result.Games).Name);
        Assert.Equal(platform, result.Games[0].Platform);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public async Task RegistryFiltersLauncherAndUnrelatedApplications()
    {
        var entries = new[]
        {
            new RegistryGameEntry("launcher",
                                  new Dictionary<string, string>
                                  {
                                      ["DisplayName"] = "Battle.net", ["Publisher"] = "Blizzard Entertainment",
                                      ["InstallLocation"] = _root
                                  }),
            new RegistryGameEntry("other",
                                  new Dictionary<string, string>
                                  {
                                      ["DisplayName"] = "Other", ["Publisher"] = "Other", ["InstallLocation"] = _root
                                  })
        };
        Assert.Empty((await new BattleNetScanner(entries).ScanGames_Async(Context(GamePlatform.BattleNet), Ct)).Games);
        Assert.Empty((await new UbisoftScanner(entries).ScanGames_Async(Context(GamePlatform.Ubisoft), Ct)).Games);
    }

    [Fact]
    public async Task XboxRequiresGameMetadataAndUsesContentFolder()
    {
        var library = Folder("XboxGames");
        var content = Folder("XboxGames/Example/Content");
        Folder("XboxGames/Empty");
        await File.WriteAllTextAsync(Path.Combine(content, "game.exe"), "fixture", Ct);
        await File.WriteAllTextAsync(Path.Combine(content, "MicrosoftGame.config"),
                                     "<Game><Identity Name=\"Example.Identity\"/><ShellVisuals DefaultDisplayName=\"Example Game\"/><ExecutableList><Executable Name=\"game.exe\"/></ExecutableList></Game>",
                                     Ct);
        var result = await new XboxScanner([library]).ScanGames_Async(Context(GamePlatform.Xbox), Ct);
        Assert.Equal("Example Game", Assert.Single(result.Games).Name);
        Assert.Equal(content, result.Games[0].InstallPath);
    }

    [Fact]
    public async Task EpicSkipsIncompleteAndNonApplicationManifestsAndFindsLaunchExecutable()
    {
        var game = Folder("Epic");
        await File.WriteAllTextAsync(Path.Combine(game, "game.exe"), "fixture", Ct);
        await Json("good.item",
                   new
                   {
                       DisplayName = "Game", AppName = "good", InstallLocation = game, LaunchExecutable = "game.exe"
                   });
        await Json("partial.item",
                   new
                   {
                       DisplayName = "Partial", AppName = "partial", InstallLocation = game, bIsIncompleteInstall = true
                   });
        await Json("dlc.item",
                   new { DisplayName = "DLC", AppName = "dlc", InstallLocation = game, bIsApplication = false });
        var result = await new EpicScanner([_root]).ScanGames_Async(Context(GamePlatform.Epic), Ct);
        Assert.Equal("good", Assert.Single(result.Games).ExternalId);
        Assert.Equal(Path.Combine(game, "game.exe"), result.Games[0].ExecutablePath);
    }

    [Fact]
    public async Task AllNewSourcesHonorCancellation()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        IGameScanner[] scanners =
        [
            new HeroicScanner(GamePlatform.Epic, []), new LutrisScanner([]), new GogScanner([]), new XboxScanner([]),
            new EpicScanner([])
        ];
        foreach (var scanner in scanners)
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => scanner.ScanGames_Async(Context(scanner.Platform),
                                                                     cancelled.Token));
    }

    [Fact]
    public void LinuxLocationsIncludeXdgAndFlatpakAndWindowsUsesRoamingConfig()
    {
        var home = Folder("home");
        var config = Folder("xdg-config");
        var data = Folder("xdg-data");
        Assert.Contains(Path.Combine(config, "heroic"), LauncherLocations.HeroicRoots(home, config, false));
        Assert.Contains(Path.Combine(home, ".var/app/com.heroicgameslauncher.hgl/config/heroic"),
                        LauncherLocations.HeroicRoots(home, config, false));
        Assert.Equal([Path.Combine(config, "heroic")], LauncherLocations.HeroicRoots(home, config, true));
        Assert.Contains(Path.Combine(data, "lutris/games"), LauncherLocations.LutrisRoots(home, config, data));
    }

}
