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

/// <summary>The controller buttons that open OptiScaler's in-game menu while this app runs beside the game.</summary>
public interface IControllerShortcut
{
    /// <summary>Why the shortcut cannot work on this system; null when it can.</summary>
    string? Problem { get; }

    /// <summary>The buttons that have to be held together, one bit each; 0 turns the shortcut off.</summary>
    uint Buttons { get; set; }

    /// <summary>The buttons' names, e.g. "View (Select) + Menu (Start)".</summary>
    string Describe(uint buttons);

    /// <summary>Waits for the next button or combination pressed on a controller; 0 when none comes in time.</summary>
    Task<uint> CaptureAsync();
}
