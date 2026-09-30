using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using OptiscalerApp.DependencyInjection;
using OptiscalerApp.Development;
using OptiscalerApp.Management;
using OptiscalerApp.Models;
using OptiscalerApp.Paths;
using OptiscalerApp.Persistence;
using OptiscalerApp.Scanning;
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
        await repository.SaveAppConfigurationAsync(new AppConfiguration { AutoScan = true }, Ct);
        await repository.SaveAppConfigurationAsync(new AppConfiguration { AutoScan = false }, Ct);
        await File.WriteAllTextAsync(_paths.ConfigurationFilePath, corrupt, Ct);
        Assert.True((await repository.LoadAppConfigurationAsync(Ct)).AutoScan);

        // A fresh store must validate the old primary before copying it over the good backup.
        repository = new JsonAppConfigurationRepository(_paths);
        await repository.SaveAppConfigurationAsync(new AppConfiguration { AutoScan = false }, Ct);
        await File.WriteAllTextAsync(_paths.ConfigurationFilePath, corrupt, Ct);
        Assert.True((await repository.LoadAppConfigurationAsync(Ct)).AutoScan);
    }

    [Fact]
    public async Task InvalidConfigurationWithoutBackupIsReported()
    {
        Directory.CreateDirectory(_paths.RootDirectory);
        await File.WriteAllTextAsync(_paths.ConfigurationFilePath, "{\"schemaVersion\":99}", Ct);
        await Assert.ThrowsAsync<InvalidDataException>(() =>
                                                           new JsonAppConfigurationRepository(_paths)
                                                               .LoadAppConfigurationAsync(Ct));
    }

    [Fact]
    public async Task ProfilesSurviveRestartAndRejectDuplicateIdsOrInvalidOverrides()
    {
        var repository = new JsonProfileRepository(_paths);
        Assert.Empty((await repository.LoadProfileCatalogAsync(Ct)).Profiles);
        var profile = TestData.Profile("Balanced", ("Upscalers.Dx12Upscaler", "xess"), ("Sharpness.Sharpness", "0.5"));
        await repository.SaveProfileCatalogAsync(new ProfileCatalog { Profiles = [profile] }, Ct);
        var restarted = new JsonProfileRepository(_paths);
        Assert.Equal(profile, Assert.Single((await restarted.LoadProfileCatalogAsync(Ct)).Profiles));

        foreach (var profiles in new List<RenderProfile>[]
                 {
                     [profile, profile], [TestData.Profile("Too sharp", ("Sharpness.Sharpness", "2"))], [null!]
                 })
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                                                               restarted.SaveProfileCatalogAsync(new ProfileCatalog
                                                               {
                                                                   Profiles = profiles
                                                               }, Ct));
        Assert.Equal(profile, Assert.Single((await restarted.LoadProfileCatalogAsync(Ct)).Profiles));
    }

    [Fact]
    public async Task VersionOneProfilesAreUpgradedToSettings()
    {
        var id = Guid.NewGuid();
        Directory.CreateDirectory(_paths.RootDirectory);
        await File.WriteAllTextAsync(Path.Combine(_paths.RootDirectory, "profiles.json"), $$"""
            {
              "schemaVersion": 1,
              "defaultProfileId": "{{id}}",
              "profiles": [{
                "id": "{{id}}", "name": "Deck", "description": "Old",
                "dx11Upscaler": "auto", "dx12Upscaler": "xess", "vulkanUpscaler": "auto",
                "sharpness": 0.35, "enableLogging": false, "spoofDxgi": false,
                "overlayKey": 36, "frameGenKey": -1, "frameGenInput": "auto", "frameGenOutput": "fsrfg",
                "disableOverlays": null, "loadReshade": true, "loadSpecialK": false, "framerateLimit": 40,
                "settings": { "Spoofing.Vulkan": "true" }
              }]
            }
            """, Ct);
        var repository = new JsonProfileRepository(_paths);

        var catalog = await repository.LoadProfileCatalogAsync(Ct);

        var profile = Assert.Single(catalog.Profiles);
        Assert.Equal(id, catalog.DefaultProfileId);
        Assert.Equal(("Deck", "Old"), (profile.Name, profile.Description));
        Assert.Equal(new Dictionary<string, string>
        {
            ["Spoofing.Vulkan"] = "true", ["Upscalers.Dx12Upscaler"] = "xess",
            ["Sharpness.OverrideSharpness"] = "true", ["Sharpness.Sharpness"] = "0.35", ["Spoofing.Dxgi"] = "false",
            ["Menu.ShortcutKey"] = "0x24", ["Menu.FGShortcutKey"] = "-1", ["FrameGen.FGOutput"] = "fsrfg",
            ["Plugins.LoadReshade"] = "true", ["Framerate.FramerateLimit"] = "40"
        }, profile.Settings);

        // The next save writes the current schema.
        await repository.SaveProfileCatalogAsync(catalog, Ct);
        Assert.Contains("\"schemaVersion\": 2",
                        await File.ReadAllTextAsync(Path.Combine(_paths.RootDirectory, "profiles.json"), Ct));
    }

    [Fact]
    public async Task SemanticallyInvalidProfileCatalogUsesBackup()
    {
        var repository = new JsonProfileRepository(_paths);
        var profile = new RenderProfile { Name = "Original" };
        await repository.SaveProfileCatalogAsync(new ProfileCatalog { Profiles = [profile] }, Ct);
        await repository.SaveProfileCatalogAsync(new ProfileCatalog(), Ct);
        await File.WriteAllTextAsync(Path.Combine(_paths.RootDirectory, "profiles.json"),
                                     "{\"profiles\":[null]}", Ct);
        Assert.Equal(profile, Assert.Single((await repository.LoadProfileCatalogAsync(Ct)).Profiles));
    }

    [Fact]
    public async Task CatalogRejectsDuplicateIdsUnknownPlatformsAndRelativeInstallations()
    {
        var repository = new JsonGameCatalogRepository(_paths);
        var game = new GameRecord
        {
            Id = new GameId("test:1"),
            Name = "Example",
            Platform = GamePlatform.Manual,
            Installations = [new GameInstallation { RootPath = _paths.RootDirectory }]
        };
        await repository.SaveGameCatalogAsync(new GameCatalog { Games = [game] }, Ct);
        await Assert.ThrowsAsync<InvalidDataException>(() =>
                                                           repository.SaveGameCatalogAsync(new GameCatalog
                                                           {
                                                               Games = [game, game]
                                                           }, Ct));
        game.Platform = (GamePlatform)999;
        await Assert.ThrowsAsync<InvalidDataException>(() =>
                                                           repository.SaveGameCatalogAsync(new GameCatalog
                                                           {
                                                               Games = [game]
                                                           }, Ct));
        game.Platform = GamePlatform.Manual;
        game.Installations[0].RootPath = "relative";
        await Assert.ThrowsAsync<InvalidDataException>(() =>
                                                           repository.SaveGameCatalogAsync(new GameCatalog
                                                           {
                                                               Games = [game]
                                                           }, Ct));
        var saved = Assert.Single((await repository.LoadGameCatalogAsync(Ct)).Games);
        Assert.Equal(_paths.RootDirectory, Assert.Single(saved.Installations).RootPath);
    }

    [Fact]
    public async Task NullCatalogEntryRecoversFromBackup()
    {
        var repository = new JsonGameCatalogRepository(_paths);
        await repository.SaveGameCatalogAsync(new GameCatalog(), Ct);
        await repository.SaveGameCatalogAsync(new GameCatalog(), Ct);
        await File.WriteAllTextAsync(_paths.GamesFilePath, "{\"games\":[null]}", Ct);
        Assert.Empty((await repository.LoadGameCatalogAsync(Ct)).Games);
    }

    [Fact]
    public async Task DemoWorkspaceSupportsInstallVerifyRestoreOutsideApplicationData()
    {
        await DemoWorkspace.CreateAsync(_paths.RootDirectory, Ct);
        var paths = new AppPaths(Path.Combine(_paths.RootDirectory, "data"));
        var game = Assert.Single((await new JsonGameCatalogRepository(paths).LoadGameCatalogAsync(Ct)).Games);
        var profiles = await new JsonProfileRepository(paths).LoadProfileCatalogAsync(Ct);
        using var client = new HttpClient();
        var service = new GameInstallationService(paths, new PackageDownloadService(paths, client));
        var executable = Assert.Single(game.Installations).PrimaryExecutablePath!;
        var target = Path.GetDirectoryName(executable)!;
        var plan = await service.PreviewInstallationAsync(executable, Path.Combine(_paths.RootDirectory, "Package"),
                                                           "dxgi.dll", Assert.Single(profiles.Profiles), Ct);
        await service.ExecuteInstallationPlanAsync(plan, Ct);
        Assert.True((await service.VerifyInstallationAsync(target, Ct)).IsVerified);
        Assert.Contains("Dx12Upscaler=xess", await File.ReadAllTextAsync(Path.Combine(target, "OptiScaler.ini"), Ct));
        await service.RestoreLatestOperationAsync(target, Ct);
        Assert.False(File.Exists(Path.Combine(target, "dxgi.dll")));
        Assert.True(File.Exists(executable));
    }

    [Fact]
    public void CompositionResolvesEveryServiceAndLauncher()
    {
        using var provider = new ServiceCollection().AddOptiscalerServices()
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
        Assert.Same(provider.GetRequiredService<IProfileRepository>(),
                    provider.GetRequiredService<IProfileRepository>());
        Assert.Same(provider.GetRequiredService<IGameInstallationService>(),
                    provider.GetRequiredService<IGameInstallationService>());
    }

    [Fact]
    public void IniPreservesUnknownSettingsAndReplacesEveryDuplicateOverride()
    {
        var ini = "; header\r\n[Upscalers]\r\nDx12Upscaler=auto\r\nDx12Upscaler=dlss\r\n[Unknown]\r\nA=B\r\n";
        var output = ProfileIni.ApplyProfileToIni(ini, TestData.Profile("Test", ("Upscalers.Dx12Upscaler", "xess")));
        Assert.Contains("; header\r\n", output);
        Assert.Contains("[Unknown]\r\nA=B", output);
        Assert.DoesNotContain("Dx12Upscaler=dlss", output);
        Assert.DoesNotContain("Dx12Upscaler=auto", output);
    }
}