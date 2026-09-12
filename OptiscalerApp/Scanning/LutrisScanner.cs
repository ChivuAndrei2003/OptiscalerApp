using System.Text.RegularExpressions;
using OptiscalerApp.Models;
using YamlDotNet.Serialization;

namespace OptiscalerApp.Scanning;

public class LutrisScanner(IEnumerable<string>? gameConfigRoots = null) : IGameScanner
{
    public GamePlatform Platform => GamePlatform.Lutris;


    public Task<ScanResult> ScanGames_Async(ScanContext context,
                                            CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            var result = new ScanResult();

            if (!context.IsEnabled(Platform)) return result;

            var roots = gameConfigRoots
                        ?? (OperatingSystem.IsLinux()
                ? LauncherLocations.LutrisRoots(LauncherLocations.Home, LauncherLocations.ConfigHome,
                                                LauncherLocations.DataHome)
                : []);

            var yaml = new DeserializerBuilder().IgnoreUnmatchedProperties().Build();

            foreach (var root in roots)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!Directory.Exists(root)) continue;

                try
                {
                    foreach (var file in Directory.EnumerateFiles(root)
                                 .Where(p => Path.GetExtension(p) is ".yml" or ".yaml"))
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        try
                        {
                            var config = yaml.Deserialize<LutrisConfig>(File.ReadAllText(file));
                            var exe = config?.Game?.Exe;

                            if (string.IsNullOrWhiteSpace(exe)) continue;

                            var prefix = ExpandHome(config?.Game?.Prefix);
                            exe = ExpandHome(exe)!;

                            if (Regex.IsMatch(exe, "^[A-Za-z]:[\\\\/]"))
                            {
                                if (string.IsNullOrWhiteSpace(prefix))
                                    throw new InvalidDataException("Wine executable has no prefix.");

                                var drive = char.ToLowerInvariant(exe[0]);
                                var driveRoot = drive == 'c'
                                    ? Path.Combine(prefix, "drive_c")
                                    : Path.Combine(prefix, "dosdevices", $"{drive}:");
                                exe = Path.Combine(driveRoot, exe[3..].Replace('\\', '/'));
                            }
                            else if (!Path.IsPathFullyQualified(exe))
                            {
                                var working = ExpandHome(config?.Game?.WorkingDirectory);

                                if (string.IsNullOrWhiteSpace(working) || !Path.IsPathFullyQualified(working))
                                    throw new InvalidDataException("Relative executable has no absolute working directory.");

                                exe = Path.Combine(working, exe);
                            }

                            exe = ScanPaths.NormalizeAbsoluteGamePath(exe);

                            if (!File.Exists(exe)) continue;

                            var slug = Path.GetFileNameWithoutExtension(file);

                            var title = config?.Name ?? Regex
                                .Replace(slug, @"-\d+$", "")
                                .Replace('-', ' ');

                            ScanSource.AddDiscoveredGame(result, Platform, title, slug, Path.GetDirectoryName(exe),
                                                         exe);
                        }
                        catch (Exception ex) when (ScanSource.IsGameSourceReadError(ex) ||
                                                   ex is YamlDotNet.Core.YamlException)
                        {
                            ScanSource.AddScanWarning(result, Platform, file, ex);
                        }
                    }
                }
                catch (Exception ex) when (ScanSource.IsGameSourceReadError(ex))
                {
                    ScanSource.AddScanWarning(result, Platform, root, ex);
                }
            }

            return result;
        }, cancellationToken);
    }

    private static string? ExpandHome(string? path)
    {
        return path?.StartsWith("~/", StringComparison.Ordinal) == true
            ? Path.Combine(LauncherLocations.Home, path[2..])
            : path;
    }

    public sealed class LutrisConfig
    {
        [YamlMember(Alias = "name")] public string? Name { get; set; }
        [YamlMember(Alias = "game")] public LutrisGame? Game { get; set; }
    }

    public sealed class LutrisGame
    {
        [YamlMember(Alias = "exe")] public string? Exe { get; set; }
        [YamlMember(Alias = "prefix")] public string? Prefix { get; set; }
        [YamlMember(Alias = "working_dir")] public string? WorkingDirectory { get; set; }
    }
}
