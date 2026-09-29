using Avalonia.Controls;
using Avalonia.Controls.Templates;
using OptiscalerApp.ViewModels;
using OptiscalerApp.Views;

namespace OptiscalerApp;

/// <summary>Shows each page view model with its view; listed explicitly so nothing depends on reflection.</summary>
public sealed class ViewLocator : IDataTemplate
{
    public Control Build(object? data)
    {
        return data switch
        {
            GamesViewModel => new GamesView(),
            ManageGameViewModel => new ManageGameView(),
            ProfilesViewModel => new ProfilesView(),
            ProfileEditorViewModel => new ProfileEditorView(),
            SettingsViewModel => new SettingsView(),
            _ => new TextBlock { Text = $"No view for {data?.GetType().Name}" }
        };
    }

    public bool Match(object? data) { return data is ViewModelBase; }
}
