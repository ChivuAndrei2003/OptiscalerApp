using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using OptiscalerApp.DependencyInjection;
using OptiscalerApp.Persistence;
using OptiscalerApp.ViewModels;
using OptiscalerApp.Views;

namespace OptiscalerApp;

public class App : Application
{
    private ServiceProvider? _serviceProvider;

    public override void Initialize() { AvaloniaXamlLoader.Load(this); }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _serviceProvider = new ServiceCollection().AddOptiscalerServices().AddOptiscalerViewModels()
                .BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

            var window = new MainWindow { DataContext = _serviceProvider.GetRequiredService<MainWindowViewModel>() };
            _serviceProvider.GetRequiredService<TopLevelServices>().Owner = window;
            desktop.MainWindow = window;
            var shortcut = _serviceProvider.GetRequiredService<ControllerShortcut>();

            // SDL can take a while to list the input devices, so the window is shown first.
            Dispatcher.UIThread.Post(() => GamepadNavigation.Attach(window, shortcut), DispatcherPriority.Background);
            _ = LoadShortcutAsync(shortcut, _serviceProvider.GetRequiredService<IAppConfigurationRepository>());

            desktop.Exit += (_, _) =>
            {
                _serviceProvider.Dispose();
                _serviceProvider = null;
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>The saved controller shortcut works from startup, without a visit to Settings.</summary>
    private static async Task LoadShortcutAsync(ControllerShortcut shortcut, IAppConfigurationRepository repository)
    {
        try
        {
            shortcut.Buttons = (await repository.LoadAppConfigurationAsync()).MenuButtons;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            // Settings reports unreadable configuration when it is opened.
        }
    }
}
