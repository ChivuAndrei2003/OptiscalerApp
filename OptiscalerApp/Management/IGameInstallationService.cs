using OptiscalerApp.Models;

namespace OptiscalerApp.Management;

public interface IGameInstallationService
{
    /// <param name="keepCurrentSettings">Carries customized values from the game's current OptiScaler.ini.</param>
    Task<InstallPlan> PreviewInstallation_Async(string executablePath, string packageDirectory, string proxyName,
                                                RenderProfile? profile, CancellationToken cancellationToken = default,
                                                bool keepCurrentSettings = false);

    Task<InstallPlan> PreviewPackageInstallation_Async(
        string executablePath, string packageDirectory, string proxyName, RenderProfile? profile,
        IReadOnlyList<ComponentInstallSelection> components, IProgress<string>? progress = null,
        CancellationToken cancellationToken = default, bool keepCurrentSettings = false);

    Task<InstallPlan> PreviewNativeDllSwap_Async(string destinationDll, string sourceDll,
                                                 CancellationToken cancellationToken = default);

    Task<InstallPlan> PreviewProfileApplication_Async(string executablePath, RenderProfile profile,
                                                      CancellationToken cancellationToken = default);

    Task ExecuteInstallationPlan_Async(InstallPlan plan, CancellationToken cancellationToken = default);

    Task<VerificationResult> VerifyInstallation_Async(string targetDirectory,
                                                      CancellationToken cancellationToken = default);

    Task RestoreLatestOperation_Async(string targetDirectory, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OperationJournal>> GetOperationHistory_Async(CancellationToken cancellationToken = default);

    /// <summary>Every folder with an unrestored operation, with a quick size-based health check.</summary>
    Task<IReadOnlyList<ManagedTarget>> GetManagedTargets_Async(CancellationToken cancellationToken = default);
}
