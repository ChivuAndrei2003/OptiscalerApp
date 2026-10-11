using OptiscalerApp.Management;
using OptiscalerApp.Models;
using Xunit;

namespace Optiscaler.Tests;

public sealed class ProfileIniTests
{
    private const string PackageIni = """
                                      [Upscalers]
                                      ; Select Upscaler for Dx12 games
                                      Dx12Upscaler=auto
                                      [Spoofing]
                                      Dxgi=auto
                                      [Menu]
                                      ShortcutKey=auto
                                      [FrameGen]
                                      Enabled=auto
                                      FGInput=auto
                                      FGOutput=auto
                                      [DLSS]
                                      Enabled=auto
                                      """;

    [Fact]
    public void WritesSettingsIntoTheirDocumentedSections()
    {
        var ini = ProfileIni.ApplyProfileToIni(PackageIni, TestData.Profile("Handheld",
            ("Spoofing.Dxgi", "false"), ("Menu.ShortcutKey", "0x24"), ("Menu.FGShortcutKey", "-1"),
            ("FrameGen.FGInput", "upscaler"), ("FrameGen.FGOutput", "fsrfg"), ("Hotfix.DisableOverlays", "false"),
            ("Plugins.LoadReshade", "true"), ("Framerate.FramerateLimit", "58.5"),
            ("Upscalers.VulkanUpscaler", "xess")));
        var values = ProfileIni.ReadIniValues(ini);

        Assert.Equal("false", values[("Spoofing", "Dxgi")]);
        Assert.Equal("0x24", values[("Menu", "ShortcutKey")]);
        Assert.Equal("-1", values[("Menu", "FGShortcutKey")]);
        Assert.Equal("fsrfg", values[("FrameGen", "FGOutput")]);

        // Choosing an output turns frame generation on; a key with the same name in another section is left alone.
        Assert.Equal("true", values[("FrameGen", "Enabled")]);
        Assert.Equal("auto", values[("DLSS", "Enabled")]);
        Assert.Equal("false", values[("Hotfix", "DisableOverlays")]);
        Assert.Equal("true", values[("Plugins", "LoadReshade")]);
        Assert.Equal("58.5", values[("Framerate", "FramerateLimit")]);
        Assert.Equal("xess", values[("Upscalers", "VulkanUpscaler")]);
        Assert.Contains("; Select Upscaler for Dx12 games", ini);
    }

    [Fact]
    public void PreservesUnknownSettingsAndReplacesEveryDuplicateOverride()
    {
        var ini = "; header\r\n[Upscalers]\r\nDx12Upscaler=auto\r\nDx12Upscaler=dlss\r\n[Unknown]\r\nA=B\r\n";

        var output = ProfileIni.ApplyProfileToIni(ini, TestData.Profile("Test", ("Upscalers.Dx12Upscaler", "xess")));

        Assert.Contains("; header\r\n", output);
        Assert.Contains("[Unknown]\r\nA=B", output);
        Assert.DoesNotContain("Dx12Upscaler=dlss", output);
        Assert.DoesNotContain("Dx12Upscaler=auto", output);
    }

    [Fact]
    public void ApplyingAProfileResetsOverridesItLeavesOnDefault()
    {
        var installed = ProfileIni.ApplyProfileToIni("[Spoofing]\nVulkan=auto\n",
                                                     TestData.Profile("Previous", ("Spoofing.Vulkan", "false")));
        Assert.Contains("Vulkan=false", installed);

        Assert.Contains("Vulkan=auto", ProfileIni.ApplyProfileToIni(installed, new RenderProfile { Name = "Next" }));

        // Keeping the game's settings writes only the profile's own overrides.
        Assert.Contains("Vulkan=false",
                        ProfileIni.ApplyProfileToIni(installed, new RenderProfile { Name = "Next" }, true));
    }

    [Theory]
    [InlineData("Unknown.Key", "true")]
    [InlineData("framegen.drawuioverfg", "true")]
    [InlineData("FrameGen.DrawUIOverFG", "auto")]
    [InlineData("OutputScaling.Multiplier", "5")]
    [InlineData("Upscalers.Dx12Upscaler", "fsr31")]
    [InlineData("Spoofing.SpoofedGPUName", "RTX\n[Log]")]
    public void InvalidSettingsAreRejected(string id, string value)
    {
        Assert.Throws<InvalidDataException>(() => ProfileIni.ValidateProfile(TestData.Profile("Invalid", (id, value))));
    }

    [Theory]
    [InlineData("0x23", null, "different keys")] // End is already the default FG key.
    [InlineData(null, "0x2D", "different keys")] // Insert is already the default overlay key.
    [InlineData("0x21", null, "Open OptiScaler menu")] // Page Up belongs to the FPS overlay.
    [InlineData("0x1FF", null, "Open OptiScaler menu")]
    public void RejectsShortcutsThatCollide(string? overlay, string? frameGen, string message)
    {
        var settings = new Dictionary<string, string>();
        if (overlay is not null) settings["Menu.ShortcutKey"] = overlay;
        if (frameGen is not null) settings["Menu.FGShortcutKey"] = frameGen;

        var error = Assert.Throws<InvalidDataException>(() =>
                                                            ProfileIni.ValidateProfile(new RenderProfile
                                                            {
                                                                Settings = settings
                                                            }));

        Assert.Contains(message, error.Message);
    }

