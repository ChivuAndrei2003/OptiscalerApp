namespace OptiscalerApp.ViewModels;

public class MainWindowViewModel : ViewModelBase
{
    public string Greeting { get; } = "Welcome to Avalonia!";
    public object SortProperty { get; }
    public object SortDescending { get; }
}