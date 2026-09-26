using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using OptiscalerApp.Management;
using OptiscalerApp.Models;
using OptiscalerApp.ViewModels;

namespace OptiscalerApp.Views;

public partial class ProfilesView : UserControl
{
    public ProfilesView()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            if (DataContext is ProfilesViewModel vm) await vm.LoadProfiles_Async();
        };
    }

    private void NewProfile_OnClick(object? sender, RoutedEventArgs e) { ShowEditor(null); }

    private async void Import_OnClick_Async(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ProfilesViewModel { CanCreate: true } vm ||
            TopLevel.GetTopLevel(this) is not { } top)
            return;

        try
        {
            var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Import OptiScaler.ini",
                AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType("INI configuration") { Patterns = ["*.ini"] }]
            });

            if (files.Count == 0) return;

            await using var stream = await files[0].OpenReadAsync();

            // OptiScaler.ini is well under 100 KB; refuse anything that is clearly not one.
            if (stream.CanSeek && stream.Length > 1024 * 1024)
                throw new InvalidDataException("The file is too large to be an OptiScaler.ini.");

            using var reader = new StreamReader(stream);
            var profile = ProfileIni.ReadProfileFromIni(await reader.ReadToEndAsync(),
                                                        Path.GetFileNameWithoutExtension(files[0].Name));
            ShowEditor(profile, false);
            vm.StatusMessage = $"Imported {files[0].Name}. Review the values, then save the profile.";
        }
        catch (Exception ex)
        {
            vm.StatusMessage = $"Could not import profile: {ex.Message}";
        }
    }

    private void Edit_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ProfilesViewModel { CanEdit: true } vm) ShowEditor(vm.SelectedProfile);
    }

    private void ShowEditor(RenderProfile? profile, bool editing = true)
    {
        if (DataContext is not ProfilesViewModel { CanCreate: true } vm) return;

        var editor = new NewProfileDialog { SaveProfile = vm.SaveProfile_Async };
        if (profile is not null) editor.SetProfile(profile, editing);
        editor.Finished += (_, _) =>
        {
            NewProfileHost.IsVisible = false;
            NewProfileHost.Content = null;
            ProfilesOverview.IsVisible = true;
        };
        NewProfileHost.Content = editor;
        ProfilesOverview.IsVisible = false;
        NewProfileHost.IsVisible = true;
    }

    private async void SetDefault_OnClick_Async(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ProfilesViewModel vm) await vm.SetDefaultProfile_Async();
    }

    private async void Duplicate_OnClick_Async(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ProfilesViewModel vm) await vm.DuplicateProfile_Async();
    }

    private async void Delete_OnClick_Async(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ProfilesViewModel vm) await vm.DeleteProfile_Async();
    }

    private async void Export_OnClick_Async(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ProfilesViewModel { CanEdit: true, SelectedProfile: { } profile } vm ||
            TopLevel.GetTopLevel(this) is not { } top)
            return;

        vm.IsBusy = true;

        try
        {
            using var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title =
                    "Export profile configuration",
                SuggestedFileName = "OptiScaler.ini",
                DefaultExtension = "ini",
                FileTypeChoices =
                [
                    new
                        FilePickerFileType("INI configuration") { Patterns = ["*.ini"] }
                ]
            });

            if (file is null) return;

            await using var stream = await file.OpenWriteAsync();
            if (stream.CanSeek) stream.SetLength(0);
            await using var writer = new StreamWriter(stream);
            await writer.WriteAsync(ProfileIni.ApplyProfileToIni("", profile));
            await writer.FlushAsync();
            vm.StatusMessage = $"Exported {profile.Name}.";
        }
        catch (Exception ex)
        {
            vm.StatusMessage = $"Could not export profile: {ex.Message}";
        }
        finally
        {
            vm.IsBusy = false;
        }
    }
}