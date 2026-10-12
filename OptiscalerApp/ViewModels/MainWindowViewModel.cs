using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace OptiscalerApp.ViewModels;

/// <summary>The window shell: which page is shown and the sidebar that switches between them.</summary>
public sealed partial class MainWindowViewModel : ViewModelBase
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGamesPage), nameof(IsProfilesPage), nameof(IsSettingsPage), nameof(IsHelpPage))]
    private ViewModelBase _currentPage;

    [ObservableProperty] private bool _isPaneOpen = true;

    public MainWindowViewModel(GamesViewModel games, ProfilesViewModel profiles, SettingsViewModel settings,
                               HelpViewModel help)
    {
        Games = games;
        Profiles = profiles;
        Settings = settings;
        Help = help;
        _currentPage = games;
        games.ManageRequested += ShowManageGame;
    }

    public GamesViewModel Games { get; }

    public ProfilesViewModel Profiles { get; }

    public SettingsViewModel Settings { get; }

    public HelpViewModel Help { get; }

    /// <summary>Managing a game is part of the Games section, so its navigation button stays active.</summary>
    public bool IsGamesPage => CurrentPage is GamesViewModel or ManageGameViewModel;

    public bool IsProfilesPage => CurrentPage == Profiles;

    public bool IsSettingsPage => CurrentPage == Settings;

    public bool IsHelpPage => CurrentPage == Help;

    /// <summary>Loads the library, then refreshes the wiki list in the background and scans if the user asked to.</summary>
    public async Task InitializeAsync()
    {
        await Games.LoadGameLibraryAsync();

        // The wiki list may need a download; the library is already usable while it runs.
        _ = Games.RefreshCompatibilityAsync();
        await Games.ScanIfAutomaticAsync();
    }

    [RelayCommand]
    private void ShowGames()
    {
        CurrentPage = Games;

        // Returning from Manage Game may follow an install or restore.
        if (Games.IsLoaded) _ = Games.RefreshLibraryStatusAsync();
    }

    [RelayCommand]
    private Task ShowProfiles()
    {
        CurrentPage = Profiles;

        return Profiles.LoadProfilesAsync();
    }

    [RelayCommand]
    private Task ShowSettings()
    {
        CurrentPage = Settings;

        return Settings.LoadCommand.ExecuteAsync(null);
    }

    [RelayCommand]
    private void ShowHelp() { CurrentPage = Help; }

    [RelayCommand]
    private void TogglePane() { IsPaneOpen = !IsPaneOpen; }

    /// <summary>Steps through the sidebar's pages in order, as a controller's shoulder buttons do.</summary>
    public void ShowAdjacentPage(int step)
    {
        ICommand[] pages = [ShowGamesCommand, ShowProfilesCommand, ShowHelpCommand, ShowSettingsCommand];
        var current = IsGamesPage ? 0 : IsProfilesPage ? 1 : IsHelpPage ? 2 : 3;
        var next = Math.Clamp(current + step, 0, pages.Length - 1);
        if (next != current) pages[next].Execute(null);
    }

    /// <summary>
    ///     Returns from Manage Game to the library, closing an open preview first; what Escape and a controller's B
    ///     button ask for.
    /// </summary>
    public bool GoBack()
    {
        if (CurrentPage is not ManageGameViewModel game || !game.BackCommand.CanExecute(null)) return false;

        if (game.IsPreviewing)
            game.CancelPreviewCommand.Execute(null);
        else
            game.BackCommand.Execute(null);

        return true;
    }

    private void ShowManageGame(ManageGameViewModel page)
    {
        page.Closed += (_, _) => ShowGames();
        CurrentPage = page;
        page.LoadCommand.Execute(null);
    }
}
