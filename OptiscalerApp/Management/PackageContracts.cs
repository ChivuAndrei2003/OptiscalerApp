namespace OptiscalerApp.Management;

public sealed record DownloadComponent(string Name, string Repository, string AssetPrefix, string[] FileNames)
{
    // Same release repositories and file locations as Optiscaler-Client/config.json.
    public static readonly DownloadComponent Fsr = new("FSR 4 / INT8", "Optiscaler-Client/OptiScaler-Extras", "FSR",
    [
        "amd_fidelityfx_upscaler_dx12.dll", "amdxcffx64.dll", "amdxc64.dll"
    ]);

    public static readonly DownloadComponent FakeNvapi =
        new("FakeNvapi", "optiscaler/fakenvapi", "fakenvapi", ["fakenvapi.dll", "fakenvapi.ini"]);

    public static readonly DownloadComponent OptiPatcher =
        new("OptiPatcher", "optiscaler/OptiPatcher", "OptiPatcher", ["OptiPatcher.asi"]);

    public static readonly DownloadComponent Nukem =
        new("NukemFG", "Nukem9/dlssg-to-fsr3", "dlssg-to-fsr3", ["dlssg_to_fsr3_amd_is_better.dll"]);

    public string Destination(string fileName)
    {
        if (fileName.Equals("OptiPatcher.asi", StringComparison.OrdinalIgnoreCase))
            return Path.Combine("plugins", fileName);
        if (fileName.Equals("amdxc64.dll", StringComparison.OrdinalIgnoreCase))
            return Path.Combine("OptiScaler", fileName);

        return fileName;
    }
}

public sealed record PackageRelease(string Version, string AssetName, string DownloadUrl, string? Digest)
{
    public override string ToString() { return Version; }
}

/// <summary>No override uses the bundled component; KeepExisting leaves installed files untouched.</summary>
public sealed record ComponentInstallSelection(
    DownloadComponent Component,
    PackageRelease? Release = null,
    string? LocalPath = null,
    string LocalVersion = "local",
    bool KeepExisting = false);
