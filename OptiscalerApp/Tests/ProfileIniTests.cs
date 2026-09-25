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
    public void WritesTheNewSettingsIntoTheirDocumentedSections()
    {
        var ini = ProfileIni.ApplyProfileToIni(PackageIni, new RenderProfile
        {
            Name = "Handheld",
            SpoofDxgi = false,
            OverlayKey = 0x24,
            FrameGenKey = -1,
            FrameGenInput = "upscaler",
            FrameGenOutput = "fsrfg",
            DisableOverlays = false,
            LoadReshade = true,
            FramerateLimit = 58.5m,
            VulkanUpscaler = "xess"
        });
        var values = ProfileIni.ReadIniValues(ini);

        Assert.Equal("false", values[("Spoofing", "Dxgi")]);
        Assert.Equal("0x24", values[("Menu", "ShortcutKey")]);
        Assert.Equal("-1", values[("Menu", "FGShortcutKey")]);
        Assert.Equal("true", values[("FrameGen", "Enabled")]);
        Assert.Equal("fsrfg", values[("FrameGen", "FGOutput")]);

        // A key with the same name in another section is left alone.
        Assert.Equal("auto", values[("DLSS", "Enabled")]);
        Assert.Equal("false", values[("Hotfix", "DisableOverlays")]);
        Assert.Equal("true", values[("Plugins", "LoadReshade")]);
        Assert.Equal("58.5", values[("Framerate", "FramerateLimit")]);
        Assert.Equal("xess", values[("Upscalers", "VulkanUpscaler")]);
        Assert.Contains("; Select Upscaler for Dx12 games", ini);
    }

    [Theory]
    [InlineData(0x23, null, "different keys")] // End is already the default FG key.
    [InlineData(0x21, null, "Page Up")]
    [InlineData(null, 0x22, "Page Up")]
    [InlineData(0x1FF, null, "virtual-key")]
    public void RejectsShortcutsThatCollide(int? overlay, int? frameGen, string message)
    {
        var error = Assert.Throws<InvalidDataException>(() => ProfileIni.ValidateProfile(new RenderProfile
        {
            OverlayKey = overlay, FrameGenKey = frameGen
        }));

        Assert.Contains(message, error.Message);
    }

    [Fact]
    public void DisabledShortcutsNeverCollide()
    {
        ProfileIni.ValidateProfile(new RenderProfile { OverlayKey = -1, FrameGenKey = -1 });
    }

    [Fact]
    public void ImportedProfileRoundTripsThroughTheIni()
    {
        var profile = new RenderProfile
        {
            Name = "Original",
            Dx12Upscaler = "xess",
            SpoofDxgi = true,
            OverlayKey = 0x70,
            FrameGenOutput = "xefg",
            DisableOverlays = true,
            LoadSpecialK = true,
            Sharpness = 0.3m,
            EnableLogging = true
        };

        var imported = ProfileIni.ReadProfileFromIni(ProfileIni.ApplyProfileToIni(PackageIni, profile), "Imported");

        Assert.Equal(profile with { Id = imported.Id, Name = "Imported", Description = imported.Description },
                     imported);
    }

    [Fact]
    public void ImportIgnoresValuesTheProfileCannotRepresent()
    {
        var imported = ProfileIni.ReadProfileFromIni("[Upscalers]\nDx12Upscaler=future\n[Menu]\nShortcutKey=0x21\n",
                                                     "Imported");

        Assert.Equal("auto", imported.Dx12Upscaler);
        Assert.Null(imported.OverlayKey);
    }

    [Fact]
    public void ImportKeepsFrameGenerationTurnedOff()
    {
        var imported = ProfileIni.ReadProfileFromIni("[FrameGen]\nEnabled=false\nFGOutput=auto\n", "Off");

        Assert.Equal("nofg", imported.FrameGenOutput);
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
        var text = ProfileIni.Describe(new RenderProfile { OverlayKey = 0x24, LoadReshade = true });

        Assert.Contains("Overlay key: Home", text);
        Assert.Contains("Loads ReShade", text);
        Assert.DoesNotContain("Frame generation", text);
    }
}
