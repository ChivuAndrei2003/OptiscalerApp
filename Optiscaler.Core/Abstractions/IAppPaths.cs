namespace Optiscaler.Core.Abstractions;

/// <summary>
/// Provides the canonical locations used for application-owned data.
/// </summary>
public interface IAppPaths
{
    string RootDirectory { get; }

    string GamesFilePath { get; }

    string ConfigurationFilePath { get; }
}