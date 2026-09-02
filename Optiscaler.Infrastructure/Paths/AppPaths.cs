using Optiscaler.Core.Abstractions;

namespace Optiscaler.Infrastructure.Paths;

public sealed class AppPaths : IAppPaths
{
    private const string ApplicationDirectoryName = "OptiscalerApp";

    public AppPaths()
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

        RootDirectory = Path.Combine(appData, ApplicationDirectoryName);
        GamesFilePath = Path.Combine(RootDirectory, "games.json");
        ConfigurationFilePath = Path.Combine(RootDirectory, "config.json");
        AnalysisCacheFilePath = Path.Combine(RootDirectory, "analysis-cache.json");
        ArtifactCacheDirectory = Path.Combine(RootDirectory, "Artifacts");
        ProfilesDirectory = Path.Combine(RootDirectory, "Profiles");
        TransactionsDirectory = Path.Combine(RootDirectory, "Transactions");
    }

    public string RootDirectory { get; }
    public string GamesFilePath { get; }
    public string ConfigurationFilePath { get; }
    public string AnalysisCacheFilePath { get; }
    public string ArtifactCacheDirectory { get; }
    public string ProfilesDirectory { get; }
    public string TransactionsDirectory { get; }
}