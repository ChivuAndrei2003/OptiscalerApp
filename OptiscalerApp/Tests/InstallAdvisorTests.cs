using OptiscalerApp.Management;
using OptiscalerApp.Models;
using Xunit;

namespace Optiscaler.Tests;

public sealed class InstallAdvisorTests
{
    private static readonly GpuInfo Radeon = new("AMD Radeon RX 7800 XT", GpuVendor.AMD, 0x1002, 0x747E, 16UL << 30);
    private static readonly GpuInfo GeForce = new("NVIDIA GeForce RTX 4070", GpuVendor.Nvidia, 0x10DE, 0x2786, 12UL << 30);
    private static readonly GpuInfo IntelIgpu = new("Intel UHD Graphics 770", GpuVendor.Intel, 0x8086, 0x4680, 128UL << 20);

    private static CompatibilityEntry Entry(string notes = "", bool optiPatcher = false,
                                            CompatibilityStatus status = CompatibilityStatus.Working)
    {
        return CompatibilityListParser.Parse(
                                             "| Game | Compatibility | Upscaler <br>Inputs | OptiPatcher <br>Support | Notes | Images |\n" +
                                             "| --- | --- | --- | --- | --- | --- |\n" +
                                             $"| Test | {(status == CompatibilityStatus.NotWorking ? "❌" : "✅")} | DLSS | {(optiPatcher ? "✨" : "")} | {notes} |  |")
            .Single();
    }

    [Fact]
    public void AmdGetsFakeNvapiOptiPatcherAndNukemWhenTheGameSupportsThem()
    {
        var advice = InstallAdvisor.Recommend(new AdvisorInput
        {
            Gpu = Radeon, Compatibility = Entry(optiPatcher: true), HasUpscalerInputs = true,
            HasDlssFrameGeneration = true, IsLinux = false
        });

        Assert.Equal("dxgi.dll", advice.Proxy);
        Assert.Equal(ComponentAdvice.Install, advice.FakeNvapi);
        Assert.Equal(ComponentAdvice.Install, advice.OptiPatcher);
        Assert.Equal(ComponentAdvice.Install, advice.Nukem);
        Assert.Empty(advice.Warnings);
        Assert.Equal(4, advice.Reasons.Count);
    }

    [Fact]
    public void NvidiaSkipsTheAmdAndIntelHelpers()
    {
        var advice = InstallAdvisor.Recommend(new AdvisorInput
        {
            Gpu = GeForce, Compatibility = Entry(optiPatcher: true), HasUpscalerInputs = true, IsLinux = false
        });

        Assert.Equal(ComponentAdvice.Skip, advice.FakeNvapi);
        Assert.Equal(ComponentAdvice.Skip, advice.OptiPatcher);
        Assert.Equal(ComponentAdvice.Skip, advice.Nukem);
    }

    [Theory]
    [InlineData("Install as `d3d12.dll` to bypass verification checks.", GamePlatform.Steam, false, "d3d12.dll")]
    [InlineData("Install as `winmm.dll` for Xbox version.", GamePlatform.Steam, false, "dxgi.dll")]
    [InlineData("Install as `winmm.dll` for Xbox version.", GamePlatform.Xbox, false, "winmm.dll")]
    [InlineData("For Linux use OptiScaler as `winmm.dll`", GamePlatform.Steam, false, "dxgi.dll")]
    [InlineData("For Linux use OptiScaler as `winmm.dll`", GamePlatform.Steam, true, "winmm.dll")]
    [InlineData("Turn City Glow off. Install OptiScaler as `d3d12.dll` and force DX12.", GamePlatform.Steam, false,
                "d3d12.dll")]
    public void FollowsWikiNotesThatApplyToThisSetup(string notes, GamePlatform platform, bool linux, string proxy)
    {
        var advice = InstallAdvisor.Recommend(new AdvisorInput
        {
            Gpu = Radeon, Compatibility = Entry(notes), Platform = platform, HasUpscalerInputs = true, IsLinux = linux
        });

        Assert.Equal(proxy, advice.Proxy);
    }

    [Fact]
    public void AvoidsAProxyAnotherModAlreadyUses()
    {
        var advice = InstallAdvisor.Recommend(new AdvisorInput
        {
            Gpu = Radeon, HasUpscalerInputs = true, OccupiedProxies = ["dxgi.dll"], IsLinux = false
        });

        Assert.Equal("winmm.dll", advice.Proxy);
        Assert.Contains(advice.Reasons, r => r.Contains("ReShade"));
    }

    [Fact]
    public void WarnsAboutAntiCheatBrokenGamesAndMissingInputs()
    {
        var broken = InstallAdvisor.Recommend(new AdvisorInput
        {
            Compatibility = Entry("EA Anti-cheat blocks unknown .dlls", status: CompatibilityStatus.NotWorking),
            HasAntiCheat = true, IsLinux = false
        });
        var unknown = InstallAdvisor.Recommend(new AdvisorInput { IsLinux = false });

        Assert.Equal(2, broken.Warnings.Count);
        Assert.Contains(broken.Warnings, w => w.Contains("EA Anti-cheat blocks"));
        Assert.Equal(ComponentAdvice.Unchanged, unknown.FakeNvapi);
        Assert.Contains(unknown.Warnings, w => w.Contains("No DLSS, FSR or XeSS"));
    }

    [Fact]
    public void PicksTheDiscreteGpu()
    {
        Assert.Equal(Radeon, InstallAdvisor.PickPrimaryGpu([IntelIgpu, Radeon]));
        Assert.Null(InstallAdvisor.PickPrimaryGpu([]));

        // Linux reports no VRAM, so the vendor decides.
        Assert.Equal(GpuVendor.Nvidia, InstallAdvisor.PickPrimaryGpu([
            IntelIgpu with { DedicatedVram = null }, GeForce with { DedicatedVram = null }
        ])!.Vendor);
    }
}
