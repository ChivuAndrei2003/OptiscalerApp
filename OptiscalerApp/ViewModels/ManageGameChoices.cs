using CommunityToolkit.Mvvm.ComponentModel;
using OptiscalerApp.Management;

namespace OptiscalerApp.ViewModels;

/// <summary>File and folder pickers, supplied by the view.</summary>
public interface IFileDialogs
{
    Task<string?> PickFile_Async(string title, string pattern);

    Task<string?> PickFolder_Async(string title);
}

public enum ReleaseChannel
{
    Stable,
    Beta
}

public enum VersionAction
{
    UseCurrent,
    Download,
    FetchReleases,
    BrowseLocal
}

/// <summary>An entry in the OptiScaler version list; picking anything but the current package starts an action.</summary>
public sealed record VersionChoice(string Label, VersionAction Action, PackageRelease? Release = null)
{
    public override string ToString() { return Label; }
}

public enum ComponentSource
{
    Bundle,
    KeepExisting,
    Release,
    Local,
    BrowseLocal
}

public sealed record ComponentChoice(
    string Label,
    ComponentSource Source,
    PackageRelease? Release = null,
    string? LocalPath = null)
{
    public static readonly ComponentChoice Bundle = new("Use package bundle", ComponentSource.Bundle);
    public static readonly ComponentChoice KeepExisting = new("Keep existing", ComponentSource.KeepExisting);
    public static readonly ComponentChoice BrowseLocal = new("Choose local file…", ComponentSource.BrowseLocal);

    public override string ToString() { return Label; }
}

/// <summary>The version choice for one optional component such as FakeNvapi or OptiPatcher.</summary>
public sealed partial class ComponentOption(DownloadComponent component) : ObservableObject
{
    [ObservableProperty] private IReadOnlyList<ComponentChoice> _choices = [ComponentChoice.Bundle];

    [ObservableProperty] private ComponentChoice? _selected = ComponentChoice.Bundle;

    [ObservableProperty] private string _tip = "";

    public DownloadComponent Component { get; } = component;

    public event Action<ComponentOption>? BrowseRequested;

    partial void OnSelectedChanged(ComponentChoice? value)
    {
        if (value?.Source == ComponentSource.BrowseLocal) BrowseRequested?.Invoke(this);
    }

    public ComponentInstallSelection ToSelection()
    {
        return Selected switch
        {
            { Source: ComponentSource.Release, Release: { } release } => new ComponentInstallSelection(Component,
                release),
            { Source: ComponentSource.Local, LocalPath: { } path } => new ComponentInstallSelection(
             Component, LocalPath: path, LocalVersion: ManageGameViewModel.ReadVersion(path)),
            { Source: ComponentSource.KeepExisting } => new ComponentInstallSelection(Component, KeepExisting: true),
            _ => new ComponentInstallSelection(Component)
        };
    }
}
