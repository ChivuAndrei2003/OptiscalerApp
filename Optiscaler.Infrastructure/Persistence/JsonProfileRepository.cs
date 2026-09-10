using Optiscaler.Core.Abstractions;
using Optiscaler.Core.Management;
using Optiscaler.Infrastructure.Management;

namespace Optiscaler.Infrastructure.Persistence;

/// <summary>Persists validated profiles with the same backup recovery as settings and the game catalog.</summary>
public sealed class JsonProfileRepository(IAppPaths paths) : IProfileRepository
{
    private readonly AtomicJsonFile<ProfileCatalog> _store = new(
                                                                 Path.Combine(paths.RootDirectory, "profiles.json"),
                                                                 OptiscalerJsonContext.Default.ProfileCatalog,
                                                                 ValidateProfileCatalog);

    public async Task<ProfileCatalog> LoadProfileCatalog_Async(CancellationToken cancellationToken = default)
    {
        var catalog = await _store.LoadJsonFile_Async(cancellationToken).ConfigureAwait(false) ?? new ProfileCatalog();
        ValidateProfileCatalog(catalog);

        return catalog;
    }

    public Task SaveProfileCatalog_Async(ProfileCatalog catalog, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ValidateProfileCatalog(catalog);

        return _store.SaveJsonFile_Async(catalog, cancellationToken);
    }

    private static void ValidateProfileCatalog(ProfileCatalog catalog)
    {
        if (catalog.SchemaVersion != 1 || catalog.Profiles is null || catalog.Profiles.Any(p => p is null))
            throw new InvalidDataException("Invalid or unsupported profile catalog.");

        foreach (var profile in catalog.Profiles) ProfileIni.ValidateProfile(profile);

        if (catalog.Profiles.Select(p => p.Id).Distinct().Count() != catalog.Profiles.Count)
            throw new InvalidDataException("Profile IDs must be unique.");
    }
}
