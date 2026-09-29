using Avalonia.Controls;
using Avalonia.Interactivity;

namespace OptiscalerApp.Views;

public partial class ProfilesView : UserControl
{
    public ProfilesView() { InitializeComponent(); }

    private void HideDeleteConfirmation(object? sender, RoutedEventArgs e) { DeleteButton.Flyout?.Hide(); }
}
