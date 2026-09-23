using OptiscalerApp.Models;

namespace OptiscalerApp.Management;

/// <summary>A launcher URI or an executable to start; exactly one is set.</summary>
public sealed record LaunchTarget(Uri? Uri, string? Executable);

public static class GameLauncher
{
    /// <summary>
    ///     Steam games start through Steam so overlays, cloud saves and DRM keep working. Other games start their
    ///     executable directly, which only works on Windows; the Epic URI is avoided because Heroic installs report
    ///     the same platform.
    /// </summary>
    public static LaunchTarget? Resolve(GameRecord game, string? executable)
    {
        if (game.Platform == GamePlatform.Steam && game.ExternalId is { Length: > 0 } appId && appId.All(char.IsDigit))
            return new LaunchTarget(new Uri($"steam://rungameid/{appId}"), null);

        return OperatingSystem.IsWindows() && !string.IsNullOrWhiteSpace(executable) && File.Exists(executable)
            ? new LaunchTarget(null, executable)
            : null;
    }

    /// <summary>Proton and Wine load a game's own DLL only when the launch options override the proxy name.</summary>
    public static string LinuxLaunchOptions(string proxyName)
    {
        return $"WINEDLLOVERRIDES=\"{Path.GetFileNameWithoutExtension(proxyName).ToLowerInvariant()}=n,b\" %COMMAND%";
    }
}
