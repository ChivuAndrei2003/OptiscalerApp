using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using OptiscalerApp.Management;
using OptiscalerApp.ViewModels;

namespace OptiscalerApp.Views;

/// <summary>Pickers, clipboard and launching through the main window; unavailable until a window is attached.</summary>
public sealed class TopLevelServices : IFileDialogs, IShellActions
{
    public TopLevel? Owner { get; set; }

    private TopLevel Top => Owner ?? throw new InvalidOperationException("The window is unavailable.");

    public async Task<string?> PickFileAsync(string title, string pattern)
    {
        var files = await Top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType(title) { Patterns = [pattern] }]
        });

        return LocalPaths(files).FirstOrDefault();
    }

    public async Task<string?> PickFolderAsync(string title)
    {
        return (await PickFoldersAsync(title, false)).FirstOrDefault();
    }

    public Task<IReadOnlyList<string>> PickFoldersAsync(string title) { return PickFoldersAsync(title, true); }

    public async Task<string?> PickSaveFileAsync(string title, string suggestedName, string pattern)
    {
        using var file = await Top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName,
            DefaultExtension = Path.GetExtension(pattern).TrimStart('.'),
            FileTypeChoices = [new FilePickerFileType(title) { Patterns = [pattern] }]
        });

        return file is null ? null : LocalPath(file);
    }

    public async Task SetClipboardTextAsync(string text)
    {
        if (Top.Clipboard is not { } clipboard) throw new InvalidOperationException("The clipboard is unavailable.");

        await clipboard.SetTextAsync(text);
    }

    public async Task<bool> OpenAsync(LaunchTarget target)
    {
        if (target.Uri is { } uri) return await Top.Launcher.LaunchUriAsync(uri);

        if (target.Executable is not { } executable) return false;

        // Games often load data relative to their own folder.
        using var process = Process.Start(new ProcessStartInfo(executable)
        {
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(executable)
        });

        return process is not null;
    }

    public Task<bool> OpenFolderAsync(string path)
    {
        return Top.Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(path));
    }

    private async Task<IReadOnlyList<string>> PickFoldersAsync(string title, bool multiple)
    {
        var folders = await Top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = multiple
        });

        return LocalPaths(folders);
    }

    private static List<string> LocalPaths(IReadOnlyList<IStorageItem> items)
    {
        try
        {
            return items.Select(LocalPath).ToList();
        }
        finally
        {
            foreach (var item in items) item.Dispose();
        }
    }

    private static string LocalPath(IStorageItem item)
    {
        return item.TryGetLocalPath() ??
               throw new InvalidOperationException("Select files and folders available on this computer.");
    }
}
