using System.Text.Json.Nodes;
using OptiscalerApp.Development;
using OptiscalerApp.Management;
using OptiscalerApp.Models;
using OptiscalerApp.Paths;
using OptiscalerApp.Persistence;
using Xunit;

namespace Optiscaler.Tests;

/// <summary>Installs a local package into a game folder made of PE-shaped files.</summary>
public sealed class InstallationTests : TempFolderTest
{
    private static readonly string BundledNvapi = Path.Combine("OptiScaler", "fakenvapi.dll");

    private readonly AppPaths _paths;

    public InstallationTests()
    {
        _paths = new AppPaths(Path.Combine(Root, "data"));
        Directory.CreateDirectory(Game);
        TestData.WritePe(Exe, false);
        TestData.WritePackage(Package, "; original\n[Upscalers]\nDx12Upscaler=auto\n[Spoofing]\nDxgi=auto\n" +
                                       "[Menu]\nShortcutKey=auto\n[Plugins]\nLoadAsiPlugins=auto\n[Other]\nKeep=42\n");
    }

    private string Game => Path.Combine(Root, "game");
    private string Package => Path.Combine(Root, "package");
    private string Exe => Path.Combine(Game, "game.exe");
    private string GameIni => Path.Combine(Game, "OptiScaler.ini");
    private string Proxy => Path.Combine(Game, "dxgi.dll");

    /// <summary>A new service each time, as after restarting the app.</summary>
    private GameInstallationService Service() { return TestData.Installer(_paths.RootDirectory); }

    private Task<InstallPlan> Preview(GameInstallationService service, RenderProfile? profile = null)
    {
        return service.PreviewInstallationAsync(Exe, Package, "dxgi.dll", profile, Ct);
    }

    private async Task<GameInstallationService> Install()
    {
        var service = Service();
        await service.ExecuteInstallationPlanAsync(await Preview(service), Ct);

        return service;
    }

    private async Task ApplyProfile(GameInstallationService service, RenderProfile profile)
    {
        await service.ExecuteInstallationPlanAsync(await service.PreviewProfileApplicationAsync(Exe, profile, Ct), Ct);
    }

    private async Task<string> LatestOperationDirectory(GameInstallationService service)
    {
        var id = (await service.GetOperationHistoryAsync(Ct))[0].Id;

        return Path.Combine(_paths.TransactionsDirectory, id.ToString("N"));
    }

    [Fact]
    public async Task InstallsVerifiesAndRestoresOriginalBytesAcrossRestart()
    {
        var original = new byte[] { 7, 8, 9 };
        await File.WriteAllBytesAsync(Proxy, original, Ct);
        Directory.CreateDirectory(Path.Combine(Package, "assets"));
        await File.WriteAllTextAsync(Path.Combine(Package, "assets", "settings.json"), "{}", Ct);
        var service = Service();
        var plan = await Preview(service);
        Assert.False(File.Exists(GameIni));

        await service.ExecuteInstallationPlanAsync(plan, Ct);
        Assert.True((await Service().VerifyInstallationAsync(Game, Ct)).IsVerified);
        Assert.True(File.Exists(Path.Combine(Game, "assets", "settings.json")));

        await Service().RestoreLatestOperationAsync(Game, Ct);
        Assert.Equal(original, await File.ReadAllBytesAsync(Proxy, Ct));
        Assert.False(File.Exists(GameIni));
        Assert.Equal(OperationState.Restored, Assert.Single(await Service().GetOperationHistoryAsync(Ct)).State);
    }

    [Fact]
    public async Task UninstallLeavesNoPackageFoldersLogsOrSetupScriptsBehind()
    {
        Directory.CreateDirectory(Path.Combine(Package, "Licenses"));
        await File.WriteAllTextAsync(Path.Combine(Package, "Licenses", "LICENSE.txt"), "license", Ct);
        await File.WriteAllTextAsync(Path.Combine(Package, "setup_windows.bat"), "rem", Ct);
        await File.WriteAllTextAsync(Path.Combine(Package, "!! README_EXTRACT ALL FILES TO GAME FOLDER !!.txt"),
                                     "readme", Ct);
        var service = await Install();

        // The package's scripts are for installing by hand; a game needs neither them nor the reminder.
        Assert.Equal(["dxgi.dll", "game.exe", "Licenses", "OptiScaler.ini"],
                     Directory.EnumerateFileSystemEntries(Game).Select(Path.GetFileName).Order());

        // OptiScaler and FakeNvapi write these while the game runs.
        await File.WriteAllTextAsync(Path.Combine(Game, "OptiScaler.log"), "log", Ct);
        await File.WriteAllTextAsync(Path.Combine(Game, "fakenvapi.log"), "log", Ct);
        await service.RestoreAllOperationsAsync(Game, Ct);

        Assert.Equal(["game.exe"], Directory.EnumerateFileSystemEntries(Game).Select(Path.GetFileName));
    }

