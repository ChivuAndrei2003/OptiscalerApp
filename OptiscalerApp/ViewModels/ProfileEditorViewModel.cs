using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OptiscalerApp.Management;
using OptiscalerApp.Models;

namespace OptiscalerApp.ViewModels;

/// <summary>Creates or edits one profile; nothing is saved until <see cref="SaveCommand" /> succeeds.</summary>
public sealed partial class ProfileEditorViewModel : ViewModelBase
{
    private readonly RenderProfile _profile;
    private readonly Func<RenderProfile, Task<bool>> _save;

    [ObservableProperty] private string _description;

    [ObservableProperty] private bool _hasNoMatches;

    [ObservableProperty] private string _name;

    [ObservableProperty] private string _overrideCountText = "";

    [ObservableProperty] private string _searchText = "";

    [ObservableProperty] private string _validationText = "";

    public ProfileEditorViewModel(RenderProfile profile, bool editing, Func<RenderProfile, Task<bool>> save)
    {
        _profile = profile;
        _save = save;
        _name = profile.Name;
        _description = profile.Description;
        Title = editing ? "Edit Profile" : "New Profile";
        Groups = ProfileSettingGroup.Create(profile.Settings);

        foreach (var field in Fields)
            field.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ProfileSettingField.IsOverridden)) UpdateOverrideCount();
            };

        UpdateOverrideCount();
    }

    public string Title { get; }

    public IReadOnlyList<ProfileSettingGroup> Groups { get; }

    private IEnumerable<ProfileSettingField> Fields => Groups.SelectMany(g => g.Fields);

    /// <summary>Raised when the editor is done, after a successful save or a cancel.</summary>
    public event EventHandler? Closed;

    /// <summary>Raised for a field whose value is invalid, once search no longer hides it, so the view can focus it.</summary>
    public event Action<ProfileSettingField>? InvalidFieldShown;

    partial void OnSearchTextChanged(string value)
    {
        var search = value.Trim();
        foreach (var group in Groups) group.ApplySearch(search);
        HasNoMatches = Groups.All(g => !g.IsVisible);
    }

    [RelayCommand]
    private void ResetToDefaults()
    {
        foreach (var field in Fields) field.Load(null);
    }

    [RelayCommand]
    private void Cancel() { Closed?.Invoke(this, EventArgs.Empty); }

    [RelayCommand]
    private async Task Save()
    {
        try
        {
            var profile = _profile with
            {
                Name = Name.Trim(),
                Description = Description.Trim(),
                Settings = ReadSettings()
            };
            ProfileIni.ValidateProfile(profile);
            ValidationText = "";

            if (await _save(profile))
                Closed?.Invoke(this, EventArgs.Empty);
            else
                ValidationText = "The profile could not be saved. Your changes are still here; retry saving.";
        }
        catch (InvalidDataException ex)
        {
            ValidationText = ex.Message;
        }
    }

    private Dictionary<string, string> ReadSettings()
    {
        var settings = new Dictionary<string, string>();

        foreach (var field in Fields)
            try
            {
                if (field.ReadValue() is { } value) settings[field.Setting.Id] = value;
            }
            catch (InvalidDataException)
            {
                // The search must not hide the value that needs fixing.
                if (!field.IsVisible) SearchText = "";
                InvalidFieldShown?.Invoke(field);

                throw;
            }

        return settings;
    }

    private void UpdateOverrideCount()
    {
        var count = Fields.Count(f => f.IsOverridden);
        OverrideCountText = count switch
        {
            0 => "All options use OptiScaler defaults.",
            1 => "1 option overridden.",
            _ => $"{count} options overridden."
        };
    }
}
