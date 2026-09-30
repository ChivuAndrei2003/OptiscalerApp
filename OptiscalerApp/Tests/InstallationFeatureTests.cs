using OptiscalerApp.Management;
using OptiscalerApp.Models;
using OptiscalerApp.Paths;
using Xunit;

namespace Optiscaler.Tests;

/// <summary>Settings carry-over, INI previews, plugin loading and library health for managed installs.</summary>
public sealed class InstallationFeatureTests : IDisposable
{
    private readonly HttpClient _client = new(StubHttpHandler.Offline());
    private readonly AppPaths _paths;

    private readonly string _root = TestData.TempRoot("Optiscaler-features-");

    public InstallationFeatureTests()
    {
        _paths = new AppPaths(Path.Combine(_root, "data"));
        Directory.CreateDirectory(Game);
        Directory.CreateDirectory(Package);
        InstallationTests.WritePe(Exe, false);
        InstallationTests.WritePe(Path.Combine(Package, "OptiScaler.dll"), true);
        File.WriteAllText(Path.Combine(Package, "OptiScaler.ini"),
                          "[Spoofing]\nDxgi=auto\n[Menu]\nShortcutKey=auto\n[Plugins]\nLoadAsiPlugins=auto\n");
    }

    private string Game => Path.Combine(_root, "game");
    private string Package => Path.Combine(_root, "package");
    private string Exe => Path.Combine(Game, "game.exe");
    private string GameIni => Path.Combine(Game, "OptiScaler.ini");
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

    [Fact]
    public async Task UpdateKeepsCustomizedSettingsAndThePreviewListsWhatChanges()
    {
        var service = Service();
        await service.ExecuteInstallationPlanAsync(
                                                    await service.PreviewInstallationAsync(Exe, Package, "dxgi.dll",
                                                        null, Ct), Ct);

        // The user tunes the installed file, then updates OptiScaler.
        await File.WriteAllTextAsync(GameIni, "[Spoofing]\nDxgi=false\n[Menu]\nShortcutKey=0x24\n", Ct);
        var kept = await service.PreviewInstallationAsync(Exe, Package, "dxgi.dll",
                                                           TestData.Profile("Keys", ("Menu.ShortcutKey", "0x70")), Ct,
                                                           true);
        var reset = await service.PreviewInstallationAsync(Exe, Package, "dxgi.dll", null, Ct);

        Assert.Contains("kept 2 current settings", kept.Description);
        var values = ProfileIni.ReadIniValues(kept.Files.Single(f => f.RelativePath == "OptiScaler.ini").GeneratedText!);
        Assert.Equal("false", values[("Spoofing", "Dxgi")]);

        // The profile is the most explicit choice, so it wins over the kept value.
        Assert.Equal("0x70", values[("Menu", "ShortcutKey")]);
        Assert.Contains(kept.IniChanges, c => c is { Key: "ShortcutKey", Before: "0x24", After: "0x70" });

        // Without carry-over the preview warns that the custom values would be reset.
        Assert.Contains(reset.IniChanges, c => c is { Key: "Dxgi", Before: "false", After: "auto" });
        Assert.Null(reset.Files.Single(f => f.RelativePath == "OptiScaler.ini").GeneratedText);
    }

    [Fact]
    public async Task OptiPatcherTurnsOnAsiPluginLoading()
    {
        var patcher = Path.Combine(_root, "OptiPatcher.asi");
        InstallationTests.WritePe(patcher, true);
        var service = Service();

        var plan = await service.PreviewPackageInstallationAsync(Exe, Package, "dxgi.dll", null,
                                                                  [
                                                                      new ComponentInstallSelection(
                                                                       DownloadComponent.OptiPatcher,
                                                                       LocalPath: patcher)
                                                                  ], cancellationToken: Ct);
        await service.ExecuteInstallationPlanAsync(plan, Ct);

        Assert.Contains("LoadAsiPlugins=true", await File.ReadAllTextAsync(GameIni, Ct));
        Assert.Contains(plan.IniChanges, c => c.Key == "LoadAsiPlugins");
        Assert.True(File.Exists(Path.Combine(Game, "plugins", "OptiPatcher.asi")));
        Assert.True((await service.VerifyInstallationAsync(Game, Ct)).IsVerified);

        // Updating while keeping the existing plugin must not switch it off again.
        var update = await service.PreviewPackageInstallationAsync(Exe, Package, "dxgi.dll", null,
                                                                    [
                                                                        new ComponentInstallSelection(
                                                                         DownloadComponent.OptiPatcher,
                                                                         KeepExisting: true)
                                                                    ], cancellationToken: Ct);
        Assert.Contains("LoadAsiPlugins=true",
                        update.Files.Single(f => f.RelativePath == "OptiScaler.ini").GeneratedText);
    }

    [Fact]
    public async Task ManagedTargetsReportVersionAndFilesChangedByAGameUpdate()
    {
        var service = Service();
        Assert.Empty(await service.GetManagedTargetsAsync(Ct));

        var plan = await service.PreviewInstallationAsync(Exe, Package, "dxgi.dll", null, Ct);
        await service.ExecuteInstallationPlanAsync(plan with { Version = "v0.9.4" }, Ct);
        var target = Assert.Single(await service.GetManagedTargetsAsync(Ct));
        Assert.Equal(ManagedHealth.Healthy, target.Health);
        Assert.Equal("v0.9.4", target.Version);
        Assert.True(PathUtil.AreSame(PathUtil.Normalize(Game), target.TargetDirectory));

        // A later profile change has no version of its own; the installed version is still reported.
        await service.ExecuteInstallationPlanAsync(
                                                    await service.PreviewProfileApplicationAsync(Exe,
                                                        TestData.Profile("Spoof off", ("Spoofing.Dxgi", "false")),
                                                        Ct), Ct);
        Assert.Equal("v0.9.4", Assert.Single(await service.GetManagedTargetsAsync(Ct)).Version);

        // Verifying game files in a launcher replaces or deletes what OptiScaler added.
        File.Delete(Path.Combine(Game, "dxgi.dll"));
        Assert.Equal(ManagedHealth.FilesChanged, Assert.Single(await service.GetManagedTargetsAsync(Ct)).Health);

        await service.RestoreLatestOperationAsync(Game, Ct);
        Assert.Equal(ManagedHealth.FilesChanged, Assert.Single(await service.GetManagedTargetsAsync(Ct)).Health);
    }
}