    [Fact]
    public void DisabledShortcutsNeverCollide()
    {
        ProfileIni.ValidateProfile(TestData.Profile("Keys", ("Menu.ShortcutKey", "-1"), ("Menu.FGShortcutKey", "-1")));
    }

    [Fact]
    public void ProfilesRoundTripThroughTheIni()
    {
        var profile = TestData.Profile("Original", ("Upscalers.Dx12Upscaler", "xess"), ("Spoofing.Dxgi", "true"),
                                       ("Menu.ShortcutKey", "0x70"), ("FrameGen.FGOutput", "xefg"),
                                       ("Hotfix.DisableOverlays", "true"), ("Plugins.LoadSpecialK", "true"),
                                       ("Sharpness.OverrideSharpness", "true"), ("Sharpness.Sharpness", "0.3"),
                                       ("Log.LogToFile", "true"));

        var imported = ProfileIni.ReadProfileFromIni(ProfileIni.ApplyProfileToIni(PackageIni, profile), "Imported");

        Assert.Equal(profile.Settings, imported.Settings);
    }

    [Fact]
    public void ImportReadsSupportedNonAutoValues()
    {
        const string ini = """
                           [Upscalers]
                           Dx11Upscaler=auto
                           Dx12Upscaler=XeSS
                           [Sharpness]
                           OverrideSharpness=true
                           Sharpness=0.3
                           [Menu]
                           Scale=1.5
                           ShowFps=auto
                           ; ShowFps=true
                           [Spoofing]
                           Vulkan=False
                           [Log]
                           LogToFile=True
                           LogLevel=9
                           """;

        var profile = ProfileIni.ReadProfileFromIni(ini.Replace("\n", "\r\n"), "Imported");

        Assert.Equal("Imported", profile.Name);
        Assert.Equal(new Dictionary<string, string>
        {
            ["Upscalers.Dx12Upscaler"] = "xess", ["Sharpness.OverrideSharpness"] = "true",
            ["Sharpness.Sharpness"] = "0.3", ["Menu.Scale"] = "1.5", ["Spoofing.Vulkan"] = "false",
            ["Log.LogToFile"] = "true"
        }, profile.Settings);
    }

    [Fact]
    public void ImportDropsUnknownValuesAndCollidingShortcutsInsteadOfFailing()
    {
        var imported = ProfileIni.ReadProfileFromIni(
            "[Upscalers]\nDx12Upscaler=future\n[Menu]\nShortcutKey=0x23\n[Log]\nLogToFile=true\n", "Imported");

        Assert.Equal(new Dictionary<string, string> { ["Log.LogToFile"] = "true" }, imported.Settings);
    }

    [Fact]
    public void ImportKeepsFrameGenerationTurnedOff()
    {
        var imported = ProfileIni.ReadProfileFromIni("[FrameGen]\nEnabled=false\nFGOutput=auto\n", "Off");

        Assert.Equal("nofg", imported.Settings["FrameGen.FGOutput"]);
        Assert.Contains("Enabled=false", ProfileIni.ApplyProfileToIni("", imported));
    }

    [Fact]
    public void ADroppedCustomKeyShowsAsRevertingToAuto()
    {
        var change = Assert.Single(ProfileIni.CompareIni("[Spoofing]\nDxgi=false\n[Log]\nLogToFile=auto\n", "[Log]\n"));

        Assert.Equal(new IniChange("Spoofing", "Dxgi", "false", "auto"), change);
    }

    [Fact]
    public void ComparesEffectiveValuesOnly()
    {
        var changes = ProfileIni.CompareIni("[Spoofing]\nDxgi=auto\n[Menu]\nShortcutKey=0x2D\n",
                                            "[Spoofing]\nDxgi=false\n[Menu]\nShortcutKey=0x2d\n[Log]\nLogToFile=auto\n");

        var change = Assert.Single(changes);
        Assert.Equal(new IniChange("Spoofing", "Dxgi", "auto", "false"), change);
        Assert.Equal("[Spoofing] Dxgi: auto → false", change.ToString());
    }

    [Fact]
    public void CarriesOverCustomSettingsTheNewPackageStillDefines()
    {
        const string current = "[Spoofing]\nDxgi=false\n[Upscalers]\nDx12Upscaler=auto\n[Removed]\nOldKey=1\n";

        var merged = ProfileIni.CarryOverSettings(PackageIni, current, out var carried);
        var values = ProfileIni.ReadIniValues(merged);

        Assert.Equal(1, carried);
        Assert.Equal("false", values[("Spoofing", "Dxgi")]);
        Assert.False(values.ContainsKey(("Removed", "OldKey")));
        Assert.Contains("; Select Upscaler for Dx12 games", merged);
    }

    [Fact]
    public void DescribesOnlyOverriddenSettings()
    {
        var text = ProfileIni.Describe(TestData.Profile("Keys", ("Menu.ShortcutKey", "0x24"),
                                                        ("Plugins.LoadReshade", "true")));

        Assert.Equal("Open OptiScaler menu: Home\nLoad ReShade: On", text);
        Assert.Equal("Uses OptiScaler's defaults.", ProfileIni.Describe(new RenderProfile()));
    }
}
