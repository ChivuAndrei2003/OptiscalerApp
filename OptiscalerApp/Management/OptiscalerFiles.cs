namespace OptiscalerApp.Management;

/// <summary>File names OptiScaler and the games it targets use, shared by analysis, installation and settings.</summary>
public static class OptiscalerFiles
{
    /// <summary>Names OptiScaler.dll can be installed under so the game loads it; the first is the default.</summary>
    public static readonly string[] ProxyNames =
        ["dxgi.dll", "winmm.dll", "d3d12.dll", "dbghelp.dll", "version.dll", "wininet.dll", "winhttp.dll"];

    /// <summary>Upscaler libraries a game ships that can be swapped for another version.</summary>
    public static readonly string[] NativeNames =
        ["nvngx_dlss.dll", "nvngx_dlssg.dll", "nvngx_dlssd.dll", "libxess.dll", "amd_fidelityfx_upscaler_dx12.dll"];

    /// <summary>
    ///     FSR libraries games ship: FSR 3.1 as one amd_fidelityfx_dx12.dll, later versions as a loader plus an
    ///     upscaler DLL, and FSR 2 under its ffx_fsr2_api names.
    /// </summary>
    public static readonly string[] FsrNames =
    [
        "amd_fidelityfx_upscaler_dx12.dll", "amd_fidelityfx_dx12.dll", "amd_fidelityfx_vk.dll",
        "ffx_fsr2_api_x64.dll", "ffx_fsr2_api_dx12_x64.dll", "ffx_fsr2_api_vk_x64.dll"
    ];

    /// <summary>Logs OptiScaler, FakeNvapi and NukemFG write next to the game while it runs.</summary>
    public static readonly string[] RuntimeLogNames = ["OptiScaler.log", "fakenvapi.log", "dlssg_to_fsr3.log"];

    private static readonly string[] SetupScriptNames = ["setup_windows.bat", "setup_linux.sh"];

    /// <summary>The package's manual-install scripts and its "extract all files" reminder, which no game needs.</summary>
    public static bool IsSetupFile(string relativePath)
    {
        return SetupScriptNames.Contains(relativePath, StringComparer.OrdinalIgnoreCase) ||
               (relativePath.StartsWith("!!", StringComparison.Ordinal) &&
                relativePath.EndsWith(".txt", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    ///     True when a library is OptiScaler under any filename, told by the original filename in its version
    ///     resource, the way OptiScaler's own setup script recognizes an existing installation.
    /// </summary>
    public static bool IsOptiscalerLibrary(string path)
    {
        return string.Equals(SafeFiles.ReadLibraryIdentity(path).OriginalFilename, "OptiScaler.dll",
                             StringComparison.OrdinalIgnoreCase);
    }
}
