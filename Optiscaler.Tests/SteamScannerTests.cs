using Optiscaler.Core.Games;
using Optiscaler.Core.Scanning;
using Optiscaler.Infrastructure.Scanning;
using Xunit;

namespace Optiscaler.Tests;

public sealed class SteamScannerTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "Optiscaler-SteamTests", Guid.NewGuid().ToString("N"));

    private static ScanContext Context => new() { EnabledPlatforms = new HashSet<GamePlatform> { GamePlatform.Steam } };

    [Fact]
    public async Task ReadsPrimaryModernAndLegacyLibrariesWithoutDuplicates()
    {
        var primary = Library("Steam");
        var modern = Library("Other library");
        var legacy = Library("Legacy");
        Game(primary, "10", "First game");
        Game(modern, "20", "Second game");
        Game(legacy, "30", "Third game");
        File.WriteAllText(Path.Combine(primary, "steamapps", "libraryfolders.vdf"), $$"""
                                "libraryfolders"
                                {
                                    "0" { "path" "{{Escape(primary)}}" }
                                    "1" { "path" "{{Escape(modern)}}" "apps" { "20" "100" } }
                                    "2" "{{Escape(legacy)}}"
                                }
                                """);
        var result =
            await new SteamScanner([primary, modern]).ScanGames_Async(Context, TestContext.Current.CancellationToken);
        Assert.Equal(["10", "20", "30"], result.Games.Select(game => game.ExternalId).Order());
        Assert.All(result.Games, game => Assert.Null(game.ExecutablePath));
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public async Task BadManifestsDoNotHideHealthyGames()
    {
        var root = Library("Steam");
        Game(root, "10", "Healthy");
        Game(root, "20", "Stale");
        Directory.Delete(Path.Combine(root, "steamapps", "common", "Stale"));
        File.WriteAllText(Path.Combine(root, "steamapps", "appmanifest_30.acf"), "\"AppState\" { \"appid\"");
        Game(root, "40", "Escape", "../outside");
        Game(root, "50", "Mismatch");
        File.Move(Path.Combine(root, "steamapps", "appmanifest_50.acf"),
                  Path.Combine(root, "steamapps", "appmanifest_51.acf"));
        var result = await new SteamScanner([root]).ScanGames_Async(Context, TestContext.Current.CancellationToken);
        Assert.Equal("10", Assert.Single(result.Games).ExternalId);
        Assert.Equal(4, result.Diagnostics.Count);
        Assert.All(result.Diagnostics, diagnostic => Assert.Equal("steam.manifest_invalid", diagnostic.Code));
    }

    [Fact]
    public async Task BadLibraryListStillAllowsPrimaryLibrary()
    {
        var root = Library("Steam");
        Game(root, "10", "Healthy");
        File.WriteAllText(Path.Combine(root, "steamapps", "libraryfolders.vdf"), "\"wrong\" { }");
        var result = await new SteamScanner([root]).ScanGames_Async(Context, TestContext.Current.CancellationToken);
        Assert.Single(result.Games);
        Assert.Equal("steam.library_manifest_invalid", Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public async Task AllowedRootsRespectDirectoryBoundaries()
    {
        var allowed = Library("drive");
        var sibling = Library("drive-other");
        Game(allowed, "10", "Allowed");
        Game(sibling, "20", "Excluded");
        var result =
            await new GameDiscoveryCoordinator([new SteamScanner([allowed, sibling])]).ScanGames_Async(Context with
            {
                AllowedDriveRoots = [allowed]
            }, TestContext.Current.CancellationToken);
        Assert.Equal("10", Assert.Single(result.Games).ExternalId);
    }

    [Fact]
    public async Task InvalidAllowListDoesNotFallBackToUnrestrictedScan()
    {
        var root = Library("Steam");
        Game(root, "10", "Game");
        var result =
            await new GameDiscoveryCoordinator([new SteamScanner([root])]).ScanGames_Async(Context with
            {
                AllowedDriveRoots = ["relative"]
            }, TestContext.Current.CancellationToken);
        Assert.Empty(result.Games);
        Assert.Equal("scan.invalid_allowed_root", Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public async Task AcceptsCustomSteamAppsFolderAndConfigLibraryList()
    {
        var primary = Library("Steam");
        var secondary = Library("Secondary");
        Game(secondary, "10", "Custom");
        Directory.CreateDirectory(Path.Combine(primary, "config"));
        File.WriteAllText(Path.Combine(primary, "config", "libraryfolders.vdf"),
                          $"\"LibraryFolders\" {{ \"1\" {{ \"path\" \"{Escape(secondary)}\" }} }}");
        var result =
            await new SteamScanner([]).ScanGames_Async(Context with { CustomFolders = [Path.Combine(primary, "steamapps")] },
                                                 TestContext.Current.CancellationToken);
        Assert.Equal("10", Assert.Single(result.Games).ExternalId);
    }

    [Fact]
    public async Task DisabledScannerDoesNotReadSources()
    {
        var result =
            await new SteamScanner(["invalid"]).ScanGames_Async(Context with
            {
                EnabledPlatforms = new HashSet<GamePlatform>()
            }, TestContext.Current.CancellationToken);
        Assert.Empty(result.Games);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public async Task PreservesCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                                                                    new SteamScanner([]).ScanGames_Async(Context,
                                                                     cancellation.Token));
    }

    [Fact]
    public async Task DeduplicatesDirectoryAliasesOnUnix()
    {
        if (OperatingSystem.IsWindows()) return; // Creating Windows symlinks can require elevation.

        var root = Library("Steam");
        Game(root, "10", "Game");
        var alias = Path.Combine(_root, "alias");
        Directory.CreateSymbolicLink(alias, root);
        var result = await new SteamScanner([root, alias]).ScanGames_Async(Context, TestContext.Current.CancellationToken);
        Assert.Single(result.Games);
        Assert.Empty(result.Diagnostics);
    }

    [Theory]
    [InlineData("name")]
    [InlineData("installdir")]
    [InlineData("appid")]
    public async Task TypedManifestRejectsMissingRequiredFields(string field)
    {
        var root = Library("Steam");
        Game(root, "10", "Valid");
        Game(root, "20", "Invalid");
        var manifest = Path.Combine(root, "steamapps", "appmanifest_20.acf");
        var lines = File.ReadAllLines(manifest).Where(line => !line.Contains($"\"{field}\""));
        File.WriteAllLines(manifest, lines.ToArray());
        var result = await new SteamScanner([root]).ScanGames_Async(Context, TestContext.Current.CancellationToken);
        Assert.Equal("10", Assert.Single(result.Games).ExternalId);
        Assert.Equal("steam.manifest_invalid", Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public async Task TypedManifestReadsEscapedNamesAndIgnoresExtraMetadata()
    {
        var root = Library("Steam");
        Game(root, "10", "Game");
        var manifest = Path.Combine(root, "steamapps", "appmanifest_10.acf");
        var text = File.ReadAllText(manifest).Replace("\"name\" \"Game\"",
                                                      $"\"name\" \"{Escape("A \"quoted\" game")}\"");
        text = text.Insert(text.LastIndexOf('}'), "\"InstalledDepots\" { \"123\" { \"manifest\" \"456\" } }\n");
        File.WriteAllText(manifest, text);
        var result = await new SteamScanner([root]).ScanGames_Async(Context, TestContext.Current.CancellationToken);
        Assert.Equal("A \"quoted\" game", Assert.Single(result.Games).Name);
        Assert.Empty(result.Diagnostics);
    }

    private string Library(string name)
    {
        var path = Path.Combine(_root, name);
        Directory.CreateDirectory(Path.Combine(path, "steamapps", "common"));

        return path;
    }

    private static void Game(string root, string id, string name, string? installDirectory = null)
    {
        Directory.CreateDirectory(Path.Combine(root, "steamapps", "common", name));
        File.WriteAllText(Path.Combine(root, "steamapps", $"appmanifest_{id}.acf"), $$"""
                                "AppState"
                                {
                                    "appid" "{{id}}"
                                    "name" "{{Escape(name)}}"
                                    "installdir" "{{Escape(installDirectory ?? name)}}"
                                }
                                """);
    }

    private static string Escape(string value)
    {
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}