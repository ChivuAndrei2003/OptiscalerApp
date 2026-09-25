using Microsoft.Extensions.DependencyInjection;
using OptiscalerApp.DependencyInjection;
using OptiscalerApp.Models;
using OptiscalerApp.Paths;
using OptiscalerApp.ViewModels;

namespace Optiscaler.Tests;

/// <summary>Fixtures shared by the feature tests.</summary>
internal static class TestData
{
    public static readonly GpuInfo Radeon = new("AMD Radeon RX 6800", GpuVendor.AMD, 0x1002, 0x73BF, 16UL << 30);

    /// <summary>A unique folder; on macOS under /private/tmp so paths do not change when links are resolved.</summary>
    public static string TempRoot(string prefix)
    {
        return Path.Combine(OperatingSystem.IsMacOS() ? "/private/tmp" : Path.GetTempPath(),
                            prefix + Guid.NewGuid().ToString("N"));
    }

    /// <summary>The wiki's main compatibility table with the given rows.</summary>
    public static string WikiTable(params string[] rows)
    {
        return "| Game | Compatibility | Upscaler <br>Inputs | OptiPatcher <br>Support | Notes | Images |\n" +
               "| ---- | :---: | :---: | :---: | ----- | :---: |\n" + string.Join("\n", rows);
    }

    /// <summary>The app's services over a private data folder, with every HTTP request answered locally.</summary>
    public static ServiceProvider LibraryServices(string dataRoot, HttpMessageHandler? http = null)
    {
        return new ServiceCollection().AddOptiscalerServices()
            .AddSingleton<IAppPaths>(new AppPaths(dataRoot))
            .AddSingleton(new HttpClient(http ?? StubHttpHandler.Offline()))
            .AddSingleton<ProfilesViewModel>().AddSingleton<MainWindowViewModel>().BuildServiceProvider();
    }
}
