using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using OptiscalerApp;
using OptiscalerApp.Management;
using OptiscalerApp.Models;
using OptiscalerApp.Paths;
using OptiscalerApp.Persistence;
using OptiscalerApp.ViewModels;
using OptiscalerApp.Views;
using Xunit;

namespace Optiscaler.Tests;

/// <summary>Renders the real XAML headlessly, so broken bindings fail here instead of at runtime.</summary>
public sealed class ManageGameViewTests : IDisposable
{
    // One session for the whole run: Avalonia can be initialized only once per process, and disposing the
    // session blocks on its dispatcher thread.
    internal static readonly Lazy<HeadlessUnitTestSession> Session =
        new(() => HeadlessUnitTestSession.StartNew(typeof(HeadlessApp)));

    private readonly HttpClient _client = new();
    private readonly string _root = Path.Combine(Path.GetTempPath(), "Optiscaler-view-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        _client.Dispose();
        Directory.Delete(_root, true);
    }

    [Fact]
    public async Task ViewReflectsAnalysisPreviewAndApply()
    {
        var game = Directory.CreateDirectory(Path.Combine(_root, "game")).FullName;
        var package = Directory.CreateDirectory(Path.Combine(_root, "package")).FullName;
        var exe = Path.Combine(game, "game.exe");
        InstallationTests.WritePe(exe, false);
        InstallationTests.WritePe(Path.Combine(game, "libxess.dll"), true);
        InstallationTests.WritePe(Path.Combine(package, "OptiScaler.dll"), true);
        await File.WriteAllTextAsync(Path.Combine(package, "OptiScaler.ini"), "[Upscalers]\n",
                                     TestContext.Current.CancellationToken);
        var paths = new AppPaths(Path.Combine(_root, "data"));
        var packages = new PackageDownloadService(paths, _client);
        var record = new GameRecord
        {
            Id = GameId.Create(GamePlatform.Manual, null, game),
            Name = "Headless game",
            Platform = GamePlatform.Manual,
            Installations = [new GameInstallation { RootPath = game, PrimaryExecutablePath = exe }]
        };
        var vm = new ManageGameViewModel(record, new GameAnalyzer(), new GameInstallationService(paths, packages),
                                         packages, new JsonProfileRepository(paths),
                                         (_, _, _, _) => Task.FromResult(record));

        await Session.Value.Dispatch(async () =>
        {
            var view = new ManageGameView { DataContext = vm };
            var window = new Window { Width = 1280, Height = 900, Content = view };
            window.Show();
            await WaitForIdle(vm);

            Assert.Contains(Texts(view), t => t == "Headless game");
            Assert.Contains(Texts(view), t => t.StartsWith("Intel XeSS"));
            Assert.False(view.FindControl<Border>("PreviewPanel")!.IsVisible);

            // Typing the package path in the text box must reach the view model and refresh the version list.
            view.GetLogicalDescendants().OfType<TextBox>()
                .Single(b => AutomationName(b) == "OptiScaler package folder").Text = package;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(package, vm.PackagePath);
            var versionBox = view.GetLogicalDescendants().OfType<ComboBox>()
                .Single(b => AutomationName(b) == "OptiScaler version");
            Assert.Equal(VersionAction.UseCurrent, (versionBox.SelectedItem as VersionChoice)?.Action);

            await vm.PreviewInstallCommand.ExecuteAsync(null);
            Dispatcher.UIThread.RunJobs();
            Assert.True(view.FindControl<Border>("PreviewPanel")!.IsVisible, vm.Status);

            await vm.ApplyCommand.ExecuteAsync(null);
            Dispatcher.UIThread.RunJobs();
            Assert.False(view.FindControl<Border>("PreviewPanel")!.IsVisible);
            Assert.Contains(Texts(view), t => t == "Managed files verified");
            window.Close();

            return true;
        }, TestContext.Current.CancellationToken);
    }

    private static string? AutomationName(Control control)
    {
        return Avalonia.Automation.AutomationProperties.GetName(control);
    }

    private static IEnumerable<string> Texts(Control root)
    {
        Dispatcher.UIThread.RunJobs();

        return root.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text ?? "");
    }

    private static async Task WaitForIdle(ManageGameViewModel vm)
    {
        for (var i = 0; i < 200 && (vm.IsBusy || vm.Status.StartsWith("Installation selection")); i++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }
    }

    private static class HeadlessApp
    {
        public static AppBuilder BuildAvaloniaApp()
        {
            return AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
        }
    }
}
