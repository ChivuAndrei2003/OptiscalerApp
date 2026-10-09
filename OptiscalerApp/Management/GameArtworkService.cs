using System.Security;
using System.Security.Cryptography;
using System.Text;
using OptiscalerApp.Models;
using OptiscalerApp.Paths;
using OptiscalerApp.Scanning;

namespace OptiscalerApp.Management;

/// <summary>
///     Finds cover images without any network access: the art Steam already downloaded for its library, an image in
///     the game folder, or else a cached executable icon.
/// </summary>
/// <param name="steamRoots">Steam installations to read cover art from; located automatically when null.</param>
public sealed class GameArtworkService(IAppPaths paths, IReadOnlyList<string>? steamRoots = null)
{
    private static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".webp", ".ico"];
    private static readonly string[] ImageNames = ["cover", "poster", "folder", "icon", "game", "header"];

    // The portrait capsule, under the name current Steam clients use and the one older clients used.
    private static readonly string[] SteamCoverNames = ["library_capsule.jpg", "library_600x900.jpg"];

    private readonly Lazy<IReadOnlyList<string>> _steamRoots = new(() => steamRoots ?? SteamScanner.FindInstallRoots());

    public Task<bool> PopulateArtworkAsync(IEnumerable<GameRecord> games,
                                            CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            var changed = false;

            foreach (var game in games)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Steam's art also replaces an executable icon cached before Steam had downloaded it.
                if (game.Platform == GamePlatform.Steam && FindSteamCover(game.ExternalId) is { } steamCover)
                {
                    changed |= game.CoverImage != steamCover;
                    game.CoverImage = steamCover;

                    continue;
                }

                if (File.Exists(game.CoverImage)) continue;

                foreach (var installation in game.Installations)
                {
                    var cover = FindArtwork(installation);

                    if (cover is null) continue;

                    game.CoverImage = cover;
                    changed = true;

                    break;
                }
            }

            return changed;
        }, cancellationToken);
    }

    private string? FindArtwork(GameInstallation installation)
    {
        try
        {
            var root = PathUtil.Normalize(installation.RootPath);

            if (!Directory.Exists(root)) return null;

            var cover = FindCoverImage(root);

            if (cover is not null) return PathUtil.Normalize(cover);

            var executable = installation.PrimaryExecutablePath;

            if (string.IsNullOrWhiteSpace(executable))
            {
                // Only an unambiguous executable is used: the one in the root that is not a crash reporter,
                // uninstaller or similar helper.
                var executables = Directory.EnumerateFiles(root)
                    .Where(file => Path.GetExtension(file).Equals(".exe", StringComparison.OrdinalIgnoreCase) &&
                                   !ExecutableResolver.IsHelper(file))
                    .Take(2).ToList();
                if (executables.Count == 1) executable = executables[0];
            }

            if (executable is null || !File.Exists(executable) || ExecutableIconReader.Read(executable) is not { } icon)
                return null;

            Directory.CreateDirectory(paths.CoversDirectory);
            var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(executable)));
            var destination = Path.Combine(paths.CoversDirectory, key + ".ico");
            File.WriteAllBytes(destination, icon);

            return destination;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                                       or NotSupportedException or SecurityException)
        {
            // Missing or unreadable artwork must not prevent adding the game.
            return null;
        }
    }

    /// <summary>The library capsule Steam keeps in appcache/librarycache for every game shown in its library.</summary>
    private string? FindSteamCover(string? appId)
    {
        if (string.IsNullOrEmpty(appId) || !appId.All(char.IsAsciiDigit)) return null;

        foreach (var root in _steamRoots.Value)
            try
            {
                var cache = Path.Combine(root, "appcache", "librarycache");

                // Older clients kept every image in one folder, prefixed with the app ID.
                var flat = Path.Combine(cache, $"{appId}_library_600x900.jpg");

                if (File.Exists(flat)) return flat;

                var folder = Path.Combine(cache, appId);

                if (!Directory.Exists(folder)) continue;

                // Current clients keep each image in its own content-hash subfolder.
                var images = Directory.EnumerateFiles(folder, "library_*.jpg", SearchOption.AllDirectories).ToList();
                var cover = SteamCoverNames
                    .Select(name => images.FirstOrDefault(image => Path.GetFileName(image).Equals(name,
                                                              StringComparison.OrdinalIgnoreCase)))
                    .FirstOrDefault(image => image is not null);

                if (cover is not null) return cover;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                // Steam may be rewriting its cache; the game still gets an icon or no cover.
            }

        return null;
    }

    private static string? FindCoverImage(string root)
    {
        var folders = new[]
        {
            root, Path.Combine(root, "assets"), Path.Combine(root, "images"), Path.Combine(root, "artwork")
        };

        return folders.Where(Directory.Exists)
            .SelectMany(folder => Directory.EnumerateFiles(folder))
            .Where(file => ImageExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
            .Where(file => ImageNames.Contains(Path.GetFileNameWithoutExtension(file),
                                               StringComparer.OrdinalIgnoreCase))
            .OrderByDescending(file => Path.GetFileNameWithoutExtension(file)
                                   .Equals("cover", StringComparison.OrdinalIgnoreCase))
            .ThenBy(file => file, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }
}