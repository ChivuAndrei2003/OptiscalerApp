using OptiscalerApp.Models;
using OptiscalerApp.Management;
using OptiscalerApp.Paths;
using Xunit;

namespace Optiscaler.Tests;

/// <summary>Temporary PE-shaped files test filesystem behavior, never GPU compatibility.</summary>
public sealed class InstallationTests : IDisposable
{
    private readonly string _root = Path.Combine(OperatingSystem.IsMacOS() ? "/private/tmp" : Path.GetTempPath(),
                                                 "Optiscaler-install-" + Guid.NewGuid().ToString("N"));

    private readonly AppPaths _paths;
    private string Game => Path.Combine(_root, "game");
    private string Package => Path.Combine(_root, "package");
    private string Exe => Path.Combine(Game, "game.exe");
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    public InstallationTests()
    {
        _paths = new AppPaths(Path.Combine(_root, "data"));
        Directory.CreateDirectory(Game);
        Directory.CreateDirectory(Package);
        WritePe(Exe, false);
        WritePe(Path.Combine(Package, "OptiScaler.dll"), true);
        File.WriteAllText(Path.Combine(Package, "OptiScaler.ini"),
                          "; original\n[Upscalers]\nDx12Upscaler=auto\n[Other]\nKeep=42\n");
    }

    public void Dispose()
    {
        Directory.Delete(_root, true);
    }

    private GameInstallationService Service()
    {
        return new GameInstallationService(_paths);
    }

    private Task<InstallPlan> Preview(GameInstallationService service)
    {
        return service.PreviewInstallation_Async(Exe, Package, "dxgi.dll", null, Ct);
    }

    [Fact]
    public async Task InstallsVerifiesAndRestoresOriginalBytesAcrossRestart()
    {
        var original = new byte[] { 7, 8, 9 };
        await File.WriteAllBytesAsync(Path.Combine(Game, "dxgi.dll"), original, Ct);
        Directory.CreateDirectory(Path.Combine(Package, "assets"));
        await File.WriteAllTextAsync(Path.Combine(Package, "assets", "settings.json"), "{}", Ct);
        var service = Service();
        var plan = await Preview(service);
        Assert.False(File.Exists(Path.Combine(Game, "OptiScaler.ini")));
        await service.ExecuteInstallationPlan_Async(plan, Ct);
        Assert.True((await Service().VerifyInstallation_Async(Game, Ct)).IsVerified);
        Assert.True(File.Exists(Path.Combine(Game, "assets", "settings.json")));
        await Service().RestoreLatestOperation_Async(Game, Ct);
        Assert.Equal(original, await File.ReadAllBytesAsync(Path.Combine(Game, "dxgi.dll"), Ct));
        Assert.False(File.Exists(Path.Combine(Game, "OptiScaler.ini")));
        Assert.Null((await Service().VerifyInstallation_Async(Game, Ct)).Journal);
        Assert.Equal(OperationState.Restored, Assert.Single(await Service().GetOperationHistory_Async(Ct)).State);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RejectsChangedSourceOrDestinationBeforeWriting(bool source)
    {
        var service = Service();
        var plan = await Preview(service);
        await File.WriteAllTextAsync(Path.Combine(source ? Package : Game, source ? "OptiScaler.dll" : "dxgi.dll"),
                                     "changed", Ct);
        await Assert.ThrowsAsync<IOException>(() => service.ExecuteInstallationPlan_Async(plan, Ct));
        Assert.False(File.Exists(Path.Combine(Game, "OptiScaler.ini")));
        Assert.Empty(await service.GetOperationHistory_Async(Ct));
    }

    [Fact]
    public async Task RestorePreservesLaterUserEdits()
    {
        var service = Service();
        await service.ExecuteInstallationPlan_Async(await Preview(service), Ct);
        await File.WriteAllTextAsync(Path.Combine(Game, "OptiScaler.ini"), "user edit", Ct);
        Assert.False((await service.VerifyInstallation_Async(Game, Ct)).IsVerified);
        await Assert.ThrowsAsync<IOException>(() => service.RestoreLatestOperation_Async(Game, Ct));
        Assert.Equal("user edit", await File.ReadAllTextAsync(Path.Combine(Game, "OptiScaler.ini"), Ct));
        Assert.True(File.Exists(Path.Combine(Game, "dxgi.dll")));
    }

    [Fact]
    public async Task CorruptBackupBlocksEntireRestore()
    {
        await File.WriteAllTextAsync(Path.Combine(Game, "OptiScaler.ini"), "original", Ct);
        var service = Service();
        var plan = await Preview(service);
        await service.ExecuteInstallationPlan_Async(plan, Ct);
        await File.WriteAllTextAsync(Path.Combine(_paths.RootDirectory, "transactions", plan.Id.ToString("N"),
                                                  "original", "OptiScaler.ini"), "broken", Ct);
        await Assert.ThrowsAsync<IOException>(() => service.RestoreLatestOperation_Async(Game, Ct));
        Assert.True(File.Exists(Path.Combine(Game, "dxgi.dll")));
    }

    [Fact]
    public async Task ProfileOverlayRestoresInReverseOrder()
    {
        var service = Service();
        await service.ExecuteInstallationPlan_Async(await Preview(service), Ct);
        var installedIni = await File.ReadAllTextAsync(Path.Combine(Game, "OptiScaler.ini"), Ct);
        var profile = new RenderProfile
            { Name = "Sharper", Dx12Upscaler = "xess", Sharpness = 0.6m, EnableLogging = true };
        await service.ExecuteInstallationPlan_Async(await service.PreviewProfileApplication_Async(Exe, profile, Ct), Ct);
        var modified = await File.ReadAllTextAsync(Path.Combine(Game, "OptiScaler.ini"), Ct);
        Assert.Contains("Keep=42", modified);
        Assert.Contains("Dx12Upscaler=xess", modified);
        Assert.Contains("OverrideSharpness=true", modified);
        await service.RestoreLatestOperation_Async(Game, Ct);
        Assert.Equal(installedIni, await File.ReadAllTextAsync(Path.Combine(Game, "OptiScaler.ini"), Ct));
        Assert.True((await service.VerifyInstallation_Async(Game, Ct)).IsVerified);
        await service.RestoreLatestOperation_Async(Game, Ct);
        Assert.False(File.Exists(Path.Combine(Game, "dxgi.dll")));
    }

    [Fact]
    public async Task ProfileVerificationStillDetectsChangedUnderlyingDll()
    {
        var service = Service();
        await service.ExecuteInstallationPlan_Async(await Preview(service), Ct);
        await service.ExecuteInstallationPlan_Async(await service.PreviewProfileApplication_Async(Exe,
                                                                     new RenderProfile
                                                                         { Name = "Logging", EnableLogging = true },
                                                                     Ct), Ct);
        await File.WriteAllTextAsync(Path.Combine(Game, "dxgi.dll"), "changed later", Ct);
        var result = await service.VerifyInstallation_Async(Game, Ct);
        Assert.False(result.IsVerified);
        Assert.Contains(result.Issues, issue => issue.Contains("dxgi.dll"));
    }

    [Fact]
    public async Task RejectsReplayingTheSamePreview()
    {
        var service = Service();
        var plan = await Preview(service);
        await service.ExecuteInstallationPlan_Async(plan, Ct);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ExecuteInstallationPlan_Async(plan, Ct));
        Assert.Single(await service.GetOperationHistory_Async(Ct));
    }

