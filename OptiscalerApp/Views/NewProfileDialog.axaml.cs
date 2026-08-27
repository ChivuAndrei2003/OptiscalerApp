using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;


namespace OptiscalerApp.Views;

public partial class NewProfileDialog : UserControl
{
    private bool? _usesSingleColumn;

    public NewProfileDialog()
    {
        InitializeComponent();
    }

    public string ProfileName => ProfileNameTextBox.Text?.Trim() ?? string.Empty;

    public string ProfileDescription => DescriptionTextBox.Text?.Trim() ?? string.Empty;

    public event EventHandler? CancelRequested;

    public event EventHandler<ProfileCreatedEventArgs>? ProfileCreated;

    public void ResetForm()
    {
        ProfileNameTextBox.Text = string.Empty;
        DescriptionTextBox.Text = string.Empty;
        PluginPathTextBox.Text = string.Empty;
        EasyModeButton.IsChecked = true;
        AdvancedModeButton.IsChecked = false;
        ValidationText.IsVisible = false;
        CreateProfileButton.IsEnabled = false;
    }

    private void ProfileName_OnTextChanged(object? sender, TextChangedEventArgs e)
    {
        CreateProfileButton.IsEnabled = !string.IsNullOrWhiteSpace(ProfileNameTextBox.Text);
        ValidationText.IsVisible = false;
    }

    private void EasyMode_OnClick(object? sender, RoutedEventArgs e)
    {
        EasyModeButton.IsChecked = true;
        AdvancedModeButton.IsChecked = false;
    }

    private void AdvancedMode_OnClick(object? sender, RoutedEventArgs e)
    {
        EasyModeButton.IsChecked = false;
        AdvancedModeButton.IsChecked = true;
    }

    private void SectionsGrid_OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        var useSingleColumn = e.NewSize.Width < 680;
        if (_usesSingleColumn == useSingleColumn) return;

        _usesSingleColumn = useSingleColumn;
        SectionsGrid.ColumnDefinitions = new ColumnDefinitions(useSingleColumn ? "*" : "*,*");

        if (useSingleColumn)
        {
            PlaceSection(BasicSection, row: 0, column: 0);
            PlaceSection(PerformanceSection, row: 1, column: 0);
            PlaceSection(SharpnessSection, row: 2, column: 0);
            PlaceSection(PluginsSection, row: 3, column: 0);
            PlaceSection(SpoofingSection, row: 4, column: 0);
            Grid.SetColumnSpan(SpoofingSection, 1);
            return;
        }

        PlaceSection(BasicSection, row: 0, column: 0);
        PlaceSection(PerformanceSection, row: 0, column: 1);
        PlaceSection(SharpnessSection, row: 1, column: 0);
        PlaceSection(PluginsSection, row: 1, column: 1);
        PlaceSection(SpoofingSection, row: 2, column: 0);
        Grid.SetColumnSpan(SpoofingSection, 2);
    }

    private static void PlaceSection(Control section, int row, int column)
    {
        Grid.SetRow(section, row);
        Grid.SetColumn(section, column);
    }

    private async void BrowsePlugins_OnClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel is null) return;

            var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Select plugins folder",
                AllowMultiple = false
            });

            var folder = folders.FirstOrDefault();
            if (folder is not null) PluginPathTextBox.Text = folder.Path.LocalPath;
        }
        catch (Exception )
        {
            throw ; //todo
        }
    }

    private void Cancel_OnClick(object? sender, RoutedEventArgs e)
    {
        CancelRequested?.Invoke(this, EventArgs.Empty);
    }

    private void CreateProfile_OnClick(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ProfileNameTextBox.Text))
        {
            ValidationText.IsVisible = true;
            return;
        }

        ProfileCreated?.Invoke(
            this,
            new ProfileCreatedEventArgs(ProfileName, ProfileDescription));
    }
}

public sealed class ProfileCreatedEventArgs(string profileName, string profileDescription) : EventArgs
{
    public string ProfileName { get; } = profileName;

    public string ProfileDescription { get; } = profileDescription;
}
