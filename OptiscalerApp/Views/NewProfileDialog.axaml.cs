using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Optiscaler.Core.Management;
using Optiscaler.Infrastructure.Management;

namespace OptiscalerApp.Views;

public partial class NewProfileDialog : UserControl
{
    private RenderProfile _profile = new();
    public Func<RenderProfile, Task<bool>>? SaveProfile { get; set; }
    public event EventHandler? Finished;
    public NewProfileDialog()
    {
        InitializeComponent();
        Dx11Box.ItemsSource = ProfileIni.Dx11Options;
        Dx12Box.ItemsSource = ProfileIni.Dx12Options;
        SetProfile(new RenderProfile { Name = "" });
    }
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
    }
    private void Cancel_OnClick(object? sender, RoutedEventArgs e) => Finished?.Invoke(this, EventArgs.Empty);
    private async void SaveProfile_OnClick_Async(object? sender, RoutedEventArgs e)
    {
        try
        {
            var profile = _profile with
            {
                Name = ProfileNameTextBox.Text?.Trim() ?? "",
                Description = DescriptionTextBox.Text?.Trim() ?? "",
                Dx11Upscaler = Dx11Box.SelectedItem as string ?? "auto",
                Dx12Upscaler = Dx12Box.SelectedItem as string ?? "auto",
                Sharpness = OverrideSharpnessBox.IsChecked == true ? SharpnessBox.Value : null,
                EnableLogging = LoggingBox.IsChecked == true
            };
            ProfileIni.ValidateProfile(profile);
            IsEnabled = false;
            if (SaveProfile is not null && await SaveProfile(profile)) Finished?.Invoke(this, EventArgs.Empty);
            else ValidationText.Text = "The profile could not be saved. Your changes are still here; retry saving.";
        }
        catch (Exception ex) { ValidationText.Text = ex.Message; }
        finally { IsEnabled = true; }
    }
}
