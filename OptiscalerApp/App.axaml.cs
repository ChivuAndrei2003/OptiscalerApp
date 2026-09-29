using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using OptiscalerApp.DependencyInjection;
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

            desktop.Exit += (_, _) =>
            {
                _serviceProvider.Dispose();
                _serviceProvider = null;
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