    [Fact]
    public async Task RestoringAProfileKeepsTheLogsOfTheInstallationUnderIt()
    {
        var service = await Install();
        await ApplyProfile(service, TestData.Profile("Logging", ("Log.LogToFile", "true")));
        await File.WriteAllTextAsync(Path.Combine(Game, "OptiScaler.log"), "log", Ct);

        await service.RestoreLatestOperationAsync(Game, Ct);

        Assert.True(File.Exists(Path.Combine(Game, "OptiScaler.log")));
    }

    [Fact]
    public async Task RestoreKeepsAnEditedIniAndIsNotBlockedByIt()
    {
        // OptiScaler rewrites its INI when settings are saved from the in-game menu.
        var service = await Install();
        await File.WriteAllTextAsync(GameIni, "user edit", Ct);
        Assert.True((await service.VerifyInstallationAsync(Game, Ct)).IsVerified);
        Assert.Equal(ManagedHealth.Healthy, Assert.Single(await service.GetManagedTargetsAsync(Ct)).Health);

        await service.RestoreLatestOperationAsync(Game, Ct);

        Assert.Equal("user edit", await File.ReadAllTextAsync(GameIni, Ct));
        Assert.False(File.Exists(Proxy));
    }

    [Fact]
    public async Task RestoreIsBlockedByOtherFilesThatChanged()
    {
        var service = await Install();
        await File.WriteAllTextAsync(Proxy, "another mod", Ct);

        Assert.False((await service.VerifyInstallationAsync(Game, Ct)).IsVerified);
        await Assert.ThrowsAsync<IOException>(() => service.RestoreLatestOperationAsync(Game, Ct));
        Assert.Equal("another mod", await File.ReadAllTextAsync(Proxy, Ct));
        Assert.True(File.Exists(GameIni));
    }

    [Fact]
    public async Task CorruptBackupBlocksEntireRestore()
    {
        await File.WriteAllTextAsync(GameIni, "original", Ct);
        var service = await Install();
        await File.WriteAllTextAsync(Path.Combine(await LatestOperationDirectory(service), "original",
                                                  "OptiScaler.ini"), "broken", Ct);

        await Assert.ThrowsAsync<IOException>(() => service.RestoreLatestOperationAsync(Game, Ct));
        Assert.True(File.Exists(Proxy));
    }

    [Fact]
    public async Task ProfileOverlayRestoresInReverseOrder()
    {
        var service = await Install();
        var installedIni = await File.ReadAllTextAsync(GameIni, Ct);
        await ApplyProfile(service, TestData.Profile("Sharper", ("Upscalers.Dx12Upscaler", "xess"),
                                                     ("Sharpness.OverrideSharpness", "true")));
        var modified = await File.ReadAllTextAsync(GameIni, Ct);
        Assert.Contains("Keep=42", modified);
        Assert.Contains("Dx12Upscaler=xess", modified);
        Assert.Contains("OverrideSharpness=true", modified);

        await service.RestoreLatestOperationAsync(Game, Ct);
        Assert.Equal(installedIni, await File.ReadAllTextAsync(GameIni, Ct));
        Assert.True((await service.VerifyInstallationAsync(Game, Ct)).IsVerified);

        await service.RestoreLatestOperationAsync(Game, Ct);
        Assert.False(File.Exists(Proxy));
    }

    [Fact]
    public async Task ProfileVerificationStillDetectsChangedUnderlyingDll()
    {
        var service = await Install();
        await ApplyProfile(service, TestData.Profile("Logging", ("Log.LogToFile", "true")));
        await File.WriteAllTextAsync(Proxy, "changed later", Ct);

        var result = await service.VerifyInstallationAsync(Game, Ct);

        Assert.False(result.IsVerified);
        Assert.Contains(result.Issues, issue => issue.Contains("dxgi.dll"));
    }

