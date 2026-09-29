using Avalonia;
using OptiscalerApp.Development;

namespace OptiscalerApp;

internal sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        // "--demo" runs against a synthetic library in the temp folder instead of the user's own data.
        if (args.Contains("--demo"))
            Environment.SetEnvironmentVariable("OPTISCALER_DATA_DIRECTORY", DemoWorkspace.Prepare());

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            #if DEBUG
            .WithDeveloperTools()
            #endif
            .WithInterFont()
            .LogToTrace();
    }
}