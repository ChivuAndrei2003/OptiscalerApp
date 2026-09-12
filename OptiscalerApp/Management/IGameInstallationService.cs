using OptiscalerApp.Models;

namespace OptiscalerApp.Management;

public interface IGameInstallationService
{
    Task<InstallPlan> PreviewInstallation_Async(string executablePath, string packageDirectory, string proxyName,
                                                RenderProfile? profile, CancellationToken cancellationToken = default);

    Task<InstallPlan> PreviewNativeDllSwap_Async(string destinationDll, string sourceDll,
                                                 CancellationToken cancellationToken = default);

    Task<InstallPlan> PreviewProfileApplication_Async(string executablePath, RenderProfile profile,
                                                      CancellationToken cancellationToken = default);

    Task ExecuteInstallationPlan_Async(InstallPlan plan, CancellationToken cancellationToken = default);

    Task<VerificationResult> VerifyInstallation_Async(string targetDirectory,
                                                      CancellationToken cancellationToken = default);

    Task RestoreLatestOperation_Async(string targetDirectory, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OperationJournal>> GetOperationHistory_Async(CancellationToken cancellationToken = default);
}
