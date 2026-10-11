using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using OptiscalerApp;
using OptiscalerApp.Models;
using OptiscalerApp.ViewModels;
using OptiscalerApp.Views;
using Xunit;

namespace Optiscaler.Tests;

/// <summary>Renders the real XAML headlessly, so broken bindings fail here instead of at runtime.</summary>
public sealed class ViewTests : TempFolderTest
{
    // One session for the whole run: Avalonia can be initialized only once per process, and disposing the
    // session blocks on its dispatcher thread.
    private static readonly Lazy<HeadlessUnitTestSession> Session =
        new(() => HeadlessUnitTestSession.StartNew(typeof(HeadlessApp)));

    private string DataRoot => Path.Combine(Root, "data");
    private string Package => Path.Combine(Root, "package");

    private static Task OnUiThread(Func<Task> test)
    {
        return Session.Value.Dispatch(async () =>
        {
            await test();

            return true;
        }, Ct);
    }

    private static Task OnUiThread(Action test) { return Session.Value.Dispatch(test, Ct); }

    private static Window Show(Control content)
    {
        var window = content as Window ?? new Window { Content = content };
        window.Width = 1280;
        window.Height = 900;
        window.Show();
        Dispatcher.UIThread.RunJobs();

        return window;
    }

    private static IEnumerable<string> Texts(Control root)
    {
        Dispatcher.UIThread.RunJobs();

        return root.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text ?? "");
    }

    private static T Named<T>(Control root, string automationName) where T : Control
    {
        return root.GetLogicalDescendants().OfType<T>().Single(c => AutomationProperties.GetName(c) == automationName);
    }

    /// <summary>The page for a game that ships XeSS, with a local package ready to install.</summary>
    private ManageGameViewModel ManageGame()
    {
        var game = Folder("game");
        var exe = Path.Combine(game, "game.exe");
        TestData.WritePe(exe, false);
        TestData.WritePe(Path.Combine(game, "libxess.dll"), true);
        TestData.WritePackage(Package, "[Upscalers]\n");

        return TestData.ManageGame(TestData.Game(game, "Headless game", executable: exe), DataRoot);
    }

    [Fact]
    public async Task ManageGameViewFollowsAnalysisPreviewAndApply()
    {
        var vm = ManageGame();

        await OnUiThread(async () =>
        {
            var view = new ManageGameView { DataContext = vm };
            var window = Show(view);
            await vm.LoadCommand.ExecuteAsync(null);
            var preview = view.FindControl<Border>("PreviewPanel")!;

            Assert.Contains("Headless game", Texts(view));
            Assert.Contains(Texts(view), t => t.StartsWith("Intel XeSS"));
            Assert.Contains(Texts(view), t => t.StartsWith("• Injection: dxgi.dll"));
            Assert.False(preview.IsVisible);

            // Every "?" explains something when hovered.
            var helpIcons = view.GetVisualDescendants().OfType<HelpIcon>().ToList();
            Assert.Contains(helpIcons, icon => ToolTip.GetTip(icon) as string == HelpText.Injection);
            Assert.All(helpIcons, icon => Assert.False(string.IsNullOrEmpty(ToolTip.GetTip(icon) as string)));

            // Channels cannot be switched in the middle of another operation, and look disabled meanwhile.
            var beta = view.GetLogicalDescendants().OfType<Button>().Single(b => b.Content as string == "Beta");
            Assert.True(beta.IsEffectivelyEnabled);
            vm.IsBusy = true;
            Dispatcher.UIThread.RunJobs();
            Assert.False(beta.IsEffectivelyEnabled);
            vm.IsBusy = false;

            vm.Package.PackagePath = Package;
            await vm.PreviewInstallCommand.ExecuteAsync(null);
            Dispatcher.UIThread.RunJobs();
            Assert.True(preview.IsVisible, vm.Status);

            await vm.ApplyCommand.ExecuteAsync(null);
            Assert.Contains("Managed files verified", Texts(view));
            Assert.False(preview.IsVisible);
            window.Close();
        });
    }

    [Fact]
    public async Task ATypedPackagePathIsReadOnceTheBoxLosesFocus()
    {
        var vm = ManageGame();

        await OnUiThread(async () =>
        {
            var view = new ManageGameView { DataContext = vm };
            var window = Show(view);
            view.FindControl<Expander>("LocalFilesPanel")!.IsExpanded = true;
            Dispatcher.UIThread.RunJobs();
            var packageBox = Named<TextBox>(view, "OptiScaler package folder");
            var versionBox = Named<ComboBox>(view, "OptiScaler version");

            // Not on every keystroke, because each change rescans the folder.
            Assert.True(packageBox.Focus());
            packageBox.Text = Package;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("", vm.Package.PackagePath);

            Assert.True(versionBox.Focus());
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(Package, vm.Package.PackagePath);

            // The folder is read off the UI thread.
            for (var i = 0; i < 100 && versionBox.SelectedItem is null; i++)
            {
                await Task.Delay(10, Ct);
                Dispatcher.UIThread.RunJobs();
            }

            Assert.Equal(VersionAction.UseCurrent, (versionBox.SelectedItem as VersionChoice)?.Action);
            window.Close();
        });
    }

    [Fact]
    public async Task NavigationShowsEachPageWithItsView()
    {
        using var provider = TestData.LibraryServices(DataRoot);
        var shell = provider.GetRequiredService<MainWindowViewModel>();

        await OnUiThread(async () =>
        {
            var window = Show(new MainWindow { DataContext = shell });
            Assert.Single(window.GetLogicalDescendants().OfType<GamesView>());
            Assert.True(shell.IsGamesPage);

            await shell.ShowProfilesCommand.ExecuteAsync(null);
            shell.Profiles.NewProfileCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            var editor = Assert.Single(window.GetLogicalDescendants().OfType<ProfileEditorView>());
            Assert.True(shell.IsProfilesPage);
            Assert.Contains("DX12 upscaler", Texts(editor));

            await shell.ShowSettingsCommand.ExecuteAsync(null);
            Dispatcher.UIThread.RunJobs();
            Assert.Single(window.GetLogicalDescendants().OfType<SettingsView>());
            Assert.True(shell.Settings.IsLoaded);

            shell.ShowHelpCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            var help = Assert.Single(window.GetLogicalDescendants().OfType<HelpView>());
            Assert.True(shell.IsHelpPage);
            Assert.Contains(HelpText.Injection, Texts(help));
            window.Close();
        });
    }

    [Fact]
    public async Task LibraryCardsShowStatusAndOpenTheirMenu()
    {
        var game = TestData.Game(Folder("Library game"), "Library game");
        game.IsFavorite = true;
        using var provider = TestData.LibraryServices(DataRoot);
        var vm = await provider.LoadLibraryAsync(game);

        await OnUiThread(() =>
        {
            var view = new GamesView { DataContext = vm };
            var window = Show(view);

            Assert.Contains("Library game", Texts(view));
            Assert.True(Named<Border>(view, "Favorite").IsVisible);
            Named<Button>(view, "More actions").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            window.Close();
        });
    }

    [Fact]
    public async Task SearchingKeepsTheCardsThatStillMatchInPlace()
    {
        using var provider = TestData.LibraryServices(DataRoot);
        var vm = await provider.LoadLibraryAsync(TestData.Game(Folder("Alpha"), "Alpha"),
                                                 TestData.Game(Folder("Beta"), "Beta"),
                                                 TestData.Game(Folder("Alpine"), "Alpine"));

        await OnUiThread(() =>
        {
            var view = new GamesView { DataContext = vm };
            var window = Show(view);

            List<GameCoverImage> Covers() { return view.GetVisualDescendants().OfType<GameCoverImage>().ToList(); }
            string Name(GameCoverImage cover) { return ((GameCardViewModel)cover.DataContext!).Name; }

            var alpha = Covers().Single(c => Name(c) == "Alpha");

            vm.SearchText = "al";
            Dispatcher.UIThread.RunJobs();

            // Only the card that no longer matches leaves; the others keep their controls, so no cover reloads.
            Assert.Equal(["Alpha", "Alpine"], Covers().Select(Name));
            Assert.Same(alpha, Covers()[0]);

            vm.SearchText = "";
            vm.SortDescending = true;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(["Beta", "Alpine", "Alpha"], Covers().Select(Name));
            window.Close();
        });
    }

    [Fact]
    public async Task SavingAProfileRevealsAndFocusesAnInvalidOptionHiddenBySearch()
    {
        var saved = false;
        var editor = new ProfileEditorViewModel(new RenderProfile { Name = "Invalid" }, true, _ =>
        {
            saved = true;

            return Task.FromResult(true);
        });

        await OnUiThread(async () =>
        {
            var view = new ProfileEditorView { DataContext = editor };
            var window = Show(view);
            var multiplier = editor.Groups.SelectMany(g => g.Fields)
                .Single(f => f.Setting.Id == "OutputScaling.Multiplier");
            multiplier.Text = "0,3";
            editor.SearchText = "spoof";
            Dispatcher.UIThread.RunJobs();
            Assert.False(multiplier.IsVisible);

            await editor.SaveCommand.ExecuteAsync(null);
            Dispatcher.UIThread.RunJobs();

            Assert.False(saved);
            Assert.Equal("", editor.SearchText);
            Assert.Contains("Output scaling multiplier", editor.ValidationText);
            Assert.True(view.GetVisualDescendants().OfType<TextBox>().Single(t => t.DataContext == multiplier).IsFocused);
            window.Close();
        });
    }

    [Fact]
    public async Task AccentControlsKeepTextReadableAndExpandersKeepTheirTheme()
    {
        await OnUiThread(() =>
        {
            var idle = new Button { Content = "Apply changes", Classes = { "primary" } };
            var hovered = new Button { Content = "Apply changes", Classes = { "primary" } };
            var segment = new ToggleButton { Content = "Descending", Classes = { "segment" }, IsChecked = true };
            var expander = new Expander { Header = "Analysis & activity", IsExpanded = true };
            var window = Show(new StackPanel { Children = { idle, hovered, segment, expander } });
            ((IPseudoClasses)hovered.Classes).Set(":pointerover", true);
            Dispatcher.UIThread.RunJobs();

            // Fluent's hover and checked states would otherwise put light text on the light accent.
            Assert.NotNull(PresenterForeground(idle));
            Assert.Equal(PresenterForeground(idle), PresenterForeground(hovered));
            Assert.Equal(PresenterForeground(idle), PresenterForeground(segment));

            // The app's toggle styles must not turn an expanded Expander's header into an accent button.
            var header = expander.GetVisualDescendants().OfType<ToggleButton>().Single();
            Assert.True(header.IsChecked);
            Assert.NotNull(segment.Background);
            Assert.NotEqual((segment.Background as ISolidColorBrush)?.Color,
                            (header.Background as ISolidColorBrush)?.Color);
            window.Close();
        });
    }

    private static Color? PresenterForeground(Control control)
    {
        return (control.GetVisualDescendants().OfType<ContentPresenter>().First().Foreground as ISolidColorBrush)
            ?.Color;
    }

    private static class HeadlessApp
    {
        public static AppBuilder BuildAvaloniaApp()
        {
            return AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
        }
    }
}
