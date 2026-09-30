using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace OptiscalerApp.ViewModels;

/// <summary>The window shell: which page is shown and the sidebar that switches between them.</summary>
public sealed partial class MainWindowViewModel : ViewModelBase
{
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsGamesPage), nameof(IsProfilesPage), nameof(IsSettingsPage))]
    private ViewModelBase _currentPage;

    [ObservableProperty] private bool _isPaneOpen = true;

    public MainWindowViewModel(GamesViewModel games, ProfilesViewModel profiles, SettingsViewModel settings)
    {
        Games = games;
        Profiles = profiles;
        Settings = settings;
        _currentPage = games;
        games.ManageRequested += ShowManageGame;
    }

    public GamesViewModel Games { get; }

    public ProfilesViewModel Profiles { get; }

    public SettingsViewModel Settings { get; }

    /// <summary>Managing a game is part of the Games section, so its navigation button stays active.</summary>
    public bool IsGamesPage => CurrentPage is GamesViewModel or ManageGameViewModel;

    public bool IsProfilesPage => CurrentPage == Profiles;

    public bool IsSettingsPage => CurrentPage == Settings;

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
    private void TogglePane() { IsPaneOpen = !IsPaneOpen; }

    private void ShowManageGame(ManageGameViewModel page)
    {
        page.Closed += (_, _) => ShowGames();
        CurrentPage = page;
        page.LoadCommand.Execute(null);
    }
}
