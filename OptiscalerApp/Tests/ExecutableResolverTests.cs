using OptiscalerApp.Management;
using Xunit;

namespace Optiscaler.Tests;

public sealed class ExecutableResolverTests : TempFolderTest
{
    private string Exe(params string[] parts)
    {
        var path = Path.Combine([Root, ..parts]);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        TestData.WritePe(path, false);

        return path;
    }

    [Fact]
    public void PrefersTheUnrealShippingBinaryOverLaunchersAndEngineTools()
    {
        Exe("Expedition33.exe");
        Exe("Engine", "Binaries", "Win64", "CrashReportClient.exe");
        Exe("Engine", "Binaries", "Win64", "UnrealEditor.exe");
        Exe("_CommonRedist", "vcredist", "VC_redist.x64.exe");
        var shipping = Exe("Sandfall", "Binaries", "Win64", "SandFall-Win64-Shipping.exe");

        var candidates = ExecutableResolver.FindCandidates(Root, "Clair Obscur: Expedition 33", Ct);

        Assert.Equal(shipping, candidates[0].Path);
        Assert.Contains("Unreal Engine shipping binary", candidates[0].Reasons);
        Assert.DoesNotContain(candidates, c => c.Path.Contains("Engine" + Path.DirectorySeparatorChar));
        Assert.DoesNotContain(candidates, c => c.Path.Contains("redist", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PrefersTheExecutableNextToUpscalerLibraries()
    {
        Exe("bin", "tools", "Settings.exe");
        var game = Exe("bin", "x64", "Cyberpunk2077.exe");
        TestData.WritePe(Path.Combine(Root, "bin", "x64", "nvngx_dlss.dll"), true);
        Exe("REDprelauncher.exe");

        Assert.Equal(game, ExecutableResolver.FindBest(Root, "Cyberpunk 2077", Ct)!.Path);
    }

    [Fact]
    public void Skips32BitAndNonPeFiles()
    {
        var legacy = Path.Combine(Root, "legacy.exe");
        TestData.WritePe(legacy, false);
        var bytes = File.ReadAllBytes(legacy);
        BitConverter.GetBytes((ushort)0x014c).CopyTo(bytes, 132); // IMAGE_FILE_MACHINE_I386
        File.WriteAllBytes(legacy, bytes);
        File.WriteAllText(Path.Combine(Root, "readme.exe"), "not a program");

        Assert.Empty(ExecutableResolver.FindCandidates(Root, "legacy", Ct));
    }

    [Fact]
    public void MissingFolderHasNoCandidates()
    {
        Assert.Null(ExecutableResolver.FindBest(Path.Combine(Root, "missing"), "Game", Ct));
    }
}
