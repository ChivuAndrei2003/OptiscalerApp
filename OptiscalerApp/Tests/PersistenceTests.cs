using OptiscalerApp.Models;
using OptiscalerApp.Paths;
using OptiscalerApp.Persistence;
using Xunit;

namespace Optiscaler.Tests;

public sealed class PersistenceTests : TempFolderTest
{
    private AppPaths Paths => new(Path.Combine(Root, "data"));

    [Theory]
    [InlineData("{ broken")]
    [InlineData("{\"schemaVersion\":99}")]
    [InlineData("{\"scanSourceSettings\":{\"customFolders\":[\"relative\"]}}")]
    public async Task InvalidConfigurationRecoversWithoutReplacingHealthyBackup(string corrupt)
    {
        var repository = new JsonAppConfigurationRepository(Paths);
        await repository.SaveAppConfigurationAsync(new AppConfiguration { AutoScan = true }, Ct);
        await repository.SaveAppConfigurationAsync(new AppConfiguration { AutoScan = false }, Ct);
        await File.WriteAllTextAsync(Paths.ConfigurationFilePath, corrupt, Ct);
        Assert.True((await repository.LoadAppConfigurationAsync(Ct)).AutoScan);

        // A fresh store must validate the old primary before copying it over the good backup.
        repository = new JsonAppConfigurationRepository(Paths);
        await repository.SaveAppConfigurationAsync(new AppConfiguration { AutoScan = false }, Ct);
        await File.WriteAllTextAsync(Paths.ConfigurationFilePath, corrupt, Ct);
        Assert.True((await repository.LoadAppConfigurationAsync(Ct)).AutoScan);
    }

    [Fact]
    public async Task InvalidConfigurationWithoutBackupIsReported()
    {
        Directory.CreateDirectory(Paths.RootDirectory);
        await File.WriteAllTextAsync(Paths.ConfigurationFilePath, "{\"schemaVersion\":99}", Ct);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => new JsonAppConfigurationRepository(Paths).LoadAppConfigurationAsync(Ct));
    }

    [Fact]
    public async Task ProfilesSurviveRestartAndInvalidCatalogsAreNotSaved()
    {
        var repository = new JsonProfileRepository(Paths);
        Assert.Empty((await repository.LoadProfileCatalogAsync(Ct)).Profiles);
        var profile = TestData.Profile("Balanced", ("Upscalers.Dx12Upscaler", "xess"), ("Sharpness.Sharpness", "0.5"));
        await repository.SaveProfileCatalogAsync(new ProfileCatalog { Profiles = [profile] }, Ct);
        var restarted = new JsonProfileRepository(Paths);
        Assert.Equal(profile, Assert.Single((await restarted.LoadProfileCatalogAsync(Ct)).Profiles));

        ProfileCatalog[] invalid =
        [
            new() { Profiles = [profile, profile] },
            new() { Profiles = [TestData.Profile("Too sharp", ("Sharpness.Sharpness", "2"))] },
            new() { DefaultProfileId = Guid.NewGuid() }
        ];
        foreach (var catalog in invalid)
            await Assert.ThrowsAsync<InvalidDataException>(() => restarted.SaveProfileCatalogAsync(catalog, Ct));

        Assert.Equal(profile, Assert.Single((await restarted.LoadProfileCatalogAsync(Ct)).Profiles));
    }

    [Fact]
    public async Task VersionOneProfilesAreUpgradedToSettings()
    {
        var id = Guid.NewGuid();
        Directory.CreateDirectory(Paths.RootDirectory);
        await File.WriteAllTextAsync(Paths.ProfilesFilePath, $$"""
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
        var repository = new JsonProfileRepository(Paths);

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
        Assert.Contains("\"schemaVersion\": 2", await File.ReadAllTextAsync(Paths.ProfilesFilePath, Ct));
    }

    [Fact]
    public async Task CatalogRejectsDuplicateIdsUnknownPlatformsAndRelativeInstallations()
    {
        var repository = new JsonGameCatalogRepository(Paths);
        var game = TestData.Game(Root);
        await repository.SaveGameCatalogAsync(new GameCatalog { Games = [game] }, Ct);

        GameCatalog[] invalid =
        [
            new() { Games = [game, game] },
            new() { Games = [TestData.Game(Root, platform: (GamePlatform)999)] },
            new() { Games = [TestData.Game("relative")] }
        ];
        foreach (var catalog in invalid)
            await Assert.ThrowsAsync<InvalidDataException>(() => repository.SaveGameCatalogAsync(catalog, Ct));

        var saved = Assert.Single((await repository.LoadGameCatalogAsync(Ct)).Games);
        Assert.Equal(Root, Assert.Single(saved.Installations).RootPath);
    }
}
