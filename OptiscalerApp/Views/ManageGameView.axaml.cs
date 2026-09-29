using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Threading;
using OptiscalerApp.ViewModels;

namespace OptiscalerApp.Views;

/// <summary>Shows <see cref="ManageGameViewModel" />; owns only the responsive layout and focus.</summary>
public partial class ManageGameView : UserControl
{
    private ManageGameViewModel? _viewModel;

    public ManageGameView()
    {
        InitializeComponent();
        SizeChanged += (_, _) => UpdateLayoutColumns();
        OptionsGrid.SizeChanged += (_, _) => UpdateOptionColumns();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_viewModel is not null) _viewModel.PropertyChanged -= ViewModel_OnPropertyChanged;
        _viewModel = DataContext as ManageGameViewModel;
        if (_viewModel is not null) _viewModel.PropertyChanged += ViewModel_OnPropertyChanged;
    }

    private void ViewModel_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ManageGameViewModel.IsPreviewing) && _viewModel?.IsPreviewing == true)
            Dispatcher.UIThread.Post(() => PreviewPanel.BringIntoView(), DispatcherPriority.Loaded);
        else if (e.PropertyName == nameof(ManageGameViewModel.IsEditingDetails) &&
                 _viewModel?.IsEditingDetails == true)
            GameNameBox.Focus();
    }

    private void UpdateLayoutColumns()
    {
        var narrow = Bounds.Width < 760;
        var wide = Bounds.Width >= 1180;
        OperationPanel.ColumnDefinitions = new ColumnDefinitions(narrow ? "*" : wide ? "180,*,190" : "190,*");
        OperationPanel.RowDefinitions = new RowDefinitions(narrow ? "Auto,Auto,Auto" : "Auto,Auto");
        Grid.SetColumn(ManagementPanel, narrow ? 0 : 1);
        Grid.SetRow(ManagementPanel, narrow ? 1 : 0);
        Grid.SetColumn(GuidancePanel, wide ? 2 : 0);
        Grid.SetRow(GuidancePanel, narrow ? 2 : wide ? 0 : 1);
        Grid.SetColumnSpan(GuidancePanel, !narrow && !wide ? 2 : 1);
        var compactIdentity = narrow && Bounds.Width >= 520;
        IdentityPanel.ColumnDefinitions = new ColumnDefinitions(compactIdentity ? "140,*" : "*");
        IdentityPanel.RowDefinitions = new RowDefinitions(compactIdentity ? "Auto" : "Auto,Auto");
        Grid.SetColumn(IdentityDetails, compactIdentity ? 1 : 0);
        Grid.SetRow(IdentityDetails, compactIdentity ? 0 : 1);
        CoverPanel.Height = narrow ? 180 : wide ? 300 : 270;
        UpdateOptionColumns();
    }

    private void UpdateOptionColumns()
    {
        var columns = OptionsGrid.Bounds.Width >= 660 ? 3 : OptionsGrid.Bounds.Width >= 400 ? 2 : 1;

        if (OptionsGrid.ColumnDefinitions.Count == columns) return;

        OptionsGrid.ColumnDefinitions = new ColumnDefinitions(string.Join(",", Enumerable.Repeat("*", columns)));
        OptionsGrid.RowDefinitions =
            new RowDefinitions(string.Join(",",
                                           Enumerable.Repeat("Auto",
                                                             (OptionsGrid.Children.Count + columns - 1) / columns)));

        for (var i = 0; i < OptionsGrid.Children.Count; i++)
        {
            Grid.SetColumn(OptionsGrid.Children[i], i % columns);
            Grid.SetRow(OptionsGrid.Children[i], i / columns);
        }
    }
}
