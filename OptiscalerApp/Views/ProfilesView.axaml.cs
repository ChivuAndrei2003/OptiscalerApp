using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace OptiscalerApp.Views;

public partial class ProfilesView : UserControl
{
    public ProfilesView()
    {
        InitializeComponent();
    }

    private void NewProfile_OnClick(object? sender, RoutedEventArgs e)
    {
        var page = new NewProfileDialog();
        page.CancelRequested += NewProfilePage_OnCancelRequested;
        page.ProfileCreated += NewProfilePage_OnProfileCreated;

        NewProfileHost.Content = page;
        ProfilesOverview.IsVisible = false;
        NewProfileHost.IsVisible = true;
    }

    private void NewProfilePage_OnCancelRequested(object? sender, EventArgs e)
    {
        ShowProfilesOverview();
    }

    private void NewProfilePage_OnProfileCreated(object? sender, ProfileCreatedEventArgs e)
    {
        AddDraftProfile(e.ProfileName, e.ProfileDescription);
        ShowProfilesOverview();
    }

    private void ShowProfilesOverview()
    {
        NewProfileHost.IsVisible = false;
        NewProfileHost.Content = null;
        ProfilesOverview.IsVisible = true;
    }

    private void AddDraftProfile(string profileName, string description)
    {
        var title = new TextBlock { Text = profileName };
        title.Classes.Add("h3");

        var summary = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(description)
                ? "Custom OptiScaler configuration profile."
                : description
        };
        summary.Classes.Add("caption");

        var profileInfo = new StackPanel { Spacing = 4 };
        profileInfo.Children.Add(title);
        profileInfo.Children.Add(summary);

        var status = new TextBlock
        {
            Text = "Draft",
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };
        status.Classes.Add("caption");
        Grid.SetColumn(status, 1);

        var content = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 12
        };
        content.Children.Add(profileInfo);
        content.Children.Add(status);

        var card = new Border
        {
            Padding = new Thickness(14),
            Child = content
        };
        card.Classes.Add("softCard");
        ProfileListPanel.Children.Add(card);
    }
}
