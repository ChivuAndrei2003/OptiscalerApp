using CommunityToolkit.Mvvm.ComponentModel;
using OptiscalerApp.Management;

namespace OptiscalerApp.ViewModels;

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
    public static readonly ComponentChoice KeepExisting = new("Skip · keep existing files", ComponentSource.KeepExisting);
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

    /// <summary>The choice made before "Choose local file…", kept when the file picker is cancelled.</summary>
    public ComponentChoice? SelectedBeforeBrowse { get; private set; }

    public event Action<ComponentOption>? BrowseRequested;

    partial void OnSelectedChanged(ComponentChoice? oldValue, ComponentChoice? newValue)
    {
        if (newValue?.Source != ComponentSource.BrowseLocal) return;

        SelectedBeforeBrowse = oldValue;
        BrowseRequested?.Invoke(this);
    }

    public ComponentInstallSelection ToSelection()
    {
        return Selected switch
        {
            { Source: ComponentSource.Release, Release: { } release } => new ComponentInstallSelection(Component,
                release),
            { Source: ComponentSource.Local, LocalPath: { } path } => new ComponentInstallSelection(
             Component, LocalPath: path, LocalVersion: PackageSelectionViewModel.ReadVersion(path)),
            { Source: ComponentSource.KeepExisting } => new ComponentInstallSelection(Component, KeepExisting: true),
            _ => new ComponentInstallSelection(Component)
        };
    }
}
