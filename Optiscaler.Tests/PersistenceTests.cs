using Microsoft.Extensions.DependencyInjection;
using Optiscaler.Core.Abstractions;
using Optiscaler.Core.Configuration;
using Optiscaler.Core.Games;
using Optiscaler.Core.Management;
using Optiscaler.Infrastructure.DependencyInjection;
using Optiscaler.Infrastructure.Management;
using Optiscaler.Infrastructure.Paths;
using Optiscaler.Infrastructure.Persistence;
using Optiscaler.Infrastructure.Scanning;
using Xunit;

namespace Optiscaler.Tests;

public sealed class PersistenceTests : IDisposable
{
    private readonly AppPaths _paths = new(Path.Combine(
        OperatingSystem.IsMacOS() ? "/private/tmp" : Path.GetTempPath(),
        "Optiscaler-persistence-" + Guid.NewGuid().ToString("N")));
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        if (Directory.Exists(_paths.RootDirectory)) Directory.Delete(_paths.RootDirectory, true);
    }

    [Theory]
    [InlineData("{ broken")]
    [InlineData("null")]
    [InlineData("{\"schemaVersion\":99}")]
    [InlineData("{\"scanSourceSettings\":null}")]
    [InlineData("{\"scanSourceSettings\":{\"customFolders\":[\"relative\"]}}")]
    [InlineData("{\"scanSourceSettings\":{\"enabledPlatforms\":[999]}}")]
    public async Task InvalidConfigurationRecoversWithoutReplacingHealthyBackup(string corrupt)
    {
        var repository = new JsonAppConfigurationRepository(_paths);
        await repository.SaveAppConfiguration_Async(new AppConfiguration { AutoScan = true }, Ct);
        await repository.SaveAppConfiguration_Async(new AppConfiguration { AutoScan = false }, Ct);
        await File.WriteAllTextAsync(_paths.ConfigurationFilePath, corrupt, Ct);
        Assert.True((await repository.LoadAppConfiguration_Async(Ct)).AutoScan);

        // A fresh store must validate the old primary before copying it over the good backup.
        repository = new JsonAppConfigurationRepository(_paths);
        await repository.SaveAppConfiguration_Async(new AppConfiguration { AutoScan = false }, Ct);
        await File.WriteAllTextAsync(_paths.ConfigurationFilePath, corrupt, Ct);
        Assert.True((await repository.LoadAppConfiguration_Async(Ct)).AutoScan);
    }

    [Fact]
    public async Task InvalidConfigurationWithoutBackupIsReported()
    {
        Directory.CreateDirectory(_paths.RootDirectory);
        await File.WriteAllTextAsync(_paths.ConfigurationFilePath, "{\"schemaVersion\":99}", Ct);
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new JsonAppConfigurationRepository(_paths).LoadAppConfiguration_Async(Ct));
    }

    [Fact]
    public async Task ProfilesSurviveRestartAndRejectDuplicateIdsOrInvalidOverrides()
    {
        var repository = new JsonProfileRepository(_paths);
        Assert.Empty((await repository.LoadProfileCatalog_Async(Ct)).Profiles);
        var profile = new RenderProfile { Name = "Balanced", Dx12Upscaler = "xess", Sharpness = 0.5m };
        await repository.SaveProfileCatalog_Async(new ProfileCatalog { Profiles = [profile] }, Ct);
        var restarted = new JsonProfileRepository(_paths);
        Assert.Equal(profile, Assert.Single((await restarted.LoadProfileCatalog_Async(Ct)).Profiles));

        foreach (var profiles in new List<RenderProfile>[]
                 { [profile, profile], [profile with { Sharpness = 2m }], [null!] })
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                restarted.SaveProfileCatalog_Async(new ProfileCatalog { Profiles = profiles }, Ct));
        Assert.Equal(profile, Assert.Single((await restarted.LoadProfileCatalog_Async(Ct)).Profiles));
    }

    [Fact]
    public async Task SemanticallyInvalidProfileCatalogUsesBackup()
    {
        var repository = new JsonProfileRepository(_paths);
        var profile = new RenderProfile { Name = "Original" };
        await repository.SaveProfileCatalog_Async(new ProfileCatalog { Profiles = [profile] }, Ct);
        await repository.SaveProfileCatalog_Async(new ProfileCatalog(), Ct);
        await File.WriteAllTextAsync(Path.Combine(_paths.RootDirectory, "profiles.json"),
            "{\"profiles\":[null]}", Ct);
        Assert.Equal(profile, Assert.Single((await repository.LoadProfileCatalog_Async(Ct)).Profiles));
    }

    [Fact]
    public async Task CatalogRejectsDuplicateIdsUnknownPlatformsAndRelativeInstallations()
    {
        var repository = new JsonGameCatalogRepository(_paths);
        var game = new GameRecord
        {
            Id = new GameId("test:1"), Name = "Example", Platform = GamePlatform.Manual,
            Installations = [new GameInstallation { RootPath = _paths.RootDirectory }]
        };
        await repository.SaveGameCatalog_Async(new GameCatalog { Games = [game] }, Ct);
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            repository.SaveGameCatalog_Async(new GameCatalog { Games = [game, game] }, Ct));
        game.Platform = (GamePlatform)999;
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            repository.SaveGameCatalog_Async(new GameCatalog { Games = [game] }, Ct));
        game.Platform = GamePlatform.Manual;
        game.Installations[0].RootPath = "relative";
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            repository.SaveGameCatalog_Async(new GameCatalog { Games = [game] }, Ct));
        var saved = Assert.Single((await repository.LoadGameCatalog_Async(Ct)).Games);
        Assert.Equal(_paths.RootDirectory, Assert.Single(saved.Installations).RootPath);
    }

    [Fact]
    public async Task NullCatalogEntryRecoversFromBackup()
    {
        var repository = new JsonGameCatalogRepository(_paths);
        await repository.SaveGameCatalog_Async(new GameCatalog(), Ct);
        await repository.SaveGameCatalog_Async(new GameCatalog(), Ct);
        await File.WriteAllTextAsync(_paths.GamesFilePath, "{\"games\":[null]}", Ct);
        Assert.Empty((await repository.LoadGameCatalog_Async(Ct)).Games);
    }

    [Fact]
    public void CompositionResolvesEveryServiceAndLauncher()
    {
        using var provider = new ServiceCollection().AddOptiscalerInfrastructure()
            .AddSingleton<IAppPaths>(_paths)
            .BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        var scanners = provider.GetServices<IGameScanner>().ToArray();
        Assert.Equal(11, scanners.Length);
        Assert.Single(scanners.OfType<SteamScanner>());
        Assert.Equal(2, scanners.OfType<HeroicScanner>().Count());
        Assert.NotNull(provider.GetRequiredService<GameDiscoveryCoordinator>());
        Assert.NotNull(provider.GetRequiredService<IGameAnalyzer>());
        Assert.NotNull(provider.GetRequiredService<IGameCatalogRepository>());
        Assert.NotNull(provider.GetRequiredService<IAppConfigurationRepository>());
        Assert.Same(provider.GetRequiredService<IProfileRepository>(), provider.GetRequiredService<IProfileRepository>());
        Assert.Same(provider.GetRequiredService<IGameInstallationService>(),
            provider.GetRequiredService<IGameInstallationService>());
    }

    [Fact]
    public void IniPreservesUnknownSettingsAndReplacesEveryDuplicateOverride()
    {
        var ini = "; header\r\n[Upscalers]\r\nDx12Upscaler=auto\r\nDx12Upscaler=dlss\r\n[Unknown]\r\nA=B\r\n";
        var output = ProfileIni.ApplyProfileToIni(ini, new RenderProfile { Name = "Test", Dx12Upscaler = "xess" });
        Assert.Contains("; header\r\n", output);
        Assert.Contains("[Unknown]\r\nA=B", output);
        Assert.DoesNotContain("Dx12Upscaler=dlss", output);
        Assert.DoesNotContain("Dx12Upscaler=auto", output);
    }
}
