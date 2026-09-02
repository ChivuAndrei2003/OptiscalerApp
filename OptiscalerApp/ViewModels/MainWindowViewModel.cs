using System.Threading;
using System.Threading.Tasks;
using Optiscaler.Infrastructure.Scanning;

namespace OptiscalerApp.ViewModels;

using Optiscaler.Core.Abstractions;
using Optiscaler.Core.Games;

public class MainWindowViewModel : ViewModelBase
{
    private readonly IGameCatalogRepository? _gameCatalogRepository;
    private readonly IAppConfigurationRepository? _configurationRepository;
    private readonly GameDiscoveryCoordinator? _gameDiscoveryCoordinator;

    public MainWindowViewModel()
    {
        Greeting = "Welcome to Avalonia!";
    }

    public MainWindowViewModel(IGameCatalogRepository gameCatalogRepository,
        IAppConfigurationRepository appConfigurationRepository, GameDiscoveryCoordinator gameDiscoveryCoordinator)
    {
        _gameCatalogRepository = gameCatalogRepository;
        _configurationRepository = appConfigurationRepository;
        _gameDiscoveryCoordinator = gameDiscoveryCoordinator;

        Greeting = "Welcome to Avalonia!";
    }

    public string Greeting { get; } = "Welcome to Avalonia!";
    public int SortIndex { get; set; }
    public bool SortDescending { get; set; }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (_gameCatalogRepository is null || _configurationRepository is null) return;

        var configuration = await _configurationRepository.LoadAsync(cancellationToken);

        var catalog = await _gameCatalogRepository.LoadAsync(cancellationToken);
    }
}