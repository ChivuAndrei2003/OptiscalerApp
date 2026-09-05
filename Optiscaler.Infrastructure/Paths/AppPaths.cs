using Optiscaler.Core.Abstractions;

namespace Optiscaler.Infrastructure.Paths;

//

public sealed class AppPaths : IAppPaths
{
    private const string ApplicationDirectoryName = "OptiscalerApp";

    public AppPaths() : this(GetDefaultRootDirectory())
    {
    }

    public AppPaths(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        RootDirectory = Path.GetFullPath(rootDirectory);
        GamesFilePath = Path.Combine(RootDirectory, "games.json");
        ConfigurationFilePath = Path.Combine(RootDirectory, "config.json");
    }

    private static string GetDefaultRootDirectory()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        if (string.IsNullOrWhiteSpace(appData))
        {
            var xdgConfigHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            var userHome = Environment.GetEnvironmentVariable("HOME");

            appData = !string.IsNullOrWhiteSpace(xdgConfigHome)
                ? xdgConfigHome
                : !string.IsNullOrWhiteSpace(userHome)
                    ? Path.Combine(userHome, ".config")
                    : AppContext.BaseDirectory;
        }

        return Path.Combine(appData, ApplicationDirectoryName);
    }

    public string RootDirectory { get; }

    public string GamesFilePath { get; }

    public string ConfigurationFilePath { get; }
}