    [Fact]
    public async Task InterruptedWriteAheadJournalCanBeRestoredAfterRestart()
    {
        await File.WriteAllTextAsync(GameIni, "original", Ct);
        var service = await Install();

        // Simulate a process exit after the first destination was replaced, before the second.
        var journalPath = Path.Combine(await LatestOperationDirectory(service), "journal.json");
        var node = JsonNode.Parse(await File.ReadAllTextAsync(journalPath, Ct))!;
        node["state"] = (int)OperationState.Applying;
        await File.WriteAllTextAsync(journalPath, node.ToJsonString(), Ct);
        await File.WriteAllTextAsync(GameIni, "original", Ct);
        var restarted = Service();

        Assert.False((await restarted.VerifyInstallationAsync(Game, Ct)).IsVerified);
        var plan = await Preview(restarted);
        await Assert.ThrowsAsync<InvalidOperationException>(() => restarted.ExecuteInstallationPlanAsync(plan, Ct));

        await restarted.RestoreLatestOperationAsync(Game, Ct);
        Assert.False(File.Exists(Proxy));
        Assert.Equal("original", await File.ReadAllTextAsync(GameIni, Ct));
    }

    [Fact]
    public async Task NativeDllSwapInASubfolderIsVerifiedAndRestored()
    {
        var destination = Path.Combine(Folder("game", "plugins"), "nvngx_dlss.dll");
        var source = Path.Combine(Package, "nvngx_dlss.dll");
        TestData.WritePe(destination, true);
        TestData.WritePe(source, true);
        await File.AppendAllTextAsync(source, "new-version", Ct);
        var original = await File.ReadAllBytesAsync(destination, Ct);
        var service = Service();

        await service.ExecuteInstallationPlanAsync(
            await service.PreviewNativeDllSwapAsync(Exe, destination, source, Ct), Ct);
        Assert.True((await service.VerifyInstallationAsync(Game, Ct)).IsVerified);

        await service.RestoreLatestOperationAsync(Game, Ct);
        Assert.Equal(original, await File.ReadAllBytesAsync(destination, Ct));

        // A DLL outside the game is never a destination.
        await Assert.ThrowsAsync<InvalidDataException>(
            () => service.PreviewNativeDllSwapAsync(Exe, source, destination, Ct));
    }

    [Fact]
    public async Task RejectsASecondProxyNameWhileOptiscalerIsInstalledUnderAnother()
    {
        var service = await Install();

        // The same name updates in place; another one would leave both copies loading.
        await Preview(service);
        var error = await Assert.ThrowsAsync<InvalidDataException>(
            () => service.PreviewInstallationAsync(Exe, Package, "winmm.dll", null, Ct));
        Assert.Contains("dxgi.dll", error.Message);

        // Once another mod has overwritten the proxy it is no longer OptiScaler, so a different name is fine.
        await File.WriteAllTextAsync(Proxy, "ReShade", Ct);
        await service.PreviewInstallationAsync(Exe, Package, "winmm.dll", null, Ct);
    }

    [Fact]
    public async Task InstallsThroughSymlinkedGameFolder()
    {
        // Libraries moved to another drive are often reached through a link; the real folder is what gets tracked.
        var alias = Path.Combine(Root, "game-alias");
        Directory.CreateSymbolicLink(alias, Game);
        var service = Service();

        await service.ExecuteInstallationPlanAsync(
            await service.PreviewInstallationAsync(Path.Combine(alias, "game.exe"), Package, "dxgi.dll", null, Ct), Ct);
        Assert.True(File.Exists(Proxy));
        Assert.True((await service.VerifyInstallationAsync(alias, Ct)).IsVerified);
        Assert.True((await service.VerifyInstallationAsync(Game, Ct)).IsVerified);

        await service.RestoreLatestOperationAsync(alias, Ct);
        Assert.False(File.Exists(Proxy));
    }

    [Fact]
    public async Task MissingSourceFailsBeforeAnyGameFileChanges()
    {
        await File.WriteAllTextAsync(GameIni, "original", Ct);
        var service = Service();
        var plan = await Preview(service);
        File.Delete(Path.Combine(Package, "OptiScaler.dll"));

        await Assert.ThrowsAsync<FileNotFoundException>(() => service.ExecuteInstallationPlanAsync(plan, Ct));
        Assert.Equal("original", await File.ReadAllTextAsync(GameIni, Ct));
        Assert.False(File.Exists(Proxy));
        Assert.Empty(await service.GetOperationHistoryAsync(Ct));
    }

    [Fact]
    public async Task RejectsTraversalInForgedPlan()
    {
        var service = Service();
        var plan = await Preview(service);
        var bad = plan with { Files = [plan.Files[0] with { RelativePath = "../escape.dll" }] };

        await Assert.ThrowsAsync<InvalidDataException>(() => service.ExecuteInstallationPlanAsync(bad, Ct));
        Assert.False(File.Exists(Path.Combine(Root, "escape.dll")));
    }

