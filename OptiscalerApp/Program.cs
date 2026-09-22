using Avalonia;

namespace OptiscalerApp;

internal sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    
    public static void Main(string[] args)
    {
        var demo = args.Contains("--demo");
#if DEBUG
        demo = !args.Contains("--no-demo");
#endif
        if (demo)
        {
            var root = Path.Combine(OperatingSystem.IsMacOS() ? "/private/tmp" : Path.GetTempPath(),
                                    "Optiscaler-ui-demo");
            if (!File.Exists(Path.Combine(root, "data", "games.json")))
                Development.DemoWorkspace.Create_Async(root).GetAwaiter().GetResult();
            Development.DemoWorkspace.ActiveRoot = root;
            Environment.SetEnvironmentVariable("OPTISCALER_DATA_DIRECTORY", Path.Combine(root, "data"));
        }

        BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);

            
    }




    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
            
     
           
     
}