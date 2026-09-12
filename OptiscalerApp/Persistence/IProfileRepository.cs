using OptiscalerApp.Models;

namespace OptiscalerApp.Persistence;

public interface IProfileRepository
{
    Task<ProfileCatalog> LoadProfileCatalog_Async(CancellationToken cancellationToken = default);
    Task SaveProfileCatalog_Async(ProfileCatalog catalog, CancellationToken cancellationToken = default);
}
