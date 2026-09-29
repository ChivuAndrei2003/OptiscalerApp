using OptiscalerApp.Management;

namespace OptiscalerApp.ViewModels;

/// <summary>File and folder pickers. Every result is a local path; null or empty means the user cancelled.</summary>
public interface IFileDialogs
{
    Task<string?> PickFile_Async(string title, string pattern);

    Task<string?> PickFolder_Async(string title);

    Task<IReadOnlyList<string>> PickFolders_Async(string title);

    Task<string?> PickSaveFile_Async(string title, string suggestedName, string pattern);
}

/// <summary>Clipboard, launching and opening folders, which all need the window's platform services.</summary>
public interface IShellActions
{
    Task SetClipboardText_Async(string text);

    Task<bool> Open_Async(LaunchTarget target);

    Task<bool> OpenFolder_Async(string path);
}
