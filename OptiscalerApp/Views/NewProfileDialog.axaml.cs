using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OptiscalerApp.Management;
using OptiscalerApp.Models;
using OptiscalerApp.ViewModels;

namespace OptiscalerApp.Views;

public partial class NewProfileDialog : UserControl
{
    private IReadOnlyList<ProfileSettingGroup> _groups = [];
    private RenderProfile _profile = new();

    public NewProfileDialog()
    {
        InitializeComponent();
        Dx11Box.ItemsSource = ProfileIni.Dx11Options;
        Dx12Box.ItemsSource = ProfileIni.Dx12Options;
        SettingsSearchBox.TextChanged += (_, _) => ApplySearch();
        SetProfile(new RenderProfile { Name = "" });
    }

    public Func<RenderProfile, Task<bool>>? SaveProfile { get; set; }
    public event EventHandler? Finished;

    private IEnumerable<ProfileSettingField> Fields => _groups.SelectMany(g => g.Fields);

    public void SetProfile(RenderProfile profile, bool editing = false)
    {
        _profile = profile;
        TitleText.Text = editing ? "Edit Profile" : "New Profile";
        ProfileNameTextBox.Text = profile.Name;
        DescriptionTextBox.Text = profile.Description;
        Dx11Box.SelectedItem = profile.Dx11Upscaler;
        Dx12Box.SelectedItem = profile.Dx12Upscaler;
        OverrideSharpnessBox.IsChecked = profile.Sharpness is not null;
        SharpnessBox.Value = profile.Sharpness ?? 0.5m;
        LoggingBox.IsChecked = profile.EnableLogging;

        foreach (var field in Fields) field.PropertyChanged -= Field_OnPropertyChanged;
        _groups = ProfileSettingGroup.Create(profile.Settings);
        foreach (var field in Fields) field.PropertyChanged += Field_OnPropertyChanged;
        GroupsList.ItemsSource = _groups;
        ApplySearch();
        UpdateOverrideCount();
    }

    private void Field_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ProfileSettingField.IsOverridden)) UpdateOverrideCount();
    }

    private void UpdateOverrideCount()
    {
        var count = Fields.Count(f => f.IsOverridden);
        OverrideCountText.Text = count switch
        {
            0 => "All options use OptiScaler defaults.",
            1 => "1 option overridden.",
            _ => $"{count} options overridden."
        };
    }

    private void ApplySearch()
    {
        var search = SettingsSearchBox.Text?.Trim() ?? "";
        foreach (var group in _groups) group.ApplySearch(search);
        NoMatchesText.IsVisible = _groups.All(g => !g.IsVisible);
    }

    private void ResetAdvanced_OnClick(object? sender, RoutedEventArgs e)
    {
        foreach (var field in Fields) field.Load(null);
    }

    /// <summary>Reads one field; an invalid one is revealed and focused so the search cannot hide the error.</summary>
    private string? ReadField(ProfileSettingField field)
    {
        try
        {
            return field.ReadValue();
        }
        catch (InvalidDataException)
        {
            if (!field.IsVisible)
            {
                SettingsSearchBox.Text = "";
                ApplySearch();
            }

            // Only text boxes can hold invalid values; focusing one after layout scrolls it into view.
            Dispatcher.UIThread.Post(() => GroupsList.GetVisualDescendants().OfType<TextBox>()
                                         .FirstOrDefault(box => box.DataContext == field)?.Focus(),
                                     DispatcherPriority.Background);

            throw;
        }
    }

    private void Cancel_OnClick(object? sender, RoutedEventArgs e) { Finished?.Invoke(this, EventArgs.Empty); }

    private async void SaveProfile_OnClick_Async(object? sender, RoutedEventArgs e)
    {
        try
        {
            var settings = new Dictionary<string, string>();

            foreach (var field in Fields)
                if (ReadField(field) is { } value)
                    settings[field.Setting.Id] = value;

            var profile = _profile with
            {
                Name = ProfileNameTextBox.Text?.Trim() ?? "",
                Description = DescriptionTextBox.Text?.Trim() ?? "",
                Dx11Upscaler = Dx11Box.SelectedItem as string ?? "auto",
                Dx12Upscaler = Dx12Box.SelectedItem as string ?? "auto",
                Sharpness = OverrideSharpnessBox.IsChecked == true ? SharpnessBox.Value : null,
                EnableLogging = LoggingBox.IsChecked == true,
                Settings = settings
            };
            ProfileIni.ValidateProfile(profile);
            ValidationText.Text = "";
            IsEnabled = false;
            if (SaveProfile is not null && await SaveProfile(profile))
                Finished?.Invoke(this, EventArgs.Empty);
            else
                ValidationText.Text = "The profile could not be saved. Your changes are still here; retry saving.";
        }
        catch (Exception ex)
        {
            ValidationText.Text = ex.Message;
        }
        finally
        {
            IsEnabled = true;
        }
    }
}
