using OptiscalerApp.Management;
using OptiscalerApp.Models;

namespace Optiscaler.Tests;

internal static class InstallationServiceExtensions
{
    /// <summary>Previews a package install with its bundled components only, as most tests need.</summary>
    public static Task<InstallPlan> PreviewInstallation_Async(this IGameInstallationService service,
                                                              string executablePath, string packageDirectory,
                                                              string proxyName, RenderProfile? profile,
                                                              CancellationToken cancellationToken = default,
                                                              bool keepCurrentSettings = false)
    {
        return service.PreviewPackageInstallation_Async(executablePath, packageDirectory, proxyName, profile, [],
                                                        null, cancellationToken, keepCurrentSettings);
    }
}