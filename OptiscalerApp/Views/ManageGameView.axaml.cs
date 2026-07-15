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
        GameCoverNameText.Text = gameName;
        GamePathText.Text = $"~/Games/{gameName}";
    }

    private void BackToGames_OnClick(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is MainWindow mainWindow) mainWindow.ShowGames();
    }

    private void Inject_OnClick(object? sender, RoutedEventArgs e)
    {
        SetInstallationStatus("Auto install ready", "BrAccent");
    }

    private void ManualInstall_OnClick(object? sender, RoutedEventArgs e)
    {
        SetInstallationStatus("Manual package ready", "BrWarning");
    }

    private void SetInstallationStatus(string status, string brushResource)
    {
        var statusBrush = Application.Current?.FindResource(brushResource) as IBrush;

        InjectionStatusText.Text = status;
        InjectionStatusText.Foreground = statusBrush;
        InjectionStatusDot.Foreground = statusBrush;
    }
}
