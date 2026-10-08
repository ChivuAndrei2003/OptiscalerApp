using System.Security;
using System.Security.Cryptography;
using System.Text;
using OptiscalerApp.Models;
using OptiscalerApp.Paths;

namespace OptiscalerApp.Management;

/// <summary>Finds local cover images, falling back to a cached executable icon.</summary>
public sealed class GameArtworkService(IAppPaths paths)
{
    private static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".webp", ".ico"];
    private static readonly string[] ImageNames = ["cover", "poster", "folder", "icon", "game", "header"];

    public Task<bool> PopulateArtworkAsync(IEnumerable<GameRecord> games,
                                            CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            var changed = false;

            foreach (var game in games)
            {
                cancellationToken.ThrowIfCancellationRequested();

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