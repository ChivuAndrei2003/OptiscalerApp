using System.Globalization;
using System.Runtime.Versioning;
using System.Security;
using Microsoft.Win32;
using OptiscalerApp.Models;
using ValveKeyValue;

namespace OptiscalerApp.Scanning;

/// <summary>
/// Discovers Steam installations from local library metadata without modifying Steam files.
/// </summary>
/// <remarks>
/// Custom folders may identify a Steam library root or its steamapps directory.
/// Drive filtering, game deduplication, and sorting belong to GameDiscoveryCoordinator.
/// Executable selection and game compatibility belong to the analysis stage.
/// </remarks>
public sealed class SteamScanner : IGameScanner
{
    private readonly IReadOnlyList<string>? _steamRoots;

    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    /// <summary>Creates a scanner that locates Steam using the current operating system.</summary>
    public SteamScanner()
    {
    }

    public SteamScanner(IEnumerable<string> steamRoots)
    {
        ArgumentNullException.ThrowIfNull(steamRoots);
        _steamRoots = steamRoots.ToArray();
    }

    public GamePlatform Platform => GamePlatform.Steam;


    public Task<ScanResult> ScanGames_Async(ScanContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        if (!context.IsEnabled(Platform))
            return Task.FromResult(new ScanResult());

        return Task.Run(() => ScanSteamLibraries(context, cancellationToken), cancellationToken);
    }

    private ScanResult ScanSteamLibraries(ScanContext context, CancellationToken cancellationToken)
    {
        var result = new ScanResult();
        var roots = (_steamRoots ?? GetSteamInstallPaths(result)).Concat(context.CustomFolders);
        var libraries = GetLibraryFolders(roots, result, cancellationToken);

        foreach (var library in libraries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var steamApps = Path.Combine(library, "steamapps");

            if (!Directory.Exists(steamApps))
                continue;

            try
            {
                foreach (var manifest in Directory.EnumerateFiles(steamApps, "appmanifest_*.acf"))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var game = ParseManifest(manifest, result);
                    if (game is not null)
                        result.Games.Add(game);
                }
            }
            catch (Exception exception) when (IsSourceError(exception))
            {
                AddScanWarning(result, "steam.library_unreadable", steamApps, exception.Message);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        return result;
    }

    private static HashSet<string> GetLibraryFolders(IEnumerable<string> roots,
                                                     ScanResult result, CancellationToken cancellationToken)
    {
        var libraries = new HashSet<string>(PathComparer);

        foreach (var root in roots)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var library = NormalizeLibraryRoot(root);
                libraries.Add(library);
                ReadLibraryFolders(library, libraries, result, cancellationToken);
            }
            catch (Exception exception) when (IsSourceError(exception))
            {
                AddScanWarning(result, "steam.library_unreadable", root, exception.Message);
            }
        }

