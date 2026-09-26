using System.Net;
using OptiscalerApp.Management;
using OptiscalerApp.Models;
using OptiscalerApp.Paths;
using OptiscalerApp.Persistence;
using OptiscalerApp.ViewModels;
using Xunit;

namespace Optiscaler.Tests;

public sealed class ManageGameFeatureTests : IDisposable
{
    private static readonly string Wiki =
        TestData.WikiTable("| [Test Game](Test-Game) | ✅ | DLSS, XeSS | ✨ | Use OptiScaler as `winmm.dll`. |  |");

    private const string OptiPatcherRelease = """
                                              [{"tag_name":"rolling","draft":false,"prerelease":false,
                                                "assets":[{"name":"OptiPatcher.asi","browser_download_url":"https://github.com/optiscaler/OptiPatcher/releases/download/rolling/OptiPatcher.asi"}]}]
                                              """;

    private readonly HttpClient _client;
    private readonly AppPaths _paths;
    private readonly string _root = TestData.TempRoot("Optiscaler-manage-features-");

    public ManageGameFeatureTests()
    {
        _paths = new AppPaths(Path.Combine(_root, "data"));
        _client = new HttpClient(new StubHttpHandler(request => request.RequestUri!.Host switch
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
        }));
        Directory.CreateDirectory(Package);
        InstallationTests.WritePe(Path.Combine(Package, "OptiScaler.dll"), true);
        File.WriteAllText(Path.Combine(Package, "OptiScaler.ini"), "[Spoofing]\nDxgi=auto\n");
    }

    private string Game => Path.Combine(_root, "Test Game");
    private string Package => Path.Combine(_root, "package");

    public void Dispose()
    {
        _client.Dispose();
        Directory.Delete(_root, true);
    }

    private (ManageGameViewModel Vm, FakeShell Shell) Create(string? executable, GamePlatform platform = GamePlatform.Manual,
                                                            string? externalId = null)
    {
        var record = new GameRecord
        {
            Id = GameId.Create(platform, externalId, Game),
            Name = "Test Game",
            Platform = platform,
            ExternalId = externalId,
            Installations = [new GameInstallation { RootPath = Game, PrimaryExecutablePath = executable }]
        };
        var packages = new PackageDownloadService(_paths, _client);
        var shell = new FakeShell();
        var vm = new ManageGameViewModel(record, new GameAnalyzer(), new GameInstallationService(_paths, packages),
                                         packages, new JsonProfileRepository(_paths),
                                         (_, _, _, _) => Task.FromResult(record),
                                         new CompatibilityListService(_paths, _client),
                                         () => Task.FromResult<IReadOnlyList<GpuInfo>>([TestData.Radeon]))
        {
            Shell = shell
        };

        return (vm, shell);
    }

    private string WriteUnrealLayout()
    {
        InstallationTests.WritePe(Path.Combine(Directory.CreateDirectory(Game).FullName, "TestGame.exe"), false);
        var binaries = Directory.CreateDirectory(Path.Combine(Game, "TestGame", "Binaries", "Win64")).FullName;
        var shipping = Path.Combine(binaries, "TestGame-Win64-Shipping.exe");
        InstallationTests.WritePe(shipping, false);
        InstallationTests.WritePe(Path.Combine(binaries, "nvngx_dlss.dll"), true);
        InstallationTests.WritePe(Path.Combine(binaries, "nvngx_dlssg.dll"), true);

        return shipping;
    }

    [Fact]
    public async Task LoadDetectsTheExecutableAndShowsWikiCompatibility()
    {
        var shipping = WriteUnrealLayout();
        var (vm, _) = Create(null);

        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Equal(shipping, vm.ExecutablePath);
        Assert.Equal("Working (OptiScaler wiki)", vm.CompatibilityText);
        Assert.Equal("Supported", vm.OptiPatcherText);
        Assert.Contains("Wiki: DLSS, XeSS", vm.InputsText);
        Assert.Equal("https://github.com/optiscaler/OptiScaler/wiki/Test-Game", vm.CompatibilityPageUrl);
        Assert.Equal("Open wiki entry ↗", vm.CompatibilityPageLabel);
        Assert.Equal("AMD Radeon RX 6800", vm.GpuText);
        Assert.NotNull(vm.Recommendation);
    }

    [Fact]
    public async Task ApplyRecommendedSelectsOptionsForThisGpuAndGame()
    {
        var (vm, _) = Create(WriteUnrealLayout());
        await vm.LoadCommand.ExecuteAsync(null);

        await vm.ApplyRecommendedCommand.ExecuteAsync(null);

        Assert.Equal("winmm.dll", vm.SelectedProxy);
        Assert.Equal(ComponentSource.Bundle, vm.FakeNvapi.Selected?.Source);
        Assert.Equal(ComponentSource.Bundle, vm.Nukem.Selected?.Source);
        Assert.Equal(ComponentSource.Release, vm.OptiPatcher.Selected?.Source);
        Assert.StartsWith("Recommended settings selected", vm.Status);
    }

    [Fact]
    public async Task AManagedProxyOverwrittenByAnotherModCountsAsOccupied()
    {
        var shipping = WriteUnrealLayout();
        var (vm, _) = Create(shipping);
        await vm.LoadCommand.ExecuteAsync(null);
        vm.PackagePath = Package;
        vm.SelectedProxy = "winmm.dll";
        await vm.PreviewInstallCommand.ExecuteAsync(null);
        await vm.ApplyCommand.ExecuteAsync(null);

        // Its own, unchanged winmm.dll is OptiScaler's to replace, as the wiki suggests.
        Assert.Equal("winmm.dll", vm.Recommendation?.Proxy);

        // Another mod copied over it.
        var proxy = Path.Combine(Path.GetDirectoryName(shipping)!, "winmm.dll");
        await File.AppendAllTextAsync(proxy, "reshade", TestContext.Current.CancellationToken);
        await vm.VerifyCommand.ExecuteAsync(null);

        Assert.Equal("dxgi.dll", vm.Recommendation?.Proxy);
    }

    [Fact]
    public async Task DetectExecutableExplainsItsChoice()
    {
        var shipping = WriteUnrealLayout();
        var (vm, _) = Create(Path.Combine(Game, "TestGame.exe"));
        await vm.LoadCommand.ExecuteAsync(null);

        await vm.DetectExecutableCommand.ExecuteAsync(null);

        Assert.Equal(shipping, vm.ExecutablePath);
        Assert.Contains("Unreal Engine shipping binary", vm.Status);
        Assert.StartsWith("Executable candidates", vm.DetailsText);
    }

    [Fact]
    public async Task DiagnosticsAreCopiedAndShownWithoutTheHomeFolder()
    {
        var shipping = WriteUnrealLayout();
        await File.WriteAllTextAsync(Path.Combine(Path.GetDirectoryName(shipping)!, "OptiScaler.log"),
                                     "line 1\nloaded upscaler xess\n", TestContext.Current.CancellationToken);
        var (vm, shell) = Create(shipping);
        await vm.LoadCommand.ExecuteAsync(null);
        vm.PackagePath = Package;
        await vm.PreviewInstallCommand.ExecuteAsync(null);
        await vm.ApplyCommand.ExecuteAsync(null);

        await vm.CopyDiagnosticsCommand.ExecuteAsync(null);

        var report = Assert.Single(shell.Clipboard);
        Assert.Equal(report, vm.DetailsText);
        Assert.Contains("### OptiScaler report: Test Game", report);
        Assert.Contains("AMD Radeon RX 6800 (16 GB)", report);
        Assert.Contains("Wiki compatibility: Working", report);
        Assert.Contains("InstallOptiscaler · Installed", report);
        Assert.Contains("Verification: all managed files match", report);
        Assert.Contains("loaded upscaler xess", report);
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (_root.StartsWith(home, StringComparison.Ordinal)) Assert.DoesNotContain(home, report);
    }

    [Fact]
    public async Task KeepSettingsIsOfferedOnceTheGameHasAnIni()
    {
        var shipping = WriteUnrealLayout();
        var (vm, _) = Create(shipping);
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.False(vm.HasCurrentIni);

        vm.PackagePath = Package;
        await vm.PreviewInstallCommand.ExecuteAsync(null);
        await vm.ApplyCommand.ExecuteAsync(null);

        Assert.True(vm.HasCurrentIni);
        Assert.True(vm.KeepCurrentSettings);
    }

    [Fact]
    public async Task SteamGamesLaunchThroughSteamAndLinuxOptionsFollowTheProxy()
    {
        var (vm, shell) = Create(WriteUnrealLayout(), GamePlatform.Steam, "1245620");
        Assert.True(vm.CanLaunch);

        await vm.LaunchGameCommand.ExecuteAsync(null);
        vm.SelectedProxy = "winmm.dll";

        Assert.Equal(new Uri("steam://rungameid/1245620"), Assert.Single(shell.Opened).Uri);
        Assert.Equal("WINEDLLOVERRIDES=\"winmm=n,b\" %COMMAND%", vm.LinuxLaunchOptions);
    }

    private sealed class FakeShell : IShellActions
    {
        public List<string> Clipboard { get; } = [];

        public List<LaunchTarget> Opened { get; } = [];

        public Task SetClipboardText_Async(string text)
        {
            Clipboard.Add(text);

            return Task.CompletedTask;
        }

        public Task<bool> Open_Async(LaunchTarget target)
        {
            Opened.Add(target);

            return Task.FromResult(true);
        }
    }
}
