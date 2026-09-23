using OptiscalerApp.Paths;

namespace OptiscalerApp.Management;

public sealed record ExecutableCandidate(string Path, int Score, IReadOnlyList<string> Reasons);

/// <summary>
///     Ranks a game's executables the way the OptiScaler install guide describes: OptiScaler belongs next to the real
///     game binary, which for Unreal Engine games is the *-Shipping.exe under &lt;Project&gt;/Binaries/Win64 or
///     WinGDK, never the Engine folder or a launcher stub in the game root.
/// </summary>
public static class ExecutableResolver
{
    private const int MaxDepth = 8;
    private const int MaxFiles = 20000;

    private static readonly string[] ExcludedNameParts =
    [
        "crash", "redist", "setup", "unins", "prereq", "dxsetup", "cefsubprocess", "easyanticheat", "battleye",
        "installer", "updater", "reporter", "dotnet"
    ];

    private static readonly string[] SecondaryNameParts =
        ["launcher", "config", "settings", "editor", "server", "benchmark", "tool"];

    private static readonly string[] ExcludedFolders =
        ["_commonredist", "commonredist", "redist", "directx", "__installer", "easyanticheat", "battleye", ".egstore"];

    private static readonly string[] UpscalerLibraryPrefixes =
        ["nvngx_dlss", "libxess", "amd_fidelityfx", "ffx_fsr", "sl.interposer"];

    public static ExecutableCandidate? FindBest(string root, string gameName,
                                                CancellationToken cancellationToken = default)
    {
        return FindCandidates(root, gameName, cancellationToken).FirstOrDefault();
    }

    public static IReadOnlyList<ExecutableCandidate> FindCandidates(string root, string gameName,
                                                                    CancellationToken cancellationToken = default)
    {
        root = PathUtil.Normalize(root);

        if (!Directory.Exists(root)) return [];

        var candidates = new List<ExecutableCandidate>();
        var pending = new Stack<(string Path, int Depth)>();
        pending.Push((root, 0));
        var visited = 0;

        while (pending.Count > 0 && visited < MaxFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (directory, depth) = pending.Pop();
            string[] files, children;

            try
            {
                files = Directory.GetFiles(directory);
                children = Directory.GetDirectories(directory);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            visited += files.Length;
            var hasUpscalers = files.Any(file => UpscalerLibraryPrefixes.Any(prefix =>
                                                     Path.GetFileName(file)
                                                         .StartsWith(prefix, StringComparison.OrdinalIgnoreCase)));

            foreach (var file in files.Where(f => f.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)))
                if (Score(root, file, gameName, hasUpscalers) is { } candidate)
                    candidates.Add(candidate);

            if (depth >= MaxDepth) continue;

            foreach (var child in children)
            {
                var name = Path.GetFileName(child);

                if (ExcludedFolders.Contains(name, StringComparer.OrdinalIgnoreCase) || IsEngineFolder(child) ||
                    (File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0)
                    continue;

                pending.Push((child, depth + 1));
            }
        }

        return candidates.OrderByDescending(c => c.Score).ThenBy(c => c.Path.Length)
            .ThenBy(c => c.Path, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static ExecutableCandidate? Score(string root, string file, string gameName, bool hasUpscalers)
    {
        var stem = Path.GetFileNameWithoutExtension(file);

        if (ExcludedNameParts.Any(part => stem.Contains(part, StringComparison.OrdinalIgnoreCase))) return null;

        long length;

        try
        {
            // 32-bit and non-PE files cannot host OptiScaler, so they are never suggested.
            SafeFiles.RequireX64PeFile(file, false);
            length = new FileInfo(file).Length;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException
                                       or EndOfStreamException)
        {
            return null;
        }

        var score = 0;
        var reasons = new List<string>();

        if (stem.EndsWith("-Win64-Shipping", StringComparison.OrdinalIgnoreCase) ||
            stem.EndsWith("-WinGDK-Shipping", StringComparison.OrdinalIgnoreCase))
        {
            score += 40;
            reasons.Add("Unreal Engine shipping binary");
        }

        var parent = Path.GetDirectoryName(file)!;

        if (Path.GetFileName(parent) is var platform &&
            (platform.Equals("Win64", StringComparison.OrdinalIgnoreCase) ||
             platform.Equals("WinGDK", StringComparison.OrdinalIgnoreCase)) &&
            string.Equals(Path.GetFileName(Path.GetDirectoryName(parent)), "Binaries",
                          StringComparison.OrdinalIgnoreCase))
        {
            score += 30;
            reasons.Add($"in Binaries/{platform}");
        }

        if (NamesMatch(stem, gameName))
        {
            score += 20;
            reasons.Add("name matches the game");
        }

        if (hasUpscalers)
        {
            score += 25;
            reasons.Add("upscaler libraries next to it");
        }

        if (length >= 20L * 1024 * 1024)
        {
            score += 10;
            reasons.Add("large executable");
        }
        else if (length >= 5L * 1024 * 1024)
        {
            score += 5;
        }

        if (SecondaryNameParts.Any(part => stem.Contains(part, StringComparison.OrdinalIgnoreCase)))
        {
            score -= 15;
            reasons.Add("looks like a launcher or tool");
        }

        // Prefer shallower files when nothing else distinguishes them.
        score -= Path.GetRelativePath(root, file).Count(c => c == Path.DirectorySeparatorChar);

        return new ExecutableCandidate(file, score, reasons);
    }

    private static bool IsEngineFolder(string directory)
    {
        // Unreal's shared Engine/Binaries only holds tools and crash reporters.
        return Path.GetFileName(directory).Equals("Engine", StringComparison.OrdinalIgnoreCase) &&
               Directory.Exists(Path.Combine(directory, "Binaries"));
    }

    private static bool NamesMatch(string stem, string gameName)
    {
        static string Letters(string text) { return new string(text.Where(char.IsLetterOrDigit).ToArray()); }

        var exe = Letters(stem.Replace("-Win64-Shipping", "", StringComparison.OrdinalIgnoreCase)
                              .Replace("-WinGDK-Shipping", "", StringComparison.OrdinalIgnoreCase));
        var game = Letters(gameName);

        return exe.Length >= 3 && game.Length >= 3 &&
               (exe.Contains(game, StringComparison.OrdinalIgnoreCase) ||
                game.Contains(exe, StringComparison.OrdinalIgnoreCase));
    }
}
