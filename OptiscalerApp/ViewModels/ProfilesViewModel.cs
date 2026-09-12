using OptiscalerApp.Persistence;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using OptiscalerApp.Models;

namespace OptiscalerApp.ViewModels;

public partial class ProfilesViewModel(IProfileRepository repository) : ViewModelBase
{
    private ProfileCatalog _catalog = new();
    public ObservableCollection<RenderProfile> Profiles { get; } = [];
    public RenderProfile? DefaultProfile => _catalog.Profiles.FirstOrDefault(p => p.Id == _catalog.DefaultProfileId);
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanEdit))] [NotifyPropertyChangedFor(nameof(CanCreate))]
    private bool _isBusy;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanEdit))] [NotifyPropertyChangedFor(nameof(CanCreate))]
    private bool _isLoaded;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanEdit))] [NotifyPropertyChangedFor(nameof(SelectionDetails))]
    private RenderProfile? _selectedProfile;
    [ObservableProperty] private string _statusMessage = "Loading profiles…";
    [ObservableProperty] private string _searchText = "";
    public bool CanCreate => IsLoaded && !IsBusy;
    public bool CanEdit => CanCreate && SelectedProfile is not null;
    public string SelectionDetails => SelectedProfile is { } p
        ? $"{p.Name}{(p.Id == _catalog.DefaultProfileId ? " • Default" : "")}\n{p.Description}\nDX11: {p.Dx11Upscaler} • DX12: {p.Dx12Upscaler}"
        : "Select a profile to edit, duplicate, export, or delete it.";
    partial void OnSearchTextChanged(string value) => RefreshProfiles(SelectedProfile?.Id);

    public async Task LoadProfiles_Async()
    {
        if (IsBusy || IsLoaded) return;
        IsBusy = true;
        try
        {
            _catalog = await repository.LoadProfileCatalog_Async();
            IsLoaded = true;
            RefreshProfiles(null);
            StatusMessage = $"{_catalog.Profiles.Count} saved profiles.";
        }
        catch (Exception ex) { StatusMessage = $"Could not load profiles: {ex.Message}"; }
        finally { IsBusy = false; }
    }

    public Task<bool> SaveProfile_Async(RenderProfile profile) => SaveCatalog_Async(new ProfileCatalog
    {
        DefaultProfileId = _catalog.DefaultProfileId,
        Profiles = _catalog.Profiles.Where(p => p.Id != profile.Id).Append(profile).ToList()
    }, profile.Id, $"Saved {profile.Name}.");

    public Task<bool> SetDefaultProfile_Async() => SelectedProfile is { } p
        ? SaveCatalog_Async(new ProfileCatalog { Profiles = _catalog.Profiles.ToList(), DefaultProfileId = p.Id }, p.Id, $"{p.Name} is the default profile.")
        : Task.FromResult(false);

    public Task<bool> DeleteProfile_Async() => SelectedProfile is { } p
        ? SaveCatalog_Async(new ProfileCatalog
        {
            Profiles = _catalog.Profiles.Where(item => item.Id != p.Id).ToList(),
            DefaultProfileId = _catalog.DefaultProfileId == p.Id ? null : _catalog.DefaultProfileId
        }, null, $"Deleted {p.Name}.") : Task.FromResult(false);

    public Task<bool> DuplicateProfile_Async()
    {
        if (SelectedProfile is not { } p) return Task.FromResult(false);
        var stem = p.Name[..Math.Min(p.Name.Length, 85)];
        var name = stem + " (copy)";
        for (var i = 2; _catalog.Profiles.Any(item => item.Name.Equals(name, StringComparison.OrdinalIgnoreCase)); i++)
            name = $"{stem} (copy {i})";
        return SaveProfile_Async(p with { Id = Guid.NewGuid(), Name = name });
    }

    private async Task<bool> SaveCatalog_Async(ProfileCatalog catalog, Guid? selectedId, string message)
    {
        if (!CanCreate) return false;
        IsBusy = true;
        try
        {
            await repository.SaveProfileCatalog_Async(catalog);
            // Publish only committed data so a failed write leaves selection and profiles intact.
            _catalog = catalog;
            if (selectedId is not null) SearchText = "";
            RefreshProfiles(selectedId);
            StatusMessage = message;
            return true;
        }
        catch (Exception ex) { StatusMessage = $"Could not save profiles: {ex.Message}"; return false; }
        finally { IsBusy = false; }
    }

    private void RefreshProfiles(Guid? selectedId)
    {
        Profiles.Clear();
        foreach (var p in _catalog.Profiles.Where(p => p.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase))
                     .OrderByDescending(p => p.Id == _catalog.DefaultProfileId).ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
            Profiles.Add(p);
        SelectedProfile = Profiles.FirstOrDefault(p => p.Id == selectedId);
        OnPropertyChanged(nameof(SelectionDetails));
        OnPropertyChanged(nameof(DefaultProfile));
    }
}
