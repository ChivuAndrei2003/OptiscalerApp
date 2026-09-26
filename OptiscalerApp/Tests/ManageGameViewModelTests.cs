using System.Net;
using OptiscalerApp.Management;
using OptiscalerApp.Models;
using OptiscalerApp.Paths;
using OptiscalerApp.Persistence;
using OptiscalerApp.ViewModels;
using Xunit;

namespace Optiscaler.Tests;

public sealed class ManageGameViewModelTests : IDisposable
{
    private readonly HttpClient _client = new();
    private readonly AppPaths _paths;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "Optiscaler-manage-" + Guid.NewGuid().ToString("N"));

    public ManageGameViewModelTests()
    {
        _paths = new AppPaths(Path.Combine(_root, "data"));
        Directory.CreateDirectory(Game);
        Directory.CreateDirectory(Package);
        InstallationTests.WritePe(Exe, false);
        InstallationTests.WritePe(Path.Combine(Game, "nvngx_dlss.dll"), true);
        InstallationTests.WritePe(Path.Combine(Package, "OptiScaler.dll"), true);
        File.WriteAllText(Path.Combine(Package, "OptiScaler.ini"), "[Upscalers]\nDx12Upscaler=auto\n");
    }

    private string Game => Path.Combine(_root, "game");
    private string Package => Path.Combine(_root, "package");
    private string Exe => Path.Combine(Game, "game.exe");

    public void Dispose()
    {
        _client.Dispose();
        Directory.Delete(_root, true);
    }

    private (ManageGameViewModel ViewModel, List<(GameId Id, string Name)> Saves) Create(HttpClient? client = null)
    {
        var game = new GameRecord
        {
            Id = GameId.Create(GamePlatform.Manual, null, Game),
            Name = "Test game",
            Platform = GamePlatform.Manual,
            Installations = [new GameInstallation { RootPath = Game, PrimaryExecutablePath = Exe }]
        };
        var packages = new PackageDownloadService(_paths, client ?? _client);
        var saves = new List<(GameId, string)>();
        var vm = new ManageGameViewModel(game, new GameAnalyzer(), new GameInstallationService(_paths, packages),
                                         packages, new JsonProfileRepository(_paths),
                                         (id, name, _, _) =>
                                         {
                                             saves.Add((id, name));

                                             return Task.FromResult(new GameRecord
                                             {
                                                 Id = id, Name = name, Platform = game.Platform,
                                                 Installations = game.Installations
                                             });
                                         })
        {
            Dialogs = new FakeDialogs(Package)
        };

        return (vm, saves);
    }

    [Fact]
    public async Task LocalPackagePreviewApplyAndRestore()
    {
        var (vm, _) = Create();
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.Contains(vm.Components, c => c.Kind == ComponentKind.Dlss);
        Assert.Equal("OptiScaler not detected", vm.InstallStateText);

        // Picking "Choose local package…" opens the folder dialog and then shows the package version.
        vm.SelectedVersion = vm.VersionChoices.Single(c => c.Action == VersionAction.BrowseLocal);
        await WaitUntilIdle(vm);
        Assert.Equal(Package, vm.PackagePath);
        Assert.Equal(VersionAction.UseCurrent, vm.SelectedVersion?.Action);

        vm.FakeNvapi.Selected = ComponentChoice.KeepExisting;
        await vm.PreviewInstallCommand.ExecuteAsync(null);
        Assert.True(vm.IsPreviewing, vm.Status);
        Assert.Contains("Create : dxgi.dll", vm.PreviewText);

        await vm.ApplyCommand.ExecuteAsync(null);
        Assert.False(vm.IsPreviewing);
        Assert.Equal("Changes applied and verified.", vm.Status);
        Assert.Equal("Managed files verified", vm.InstallStateText);
        Assert.True(vm.CanUninstall);

        await vm.RestoreCommand.ExecuteAsync(null);
        Assert.Equal("Latest operation restored.", vm.Status);
        Assert.False(File.Exists(Path.Combine(Game, "dxgi.dll")));
        Assert.False(vm.CanRestore);
    }

    [Fact]
    public async Task FailuresAreReportedInStatusAndLeaveTheViewUsable()
    {
        var (vm, _) = Create();
        vm.ExecutablePath = "";
        await vm.PreviewProfileCommand.ExecuteAsync(null);
        Assert.Equal("Select a saved profile first.", vm.Status);
        Assert.False(vm.IsBusy);
        Assert.False(vm.IsPreviewing);
    }

    [Fact]
    public async Task SavingDetailsUpdatesTheDisplayedName()
    {
        var (vm, saves) = Create();
        vm.EditName = "Renamed";
        vm.IsEditingDetails = true;
        await vm.SaveDetailsCommand.ExecuteAsync(null);
        Assert.Equal("Renamed", Assert.Single(saves).Name);
        Assert.Equal("Renamed", vm.GameName);
        Assert.False(vm.IsEditingDetails);
    }

    [Fact]
    public async Task SwitchingChannelsFetchesEachChannelOnceUntilRefreshed()
    {
        var requests = new List<string>();
        using var client = new HttpClient(new ProfileSettingsTests.CountingHandler(() => requests.Add("")));
        var (vm, _) = Create(client);

        // The first fetch loads the OptiScaler channel plus the four channel-independent components.
        await vm.SelectChannelCommand.ExecuteAsync(ReleaseChannel.Beta);
        Assert.Equal(5, requests.Count);
        await vm.SelectChannelCommand.ExecuteAsync(ReleaseChannel.Stable);
        Assert.Equal(6, requests.Count);
        await vm.SelectChannelCommand.ExecuteAsync(ReleaseChannel.Beta);
        await vm.SelectChannelCommand.ExecuteAsync(ReleaseChannel.Stable);
        Assert.Equal(6, requests.Count);
        Assert.True(vm.IsStableChannel);
        Assert.False(vm.IsBusy);

        await vm.RefreshVersionsCommand.ExecuteAsync(null);
        Assert.Equal(11, requests.Count);
    }

    [Fact]
    public async Task ComponentFailureStaysVisibleWhenSwitchingToAFetchedChannel()
    {
        // OptiScaler releases load, but every component repository fails.
        using var client = new HttpClient(new StubHandler(uri => uri.AbsolutePath is
                                                              "/repos/optiscaler/OptiScaler/releases" or
                                                              "/repos/Optiscaler-Client/OptiScaler-Betas/releases"));
        var (vm, _) = Create(client);

        await vm.SelectChannelCommand.ExecuteAsync(ReleaseChannel.Beta);
        await vm.SelectChannelCommand.ExecuteAsync(ReleaseChannel.Stable);
        await vm.SelectChannelCommand.ExecuteAsync(ReleaseChannel.Beta);

        Assert.StartsWith("Could not refresh: FSR 4 / INT8, FakeNvapi, OptiPatcher, NukemFG", vm.ExtrasText);
    }

    private static async Task WaitUntilIdle(ManageGameViewModel vm)
    {
        // Version actions start on a yielded continuation.
        for (var i = 0; i < 100 && (vm.IsBusy || vm.SelectedVersion?.Action == VersionAction.BrowseLocal); i++)
            await Task.Delay(10, TestContext.Current.CancellationToken);
    }

    private sealed class StubHandler(Func<Uri, bool> succeeds) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
                                                               CancellationToken cancellationToken)
        {
            return Task.FromResult(succeeds(request.RequestUri!)
                                       ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]") }
                                       : new HttpResponseMessage(HttpStatusCode.InternalServerError));
        }
    }

    private sealed class FakeDialogs(string folder) : IFileDialogs
    {
        public Task<string?> PickFile_Async(string title, string pattern) { return Task.FromResult<string?>(null); }

        public Task<string?> PickFolder_Async(string title) { return Task.FromResult<string?>(folder); }
    }
}
