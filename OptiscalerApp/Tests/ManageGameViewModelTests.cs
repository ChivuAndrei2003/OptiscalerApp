using System.Net;
using OptiscalerApp.Management;
using OptiscalerApp.Models;
using OptiscalerApp.ViewModels;
using Xunit;

namespace Optiscaler.Tests;

public sealed class ManageGameViewModelTests : TempFolderTest
{
    private const string OptiPatcherRelease = """
                                              [{"tag_name":"rolling","draft":false,"prerelease":false,
                                                "assets":[{"name":"OptiPatcher.asi","browser_download_url":"https://github.com/optiscaler/OptiPatcher/releases/download/rolling/OptiPatcher.asi"}]}]
                                              """;

    private static readonly string Wiki =
        TestData.WikiTable("| [Test Game](Test-Game) | ✅ | DLSS, XeSS | ✨ | Use OptiScaler as `winmm.dll`. |  |");

    private readonly FakeShell _shell = new();

    public ManageGameViewModelTests()
    {
        TestData.WritePackage(Package, "[Upscalers]\nDx12Upscaler=auto\n");
    }

    private string Game => Path.Combine(Root, "Test Game");
    private string Package => Path.Combine(Root, "package");

    /// <summary>The wiki page and OptiPatcher's releases; everything else is not found.</summary>
    private static StubHttpHandler Online()
    {
        return new StubHttpHandler(request => request.RequestUri!.Host switch
        {
            "raw.githubusercontent.com" => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(Wiki)
            },
            "api.github.com" => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(OptiPatcherRelease)
            },
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        });
    }

    private ManageGameViewModel Create(string? executable, HttpMessageHandler? http = null,
                                       string name = "Test Game", GamePlatform platform = GamePlatform.Manual,
                                       string? externalId = null)
    {
        return TestData.ManageGame(TestData.Game(Game, name, platform, externalId, executable),
                                   Path.Combine(Root, "data"), http, new FakeDialogs(Package), _shell,
                                   [TestData.Radeon]);
    }

    /// <summary>A game with its executable and DLSS in one folder.</summary>
    private string FlatGame()
    {
        var exe = Path.Combine(Folder("Test Game"), "game.exe");
        TestData.WritePe(exe, false);
        TestData.WritePe(Path.Combine(Game, "nvngx_dlss.dll"), true);

        return exe;
    }

    /// <summary>An Unreal Engine game: a launcher stub at the top and the real binary deep below it.</summary>
    private string UnrealGame()
    {
        TestData.WritePe(Path.Combine(Folder("Test Game"), "TestGame.exe"), false);
        var binaries = Folder("Test Game", "TestGame", "Binaries", "Win64");
        var shipping = Path.Combine(binaries, "TestGame-Win64-Shipping.exe");
        TestData.WritePe(shipping, false);
        TestData.WritePe(Path.Combine(binaries, "nvngx_dlss.dll"), true);
        TestData.WritePe(Path.Combine(binaries, "nvngx_dlssg.dll"), true);

        return shipping;
    }

    private async Task Install(ManageGameViewModel vm, string proxy = "dxgi.dll")
    {
        vm.Package.PackagePath = Package;
        vm.SelectedProxy = proxy;
        await vm.PreviewInstallCommand.ExecuteAsync(null);
        await vm.ApplyCommand.ExecuteAsync(null);
    }

    private static async Task WaitUntilIdle(ManageGameViewModel vm)
    {
        // Version actions start on a yielded continuation.
        for (var i = 0; i < 100 && (vm.IsBusy || vm.Package.SelectedVersion?.Action == VersionAction.BrowseLocal); i++)
            await Task.Delay(10, Ct);
    }

    [Fact]
    public async Task LocalPackagePreviewApplyAndRestore()
    {
        var vm = Create(FlatGame());
        var proxy = Path.Combine(Game, "dxgi.dll");
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.Contains(vm.Components, c => c.Kind == ComponentKind.Dlss);
        Assert.Equal("OptiScaler not detected", vm.InstallStateText);

        // Picking "Choose local package…" opens the folder dialog and then shows the package version.
        vm.Package.SelectedVersion = vm.Package.VersionChoices.Single(c => c.Action == VersionAction.BrowseLocal);
        await WaitUntilIdle(vm);
        Assert.Equal(Package, vm.Package.PackagePath);
        Assert.Equal(VersionAction.UseCurrent, vm.Package.SelectedVersion?.Action);

        await vm.PreviewInstallCommand.ExecuteAsync(null);
        Assert.True(vm.IsPreviewing, vm.Status);
        Assert.Contains("dxgi.dll", vm.PreviewText);
        Assert.False(File.Exists(proxy));

        await vm.ApplyCommand.ExecuteAsync(null);
        Assert.False(vm.IsPreviewing);
        Assert.True(File.Exists(proxy));
        Assert.Equal("Managed files verified", vm.InstallStateText);

        await vm.RestoreCommand.ExecuteAsync(null);
        Assert.False(File.Exists(proxy));
        Assert.False(vm.CanRestore);
    }

    [Fact]
    public async Task UninstallUndoesEveryOperationInTheFolder()
    {
        var vm = Create(FlatGame());
        await vm.LoadCommand.ExecuteAsync(null);
        await Install(vm);
        vm.SelectedProfile = TestData.Profile("Later", ("Upscalers.Dx12Upscaler", "xess"));
        await vm.PreviewProfileCommand.ExecuteAsync(null);
        await vm.ApplyCommand.ExecuteAsync(null);
        Assert.True(vm.CanUninstall, vm.Status);

        await vm.UninstallCommand.ExecuteAsync(null);

        Assert.False(File.Exists(Path.Combine(Game, "dxgi.dll")));
        Assert.False(File.Exists(Path.Combine(Game, "OptiScaler.ini")));
        Assert.False(vm.CanUninstall);
        Assert.False(vm.CanRestore);
    }

    [Fact]
    public async Task LibrariesThisAppInstalledAreNotReportedAsTheGamesOwnUpscalers()
    {
        // The game ships DLSS and FSR 3.1's single DLL; XeSS only arrives with the OptiScaler package.
        var vm = Create(FlatGame());
        TestData.WritePe(Path.Combine(Game, "amd_fidelityfx_dx12.dll"), true);
        TestData.WritePe(Path.Combine(Package, "libxess.dll"), true);
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.Contains("AMD FSR", vm.InputsText);

        await Install(vm);

        Assert.Contains(vm.Components, c => c.Kind == ComponentKind.Xess);
        Assert.DoesNotContain("XeSS", vm.InputsText);
        Assert.Contains("DLSS", vm.InputsText);
    }

    [Fact]
    public async Task AProxyNamedLibraryIsNotMistakenForOptiscaler()
    {
        // Other mods install under these names too, and some games ship a library with one of them.
        var vm = Create(FlatGame());
        TestData.WritePe(Path.Combine(Game, "version.dll"), true);

        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Contains(vm.Components, c => c.Kind == ComponentKind.InjectionProxy);
        Assert.Equal("OptiScaler not detected", vm.InstallStateText);
    }

    [Fact]
    public async Task MicrosoftLibrariesUnderProxyNamesAreNotReportedAsMods()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Needs a genuine Windows system library.");

        // Unreal Engine games and Source 2 ship their own copy of dbghelp.dll.
        FlatGame();
        File.Copy(Path.Combine(Environment.SystemDirectory, "dbghelp.dll"), Path.Combine(Game, "dbghelp.dll"));

        var analysis = await new GameAnalyzer().AnalyzeGameAsync(Game, Ct);

        Assert.DoesNotContain(analysis.Components, c => c.Kind == ComponentKind.InjectionProxy);
        Assert.False(analysis.HasOptiscalerFiles);
    }

    [Fact]
    public async Task UpdatingKeepsTheProxyNameTheGameWasInstalledWith()
    {
        var exe = FlatGame();
        var first = Create(exe);
        await first.LoadCommand.ExecuteAsync(null);
        await Install(first, "winmm.dll");

        // Reopening the game starts from the app-wide default, which must not add a second copy as dxgi.dll.
        var vm = Create(exe);
        Assert.Equal("dxgi.dll", vm.SelectedProxy);
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Equal("winmm.dll", vm.SelectedProxy);
        Assert.Equal("winmm.dll", vm.Recommendation?.Proxy);
    }

    [Fact]
    public async Task RenamingShowsTheNewNameAndItsWikiEntry()
    {
        var vm = Create(FlatGame(), Online(), "Unlisted");
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.Equal("Not in the wiki's tested list", vm.CompatibilityText);

        vm.IsEditingDetails = true;
        vm.EditName = "Test Game";
        await vm.SaveDetailsCommand.ExecuteAsync(null);

        Assert.Equal("Test Game", vm.GameName);
        Assert.False(vm.IsEditingDetails);
        Assert.Equal("Working (OptiScaler wiki)", vm.CompatibilityText);
    }

    [Fact]
    public async Task CancellingTheLocalFilePickerKeepsTheEarlierChoice()
    {
        var vm = Create(FlatGame());
        vm.Package.FakeNvapi.Selected = ComponentChoice.KeepExisting;
        vm.Package.FakeNvapi.Selected = ComponentChoice.BrowseLocal;

        // The picker opens on a yielded continuation; the fake dialog cancels it.
        for (var i = 0; i < 100 && (vm.IsBusy || vm.Package.FakeNvapi.Selected == ComponentChoice.BrowseLocal); i++)
            await Task.Delay(10, Ct);

        Assert.Equal(ComponentChoice.KeepExisting, vm.Package.FakeNvapi.Selected);
    }

    [Fact]
    public async Task PreloadedChannelsSwitchWithoutFetchingOrGoingBusy()
    {
        var http = StubHttpHandler.Text("[]");
        var vm = Create(FlatGame(), http);
        await vm.LoadCommand.ExecuteAsync(null);
        await vm.Package.PreloadReleasesAsync();
        var preloaded = http.Requests;
        var wentBusy = false;
        vm.PropertyChanged += (_, e) => wentBusy |= e.PropertyName == nameof(vm.IsBusy);

        await vm.Package.SelectChannelCommand.ExecuteAsync(ReleaseChannel.Beta);
        await vm.Package.SelectChannelCommand.ExecuteAsync(ReleaseChannel.Stable);

        Assert.Equal(preloaded, http.Requests);
        Assert.False(wentBusy);

        await vm.Package.RefreshVersionsCommand.ExecuteAsync(null);
        Assert.True(http.Requests > preloaded);
    }

    [Fact]
    public async Task ChannelSwitchShowsEachChannelsOwnPackage()
    {
        var vm = Create(FlatGame());
        vm.Package.PackagePath = Package;

        await vm.Package.SelectChannelCommand.ExecuteAsync(ReleaseChannel.Beta);
        Assert.Equal("", vm.Package.PackagePath);
        Assert.Null(vm.Package.SelectedVersion);

        await vm.Package.SelectChannelCommand.ExecuteAsync(ReleaseChannel.Stable);
        Assert.Equal(Package, vm.Package.PackagePath);
        Assert.Equal(VersionAction.UseCurrent, vm.Package.SelectedVersion?.Action);
    }

    [Fact]
    public async Task ComponentFailureStaysVisibleWhenSwitchingToAFetchedChannel()
    {
        // OptiScaler releases load, but every component repository fails.
        var http = new StubHttpHandler(request => request.RequestUri!.AbsolutePath is
                                                      "/repos/optiscaler/OptiScaler/releases" or
                                                      "/repos/Optiscaler-Client/OptiScaler-Betas/releases"
                                                      ? new HttpResponseMessage(HttpStatusCode.OK)
                                                      {
                                                          Content = new StringContent("[]")
                                                      }
                                                      : new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var vm = Create(FlatGame(), http);

        await vm.Package.SelectChannelCommand.ExecuteAsync(ReleaseChannel.Beta);
        await vm.Package.SelectChannelCommand.ExecuteAsync(ReleaseChannel.Stable);
        await vm.Package.SelectChannelCommand.ExecuteAsync(ReleaseChannel.Beta);

        Assert.StartsWith("Could not refresh", vm.Package.ExtrasText);
    }

    [Fact]
    public async Task ASlowWikiDoesNotHoldUpThePageAndUpdatesItOnArrival()
    {
        var wiki = new TaskCompletionSource<string>();
        var vm = Create(FlatGame(), new SlowHttpHandler(wiki.Task));

        var load = vm.LoadCommand.ExecuteAsync(null);
        await WaitUntilIdle(vm);
        Assert.False(load.IsCompleted);
        Assert.Contains(vm.Components, c => c.Kind == ComponentKind.Dlss);
        Assert.Equal("Not in the wiki's tested list", vm.CompatibilityText);

        wiki.SetResult(Wiki);
        await load;

        Assert.Equal("Working (OptiScaler wiki)", vm.CompatibilityText);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task LoadDetectsTheExecutableAndShowsWikiCompatibility()
    {
        var shipping = UnrealGame();
        var vm = Create(null, Online());

        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Equal(shipping, vm.ExecutablePath);
        Assert.Equal("Working (OptiScaler wiki)", vm.CompatibilityText);
        Assert.Equal("Supported", vm.OptiPatcherText);
        Assert.Contains("Wiki: DLSS, XeSS", vm.InputsText);
        Assert.Equal("https://github.com/optiscaler/OptiScaler/wiki/Test-Game", vm.CompatibilityPageUrl);
        Assert.Equal("AMD Radeon RX 6800", vm.GpuText);
    }

    [Fact]
    public async Task ApplyRecommendedSelectsOptionsForThisGpuAndGame()
    {
        var vm = Create(UnrealGame(), Online());
        await vm.LoadCommand.ExecuteAsync(null);

        await vm.ApplyRecommendedCommand.ExecuteAsync(null);

        Assert.Equal("winmm.dll", vm.SelectedProxy);
        Assert.Equal(ComponentSource.Bundle, vm.Package.FakeNvapi.Selected?.Source);
        Assert.Equal(ComponentSource.Bundle, vm.Package.Nukem.Selected?.Source);
        Assert.Equal(ComponentSource.Release, vm.Package.OptiPatcher.Selected?.Source);
    }

    [Fact]
    public async Task AManagedProxyOverwrittenByAnotherModCountsAsOccupied()
    {
        var shipping = UnrealGame();
        var vm = Create(shipping, Online());
        await vm.LoadCommand.ExecuteAsync(null);
        await Install(vm, "winmm.dll");

        // Its own, unchanged winmm.dll is OptiScaler's to replace, as the wiki suggests.
        Assert.Equal("winmm.dll", vm.Recommendation?.Proxy);

        // Another mod copied over it.
        await File.AppendAllTextAsync(Path.Combine(Path.GetDirectoryName(shipping)!, "winmm.dll"), "reshade", Ct);
        await vm.VerifyCommand.ExecuteAsync(null);

        Assert.Equal("dxgi.dll", vm.Recommendation?.Proxy);
    }

    [Fact]
    public async Task DetectExecutableReplacesALauncherStubAndExplainsWhy()
    {
        var shipping = UnrealGame();
        var vm = Create(Path.Combine(Game, "TestGame.exe"), Online());
        await vm.LoadCommand.ExecuteAsync(null);

        await vm.DetectExecutableCommand.ExecuteAsync(null);

        Assert.Equal(shipping, vm.ExecutablePath);
        Assert.Contains("Unreal Engine shipping binary", vm.Status);
    }

    [Fact]
    public async Task DiagnosticsAreCopiedAndShown()
    {
        var shipping = UnrealGame();
        await File.WriteAllTextAsync(Path.Combine(Path.GetDirectoryName(shipping)!, "OptiScaler.log"),
                                     "line 1\nloaded upscaler xess\n", Ct);
        var vm = Create(shipping, Online());
        await vm.LoadCommand.ExecuteAsync(null);
        await Install(vm);

        await vm.CopyDiagnosticsCommand.ExecuteAsync(null);

        var report = Assert.Single(_shell.Clipboard);
        Assert.Equal(report, vm.DetailsText);
        Assert.Contains("Test Game", report);
        Assert.Contains("AMD Radeon RX 6800", report);
        Assert.Contains("Wiki compatibility: Working", report);
        Assert.Contains("InstallOptiscaler", report);
        Assert.Contains("loaded upscaler xess", report);
    }

    [Fact]
    public async Task KeepSettingsIsOfferedOnceTheGameHasAnIni()
    {
        var vm = Create(UnrealGame(), Online());
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.False(vm.HasCurrentIni);

        await Install(vm);

        Assert.True(vm.HasCurrentIni);
        Assert.True(vm.KeepCurrentSettings);
        Assert.False(vm.HasCurrentSettings);

        // What the in-game menu saves, in its own number formats, is read back and can become a profile.
        await File.WriteAllTextAsync(Path.Combine(Path.GetDirectoryName(vm.ExecutablePath)!, "OptiScaler.ini"),
                                     "[Upscalers]\nDx12Upscaler=xess\n[Menu]\nShortcutKey=0x8\nScale=1.250000\n" +
                                     "[FSR]\nFsrNonLinearColorSpace=true\n", Ct);
        await vm.VerifyCommand.ExecuteAsync(null);

        Assert.True(vm.CanSaveSettingsAsProfile);

        await vm.SaveSettingsAsProfileCommand.ExecuteAsync(null);

        // The FSR key is not one a profile holds, so it stays out.
        var saved = Assert.Single(vm.ProfileChoices);
        Assert.Equal(3, saved.Settings.Count);
        Assert.Equal("xess", saved.Settings["Upscalers.Dx12Upscaler"]);
        Assert.Equal("1.25", saved.Settings["Menu.Scale"]);
        Assert.Equal("0x08", saved.Settings["Menu.ShortcutKey"]);
    }

    [Fact]
    public async Task SteamGamesLaunchThroughSteam()
    {
        var vm = Create(UnrealGame(), Online(), platform: GamePlatform.Steam, externalId: "1245620");
        Assert.True(vm.CanLaunch);

        await vm.LaunchGameCommand.ExecuteAsync(null);

        Assert.Equal(new Uri("steam://rungameid/1245620"), Assert.Single(_shell.Opened).Uri);
    }

    private sealed class FakeShell : IShellActions
    {
        public List<string> Clipboard { get; } = [];

        public List<LaunchTarget> Opened { get; } = [];

        public Task SetClipboardTextAsync(string text)
        {
            Clipboard.Add(text);

            return Task.CompletedTask;
        }

        public Task<bool> OpenAsync(LaunchTarget target)
        {
            Opened.Add(target);

            return Task.FromResult(true);
        }

        public Task<bool> OpenFolderAsync(string path) { return Task.FromResult(true); }
    }
}
