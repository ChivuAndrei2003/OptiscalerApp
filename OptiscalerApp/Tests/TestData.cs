using Microsoft.Extensions.DependencyInjection;
using OptiscalerApp.DependencyInjection;
using OptiscalerApp.Management;
using OptiscalerApp.Models;
using OptiscalerApp.Paths;
using OptiscalerApp.Persistence;
using OptiscalerApp.ViewModels;
using Xunit;

namespace Optiscaler.Tests;

/// <summary>Gives every test its own folder and removes it afterwards.</summary>
public abstract class TempFolderTest : IDisposable
{
    // On macOS under /private/tmp, so paths do not change when links are resolved.
    protected string Root { get; } = Directory.CreateDirectory(Path.Combine(
        OperatingSystem.IsMacOS() ? "/private/tmp" : Path.GetTempPath(),
        "Optiscaler-tests-" + Guid.NewGuid().ToString("N"))).FullName;

    protected static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() { Directory.Delete(Root, true); }

    /// <summary>Creates a folder below the test's root and returns its full path.</summary>
    protected string Folder(params string[] parts)
    {
        return Directory.CreateDirectory(Path.Combine([Root, ..parts])).FullName;
    }
}

/// <summary>Fixtures shared by the tests.</summary>
internal static class TestData
{
    public static readonly GpuInfo Radeon = new("AMD Radeon RX 6800", GpuVendor.AMD, 0x1002, 0x73BF, 16UL << 30);

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

    /// <summary>A library record for the game installed in <paramref name="folder" />.</summary>
    public static GameRecord Game(string folder, string name = "Test game",
                                  GamePlatform platform = GamePlatform.Manual, string? externalId = null,
                                  string? executable = null)
    {
        return new GameRecord
        {
            Id = GameId.Create(platform, externalId, folder),
            Name = name,
            Platform = platform,
            ExternalId = externalId,
            Installations = [new GameInstallation { RootPath = folder, PrimaryExecutablePath = executable }]
        };
    }

    /// <summary>The smallest file the installer accepts as a 64-bit executable or DLL.</summary>
    public static void WritePe(string path, bool dll)
    {
        var bytes = new byte[256];
        bytes[0] = 0x4d;
        bytes[1] = 0x5a;
        BitConverter.GetBytes(128).CopyTo(bytes, 0x3c);
        bytes[128] = 0x50;
        bytes[129] = 0x45;
        bytes[132] = 0x64;
        bytes[133] = 0x86;
        BitConverter.GetBytes((ushort)(dll ? 0x2002 : 0x0002)).CopyTo(bytes, 150);
        File.WriteAllBytes(path, bytes);
    }

    /// <summary>A local OptiScaler package: the DLL and its default INI.</summary>
    public static string WritePackage(string folder, string ini)
    {
        Directory.CreateDirectory(folder);
        WritePe(Path.Combine(folder, "OptiScaler.dll"), true);
        File.WriteAllText(Path.Combine(folder, "OptiScaler.ini"), ini);

        return folder;
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

    /// <summary>Saves the games as the library and returns its loaded page.</summary>
    public static async Task<GamesViewModel> LoadLibraryAsync(this ServiceProvider provider, params GameRecord[] games)
    {
        var ct = TestContext.Current.CancellationToken;
        if (games.Length > 0)
            await provider.GetRequiredService<IGameCatalogRepository>()
                .SaveGameCatalogAsync(new GameCatalog { Games = games.ToList() }, ct);
        var vm = provider.GetRequiredService<GamesViewModel>();
        await vm.LoadGameLibraryAsync(ct);

        return vm;
    }

    /// <summary>The installer over a private data folder; it is offline unless a handler answers.</summary>
    public static GameInstallationService Installer(string dataRoot, HttpMessageHandler? http = null)
    {
        var paths = new AppPaths(dataRoot);

        return new GameInstallationService(paths, new PackageDownloadService(paths,
                                               new HttpClient(http ?? StubHttpHandler.Offline())));
    }

    /// <summary>The Manage Game page for one game over a private data folder, as the library opens it.</summary>
    public static ManageGameViewModel ManageGame(GameRecord game, string dataRoot, HttpMessageHandler? http = null,
                                                 IFileDialogs? dialogs = null, IShellActions? shell = null,
                                                 IReadOnlyList<GpuInfo>? gpus = null)
    {
        var paths = new AppPaths(dataRoot);
        var client = new HttpClient(http ?? StubHttpHandler.Offline());
        var packages = new PackageDownloadService(paths, client);

        return new ManageGameViewModel(game, new GameAnalyzer(), new GameInstallationService(paths, packages),
                                       packages, new JsonProfileRepository(paths), (_, name, _, _) =>
                                       {
                                           var saved = game.Clone();
                                           saved.Name = name;

                                           return Task.FromResult(saved);
                                       }, new CompatibilityListService(paths, client),
                                       () => Task.FromResult(gpus ?? []))
        {
            Dialogs = dialogs, Shell = shell
        };
    }

    /// <summary>Previews a package install with its bundled components only, as most tests need.</summary>
    public static Task<InstallPlan> PreviewInstallationAsync(this IGameInstallationService service,
                                                              string executablePath, string packageDirectory,
                                                              string proxyName, RenderProfile? profile,
                                                              CancellationToken cancellationToken = default,
                                                              bool keepCurrentSettings = false)
    {
        return service.PreviewPackageInstallationAsync(executablePath, packageDirectory, proxyName, profile, [],
                                                        null, cancellationToken, keepCurrentSettings);
    }
}

/// <summary>Pickers that answer with fixed paths; null stands for a cancelled dialog.</summary>
internal sealed class FakeDialogs(string? folder = null, string? file = null) : IFileDialogs
{
    public Task<string?> PickFileAsync(string title, string pattern) { return Task.FromResult(file); }

    public Task<string?> PickFolderAsync(string title) { return Task.FromResult(folder); }

    public Task<IReadOnlyList<string>> PickFoldersAsync(string title)
    {
        return Task.FromResult<IReadOnlyList<string>>(folder is null ? [] : [folder]);
    }

    public Task<string?> PickSaveFileAsync(string title, string suggestedName, string pattern)
    {
        return Task.FromResult(file);
    }
}
