using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OptiscalerApp.ViewModels;

namespace OptiscalerApp.Views;

public partial class ProfileEditorView : UserControl
{
    private ProfileEditorViewModel? _viewModel;

    public ProfileEditorView() { InitializeComponent(); }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_viewModel is not null) _viewModel.InvalidFieldShown -= FocusField;
        _viewModel = DataContext as ProfileEditorViewModel;
        if (_viewModel is not null) _viewModel.InvalidFieldShown += FocusField;
    }

    /// <summary>Only text boxes can hold invalid values; focusing one after layout scrolls it into view.</summary>
    private void FocusField(ProfileSettingField field)
    {
        Dispatcher.UIThread.Post(() => GroupsList.GetVisualDescendants().OfType<TextBox>()
                                     .FirstOrDefault(box => box.DataContext == field)?.Focus(),
                                 DispatcherPriority.Background);
    }
}
