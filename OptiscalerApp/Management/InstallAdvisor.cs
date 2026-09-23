using System.Text.RegularExpressions;
using OptiscalerApp.Models;

namespace OptiscalerApp.Management;

public enum ComponentAdvice
{
    Unchanged,
    Install,
    Skip
}

/// <summary>What the advisor knows about one game; every field is optional evidence.</summary>
public sealed record AdvisorInput
{
    public GpuInfo? Gpu { get; init; }
    public CompatibilityEntry? Compatibility { get; init; }
    public GamePlatform Platform { get; init; } = GamePlatform.Manual;
    public bool HasUpscalerInputs { get; init; }
    public bool HasDlssFrameGeneration { get; init; }
    public bool HasAntiCheat { get; init; }

    /// <summary>Proxy filenames already in the game folder that this app did not install, e.g. ReShade's dxgi.dll.</summary>
    public IReadOnlyCollection<string> OccupiedProxies { get; init; } = [];

    public bool IsLinux { get; init; } = OperatingSystem.IsLinux();
}

/// <summary>A suggested installation with the reason for each choice, so the user can judge it before previewing.</summary>
public sealed record InstallRecommendation(
    string Proxy,
    ComponentAdvice FakeNvapi,
    ComponentAdvice OptiPatcher,
    ComponentAdvice Nukem,
    IReadOnlyList<string> Reasons,
    IReadOnlyList<string> Warnings);

/// <summary>Turns the OptiScaler install guide's rules of thumb into explainable per-game suggestions.</summary>
public static class InstallAdvisor
{
    private const string DefaultProxy = "dxgi.dll";

    public static InstallRecommendation Recommend(AdvisorInput input)
    {
        var reasons = new List<string>();
        var warnings = new List<string>();
        var vendor = input.Gpu?.Vendor ?? GpuVendor.Unknown;
        var nonNvidia = vendor is GpuVendor.AMD or GpuVendor.Intel;
        var compat = input.Compatibility;

        var proxy = ChooseProxy(input, reasons);

        ComponentAdvice fakeNvapi;

        if (vendor == GpuVendor.Nvidia)
        {
            fakeNvapi = ComponentAdvice.Skip;
            reasons.Add("FakeNvapi: skipped, it is only meant for AMD and Intel GPUs.");
        }
        else if (nonNvidia)
        {
            fakeNvapi = ComponentAdvice.Install;
            reasons.Add("FakeNvapi: installed, so the game exposes DLSS and Reflex (converted to Anti-Lag 2 or LatencyFlex).");
        }
        else
        {
            fakeNvapi = ComponentAdvice.Unchanged;
            reasons.Add("FakeNvapi: unchanged, the GPU vendor could not be detected.");
        }

        ComponentAdvice optiPatcher;

        if (compat?.OptiPatcherSupported != true)
        {
            optiPatcher = ComponentAdvice.Skip;
            reasons.Add(compat is null
                            ? "OptiPatcher: skipped, the game is not in the wiki's tested list."
                            : "OptiPatcher: skipped, the wiki does not list OptiPatcher support for this game.");
        }
        else if (vendor == GpuVendor.Nvidia)
        {
            optiPatcher = ComponentAdvice.Skip;
            reasons.Add("OptiPatcher: skipped, NVIDIA GPUs already get DLSS inputs natively.");
        }
        else
        {
            optiPatcher = nonNvidia ? ComponentAdvice.Install : ComponentAdvice.Unchanged;
            reasons.Add("OptiPatcher: the wiki lists support; it unlocks DLSS inputs without GPU spoofing.");
        }

        ComponentAdvice nukem;

        if (!input.HasDlssFrameGeneration)
        {
            nukem = ComponentAdvice.Skip;
            reasons.Add("NukemFG: skipped, no DLSS Frame Generation (nvngx_dlssg.dll) was found for it to convert.");
        }
        else
        {
            nukem = nonNvidia ? ComponentAdvice.Install : ComponentAdvice.Unchanged;
            reasons.Add(nonNvidia
                            ? "NukemFG: installed, it turns the game's DLSS Frame Generation into FSR 3 FG."
                            : "NukemFG: unchanged; RTX 40 and newer can use DLSS Frame Generation natively.");
        }

        if (input.HasAntiCheat)
            warnings.Add("Anti-cheat files were found. Injecting OptiScaler into online games can get your account banned.");

        if (compat?.Status == CompatibilityStatus.NotWorking)
            warnings.Add("The OptiScaler wiki lists this game as not working" +
                         (compat.Notes.Length > 0 ? $": {compat.Notes}" : "."));
        else if (compat?.Status == CompatibilityStatus.WorkingOnSingleOs)
            warnings.Add("The wiki reports this game working on only one operating system. Check its notes.");

        if (!input.HasUpscalerInputs && compat is null)
            warnings.Add("No DLSS, FSR or XeSS files were found. OptiScaler needs the game to offer one of them, " +
                         "although some engines embed them in the executable.");

        return new InstallRecommendation(proxy, fakeNvapi, optiPatcher, nukem, reasons, warnings);
    }

    /// <summary>Prefers the most capable discrete GPU, since that is the one games render on.</summary>
    public static GpuInfo? PickPrimaryGpu(IReadOnlyList<GpuInfo> gpus)
    {
        return gpus.OrderByDescending(g => g.DedicatedVram ?? 0)
            .ThenBy(g => g.Vendor switch
            {
                GpuVendor.Nvidia => 0,
                GpuVendor.AMD => 1,
                GpuVendor.Intel => 2,
                _ => 3
            })
            .FirstOrDefault();
    }

    private static string ChooseProxy(AdvisorInput input, List<string> reasons)
    {
        // Wiki notes are free text, so only sentences that apply to this OS and store are trusted.
        var mentioned = input.Compatibility?.MentionedProxies ?? [];

        // Split on sentence ends only; a bare '.' would also split "winmm.dll".
        foreach (var sentence in Regex.Split(input.Compatibility?.Notes ?? "", @"(?<=[.;!?])\s+"))
        {
            var proxy = mentioned.FirstOrDefault(p => sentence.Contains(p, StringComparison.OrdinalIgnoreCase));

            if (proxy is null || input.OccupiedProxies.Contains(proxy, StringComparer.OrdinalIgnoreCase)) continue;
            if (!input.IsLinux && sentence.Contains("linux", StringComparison.OrdinalIgnoreCase)) continue;
            if (MentionsStore(sentence) && input.Platform != GamePlatform.Xbox) continue;

            reasons.Add($"Injection: {proxy}, as the OptiScaler wiki notes for this game suggest.");

            return proxy;
        }

        if (input.OccupiedProxies.Contains(DefaultProxy, StringComparer.OrdinalIgnoreCase))
        {
            var free = GameInstallationService.ProxyNames.FirstOrDefault(p =>
                           !input.OccupiedProxies.Contains(p, StringComparer.OrdinalIgnoreCase)) ?? DefaultProxy;
            reasons.Add($"Injection: {free}, because dxgi.dll already belongs to another mod such as ReShade.");

            return free;
        }

        reasons.Add("Injection: dxgi.dll, the default that works for most games.");

        return DefaultProxy;
    }

    private static bool MentionsStore(string sentence)
    {
        return new[] { "xbox", "game pass", "gamepass", "microsoft store", "ms store" }
            .Any(store => sentence.Contains(store, StringComparison.OrdinalIgnoreCase));
    }
}
