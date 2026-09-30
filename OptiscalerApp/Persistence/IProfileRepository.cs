using OptiscalerApp.Models;

namespace OptiscalerApp.Persistence;

public interface IProfileRepository
{
    Task<ProfileCatalog> LoadProfileCatalogAsync(CancellationToken cancellationToken = default);
    Task SaveProfileCatalogAsync(ProfileCatalog catalog, CancellationToken cancellationToken = default);
}