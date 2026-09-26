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
    private static readonly Option<bool?>[] SpoofOptions =
    [
        new("Default (on for AMD and Intel)", null), new("On", true),
        new("Off (the game sees your real GPU; no DLSS option)", false)
    ];

    private static readonly Option<bool?>[] OverlayOptions =
    [
        new("Default (blocked only with OptiFG)", null), new("Block (also blocks Steam Input)", true),
        new("Allow (keeps controllers and overlays working)", false)
    ];

    private static readonly Option<string>[] FrameGenInputs =
    [
        new("Default (none)", "auto"), new("Off", "nofg"), new("DLSS-G (games with DLSS Frame Generation)", "dlssg"),
        new("NVNGX FG (DLSS Enabler multi-frame)", "nvngxfg"), new("FSR FG (games with FSR 3.1 FG)", "fsrfg"),
        new("Upscaler (any game; may need Hudfix)", "upscaler"), new("FSR 3.0 FG", "fsrfg30")
    ];

    private static readonly Option<string>[] FrameGenOutputs =
    [
        new("Default (none)", "auto"), new("Off", "nofg"), new("FSR frame generation", "fsrfg"),
        new("XeSS frame generation (XeFG)", "xefg"), new("DLSS frame generation (needs Streamline)", "dlssg")
    ];

    private RenderProfile _profile = new();

    public NewProfileDialog()
    {
        InitializeComponent();
        Dx11Box.ItemsSource = ProfileIni.Dx11Options;
        Dx12Box.ItemsSource = ProfileIni.Dx12Options;
        VulkanBox.ItemsSource = ProfileIni.VulkanOptions;
        SpoofBox.ItemsSource = SpoofOptions;
        OverlaysBox.ItemsSource = OverlayOptions;
        FrameGenInputBox.ItemsSource = FrameGenInputs;
        FrameGenOutputBox.ItemsSource = FrameGenOutputs;
        OverlayKeyBox.ItemsSource = Keys("Default (Insert)");
        FrameGenKeyBox.ItemsSource = Keys("Default (End)");
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
        VulkanBox.SelectedItem = profile.VulkanUpscaler;
        OverrideSharpnessBox.IsChecked = profile.Sharpness is not null;
        SharpnessBox.Value = profile.Sharpness ?? 0.5m;
        Select(SpoofBox, profile.SpoofDxgi);
        Select(OverlaysBox, profile.DisableOverlays);
        Select(FrameGenInputBox, profile.FrameGenInput);
        Select(FrameGenOutputBox, profile.FrameGenOutput);
        Select(OverlayKeyBox, profile.OverlayKey);
        Select(FrameGenKeyBox, profile.FrameGenKey);
        ReshadeBox.IsChecked = profile.LoadReshade;
        SpecialKBox.IsChecked = profile.LoadSpecialK;
        FramerateLimitEnabledBox.IsChecked = profile.FramerateLimit is not null;
        FramerateLimitBox.Value = profile.FramerateLimit ?? 60;
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

    private static Option<int?>[] Keys(string defaultLabel)
    {
        return [new(defaultLabel, null), ..ProfileIni.ShortcutKeys.Select(k => new Option<int?>(k.Label, k.Code))];
    }

    private static void Select<T>(ComboBox box, T value)
    {
        box.SelectedItem = box.ItemsSource?.OfType<Option<T>>().FirstOrDefault(o => Equals(o.Value, value));
    }

    private static T Selected<T>(ComboBox box, T fallback)
    {
        return box.SelectedItem is Option<T> option ? option.Value : fallback;
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
                VulkanUpscaler = VulkanBox.SelectedItem as string ?? "auto",
                Sharpness = OverrideSharpnessBox.IsChecked == true ? SharpnessBox.Value : null,
                SpoofDxgi = Selected<bool?>(SpoofBox, null),
                DisableOverlays = Selected<bool?>(OverlaysBox, null),
                FrameGenInput = Selected(FrameGenInputBox, "auto"),
                FrameGenOutput = Selected(FrameGenOutputBox, "auto"),
                OverlayKey = Selected<int?>(OverlayKeyBox, null),
                FrameGenKey = Selected<int?>(FrameGenKeyBox, null),
                LoadReshade = ReshadeBox.IsChecked == true,
                LoadSpecialK = SpecialKBox.IsChecked == true,
                FramerateLimit = FramerateLimitEnabledBox.IsChecked == true ? FramerateLimitBox.Value : null,
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

    /// <summary>A labeled value for a combo box, so friendly text can stand for an INI value.</summary>
    private sealed record Option<T>(string Label, T Value)
    {
        public override string ToString() { return Label; }
    }
}
