namespace OptiscalerApp.ViewModels;

public class MainWindowViewModel : ViewModelBase
{
    public string Greeting { get; } = "Welcome to Avalonia!";
    public int SortIndex { get; set; }
    public bool SortDescending { get; set; }
}