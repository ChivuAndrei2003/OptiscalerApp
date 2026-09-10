namespace Optiscaler.Infrastructure.Scanning;

/// <summary>Locates launcher metadata without searching entire disks.</summary>
public static class LauncherLocations
{
    public static IReadOnlyList<string> HeroicRoots(string home, string configHome, bool windows)
    {
        return windows
            ? [Path.Combine(configHome, "heroic")]
            :
            [
                Path.Combine(configHome, "heroic"),
                Path.Combine(home, ".var/app/com.heroicgameslauncher.hgl/config/heroic")
            ];
    }

    public static IReadOnlyList<string> LutrisRoots(string home, string configHome, string dataHome)
    {
        return
        [
            Path.Combine(dataHome, "lutris/games"), Path.Combine(configHome, "lutris/games"),
            Path.Combine(home, ".var/app/net.lutris.Lutris/data/lutris/games"),
            Path.Combine(home, ".var/app/net.lutris.Lutris/config/lutris/games")
        ];
    }

    internal static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    internal static string ConfigHome => OperatingSystem.IsWindows()
        ? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
        : Xdg("XDG_CONFIG_HOME", ".config");

    internal static string DataHome => Xdg("XDG_DATA_HOME", ".local/share");

    private static string Xdg(string name, string fallback)
    {
        return Environment.GetEnvironmentVariable(name) is { } value && Path.IsPathFullyQualified(value)
            ? value
            : Path.Combine(Home, fallback);
    }
}