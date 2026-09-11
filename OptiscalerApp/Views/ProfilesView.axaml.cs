using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Optiscaler.Core.Management;
using Optiscaler.Infrastructure.Management;
using OptiscalerApp.ViewModels;

namespace OptiscalerApp.Views;

public partial class ProfilesView : UserControl
{
    public ProfilesView()
    {
        InitializeComponent();
        Loaded += async (_, _) => { if (DataContext is ProfilesViewModel vm) await vm.LoadProfiles_Async(); };
    }
    private void NewProfile_OnClick(object? sender, RoutedEventArgs e) => ShowEditor(null);
    private void Edit_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ProfilesViewModel { CanEdit: true } vm) ShowEditor(vm.SelectedProfile);
    }
    private void ShowEditor(RenderProfile? profile)
    {
        if (DataContext is not ProfilesViewModel { CanCreate: true } vm) return;
        var editor = new NewProfileDialog { SaveProfile = vm.SaveProfile_Async };
        if (profile is not null) editor.SetProfile(profile, true);
        editor.Finished += (_, _) => { NewProfileHost.IsVisible = false; NewProfileHost.Content = null; ProfilesOverview.IsVisible = true; };
        NewProfileHost.Content = editor;
        ProfilesOverview.IsVisible = false;
        NewProfileHost.IsVisible = true;
    }
    private async void SetDefault_OnClick_Async(object? sender, RoutedEventArgs e)
    { if (DataContext is ProfilesViewModel vm) await vm.SetDefaultProfile_Async(); }
    private async void Duplicate_OnClick_Async(object? sender, RoutedEventArgs e)
    { if (DataContext is ProfilesViewModel vm) await vm.DuplicateProfile_Async(); }
    private async void Delete_OnClick_Async(object? sender, RoutedEventArgs e)
    { if (DataContext is ProfilesViewModel vm) await vm.DeleteProfile_Async(); }
    private async void Export_OnClick_Async(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ProfilesViewModel { CanEdit: true, SelectedProfile: { } profile } vm || TopLevel.GetTopLevel(this) is not { } top) return;
        vm.IsBusy = true;
        try
        {
            using var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export profile configuration", SuggestedFileName = "OptiScaler.ini", DefaultExtension = "ini",
                FileTypeChoices = [new FilePickerFileType("INI configuration") { Patterns = ["*.ini"] }]
            });
            if (file is null) return;
            await using var stream = await file.OpenWriteAsync();
            if (stream.CanSeek) stream.SetLength(0);
            await using var writer = new StreamWriter(stream);
            await writer.WriteAsync(ProfileIni.ApplyProfileToIni("", profile));
            await writer.FlushAsync();
            vm.StatusMessage = $"Exported {profile.Name}.";
        }
        catch (Exception ex) { vm.StatusMessage = $"Could not export profile: {ex.Message}"; }
        finally { vm.IsBusy = false; }
    }
}
