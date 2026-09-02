using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Optiscaler.Infrastructure.DependencyInjection;
using OptiscalerApp.ViewModels;
using OptiscalerApp.Views;

namespace OptiscalerApp;

public class App : Application
{
    private ServiceProvider? _serviceProvider;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)

        {
            ServiceCollection services;
            services = new ServiceCollection();

            //
            services.AddOptiscalerInfrastructure();

            services.AddSingleton<MainWindowViewModel>();

            _serviceProvider = services.BuildServiceProvider(
                new ServiceProviderOptions
                {
                    ValidateOnBuild = true,
                    ValidateScopes = true
                });

            desktop.MainWindow = new MainWindow
            {
                DataContext = _serviceProvider.GetRequiredService<MainWindowViewModel>()
            };

            desktop.Exit += (_, _) =>
            {
                _serviceProvider.Dispose();
                _serviceProvider = null;
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}