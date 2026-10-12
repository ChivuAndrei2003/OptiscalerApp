using OptiscalerApp.Models;
using OptiscalerApp.Paths;
using OptiscalerApp.Persistence;
using SkiaSharp;

namespace OptiscalerApp.Development;

/// <summary>
///     Creates synthetic files for UI testing; none are executable game or rendering binaries. The workspace's
///     <c>Package</c> folder can be chosen as the local OptiScaler package.
/// </summary>
public static class DemoWorkspace
{
    private const string DemoGame = "Demo game";

    // Raised whenever the workspace's content changes, so one made by an earlier build is created again.
    private const string Version = "3";

    /// <summary>
    ///     A library large enough to show the card grid: each game with its Steam app, whose portrait art becomes the
    ///     cover, and the upscaler libraries found in its folder. The demo game has no cover, to show the placeholder.
    /// </summary>
    private static readonly (string Name, int? SteamApp, string[] Libraries)[] Games =
    [
        (DemoGame, null, ["nvngx_dlss.dll", "libxess.dll"]),
        ("Baldur's Gate 3", 1086940, ["nvngx_dlss.dll", "amd_fidelityfx_dx12.dll"]),
        ("Cyberpunk 2077", 1091500, ["nvngx_dlss.dll", "nvngx_dlssg.dll", "libxess.dll"]),
        ("DOOM Eternal", 782330, ["nvngx_dlss.dll"]),
        ("ELDEN RING", 1245620, []),
        ("Forza Horizon 5", 1551360, ["nvngx_dlss.dll", "amd_fidelityfx_dx12.dll"]),
        ("Ghost of Tsushima DIRECTOR'S CUT", 2215430, ["nvngx_dlss.dll", "libxess.dll"]),
        ("Hades II", 1145350, []),
        ("Hogwarts Legacy", 990080, ["nvngx_dlss.dll", "libxess.dll"]),
        ("Red Dead Redemption 2", 1174180, ["nvngx_dlss.dll"]),
        ("Starfield", 1716740, ["nvngx_dlss.dll", "nvngx_dlssg.dll"]),
        ("Stray", 1332010, [])
    ];

    /// <summary>Creates the workspace in the temp folder once and returns its data folder.</summary>
    public static string Prepare()
    {
        var root = Path.Combine(Path.GetTempPath(), "Optiscaler-ui-demo");
        var stamp = Path.Combine(root, "version");

        if (!File.Exists(stamp) || File.ReadAllText(stamp) != Version)
        {
            using var steam = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            CreateAsync(root, steam).GetAwaiter().GetResult();
            File.WriteAllText(stamp, Version);
        }

        return Path.Combine(root, "data");
    }

    /// <param name="steam">Downloads the covers from Steam; without it, or offline, they are drawn instead.</param>
    public static async Task CreateAsync(string root, HttpClient? steam = null,
                                         CancellationToken cancellationToken = default)
    {
        var package = Directory.CreateDirectory(Path.Combine(root, "Package")).FullName;
        var paths = new AppPaths(Path.Combine(root, "data"));
        var games = new List<GameRecord>();

        foreach (var (name, steamApp, libraries) in Games)
        {
            var folder = Directory.CreateDirectory(Path.Combine(root, name)).FullName;
            var executable = Path.Combine(folder, name == DemoGame ? "DemoGame.exe" : "Game.exe");
            WritePeFixture(executable, false);
            foreach (var library in libraries) WritePeFixture(Path.Combine(folder, library), true);

            var cover = steamApp is { } app
                ? await SaveCoverAsync(folder, name, app, games.Count * 360f / Games.Length, steam, cancellationToken)
                : null;

            games.Add(new GameRecord
            {
                Id = GameId.Create(GamePlatform.Manual, null, folder),
                Name = name,
                Platform = GamePlatform.Manual,
                CoverImage = cover,
                IsFavorite = name == "Cyberpunk 2077",
                Installations = [new GameInstallation { RootPath = folder, PrimaryExecutablePath = executable }]
            });
        }

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
        await new JsonGameCatalogRepository(paths).SaveGameCatalogAsync(new GameCatalog { Games = games },
                                                                         cancellationToken);
        var profile = new RenderProfile
        {
            Name = "Demo balanced",
            Settings = new Dictionary<string, string>
            {
                ["Upscalers.Dx12Upscaler"] = "xess", ["Sharpness.OverrideSharpness"] = "true",
                ["Sharpness.Sharpness"] = "0.3"
            }
        };
        await new JsonProfileRepository(paths).SaveProfileCatalogAsync(new ProfileCatalog
        {
            Profiles = [profile],
            DefaultProfileId = profile.Id
        }, cancellationToken);
    }

    /// <summary>
    ///     Saves the portrait art Steam's library shows for the game. Nothing is shipped with the app: the image is
    ///     fetched into the temp folder, and a cover drawn here takes its place when that fails.
    /// </summary>
    private static async Task<string> SaveCoverAsync(string folder, string title, int steamApp, float hue,
                                                     HttpClient? steam, CancellationToken cancellationToken)
    {
        if (steam is not null)
            try
            {
                var art = await steam.GetByteArrayAsync(
                    $"https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/{steamApp}/library_600x900.jpg",
                    cancellationToken);
                var downloaded = Path.Combine(folder, "cover.jpg");
                await File.WriteAllBytesAsync(downloaded, art, cancellationToken);

                return downloaded;
            }
            catch (Exception ex) when (ex is HttpRequestException ||
                                       (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
            {
                // Offline or timed out.
            }

        var drawn = Path.Combine(folder, "cover.png");
        WriteCover(drawn, title, hue);

        return drawn;
    }

    /// <summary>Draws a portrait cover in Steam's 2:3 shape: a gradient in the game's own hue with its title.</summary>
    private static void WriteCover(string path, string title, float hue)
    {
        const int width = 300, height = 450;
        using var surface = SKSurface.Create(new SKImageInfo(width, height));
        using var background = new SKPaint();
        background.Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(width, height),
                                                          [SKColor.FromHsl(hue, 65, 42), SKColor.FromHsl(hue + 40, 70, 12)],
                                                          SKShaderTileMode.Clamp);
        surface.Canvas.DrawRect(0, 0, width, height, background);

        using var font = new SKFont(SKTypeface.Default, 34) { Embolden = true };
        using var text = new SKPaint { Color = SKColors.White, IsAntialias = true };
        var words = title.Split(' ');

        // One word per line, centered as a block.
        for (var i = 0; i < words.Length; i++)
            surface.Canvas.DrawText(words[i], width / 2f, height / 2f + (i - (words.Length - 1) / 2f) * 42 + 12,
                                    SKTextAlign.Center, font, text);

        using var image = surface.Snapshot();
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        using var file = File.Create(path);
        png.SaveTo(file);
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