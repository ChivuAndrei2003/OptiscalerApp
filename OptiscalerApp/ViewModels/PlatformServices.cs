using OptiscalerApp.Management;

namespace OptiscalerApp.ViewModels;

/// <summary>File and folder pickers. Every result is a local path; null or empty means the user cancelled.</summary>
public interface IFileDialogs
{
    Task<string?> PickFileAsync(string title, string pattern);

    Task<string?> PickFolderAsync(string title);

    Task<IReadOnlyList<string>> PickFoldersAsync(string title);

    Task<string?> PickSaveFileAsync(string title, string suggestedName, string pattern);
}

/// <summary>Clipboard, launching and opening folders, which all need the window's platform services.</summary>
public interface IShellActions
{
    Task SetClipboardTextAsync(string text);

    Task<bool> OpenAsync(LaunchTarget target);

    Task<bool> OpenFolderAsync(string path);
}
