using Microsoft.Extensions.DependencyInjection;
using OptiscalerApp.DependencyInjection;
using OptiscalerApp.Management;
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

    /// <summary>A profile overriding the given <c>Section.Key</c> settings.</summary>
    public static RenderProfile Profile(string name, params (string Id, string Value)[] settings)
    {
        return new RenderProfile { Name = name, Settings = settings.ToDictionary(s => s.Id, s => s.Value) };
    }

    /// <summary>The app's services and pages over a private data folder, with every HTTP request answered locally.</summary>
    public static ServiceProvider LibraryServices(string dataRoot, HttpMessageHandler? http = null,
                                                  Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection().AddOptiscalerServices().AddOptiscalerViewModels()
            .AddSingleton<IAppPaths>(new AppPaths(dataRoot))
            .AddSingleton(new HttpClient(http ?? StubHttpHandler.Offline()));
        configure?.Invoke(services);

        return services.BuildServiceProvider();
    }
}

/// <summary>Pickers that answer with fixed paths; null stands for a cancelled dialog.</summary>
internal sealed class FakeDialogs(string? folder = null, string? file = null) : IFileDialogs
{
    public Task<string?> PickFile_Async(string title, string pattern) { return Task.FromResult(file); }

    public Task<string?> PickFolder_Async(string title) { return Task.FromResult(folder); }

    public Task<IReadOnlyList<string>> PickFolders_Async(string title)
    {
        return Task.FromResult<IReadOnlyList<string>>(folder is null ? [] : [folder]);
    }

    public Task<string?> PickSaveFile_Async(string title, string suggestedName, string pattern)
    {
        return Task.FromResult(file);
    }
}
