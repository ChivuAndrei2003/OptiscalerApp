using OptiscalerApp.Models;
using OptiscalerApp.Paths;
using OptiscalerApp.Persistence;

namespace OptiscalerApp.Development;

/// <summary>Creates synthetic files for UI testing; none are executable game or rendering binaries.</summary>
public static class DemoWorkspace
{
    public static string? ActiveRoot { get; set; }

    public static async Task Create_Async(string root, CancellationToken cancellationToken = default)
    {
        var game = Directory.CreateDirectory(Path.Combine(root, "Demo game")).FullName;
        var package = Directory.CreateDirectory(Path.Combine(root, "Package")).FullName;
        var paths = new AppPaths(Path.Combine(root, "data"));
        var executable = Path.Combine(game, "DemoGame.exe");
        WritePeFixture(executable, false);
        WritePeFixture(Path.Combine(game, "nvngx_dlss.dll"), true);
        WritePeFixture(Path.Combine(game, "libxess.dll"), true);
        WritePeFixture(Path.Combine(package, "OptiScaler.dll"), true);

        foreach (var name in new[]
                 {
                     "amd_fidelityfx_upscaler_dx12.dll", "fakenvapi.dll", "OptiPatcher.dll",
                     "dlssg_to_fsr3_amd_is_better.dll"
                 })
            WritePeFixture(Path.Combine(package, name), true);
        await File.WriteAllTextAsync(Path.Combine(package, "OptiScaler.ini"),
                                     "; Synthetic UI demo configuration\n[Upscalers]\nDx11Upscaler=auto\nDx12Upscaler=auto\n",
                                     cancellationToken);
        await new JsonGameCatalogRepository(paths).SaveGameCatalog_Async(new GameCatalog
        {
            Games =
            [
                new GameRecord
                {
                    Id = GameId.Create(GamePlatform.Manual, null, game),
                    Name = "Demo game",
                    Platform = GamePlatform.Manual,
                    Installations = [new GameInstallation { RootPath = game, PrimaryExecutablePath = executable }]
                }
            ]
        }, cancellationToken);
        var profile = new RenderProfile { Name = "Demo balanced", Dx12Upscaler = "xess", Sharpness = 0.3m };
        await new JsonProfileRepository(paths).SaveProfileCatalog_Async(new ProfileCatalog
        {
            Profiles = [profile],
            DefaultProfileId = profile.Id
        }, cancellationToken);
    }

    private static void WritePeFixture(string path, bool dll)
    {
        // Only the header fields required by installation validation are present; these files cannot run.
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
}