using CommunityToolkit.Mvvm.ComponentModel;
using OptiscalerApp.Management;

namespace OptiscalerApp.ViewModels;

/// <summary>Editable value of one optional OptiScaler.ini setting; the default choice or an empty box means auto.</summary>
public sealed partial class ProfileSettingField : ObservableObject
{
    private static readonly ProfileSettingOption AutoOption = new("", "Default (auto)");

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsOverridden))]
    private ProfileSettingOption? _selectedChoice = AutoOption;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsOverridden))]
    private string _text = "";

    [ObservableProperty] private bool _isVisible = true;

    public ProfileSettingField(ProfileSetting setting, string? value)
    {
        Setting = setting;
        Choices = [AutoOption, ..setting.Choices];
        Load(value);
    }

    public ProfileSetting Setting { get; }

    public IReadOnlyList<ProfileSettingOption> Choices { get; }

    public bool UsesChoices => Setting.Kind is ProfileSettingKind.Toggle or ProfileSettingKind.Choice;

    public bool UsesText => !UsesChoices;

    public string Placeholder => Setting.Kind == ProfileSettingKind.Number
        ? $"Default (auto) · {Setting.RangeText}"
        : "Default (auto)";

    public bool IsOverridden => RawValue is not null;

    /// <summary>The entered value before validation, or null when the setting stays on auto.</summary>
    private string? RawValue => UsesChoices
        ? SelectedChoice is { Value.Length: > 0 } choice ? choice.Value : null
        : string.IsNullOrWhiteSpace(Text) ? null : Text.Trim();

    public void Load(string? value)
    {
        SelectedChoice = Choices.FirstOrDefault(c => c.Value == value) ?? AutoOption;
        Text = UsesText ? value ?? "" : "";
    }

    /// <summary>Returns the canonical value, null for auto, or throws when the entered value is invalid.</summary>
    public string? ReadValue()
    {
        if (RawValue is not { } raw) return null;

        var message = Setting.Kind == ProfileSettingKind.Number
            ? $"{Setting.Label} must be a number from {Setting.RangeText}, using a decimal point."
            : $"Invalid value for {Setting.Label}.";

        return Setting.Normalize(raw) ?? throw new InvalidDataException(message);
    }

    public bool Matches(string search)
    {
        return search.Length == 0 || Setting.Label.Contains(search, StringComparison.OrdinalIgnoreCase) ||
               Setting.Description.Contains(search, StringComparison.OrdinalIgnoreCase) ||
               Setting.Id.Contains(search, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed partial class ProfileSettingGroup(string name, IReadOnlyList<ProfileSettingField> fields)
    : ObservableObject
{
    [ObservableProperty] private bool _isVisible = true;

    public string Name { get; } = name;

    public IReadOnlyList<ProfileSettingField> Fields { get; } = fields;

    public static IReadOnlyList<ProfileSettingGroup> Create(IReadOnlyDictionary<string, string> values)
    {
        return ProfileSettings.All.GroupBy(s => s.Group)
            .Select(g => new ProfileSettingGroup(g.Key, g.Select(s => new ProfileSettingField(s, values.GetValueOrDefault(s.Id))).ToList()))
            .ToList();
    }

    public void ApplySearch(string search)
    {
        foreach (var field in Fields) field.IsVisible = field.Matches(search);
        IsVisible = Fields.Any(f => f.IsVisible);
    }
}
