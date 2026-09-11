using Optiscaler.Core.Abstractions;
using Optiscaler.Core.Games;
using Optiscaler.Core.Scanning;
using Optiscaler.Infrastructure.Scanning;
using Xunit;

namespace Optiscaler.Tests;

public sealed class GameDiscoveryCoordinatorTests
{
    [Theory]
    [InlineData(GamePlatform.Steam)]
    [InlineData(GamePlatform.Gog)]
    public async Task AppliesDriveFilterToAnyScanner(GamePlatform platform)
    {
        var root = Path.Combine(Path.GetTempPath(), "Optiscaler-CoordinatorTests");
        var games = new[]
        {
            Game(platform, "1", Path.Combine(root, "allowed", "Game")),
            Game(platform, "2", Path.Combine(root, "allowed-other", "Game"))
        };
        var coordinator = new GameDiscoveryCoordinator([new StubScanner(platform, games)]);
        var result = await coordinator.ScanGames_Async(new ScanContext
        {
            EnabledPlatforms = new HashSet<GamePlatform> { platform },
            AllowedDriveRoots = [Path.Combine(root, "allowed")]
        }, TestContext.Current.CancellationToken);
        Assert.Equal("1", Assert.Single(result.Games).ExternalId);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public async Task DeduplicatesAndSortsScannerResults()
    {
        var root = Path.GetTempPath();
        var first = Game(GamePlatform.Steam, "1", Path.Combine(root, "GameA")) with { Name = "Alpha" };
        var last = Game(GamePlatform.Steam, "2", Path.Combine(root, "GameZ")) with { Name = "Zulu" };
        var coordinator = new GameDiscoveryCoordinator([new StubScanner(GamePlatform.Steam, [last, first, first])]);
        var result = await coordinator.ScanGames_Async(new ScanContext
        {
            EnabledPlatforms = new HashSet<GamePlatform> { GamePlatform.Steam }
        }, TestContext.Current.CancellationToken);
        Assert.Equal(["Alpha", "Zulu"], result.Games.Select(game => game.Name));
    }

    [Fact]
    public async Task InvalidAllowedRootDoesNotAllowAllGames()
    {
        var coordinator = new GameDiscoveryCoordinator([
            new StubScanner(GamePlatform.Steam, [Game(GamePlatform.Steam, "1", Path.GetTempPath())])
        ]);
        var result = await coordinator.ScanGames_Async(new ScanContext
        {
            EnabledPlatforms = new HashSet<GamePlatform> { GamePlatform.Steam },
            AllowedDriveRoots = ["relative/path"]
        }, TestContext.Current.CancellationToken);
        Assert.Empty(result.Games);
        Assert.Equal("scan.invalid_allowed_root", Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public async Task FailedScannerDoesNotHideHealthyResults()
    {
        var coordinator = new GameDiscoveryCoordinator([
            new FailingScanner(),
            new StubScanner(GamePlatform.Steam, [Game(GamePlatform.Steam, "1", Path.GetTempPath())])
        ]);
        var result = await coordinator.ScanGames_Async(new ScanContext
        {
            EnabledPlatforms = new HashSet<GamePlatform> { GamePlatform.Steam }
        }, TestContext.Current.CancellationToken);
        Assert.Single(result.Games);
        Assert.Equal("scanner.failed", Assert.Single(result.Diagnostics).Code);
    }

    private sealed class FailingScanner : IGameScanner
    {
        public GamePlatform Platform => GamePlatform.Steam;

        public Task<ScanResult> ScanGames_Async(ScanContext context, CancellationToken cancellationToken = default)
        {
            throw new IOException("Unreadable launcher metadata.");
        }
    }

    private static DiscoveredGame Game(GamePlatform platform, string id, string path)
    {
        return new DiscoveredGame
        {
            Platform = platform, ExternalId = id, Name = id, InstallPath = path
        };
    }

    private sealed class StubScanner(GamePlatform platform, IEnumerable<DiscoveredGame> games) : IGameScanner
    {
        public GamePlatform Platform => platform;

        public Task<ScanResult> ScanGames_Async(ScanContext context, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ScanResult { Games = games.ToList() });
        }
    }
}
