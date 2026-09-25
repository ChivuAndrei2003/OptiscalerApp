using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using OptiscalerApp.ViewModels;

namespace OptiscalerApp.Views;

public partial class GamesView : UserControl
{
    public GamesView() { InitializeComponent(); }

    private async void AddGames_Click_Async(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel || !viewModel.CanAddGames) return;

        var topLevel = TopLevel.GetTopLevel(this);

        if (topLevel is null) return;

        try
        {
            var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Select game folders",
                AllowMultiple = true
            });

            try
            {
                if (folders.Count == 0) return;

                var paths = folders.Select(folder => folder.TryGetLocalPath()).OfType<string>().ToArray();

                if (paths.Length != folders.Count)
                {
                    viewModel.StatusMessage = "Select folders available on this computer.";

                    return;
                }

                await viewModel.AddManualGames_Async(paths);
            }
            finally
            {
                foreach (var folder in folders) folder.Dispose();
            }
        }
        catch (Exception exception)
        {
            viewModel.StatusMessage = $"Could not open the selected folders: {exception.Message}";
        }
    }

    private async void ScanGames_OnClick_Async(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm) await vm.ScanGameLibrary_Async();
    }

    private async void CheckUpdates_OnClick_Async(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm) await vm.CheckForUpdates_Async();
    }

    private void ManageGame_OnClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.DataContext is GameCardViewModel card &&
            TopLevel.GetTopLevel(this) is MainWindow mainWindow)
            mainWindow.ShowManageGame(card.Game);
    }

    private void CardMenu_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: GameCardViewModel card } control) OpenCardMenu(control, card);
    }

    private void Card_OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (sender is not Control { DataContext: GameCardViewModel card } control) return;

        e.Handled = true;
        OpenCardMenu(control, card);
    }

    /// <summary>Built per card, so every action targets the card that was clicked.</summary>
    private void OpenCardMenu(Control target, GameCardViewModel card)
    {
        if (DataContext is not MainWindowViewModel vm) return;

        MenuItem Item(string header, Func<Task> action)
        {
            var item = new MenuItem { Header = header, IsEnabled = vm.CanAddGames };
            item.Click += async (_, _) => await action();

            return item;
        }

        // Removing needs a second click; the game's files are never touched either way.
        var remove = new MenuItem
        {
            Header = "Remove from library",
            IsEnabled = vm.CanAddGames,
            ItemsSource = new[] { Item("Confirm remove", () => vm.RemoveGame_Async(card.Game.Id)) }
        };
        var menu = new ContextMenu
        {
            ItemsSource = new object[]
            {
                Item("Manage game", () =>
                {
                    if (TopLevel.GetTopLevel(this) is MainWindow window) window.ShowManageGame(card.Game);

                    return Task.CompletedTask;
                }),
                Item(card.FavoriteMenuText, () => vm.SetFavorite_Async(card.Game.Id, !card.IsFavorite)),
                Item(card.HiddenMenuText, () => vm.SetHidden_Async(card.Game.Id, !card.IsHidden)),
                new Separator(),
                remove
            }
        };
        menu.Open(target);
    }
}