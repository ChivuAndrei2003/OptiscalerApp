using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
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
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
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
        using var offline = new HttpClient(StubHttpHandler.Offline());
        var vm = new ManageGameViewModel(record, new GameAnalyzer(), new GameInstallationService(paths, packages),
                                         packages, new JsonProfileRepository(paths),
                                         (_, _, _, _) => Task.FromResult(record),
                                         new CompatibilityListService(paths, offline),
                                         () => Task.FromResult<IReadOnlyList<GpuInfo>>([]));

        await Session.Value.Dispatch(async () =>
        {
            var view = new ManageGameView { DataContext = vm };
            var window = new Window { Width = 1280, Height = 900, Content = view };
            window.Show();
            await vm.LoadCommand.ExecuteAsync(null);

            Assert.Contains(Texts(view), t => t == "Headless game");
            Assert.Contains(Texts(view), t => t.StartsWith("Intel XeSS"));
            Assert.Contains(Texts(view), t => t == "Recommended setup");
            Assert.Contains(Texts(view), t => t.StartsWith("• Injection: dxgi.dll"));
            Assert.False(view.FindControl<Border>("PreviewPanel")!.IsVisible);

            // A typed package path reaches the view model once the box loses focus, not on every keystroke,
            // because each change rescans the folder.
            view.FindControl<Expander>("LocalFilesPanel")!.IsExpanded = true;
            Dispatcher.UIThread.RunJobs();
            var packageBox = view.GetLogicalDescendants().OfType<TextBox>()
                .Single(b => AutomationName(b) == "OptiScaler package folder");
            var versionBox = view.GetLogicalDescendants().OfType<ComboBox>()
                .Single(b => AutomationName(b) == "OptiScaler version");
            Assert.True(packageBox.Focus());
            packageBox.Text = package;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("", vm.Package.PackagePath);
            Assert.True(versionBox.Focus());
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(package, vm.Package.PackagePath);
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

    [Fact]
    public async Task NavigationShowsEachPageWithItsView()
    {
        using var provider = TestData.LibraryServices(Path.Combine(_root, "data"));
        var shell = provider.GetRequiredService<MainWindowViewModel>();

        await Session.Value.Dispatch(async () =>
        {
            var window = new MainWindow { DataContext = shell, Width = 1200, Height = 800 };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.Single(window.GetLogicalDescendants().OfType<GamesView>());
            Assert.True(shell.IsGamesPage);

            await shell.ShowProfilesCommand.ExecuteAsync(null);
            shell.Profiles.NewProfileCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.Single(window.GetLogicalDescendants().OfType<ProfileEditorView>());
            Assert.True(shell.IsProfilesPage);

            await shell.ShowSettingsCommand.ExecuteAsync(null);
            Dispatcher.UIThread.RunJobs();
            Assert.Single(window.GetLogicalDescendants().OfType<SettingsView>());
            Assert.True(shell.Settings.IsLoaded);
            window.Close();

            return true;
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task LibraryCardsShowStatusAndOpenTheirMenu()
    {
        var game = Directory.CreateDirectory(Path.Combine(_root, "Library game")).FullName;
        using var provider = TestData.LibraryServices(Path.Combine(_root, "data"));
        await provider.GetRequiredService<IGameCatalogRepository>().SaveGameCatalogAsync(new GameCatalog
        {
            Games =
            [
                new GameRecord
                {
                    Id = GameId.Create(GamePlatform.Manual, null, game), Name = "Library game",
                    Platform = GamePlatform.Manual, IsFavorite = true,
                    Installations = [new GameInstallation { RootPath = game }]
                }
            ]
        }, TestContext.Current.CancellationToken);
        var vm = provider.GetRequiredService<GamesViewModel>();
        await vm.LoadGameLibraryAsync(TestContext.Current.CancellationToken);

        await Session.Value.Dispatch(() =>
        {
            var view = new GamesView { DataContext = vm };
            var window = new Window { Width = 1200, Height = 800, Content = view };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            Assert.Contains(Texts(view), t => t == "Library game");
            Assert.Contains(view.GetLogicalDescendants().OfType<Border>(),
                            b => AutomationName(b) == "Favorite" && b.IsVisible);
            var more = view.GetLogicalDescendants().OfType<Button>().Single(b => AutomationName(b) == "More actions");
            more.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            window.Close();

            return true;
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task AccentControlsKeepTextReadableAndExpandersKeepTheirTheme()
    {
        await Session.Value.Dispatch(() =>
        {
            var primary = new Button { Content = "Apply changes", Classes = { "primary" } };
            var segment = new ToggleButton { Content = "Descending", Classes = { "segment" }, IsChecked = true };
            var expander = new Expander { Header = "Analysis & activity", IsExpanded = true };
            var window = new Window { Content = new StackPanel { Children = { primary, segment, expander } } };
            window.Show();
            ((IPseudoClasses)primary.Classes).Set(":pointerover", true);
            Dispatcher.UIThread.RunJobs();

            // Fluent's hover and checked states would otherwise put light text on the light accent.
            Assert.Equal(Color.Parse("#0B1410"), PresenterForeground(primary));
            Assert.Equal(Color.Parse("#0B1410"), PresenterForeground(segment));

            // The app's toggle styles must not turn an expanded Expander's header into an accent button.
            var header = expander.GetVisualDescendants().OfType<ToggleButton>().Single();
            Assert.True(header.IsChecked);
            Assert.NotEqual(Color.Parse("#5FD2AD"), (header.Background as ISolidColorBrush)?.Color);
            window.Close();

            return true;
        }, TestContext.Current.CancellationToken);
    }

    private static Color? PresenterForeground(Control control)
    {
        return (control.GetVisualDescendants().OfType<ContentPresenter>().First().Foreground as ISolidColorBrush)
            ?.Color;
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

    private static class HeadlessApp
    {
        public static AppBuilder BuildAvaloniaApp()
        {
            return AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
        }
    }
}
