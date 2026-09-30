namespace OptiscalerApp.Paths;

public sealed class AppPaths : IAppPaths
{
    private const string ApplicationDirectoryName = "OptiscalerApp";

    public AppPaths() : this(GetDefaultRootDirectory()) { }

    public AppPaths(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        RootDirectory = Path.GetFullPath(rootDirectory);
        GamesFilePath = Path.Combine(RootDirectory, "games.json");
        ConfigurationFilePath = Path.Combine(RootDirectory, "config.json");
        ProfilesFilePath = Path.Combine(RootDirectory, "profiles.json");
        CompatibilityFilePath = Path.Combine(RootDirectory, "compatibility.json");
        TransactionsDirectory = Path.Combine(RootDirectory, "transactions");
        PackagesDirectory = Path.Combine(RootDirectory, "packages");
        CoversDirectory = Path.Combine(RootDirectory, "covers");
    }

    public string RootDirectory { get; }

    public string GamesFilePath { get; }

    public string ConfigurationFilePath { get; }

    public string ProfilesFilePath { get; }

    public string CompatibilityFilePath { get; }

    public string TransactionsDirectory { get; }

    public string PackagesDirectory { get; }

    public string CoversDirectory { get; }

    private static string GetDefaultRootDirectory()
    {
        // An explicit root lets portable runs and tests keep their data separate from the user's library.
        var explicitRoot = Environment.GetEnvironmentVariable("OPTISCALER_DATA_DIRECTORY");

        if (!string.IsNullOrWhiteSpace(explicitRoot)) return Path.GetFullPath(explicitRoot);

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
}