namespace OptiscalerApp.Paths;

/// <summary>
/// Provides the canonical locations used for application-owned data.
/// </summary>
public interface IAppPaths
{
    string RootDirectory { get; }

    string GamesFilePath { get; }

    string ConfigurationFilePath { get; }

    string ProfilesFilePath { get; }

    /// <summary>The cached OptiScaler wiki compatibility list.</summary>
    string CompatibilityFilePath { get; }

    /// <summary>One folder per installation operation, holding its journal and the original files it replaced.</summary>
    string TransactionsDirectory { get; }

    /// <summary>Downloaded and extracted release packages.</summary>
    string PackagesDirectory { get; }

    /// <summary>Cover art and executable icons shown in the library.</summary>
    string CoversDirectory { get; }
}
