using OptiscalerApp.Models;

namespace OptiscalerApp.Management;

public interface IGameInstallationService
{
    /// <param name="keepCurrentSettings">Carries customized values from the game's current OptiScaler.ini.</param>
    Task<InstallPlan> PreviewPackageInstallationAsync(
        string executablePath, string packageDirectory, string proxyName, RenderProfile? profile,
        IReadOnlyList<ComponentInstallSelection> components, IProgress<string>? progress = null,
        CancellationToken cancellationToken = default, bool keepCurrentSettings = false);

    Task<InstallPlan> PreviewNativeDllSwapAsync(string destinationDll, string sourceDll,
                                                 CancellationToken cancellationToken = default);

    Task<InstallPlan> PreviewProfileApplicationAsync(string executablePath, RenderProfile profile,
                                                      CancellationToken cancellationToken = default);

    Task ExecuteInstallationPlanAsync(InstallPlan plan, CancellationToken cancellationToken = default);

    Task<VerificationResult> VerifyInstallationAsync(string targetDirectory,
                                                      CancellationToken cancellationToken = default);

    Task RestoreLatestOperationAsync(string targetDirectory, CancellationToken cancellationToken = default);

    /// <summary>Undoes every unrestored operation in the folder, newest first, returning it to its original files.</summary>
    Task RestoreAllOperationsAsync(string targetDirectory, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OperationJournal>> GetOperationHistoryAsync(CancellationToken cancellationToken = default);

    /// <summary>Every folder with an unrestored operation, with a quick size-based health check.</summary>
    Task<IReadOnlyList<ManagedTarget>> GetManagedTargetsAsync(CancellationToken cancellationToken = default);
}