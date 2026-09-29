using Avalonia.Controls;
using OptiscalerApp.ViewModels;

namespace OptiscalerApp.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Opened += async (_, _) =>
        {
            if (DataContext is MainWindowViewModel viewModel) await viewModel.Initialize_Async();
        };
    }
}
