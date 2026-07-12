using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace OptiscalerApp.Views;

public partial class ManageGameView : UserControl
{
    public ManageGameView()
        : this("Selected game")
    {
    }

    public ManageGameView(string gameName)
    {
        InitializeComponent();
        GameNameText.Text = gameName;
    }

    private void BackToGames_OnClick(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is MainWindow mainWindow) mainWindow.ShowGames();
    }

    private void Inject_OnClick(object? sender, RoutedEventArgs e)
    {
        InjectionStatusText.Text = "Ready to inject";
        InjectionStatusText.Foreground = Application.Current?.FindResource("BrAccent") as IBrush;
    }

    private void StopInjection_OnClick(object? sender, RoutedEventArgs e)
    {
        InjectionStatusText.Text = "Not injected";
        InjectionStatusText.Foreground = Application.Current?.FindResource("BrWarning") as IBrush;
    }
}