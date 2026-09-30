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
}
