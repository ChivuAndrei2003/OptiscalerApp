using Avalonia.Controls;
using Avalonia.Input;
using OptiscalerApp.ViewModels;

namespace OptiscalerApp.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Opened += async (_, _) =>
        {
            if (DataContext is MainWindowViewModel viewModel) await viewModel.InitializeAsync();
        };
    }

    /// <summary>
    ///     Escape that no menu or drop-down wanted goes back a page. A text box does not take the key itself, so it
    ///     is left alone there: what is being typed must not be lost. A controller's B has no such doubt.
    /// </summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!e.Handled && e.Key == Key.Escape &&
            (e.Source is not TextBox || e.KeyDeviceType == KeyDeviceType.Gamepad) &&
            DataContext is MainWindowViewModel viewModel)
            e.Handled = viewModel.GoBack();
    }
}