        return libraries;
    }

    private static void ReadLibraryFolders(string root, HashSet<string> libraries,
                                           ScanResult result, CancellationToken cancellationToken)
    {
        foreach (var folder in new[] { "steamapps", "config" })
        {
            var path = Path.Combine(root, folder, "libraryfolders.vdf");

            if (!File.Exists(path))
                continue;

            try
            {
                using var stream = File.OpenRead(path);
                var serializer = KVSerializer.Create(KVSerializationFormat.KeyValues1Text);
                var document = serializer.Deserialize(stream, new KVSerializerOptions { HasEscapeSequences = true });

                if (!string.Equals(document.Name, "libraryfolders", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Expected a libraryfolders object.");

                foreach (var (key, value) in document.Root)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (!uint.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out _))
                        continue;

                    try
                    {
                        // Older Steam versions stored the path directly under the numeric key.
                        var libraryPath = value.ValueType == KVValueType.String ? (string)value
                            : value.TryGetValue("path", out var entry) ? entry.ToString() : null;

                        if (string.IsNullOrWhiteSpace(libraryPath))
                            throw new InvalidDataException("Missing library path.");

                        libraries.Add(NormalizeLibraryRoot(libraryPath));
                    }
                    catch (Exception exception) when (IsSourceError(exception))
                    {
                        AddScanWarning(result, "steam.library_invalid", path, exception.Message);
                    }
                }
            }
            catch (Exception exception) when (IsSourceError(exception))
            {
                AddScanWarning(result, "steam.library_manifest_invalid", path, exception.Message);
            }
        }
    }

    /// <summary>Reads one app manifest; an invalid entry does not stop the next game.</summary>
    private static DiscoveredGame? ParseManifest(string manifest, ScanResult result)
    {
        try
        {
            using var stream = new FileStream(manifest, FileMode.Open, FileAccess.Read,
                                              FileShare.ReadWrite | FileShare.Delete);
            var serializer = KVSerializer.Create(KVSerializationFormat.KeyValues1Text);
            var state = serializer.Deserialize<AppState>(stream, new KVSerializerOptions { HasEscapeSequences = true });

            if (!uint.TryParse(state.AppId, NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id == 0 ||
                !string.Equals(Path.GetFileName(manifest), $"appmanifest_{state.AppId}.acf",
                               StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The app ID must match the manifest filename.");
            if (string.IsNullOrWhiteSpace(state.Name))
                throw new InvalidDataException("Missing game name.");

            // installdir is a folder name, never a path outside steamapps/common.
            var directory = state.InstallDir;

            if (string.IsNullOrWhiteSpace(directory) || directory is "." or ".." ||
                directory.IndexOfAny(['/', '\\', ':', '\0']) >= 0 || Path.IsPathRooted(directory))
                throw new InvalidDataException("The installation directory must be a single folder name.");

            var installPath =
                ScanPaths.NormalizeAbsoluteGamePath(
                                                    Path.Combine(Path.GetDirectoryName(manifest)!, "common",
                                                                 directory));

            if (!Directory.Exists(installPath))
                throw new InvalidDataException("The installation directory is missing.");

            return new DiscoveredGame
            {
                Platform = GamePlatform.Steam,
                ExternalId = state.AppId,
                Name = state.Name,
                InstallPath = installPath
            };
        }
        catch (Exception exception) when (IsSourceError(exception))
        {
            AddScanWarning(result, "steam.manifest_invalid", manifest, exception.Message);

            return null;
        }
    }

    private sealed class AppState
    {
        [KVProperty("appid")] public string? AppId { get; set; }

        [KVProperty("name")] public string? Name { get; set; }

        [KVProperty("installdir")] public string? InstallDir { get; set; }
    }

    private static string NormalizeLibraryRoot(string path)
    {
        var fullPath = ScanPaths.NormalizeAbsoluteGamePath(path);
        if (PathComparer.Equals(Path.GetFileName(fullPath), "steamapps"))
            fullPath = Path.GetDirectoryName(fullPath)!;

        return fullPath;
    }

    private static IReadOnlyList<string> GetSteamInstallPaths(ScanResult result)
    {
        if (OperatingSystem.IsWindows())
            return GetSteamInstallPathsWindows(result);
        if (OperatingSystem.IsLinux())
            return GetSteamInstallPathsLinux();

        return [];
    }

    private static IReadOnlyList<string> GetSteamInstallPathsLinux()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (string.IsNullOrEmpty(home))
            return [];

        var dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (string.IsNullOrEmpty(dataHome) || !Path.IsPathFullyQualified(dataHome))
            dataHome = Path.Combine(home, ".local", "share");

        return
        [
            Path.Combine(dataHome, "Steam"),
            Path.Combine(home, ".local", "share", "Steam"),
            Path.Combine(home, ".steam", "steam"),
            Path.Combine(home, ".steam", "root"),
            Path.Combine(home, ".var", "app", "com.valvesoftware.Steam", ".local", "share", "Steam"),
            Path.Combine(home, "snap", "steam", "common", ".steam", "steam")
        ];
    }

    [SupportedOSPlatform("windows")]
    private static IReadOnlyList<string> GetSteamInstallPathsWindows(ScanResult result)
    {
        var roots = new List<string>();

        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
            foreach (var view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
                try
                {
                    using var registry = RegistryKey.OpenBaseKey(hive, view);
                    using var key = registry.OpenSubKey(@"Software\Valve\Steam");
                    if (key?.GetValue(hive == RegistryHive.CurrentUser ? "SteamPath" : "InstallPath") is string path &&
                        !string.IsNullOrWhiteSpace(path))
                        roots.Add(path);
                }
                catch (Exception exception) when (IsSourceError(exception))
                {
                    AddScanWarning(result, "steam.registry_unreadable", $"{hive}/{view}", exception.Message);
                }

        foreach (var folder in new[]
                     { Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.ProgramFiles })
        {
            var path = Environment.GetFolderPath(folder);
            if (!string.IsNullOrEmpty(path))
                roots.Add(Path.Combine(path, "Steam"));
        }

        return roots;
    }

    private static bool IsSourceError(Exception exception)
    {
        return exception is InvalidDataException or IOException or
            UnauthorizedAccessException or SecurityException or ArgumentException or NotSupportedException
            or KeyValueException;
    }

    private static void AddScanWarning(ScanResult result, string code, string source, string message)
    {
        result.Diagnostics.Add(new ScanDiagnostic
        {
            Platform = GamePlatform.Steam,
            Severity = ScanDiagnosticSeverity.Warning,
            Code = code,
            Message = $"{source}: {message}"
        });
    }
}
