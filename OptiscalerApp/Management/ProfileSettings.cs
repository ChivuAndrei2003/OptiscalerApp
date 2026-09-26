using System.Globalization;
using System.Text.RegularExpressions;

namespace OptiscalerApp.Management;

public enum ProfileSettingKind
{
    Toggle,
    Choice,
    Number,
    Text
}

public sealed record ProfileSettingOption(string Value, string Label)
{
    public override string ToString() { return Label; }
}

/// <summary>One documented OptiScaler.ini key that profiles may override. Auto is represented by omitting it.</summary>
public sealed record ProfileSetting(
    string Group,
    string Section,
    string Key,
    string Label,
    string Description,
    ProfileSettingKind Kind,
    IReadOnlyList<ProfileSettingOption>? Options = null,
    decimal Minimum = 0,
    decimal Maximum = 0,
    string? Pattern = null)
{
    private static readonly IReadOnlyList<ProfileSettingOption> ToggleOptions =
        [new("true", "On"), new("false", "Off")];

    // No thousands separator: with it, a decimal comma such as "90,5" would silently parse as 905.
    internal const NumberStyles NumberStyle = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint;

    public string Id => $"{Section}.{Key}";

    /// <summary>The allowed range of a number setting, formatted the way it must be typed.</summary>
    public string RangeText => string.Create(CultureInfo.InvariantCulture, $"{Minimum} to {Maximum}");

    public IReadOnlyList<ProfileSettingOption> Choices => Kind == ProfileSettingKind.Toggle
        ? ToggleOptions
        : Options ?? [];

    /// <summary>Returns the canonical INI value, or null when the value is not valid for this setting.</summary>
    public string? Normalize(string? value)
    {
        value = value?.Trim();

        if (string.IsNullOrEmpty(value) || value.Equals("auto", StringComparison.OrdinalIgnoreCase)) return null;

        switch (Kind)
        {
            case ProfileSettingKind.Toggle or ProfileSettingKind.Choice:
                return Choices.FirstOrDefault(o => o.Value.Equals(value, StringComparison.OrdinalIgnoreCase))?.Value;
            case ProfileSettingKind.Number:
                return decimal.TryParse(value, NumberStyle, CultureInfo.InvariantCulture, out var number) &&
                       number >= Minimum && number <= Maximum
                    ? number.ToString(CultureInfo.InvariantCulture)
                    : null;
            default:
                // Values are written as single INI lines, so line breaks could inject other keys.
                return value.Length <= 260 && value.IndexOfAny(['\r', '\n', '\0']) < 0 &&
                       (Pattern is null || Regex.IsMatch(value, Pattern))
                    ? value
                    : null;
        }
    }
}

/// <summary>
///     The OptiScaler.ini options exposed by the profile editor, grouped as they appear in the UI. Keys that
///     <see cref="OptiscalerApp.Models.RenderProfile" /> models as typed properties (upscalers, frame generation
///     input/output, shortcuts, DXGI spoofing, overlays, ReShade, Special K, frame rate limit) are left out so each key
///     has one source.
/// </summary>
public static class ProfileSettings
{
    private const string Upscaling = "Upscaling";
    private const string FrameGeneration = "Frame generation";
    private const string Overlay = "Overlay menu";
    private const string Compatibility = "Inputs & GPU spoofing";
    private const string Plugins = "Plugins & logging";

    private static readonly ProfileSettingOption[] DlssPresets =
        [new("0", "Default"), ..Enumerable.Range(1, 15).Select(i => new ProfileSettingOption($"{i}", $"Preset {(char)('A' + i - 1)}"))];