    [Fact]
    public async Task RejectsDestinationSymlinkIntroducedAfterPreview()
    {
        var service = Service();
        var plan = await Preview(service);
        var outside = Path.Combine(Root, "outside.txt");
        await File.WriteAllTextAsync(outside, "preserve", Ct);
        File.CreateSymbolicLink(Proxy, outside);

        await Assert.ThrowsAsync<InvalidDataException>(() => service.ExecuteInstallationPlanAsync(plan, Ct));
        Assert.Equal("preserve", await File.ReadAllTextAsync(outside, Ct));
    }

    [Fact]
    public async Task RejectsNonPeAndWrongArchitecture()
    {
        await File.WriteAllTextAsync(Path.Combine(Package, "OptiScaler.dll"), "not a DLL", Ct);
        await Assert.ThrowsAsync<InvalidDataException>(() => Preview(Service()));
        TestData.WritePe(Path.Combine(Package, "OptiScaler.dll"), true);

        using (var stream = new FileStream(Exe, FileMode.Open, FileAccess.Write))
        {
            stream.Position = 132;
            stream.Write([0x4c, 0x01]); // IMAGE_FILE_MACHINE_I386
        }

        await Assert.ThrowsAsync<InvalidDataException>(() => Preview(Service()));
    }

    [Fact]
    public async Task UpdateKeepsCustomizedSettingsAndThePreviewListsWhatChanges()
    {
        var service = await Install();

        // The user tunes the installed file, then updates OptiScaler.
        await File.WriteAllTextAsync(GameIni, "[Spoofing]\nDxgi=false\n[Menu]\nShortcutKey=0x24\n", Ct);
        var kept = await service.PreviewInstallationAsync(Exe, Package, "dxgi.dll",
                                                           TestData.Profile("Keys", ("Menu.ShortcutKey", "0x70")), Ct,
                                                           true);
        var reset = await Preview(service);

        Assert.Contains("kept 2 current settings", kept.Description);
        var values = ProfileIni.ReadIniValues(kept.Files.Single(f => f.RelativePath == "OptiScaler.ini").GeneratedText!);
        Assert.Equal("false", values[("Spoofing", "Dxgi")]);

        // The profile is the most explicit choice, so it wins over the kept value.
        Assert.Equal("0x70", values[("Menu", "ShortcutKey")]);
        Assert.Contains(kept.IniChanges, c => c is { Key: "ShortcutKey", Before: "0x24", After: "0x70" });

        // Without carry-over the preview warns that the custom values would be reset.
        Assert.Contains(reset.IniChanges, c => c is { Key: "Dxgi", Before: "false", After: "auto" });
    }

    [Fact]
    public async Task ManagedTargetsReportVersionAndFilesChangedByAGameUpdate()
    {
        var service = Service();
        Assert.Empty(await service.GetManagedTargetsAsync(Ct));

        await service.ExecuteInstallationPlanAsync(await Preview(service) with { Version = "v0.9.4" }, Ct);
        var target = Assert.Single(await service.GetManagedTargetsAsync(Ct));
        Assert.Equal(ManagedHealth.Healthy, target.Health);
        Assert.Equal("v0.9.4", target.Version);

        // A later profile change has no version of its own; the installed version is still reported.
        await ApplyProfile(service, TestData.Profile("Spoof off", ("Spoofing.Dxgi", "false")));
        Assert.Equal("v0.9.4", Assert.Single(await service.GetManagedTargetsAsync(Ct)).Version);

        // Verifying game files in a launcher replaces or deletes what OptiScaler added.
        File.Delete(Proxy);
        Assert.Equal(ManagedHealth.FilesChanged, Assert.Single(await service.GetManagedTargetsAsync(Ct)).Health);
    }

    private Task<InstallPlan> PreviewWithBundledNvapi(GameInstallationService service,
                                                       ComponentInstallSelection selection)
    {
        Directory.CreateDirectory(Path.Combine(Package, "OptiScaler"));
        TestData.WritePe(Path.Combine(Package, BundledNvapi), true);

        return service.PreviewPackageInstallationAsync(Exe, Package, "dxgi.dll", null, [selection],
                                                        cancellationToken: Ct);
    }

    [Fact]
    public async Task AKeptComponentIsLeftOutOfThePlan()
    {
        var plan = await PreviewWithBundledNvapi(Service(), new ComponentInstallSelection(DownloadComponent.FakeNvapi,
                                                     KeepExisting: true));

        Assert.DoesNotContain(plan.Files, f => f.RelativePath.EndsWith("fakenvapi.dll"));
    }

