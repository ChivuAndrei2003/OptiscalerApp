using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Optiscaler.Core.Games;

namespace OptiscalerApp.Views;

public partial class ManageGameView : UserControl
{
    public ManageGameView()
    {
        InitializeComponent();
    }

    public ManageGameView(GameRecord game) : this()
    {
        GameNameText.Text = game.Name;
        GameCoverNameText.Text = game.Name;
        GamePathText.Text = game.Installations.Count > 0
            ? game.Installations[0].RootPath
            : "Installation path unavailable";
        GamePathText.SetValue(ToolTip.TipProperty, GamePathText.Text);
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

    private void setStableRelease_OnClick(object? sender, RoutedEventArgs e)
    {
        SetStableReleaseBtn.IsChecked = true;
        SetBetaReleaseBtn.IsChecked = false;
    }

    private void setBetaRelease_OnClick(object? sender, RoutedEventArgs e)
    {
        SetStableReleaseBtn.IsChecked = false;
        SetBetaReleaseBtn.IsChecked = true;
    }
}