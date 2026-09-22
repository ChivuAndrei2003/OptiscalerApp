using System.Text.Json.Nodes;
using OptiscalerApp.Management;
using OptiscalerApp.Models;
using OptiscalerApp.Paths;
using Xunit;

namespace Optiscaler.Tests;

/// <summary>Temporary PE-shaped files test filesystem behavior, never GPU compatibility.</summary>
public sealed class InstallationTests : IDisposable
{
    private readonly HttpClient _client = new();
    private readonly AppPaths _paths;

    private readonly string _root = Path.Combine(OperatingSystem.IsMacOS() ? "/private/tmp" : Path.GetTempPath(),
                                                 "Optiscaler-install-" + Guid.NewGuid().ToString("N"));

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

    private string Game => Path.Combine(_root, "game");
    private string Package => Path.Combine(_root, "package");
    private string Exe => Path.Combine(Game, "game.exe");
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _client.Dispose();
        Directory.Delete(_root, true);
    }

    private GameInstallationService Service()
    {
        return new GameInstallationService(_paths, new PackageDownloadService(_paths, _client));
    }

    private Task<InstallPlan> Preview(GameInstallationService service)
    {
        return service.PreviewInstallation_Async(Exe, Package, "dxgi.dll", null, Ct);
    }

    private async Task<string> LatestOperationDirectory(GameInstallationService service)
    {
        var id = (await service.GetOperationHistory_Async(Ct))[0].Id;

        return Path.Combine(_paths.RootDirectory, "transactions", id.ToString("N"));
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
        await File.WriteAllTextAsync(Path.Combine(await LatestOperationDirectory(service), "original",
                                                  "OptiScaler.ini"), "broken", Ct);
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
        {
            Name = "Sharper", Dx12Upscaler = "xess", Sharpness = 0.6m, EnableLogging = true
        };
        await service.ExecuteInstallationPlan_Async(await service.PreviewProfileApplication_Async(Exe, profile, Ct),
                                                    Ct);
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
                                                     new RenderProfile { Name = "Logging", EnableLogging = true },
                                                     Ct), Ct);
        await File.WriteAllTextAsync(Path.Combine(Game, "dxgi.dll"), "changed later", Ct);
        var result = await service.VerifyInstallation_Async(Game, Ct);
        Assert.False(result.IsVerified);
        Assert.Contains(result.Issues, issue => issue.Contains("dxgi.dll"));
    }

    [Fact]
    public async Task InterruptedWriteAheadJournalCanBeRestoredAfterRestart()
    {
        await File.WriteAllTextAsync(Path.Combine(Game, "OptiScaler.ini"), "original", Ct);
        var service = Service();
        var plan = await Preview(service);
        await service.ExecuteInstallationPlan_Async(plan, Ct);

        // Simulate a process exit after the first destination was replaced, before the second.
        var journalPath = Path.Combine(await LatestOperationDirectory(service), "journal.json");
        var node = JsonNode.Parse(await File.ReadAllTextAsync(journalPath, Ct))!;
        node["state"] = (int)OperationState.Applying;
        await File.WriteAllTextAsync(journalPath, node.ToJsonString(), Ct);
        await File.WriteAllTextAsync(Path.Combine(Game, "OptiScaler.ini"), "original", Ct);
        var restarted = Service();
        Assert.False((await restarted.VerifyInstallation_Async(Game, Ct)).IsVerified);
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                                                                await restarted
                                                                    .ExecuteInstallationPlan_Async(await
                                                                         Preview(restarted),
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
        await service.ExecuteInstallationPlan_Async(await service.PreviewNativeDllSwap_Async(destination, source, Ct),
                                                    Ct);
        Assert.True((await service.VerifyInstallation_Async(Game, Ct)).IsVerified);
        await service.RestoreLatestOperation_Async(Game, Ct);
        Assert.Equal(original, await File.ReadAllBytesAsync(destination, Ct));
    }

    [Fact]
    public async Task InstallsThroughSymlinkedGameFolder()
    {
        // Libraries moved to another drive are often reached through a link; the real folder is what gets tracked.
        var alias = Path.Combine(_root, "game-alias");
        Directory.CreateSymbolicLink(alias, Game);
        var service = Service();
        await service.ExecuteInstallationPlan_Async(
                                                    await service.PreviewInstallation_Async(
                                                     Path.Combine(alias, "game.exe"), Package, "dxgi.dll", null, Ct),
                                                    Ct);
        Assert.True(File.Exists(Path.Combine(Game, "dxgi.dll")));
        Assert.True((await service.VerifyInstallation_Async(alias, Ct)).IsVerified);
        Assert.True((await service.VerifyInstallation_Async(Game, Ct)).IsVerified);
        await service.RestoreLatestOperation_Async(alias, Ct);
        Assert.False(File.Exists(Path.Combine(Game, "dxgi.dll")));
    }

    [Fact]
    public async Task MissingSourceFailsBeforeAnyGameFileChanges()
    {
        await File.WriteAllTextAsync(Path.Combine(Game, "OptiScaler.ini"), "original", Ct);
        var service = Service();
        var plan = await Preview(service);
        File.Delete(Path.Combine(Package, "OptiScaler.dll"));
        await Assert.ThrowsAsync<FileNotFoundException>(() => service.ExecuteInstallationPlan_Async(plan, Ct));
        Assert.Equal("original", await File.ReadAllTextAsync(Path.Combine(Game, "OptiScaler.ini"), Ct));
        Assert.False(File.Exists(Path.Combine(Game, "dxgi.dll")));
        Assert.Empty(await service.GetOperationHistory_Async(Ct));
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
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                                                                    service.ExecuteInstallationPlan_Async(plan,
                                                                     cancelled.Token));
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
        var journalPath = Path.Combine(await LatestOperationDirectory(service), "journal.json");
        var node = JsonNode.Parse(await File.ReadAllTextAsync(journalPath, Ct))!;
        node["files"] = JsonNode.Parse(filesJson);
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