    [Fact]
    public async Task ALocalComponentReplacesTheBundledOneAndRestoreBringsBackTheGamesOwnFile()
    {
        var existing = Path.Combine(Game, "fakenvapi.dll");
        await File.WriteAllTextAsync(existing, "original", Ct);
        var local = Path.Combine(Root, "fakenvapi.dll");
        TestData.WritePe(local, true);
        var service = Service();

        var plan = await PreviewWithBundledNvapi(service, new ComponentInstallSelection(DownloadComponent.FakeNvapi,
                                                     LocalPath: local));
        Assert.Equal("fakenvapi.dll",
                     Assert.Single(plan.Files, f => f.RelativePath.EndsWith("fakenvapi.dll")).RelativePath);
        Assert.Equal("original", await File.ReadAllTextAsync(existing, Ct));

        await service.ExecuteInstallationPlanAsync(plan, Ct);
        Assert.Equal(await File.ReadAllBytesAsync(local, Ct), await File.ReadAllBytesAsync(existing, Ct));

        await service.RestoreLatestOperationAsync(Game, Ct);
        Assert.Equal("original", await File.ReadAllTextAsync(existing, Ct));
    }

    [Fact]
    public void AComponentOverrideRejectsTheWrongFileAndDuplicateVariants()
    {
        var plan = new InstallPlan(Game, OperationKind.InstallOptiscaler, "Test", []);
        var source = Path.Combine(Root, "fakenvapi.dll");
        TestData.WritePe(source, true);

        Assert.Throws<InvalidDataException>(() => GameInstallationService.AddComponentFilesToPlan(
                                                plan, DownloadComponent.Nukem, [source], "local"));
        Assert.Throws<InvalidDataException>(() => GameInstallationService.AddComponentFilesToPlan(
                                                plan, DownloadComponent.FakeNvapi, [source, source], "local"));
    }

    [Fact]
    public async Task OptiPatcherTurnsOnAsiPluginLoading()
    {
        var patcher = Path.Combine(Root, "OptiPatcher.asi");
        TestData.WritePe(patcher, true);
        var service = Service();

        var plan = await service.PreviewPackageInstallationAsync(Exe, Package, "dxgi.dll", null,
                       [new ComponentInstallSelection(DownloadComponent.OptiPatcher, LocalPath: patcher)],
                       cancellationToken: Ct);
        await service.ExecuteInstallationPlanAsync(plan, Ct);

        Assert.Contains("LoadAsiPlugins=true", await File.ReadAllTextAsync(GameIni, Ct));
        Assert.Contains(plan.IniChanges, c => c.Key == "LoadAsiPlugins");
        Assert.True(File.Exists(Path.Combine(Game, "plugins", "OptiPatcher.asi")));

        // Updating while keeping the existing plugin must not switch it off again.
        var update = await service.PreviewPackageInstallationAsync(Exe, Package, "dxgi.dll", null,
                         [new ComponentInstallSelection(DownloadComponent.OptiPatcher, KeepExisting: true)],
                         cancellationToken: Ct);
        Assert.Contains("LoadAsiPlugins=true",
                        update.Files.Single(f => f.RelativePath == "OptiScaler.ini").GeneratedText);
    }

    [Fact]
    public async Task TheDemoWorkspaceCanBeInstalledIntoWithItsProfile()
    {
        var demo = Folder("demo");
        await DemoWorkspace.CreateAsync(demo, Ct);
        var paths = new AppPaths(Path.Combine(demo, "data"));
        var game = Assert.Single((await new JsonGameCatalogRepository(paths).LoadGameCatalogAsync(Ct)).Games);
        var profile = Assert.Single((await new JsonProfileRepository(paths).LoadProfileCatalogAsync(Ct)).Profiles);
        var executable = Assert.Single(game.Installations).PrimaryExecutablePath!;
        var target = Path.GetDirectoryName(executable)!;
        var service = TestData.Installer(paths.RootDirectory);

        await service.ExecuteInstallationPlanAsync(
            await service.PreviewInstallationAsync(executable, Path.Combine(demo, "Package"), "dxgi.dll", profile, Ct),
            Ct);

        Assert.True((await service.VerifyInstallationAsync(target, Ct)).IsVerified);
        Assert.Contains("Dx12Upscaler=xess", await File.ReadAllTextAsync(Path.Combine(target, "OptiScaler.ini"), Ct));
    }
}