    public static readonly IReadOnlyList<ProfileSetting> All =
    [
        new(Upscaling, "UpscaleRatio", "UpscaleRatioOverrideEnabled", "Override upscale ratio",
            "Use one fixed render-to-output ratio for every quality mode.", ProfileSettingKind.Toggle),
        new(Upscaling, "UpscaleRatio", "UpscaleRatioOverrideValue", "Upscale ratio",
            "Output resolution divided by render resolution, from 1.0 to 6.0.", ProfileSettingKind.Number,
            Minimum: 1, Maximum: 6),
        new(Upscaling, "OutputScaling", "Enabled", "Output scaling",
            "Upscale to a larger target and downscale it, trading performance for image quality.",
            ProfileSettingKind.Toggle),
        new(Upscaling, "OutputScaling", "Multiplier", "Output scaling multiplier",
            "Target size multiplier used by output scaling, from 0.5 to 3.0.", ProfileSettingKind.Number,
            Minimum: 0.5m, Maximum: 3),
        new(Upscaling, "CAS", "Enabled", "RCAS sharpening",
            "Sharpen the upscaled image with RCAS instead of the upscaler's own sharpening.",
            ProfileSettingKind.Toggle),
        new(Upscaling, "CAS", "MotionSharpnessEnabled", "Motion-adaptive sharpening",
            "Increase RCAS sharpening on moving pixels.", ProfileSettingKind.Toggle),
        new(Upscaling, "DLSS", "RenderPresetOverride", "Override DLSS presets",
            "Replace the DLSS render preset chosen by the game.", ProfileSettingKind.Toggle),
        new(Upscaling, "DLSS", "RenderPresetForAll", "DLSS preset for all modes",
            "Render preset applied to every DLSS quality mode when presets are overridden.",
            ProfileSettingKind.Choice, DlssPresets),
        new(Upscaling, "InitFlags", "AutoExposure", "Auto exposure",
            "Let the upscaler compute exposure. Can fix ghosting or dark frames in some games.",
            ProfileSettingKind.Toggle),
        new(Upscaling, "InitFlags", "HDR", "HDR input",
            "Treat the game's color input as HDR.", ProfileSettingKind.Toggle),
        new(Upscaling, "InitFlags", "DepthInverted", "Inverted depth",
            "Tell the upscaler the game uses reversed depth.", ProfileSettingKind.Toggle),

        new(FrameGeneration, "FrameGen", "DrawUIOverFG", "Draw UI over generated frames",
            "Composite the game UI on top of generated frames to reduce HUD artifacts.", ProfileSettingKind.Toggle),
        new(FrameGeneration, "OptiFG", "HUDFix", "OptiFG HUD fix",
            "Detect the HUD-less image so generated frames do not smear the HUD.", ProfileSettingKind.Toggle),

        new(Overlay, "Menu", "OverlayMenu", "Overlay menu",
            "Use the in-game overlay menu instead of the legacy menu.", ProfileSettingKind.Toggle),
        new(Overlay, "Menu", "Scale", "Menu scale", "Size of the OptiScaler menu.", ProfileSettingKind.Choice,
            [
                new("0.5", "50%"), new("0.75", "75%"), new("1.0", "100%"), new("1.25", "125%"), new("1.5", "150%"),
                new("1.75", "175%"), new("2.0", "200%")
            ]),
        new(Overlay, "Menu", "ShowFps", "FPS overlay", "Show the frame rate overlay.", ProfileSettingKind.Toggle),
        new(Overlay, "Menu", "FpsOverlayPos", "FPS overlay position", "Screen corner of the frame rate overlay.",
            ProfileSettingKind.Choice,
            [new("0", "Top left"), new("1", "Top right"), new("2", "Bottom left"), new("3", "Bottom right")]),
        new(Overlay, "Menu", "DisableSplash", "Hide splash message",
            "Do not show the OptiScaler message when the game starts.", ProfileSettingKind.Toggle),

        new(Compatibility, "Inputs", "EnableDlssInputs", "Accept DLSS inputs",
            "Let games that request DLSS use the selected upscaler.", ProfileSettingKind.Toggle),
        new(Compatibility, "Inputs", "EnableXeSSInputs", "Accept XeSS inputs",
            "Let games that request XeSS use the selected upscaler.", ProfileSettingKind.Toggle),
        new(Compatibility, "Inputs", "EnableFsr2Inputs", "Accept FSR 2 inputs",
            "Let games that request FSR 2 use the selected upscaler.", ProfileSettingKind.Toggle),
        new(Compatibility, "Inputs", "EnableFsr3Inputs", "Accept FSR 3 inputs",
            "Let games that request FSR 3 use the selected upscaler.", ProfileSettingKind.Toggle),
        new(Compatibility, "Inputs", "EnableHotSwapping", "Upscaler hot swapping",
            "Allow changing the upscaler from the menu while the game runs.", ProfileSettingKind.Toggle),
        new(Compatibility, "Spoofing", "Vulkan", "Spoof GPU as NVIDIA (Vulkan)",
            "Report an NVIDIA GPU to Vulkan games so they offer DLSS.", ProfileSettingKind.Toggle),
        new(Compatibility, "Spoofing", "StreamlineSpoofing", "Streamline spoofing",
            "Spoof the GPU inside NVIDIA Streamline so DLSS-G can be enabled.", ProfileSettingKind.Toggle),
        new(Compatibility, "Spoofing", "SpoofedGPUName", "Spoofed GPU name",
            "GPU name reported while spoofing.", ProfileSettingKind.Text),
        new(Compatibility, "Hotfix", "PreferDedicatedGpu", "Prefer dedicated GPU",
            "Use the dedicated GPU on systems that also have integrated graphics.", ProfileSettingKind.Toggle),

        new(Plugins, "Plugins", "LoadAsiPlugins", "Load ASI plugins",
            "Load .asi plugins such as OptiPatcher from the plugins folder.", ProfileSettingKind.Toggle),
        new(Plugins, "Plugins", "Path", "Plugins folder",
            "Folder searched for plugins, relative to the game or absolute.", ProfileSettingKind.Text),
        new(Plugins, "Log", "LogLevel", "Log level", "Minimum severity written to the log.",
            ProfileSettingKind.Choice,
            [new("0", "Trace"), new("1", "Debug"), new("2", "Info"), new("3", "Warning"), new("4", "Error")]),
        new(Plugins, "Log", "LogToConsole", "Log to console", "Also write log messages to a console.",
            ProfileSettingKind.Toggle),
        new(Plugins, "Log", "OpenConsole", "Open console window", "Open a console window with the game.",
            ProfileSettingKind.Toggle)
    ];

    private static readonly Dictionary<string, ProfileSetting> ById =
        All.ToDictionary(s => s.Id, StringComparer.OrdinalIgnoreCase);

    public static ProfileSetting? Find(string id) { return ById.GetValueOrDefault(id); }
}
