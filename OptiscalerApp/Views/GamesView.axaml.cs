using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using OptiscalerApp.ViewModels;

namespace OptiscalerApp.Views;

public partial class GamesView : UserControl
{
    public GamesView() { InitializeComponent(); }

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
        if (DataContext is not GamesViewModel vm) return;

        MenuItem Item(string header, ICommand command)
        {
            return new MenuItem { Header = header, Command = command, CommandParameter = card, IsEnabled = vm.CanAddGames };
        }

        // Removing needs a second click; the game's files are never touched either way.
        var remove = new MenuItem
        {
            Header = "Remove from library",
            IsEnabled = vm.CanAddGames,
            ItemsSource = new[] { Item("Confirm remove", vm.RemoveGameCommand) }
        };
        new ContextMenu
        {
            ItemsSource = new object[]
            {
                Item("Manage game", vm.OpenGameCommand),
                Item(card.FavoriteMenuText, vm.ToggleFavoriteCommand),
                Item(card.HiddenMenuText, vm.ToggleHiddenCommand),
                new Separator(),
                remove
            }
        }.Open(target);
    }
}
