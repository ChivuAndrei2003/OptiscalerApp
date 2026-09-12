using OptiscalerApp.Models;

namespace OptiscalerApp.Persistence;

public interface IAppConfigurationRepository
{
    Task<AppConfiguration> LoadAppConfiguration_Async(CancellationToken cancellationToken = default);

    Task SaveAppConfiguration_Async(AppConfiguration configuration, CancellationToken cancellationToken = default);
}
