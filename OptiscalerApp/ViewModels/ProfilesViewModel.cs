using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OptiscalerApp.Management;
using OptiscalerApp.Models;
using OptiscalerApp.Persistence;

namespace OptiscalerApp.ViewModels;

public sealed partial class ProfilesViewModel(IProfileRepository repository, IFileDialogs dialogs) : ViewModelBase
{
    private ProfileCatalog _catalog = new();

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsEditing))]
    private ProfileEditorViewModel? _editor;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanEdit), nameof(CanCreate))]
    private bool _isBusy;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanEdit), nameof(CanCreate))]
    private bool _isLoaded;

    [ObservableProperty] private string _searchText = "";

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanEdit), nameof(SelectionDetails))]
    private RenderProfile? _selectedProfile;

    [ObservableProperty] private string _statusMessage = "Loading profiles…";

    public ObservableCollection<RenderProfile> Profiles { get; } = [];
    public bool CanCreate => IsLoaded && !IsBusy;
    public bool CanEdit => CanCreate && SelectedProfile is not null;
    public bool IsEditing => Editor is not null;

    public string SelectionDetails => SelectedProfile is { } p
        ? $"{p.Name}{(p.Id == _catalog.DefaultProfileId ? " • Default" : "")}\n{p.Description}\n{ProfileIni.Describe(p)}"
        : "Select a profile to edit, duplicate, export, or delete it.";

    partial void OnSearchTextChanged(string value) { RefreshProfiles(SelectedProfile?.Id); }

    public async Task LoadProfilesAsync()
    {
        if (IsBusy || IsLoaded) return;

        IsBusy = true;

        try
        {
            _catalog = await repository.LoadProfileCatalogAsync();
            IsLoaded = true;
            RefreshProfiles(null);
            StatusMessage = $"{_catalog.Profiles.Count} saved profiles.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not load profiles: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public Task<bool> SaveProfileAsync(RenderProfile profile)
    {
        return SaveCatalogAsync(new ProfileCatalog
                                 {
                                     DefaultProfileId = _catalog.DefaultProfileId,
                                     Profiles = _catalog.Profiles.Where(p => p.Id != profile.Id).Append(profile)
                                         .ToList()
                                 }, profile.Id, $"Saved {profile.Name}.");
    }

    [RelayCommand]
    private void NewProfile() { OpenEditor(new RenderProfile { Name = "" }, false); }

    [RelayCommand]
    private void EditProfile()
    {
        if (CanEdit && SelectedProfile is { } profile) OpenEditor(profile, true);
    }

    [RelayCommand]
    private Task SetDefaultProfile()
    {
        return SelectedProfile is { } p
            ? SaveCatalogAsync(new ProfileCatalog { Profiles = _catalog.Profiles.ToList(), DefaultProfileId = p.Id },
                                p.Id, $"{p.Name} is the default profile.")
            : Task.CompletedTask;
    }

    [RelayCommand]
    private Task DeleteProfile()
    {
        return SelectedProfile is { } p
            ? SaveCatalogAsync(new ProfileCatalog
                                {
                                    Profiles = _catalog.Profiles.Where(item => item.Id != p.Id).ToList(),
                                    DefaultProfileId = _catalog.DefaultProfileId == p.Id
                                        ? null
                                        : _catalog.DefaultProfileId
                                }, null, $"Deleted {p.Name}.")
            : Task.CompletedTask;
    }

    [RelayCommand]
    private Task DuplicateProfile()
    {
        return SelectedProfile is { } p
            ? SaveProfileAsync(p with { Id = Guid.NewGuid(), Name = UniqueName(p.Name, "copy") })
            : Task.CompletedTask;
    }

    /// <summary>Turns a tuned OptiScaler.ini, e.g. one shared for a game, into a reusable profile.</summary>
    [RelayCommand]
    private async Task ImportProfile()
    {
        if (!CanCreate) return;

        try
        {
            if (await dialogs.PickFileAsync("Import OptiScaler.ini", "*.ini") is not { } path) return;

            // The game folder names a profile better than "OptiScaler".
            var folder = Path.GetFileName(Path.GetDirectoryName(path));
            await ImportProfileAsync(await File.ReadAllTextAsync(path),
                                      string.IsNullOrWhiteSpace(folder) ? Path.GetFileNameWithoutExtension(path) : folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            StatusMessage = $"Could not import the profile: {ex.Message}";
        }
    }

    public Task<bool> ImportProfileAsync(string ini, string sourceName)
    {
        var name = UniqueName(string.IsNullOrWhiteSpace(sourceName) ? "Imported" : sourceName.Trim(), "imported");

        return SaveProfileAsync(ProfileIni.ReadProfileFromIni(ini, name));
    }

    [RelayCommand]
    private async Task ExportProfile()
    {
        if (!CanEdit || SelectedProfile is not { } profile) return;

        IsBusy = true;

        try
        {
            if (await dialogs.PickSaveFileAsync("Export profile configuration", "OptiScaler.ini", "*.ini") is not
                { } path)
                return;

            await File.WriteAllTextAsync(path, ProfileIni.ApplyProfileToIni("", profile));
            StatusMessage = $"Exported {profile.Name}.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            StatusMessage = $"Could not export profile: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void OpenEditor(RenderProfile profile, bool editing)
    {
        if (!CanCreate) return;

        var editor = new ProfileEditorViewModel(profile, editing, SaveProfileAsync);
        editor.Closed += (_, _) => Editor = null;
        Editor = editor;
    }

    private string UniqueName(string name, string suffix)
    {
        var stem = name[..Math.Min(name.Length, 85)];
        var unique = $"{stem} ({suffix})";
        for (var i = 2; _catalog.Profiles.Any(item => item.Name.Equals(unique, StringComparison.OrdinalIgnoreCase)); i++)
            unique = $"{stem} ({suffix} {i})";

        return unique;
    }

    private async Task<bool> SaveCatalogAsync(ProfileCatalog catalog, Guid? selectedId, string message)
    {
        if (!CanCreate) return false;

        IsBusy = true;

        try
        {
            await repository.SaveProfileCatalogAsync(catalog);

            // Publish only committed data so a failed write leaves selection and profiles intact.
            _catalog = catalog;
            if (selectedId is not null) SearchText = "";
            RefreshProfiles(selectedId);
            StatusMessage = message;

            return true;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not save profiles: {ex.Message}";

            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void RefreshProfiles(Guid? selectedId)
    {
        Profiles.Clear();
        foreach (var p in _catalog.Profiles.Where(p => p.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase))
                     .OrderByDescending(p => p.Id == _catalog.DefaultProfileId)
                     .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
            Profiles.Add(p);
        SelectedProfile = Profiles.FirstOrDefault(p => p.Id == selectedId);
        OnPropertyChanged(nameof(SelectionDetails));
    }
}
