using OptiscalerApp.Models;

namespace OptiscalerApp.Persistence;

public interface IAppConfigurationRepository
{
    Task<AppConfiguration> LoadAppConfigurationAsync(CancellationToken cancellationToken = default);

    Task SaveAppConfigurationAsync(AppConfiguration configuration, CancellationToken cancellationToken = default);
}