    [Fact]
    public async Task RejectsAnOverlappingOperationFromAnotherTargetFolder()
    {
        Directory.CreateDirectory(Path.Combine(Package, "bin"));
        WritePe(Path.Combine(Package, "bin", "nvngx_dlss.dll"), true);
        var service = Service();
        await service.ExecuteInstallationPlan_Async(await Preview(service), Ct);
        var replacement = Path.Combine(_root, "nvngx_dlss.dll");
        WritePe(replacement, true);
        var plan = await service.PreviewNativeDllSwap_Async(Path.Combine(Game, "bin", "nvngx_dlss.dll"), replacement, Ct);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ExecuteInstallationPlan_Async(plan, Ct));
    }

    [Fact]
    public async Task InterruptedWriteAheadJournalCanBeRestoredAfterRestart()
    {
        await File.WriteAllTextAsync(Path.Combine(Game, "OptiScaler.ini"), "original", Ct);
        var service = Service();
        var plan = await Preview(service);
        await service.ExecuteInstallationPlan_Async(plan, Ct);
        // Simulate a process exit after the first destination was replaced, before the second.
        var journalPath = Path.Combine(_paths.RootDirectory, "transactions", plan.Id.ToString("N"), "journal.json");
        var node = System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(journalPath, Ct))!;
        node["state"] = (int)OperationState.Applying;
        await File.WriteAllTextAsync(journalPath, node.ToJsonString(), Ct);
        await File.WriteAllTextAsync(Path.Combine(Game, "OptiScaler.ini"), "original", Ct);
        var restarted = Service();
        Assert.False((await restarted.VerifyInstallation_Async(Game, Ct)).IsVerified);
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                                                                await restarted.ExecuteInstallationPlan_Async(await Preview(restarted),
                                                                 Ct));
        await restarted.RestoreLatestOperation_Async(Game, Ct);
        Assert.False(File.Exists(Path.Combine(Game, "dxgi.dll")));
        Assert.Equal("original", await File.ReadAllTextAsync(Path.Combine(Game, "OptiScaler.ini"), Ct));
    }

    [Fact]
    public async Task SupportsNativeSwapAndRestore()
    {
        var destination = Path.Combine(Game, "nvngx_dlss.dll");
        var source = Path.Combine(Package, "nvngx_dlss.dll");
        WritePe(destination, true);
        WritePe(source, true);
        await File.AppendAllTextAsync(source, "new-version", Ct);
        var original = await File.ReadAllBytesAsync(destination, Ct);
        var service = Service();
        await service.ExecuteInstallationPlan_Async(await service.PreviewNativeDllSwap_Async(destination, source, Ct), Ct);
        Assert.True((await service.VerifyInstallation_Async(Game, Ct)).IsVerified);
        await service.RestoreLatestOperation_Async(Game, Ct);
        Assert.Equal(original, await File.ReadAllBytesAsync(destination, Ct));
    }

    [Fact]
    public async Task RejectsTraversalInForgedPlan()
    {
        var service = Service();
        var plan = await Preview(service);
        var bad = plan with { Files = [plan.Files[0] with { RelativePath = "../escape.dll" }] };
        await Assert.ThrowsAsync<InvalidDataException>(() => service.ExecuteInstallationPlan_Async(bad, Ct));
        Assert.False(File.Exists(Path.Combine(_root, "escape.dll")));
    }

    [Fact]
    public async Task RejectsDestinationSymlinkIntroducedAfterPreview()
    {
        var service = Service();
        var plan = await Preview(service);
        var outside = Path.Combine(_root, "outside.txt");
        await File.WriteAllTextAsync(outside, "preserve", Ct);
        File.CreateSymbolicLink(Path.Combine(Game, "dxgi.dll"), outside);
        await Assert.ThrowsAsync<InvalidDataException>(() => service.ExecuteInstallationPlan_Async(plan, Ct));
        Assert.Equal("preserve", await File.ReadAllTextAsync(outside, Ct));
    }

    [Fact]
    public async Task RejectsNonPeAndWrongArchitecture()
    {
        await File.WriteAllTextAsync(Path.Combine(Package, "OptiScaler.dll"), "not a DLL", Ct);
        await Assert.ThrowsAsync<InvalidDataException>(() => Preview(Service()));
        WritePe(Path.Combine(Package, "OptiScaler.dll"), true);

        using (var stream = new FileStream(Exe, FileMode.Open, FileAccess.Write))
        {
            stream.Position = 132;
            stream.Write([0x4c, 0x01]);
        }

        await Assert.ThrowsAsync<InvalidDataException>(() => Preview(Service()));
    }

    [Fact]
    public async Task CancellationBeforeExecutionDoesNotChangeGameFiles()
    {
        var service = Service();
        var plan = await Preview(service);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ExecuteInstallationPlan_Async(plan, cancelled.Token));
        Assert.False(File.Exists(Path.Combine(Game, "dxgi.dll")));
    }

    [Fact]
    public async Task RejectsChangedPackageIniEvenWhenProfileGeneratedThePreview()
    {
        var service = Service();
        var plan = await service.PreviewInstallation_Async(Exe, Package, "dxgi.dll", new RenderProfile { Name = "Profile" },
                                                     Ct);
        await File.AppendAllTextAsync(Path.Combine(Package, "OptiScaler.ini"), "[Later]\nNew=value", Ct);
        await Assert.ThrowsAsync<IOException>(() => service.ExecuteInstallationPlan_Async(plan, Ct));
        Assert.False(File.Exists(Path.Combine(Game, "dxgi.dll")));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[null]")]
    public async Task RejectsNullJournalFilesBeforeRestore(string filesJson)
    {
        var service = Service();
        var plan = await Preview(service);
        await service.ExecuteInstallationPlan_Async(plan, Ct);
        var journalPath = Path.Combine(_paths.RootDirectory, "transactions", plan.Id.ToString("N"), "journal.json");
        var node = System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(journalPath, Ct))!;
        node["files"] = System.Text.Json.Nodes.JsonNode.Parse(filesJson);
        await File.WriteAllTextAsync(journalPath, node.ToJsonString(), Ct);
        await Assert.ThrowsAsync<InvalidDataException>(() => Service().RestoreLatestOperation_Async(Game, Ct));
        Assert.True(File.Exists(Path.Combine(Game, "dxgi.dll")));
    }

    internal static void WritePe(string path, bool dll)
    {
        var bytes = new byte[256];
        bytes[0] = 0x4d;
        bytes[1] = 0x5a;
        BitConverter.GetBytes(128).CopyTo(bytes, 0x3c);
        bytes[128] = 0x50;
        bytes[129] = 0x45;
        bytes[132] = 0x64;
        bytes[133] = 0x86;
        BitConverter.GetBytes((ushort)(dll ? 0x2002 : 0x0002)).CopyTo(bytes, 150);
        File.WriteAllBytes(path, bytes);
    }
}
