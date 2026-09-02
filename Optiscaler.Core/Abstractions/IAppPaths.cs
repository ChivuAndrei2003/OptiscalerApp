namespace Optiscaler.Core.Abstractions;

public interface IAppPaths
{
    string RootDirectory { get; }

    string GamesFilePath { get; }
    string ConfigurationFilePath { get; }

    string AnalysisCacheFilePath { get; }

    string ArtifactCacheDirectory { get; }
    
    string ProfilesDirectory { get; }
    
    string TransactionsDirectory { get; }
    
}