using System.Globalization;
using System.Text.RegularExpressions;
using OptiscalerApp.Models;

namespace OptiscalerApp.Management;

/// <summary>Updates a small, validated set of documented keys while preserving unrelated INI lines.</summary>
public static class ProfileIni
{
    public static readonly string[] Dx11Options =
        ["auto", "fsr22", "fsr31", "xess", "xess_12", "fsr21_12", "fsr22_12", "ffx_12", "dlss"];

    public static readonly string[] Dx12Options = ["auto", "fsr21", "fsr22", "ffx", "xess", "dlss"];
    public static readonly string[] VulkanOptions = ["auto", "fsr21", "fsr22", "ffx", "xess", "fsr21_12", "ffx_12", "dlss"];
    public static readonly string[] FrameGenInputOptions = ["auto", "nofg", "dlssg", "nvngxfg", "fsrfg", "upscaler", "fsrfg30"];
    public static readonly string[] FrameGenOutputOptions = ["auto", "nofg", "fsrfg", "xefg", "dlssg"];

    // OptiScaler's defaults: Insert opens the overlay, End toggles FG, Page Up/Down drive the FPS overlay.
    private const int DefaultOverlayKey = 0x2D;
    private const int DefaultFrameGenKey = 0x23;
    private static readonly int[] FpsOverlayKeys = [0x21, 0x22];

    /// <summary>Windows virtual-key codes offered for OptiScaler's shortcuts; -1 disables a shortcut.</summary>
    public static readonly IReadOnlyList<(string Label, int Code)> ShortcutKeys =
    [
        ("None", -1), ("Insert", 0x2D), ("Home", 0x24), ("End", 0x23), ("Delete", 0x2E), ("Backspace", 0x08),
        ("Pause", 0x13), ("Scroll Lock", 0x91), ("` (tilde key)", 0xC0),
        ..Enumerable.Range(1, 12).Select(i => ($"F{i}", 0x6F + i)),
        ..Enumerable.Range(0, 10).Select(i => ($"Numpad {i}", 0x60 + i)),
        ("Numpad *", 0x6A), ("Numpad +", 0x6B), ("Numpad -", 0x6D), ("Numpad /", 0x6F)
    ];

    public static void ValidateProfile(RenderProfile profile)
    {
        if (profile.Id == Guid.Empty || string.IsNullOrWhiteSpace(profile.Name) || profile.Name.Length > 100)
            throw new InvalidDataException("A profile needs a name of 1 to 100 characters and a valid ID.");
        if (!Dx11Options.Contains(profile.Dx11Upscaler) || !Dx12Options.Contains(profile.Dx12Upscaler) ||
            !VulkanOptions.Contains(profile.VulkanUpscaler))
            throw new InvalidDataException("Unsupported upscaler value.");
        if (!FrameGenInputOptions.Contains(profile.FrameGenInput) ||
            !FrameGenOutputOptions.Contains(profile.FrameGenOutput))
            throw new InvalidDataException("Unsupported frame generation value.");
        if (profile.Sharpness is < 0 or > 1)
            throw new InvalidDataException("Sharpness must be between 0 and 1, or empty for automatic.");
        if (profile.FramerateLimit is < 0 or > 1000)
            throw new InvalidDataException("The frame rate limit must be between 0 and 1000 FPS, or empty.");

        foreach (var key in new[] { profile.OverlayKey, profile.FrameGenKey })
            if (key is not null and not -1 and (< 1 or > 0xFE))
                throw new InvalidDataException("Shortcut keys must be Windows virtual-key codes.");

        // Compare the keys OptiScaler will actually use, so a default can still collide with an override.
        var overlay = profile.OverlayKey ?? DefaultOverlayKey;
        var frameGen = profile.FrameGenKey ?? DefaultFrameGenKey;

        if (overlay != -1 && overlay == frameGen)
            throw new InvalidDataException("The overlay and frame generation shortcuts must be different keys.");
        if (FpsOverlayKeys.Contains(overlay) || FpsOverlayKeys.Contains(frameGen))
            throw new InvalidDataException(
                                           "Page Up and Page Down are OptiScaler's FPS overlay shortcuts. Choose another key.");
    }

    /// <param name="overridesOnly">
    ///     Writes only the settings the profile changes, leaving every other key as it is in <paramref name="ini" />.
    ///     Otherwise unset settings are written as "auto", resetting earlier edits to those keys.
    /// </param>
    public static string ApplyProfileToIni(string ini, RenderProfile profile, bool overridesOnly = false)
    {
        ValidateProfile(profile);
        var sharpness = profile.Sharpness?.ToString(CultureInfo.InvariantCulture);
        var limit = profile.FramerateLimit?.ToString(CultureInfo.InvariantCulture);
        var frameGen = profile.FrameGenInput != "auto" || profile.FrameGenOutput != "auto";

        (string Section, string Key, string Value, bool IsSet)[] settings =
        [
            ("Upscalers", "Dx11Upscaler", profile.Dx11Upscaler, profile.Dx11Upscaler != "auto"),
            ("Upscalers", "Dx12Upscaler", profile.Dx12Upscaler, profile.Dx12Upscaler != "auto"),
            ("Upscalers", "VulkanUpscaler", profile.VulkanUpscaler, profile.VulkanUpscaler != "auto"),
            ("Sharpness", "OverrideSharpness", sharpness is null ? "auto" : "true", sharpness is not null),
            ("Sharpness", "Sharpness", sharpness ?? "auto", sharpness is not null),
            ("Spoofing", "Dxgi", Flag(profile.SpoofDxgi), profile.SpoofDxgi is not null),
            ("Menu", "ShortcutKey", Key(profile.OverlayKey), profile.OverlayKey is not null),
            ("Menu", "FGShortcutKey", Key(profile.FrameGenKey), profile.FrameGenKey is not null),

            // Choosing an FG output is the user's request to run frame generation, not only to configure it.
            ("FrameGen", "Enabled", profile.FrameGenOutput switch
            {
                "auto" => "auto",
                "nofg" => "false",
                _ => "true"
            }, profile.FrameGenOutput != "auto"),
            ("FrameGen", "FGInput", profile.FrameGenInput, frameGen),
            ("FrameGen", "FGOutput", profile.FrameGenOutput, frameGen),
            ("Hotfix", "DisableOverlays", Flag(profile.DisableOverlays), profile.DisableOverlays is not null),
            ("Plugins", "LoadReshade", profile.LoadReshade ? "true" : "auto", profile.LoadReshade),
            ("Plugins", "LoadSpecialK", profile.LoadSpecialK ? "true" : "auto", profile.LoadSpecialK),
            ("Framerate", "FramerateLimit", limit ?? "auto", limit is not null),
            ("Log", "LogToFile", profile.EnableLogging ? "true" : "false", profile.EnableLogging)
        ];

        foreach (var (section, key, value, isSet) in settings)
            if (isSet || !overridesOnly)
                ini = SetIniValue(ini, section, key, value);

        return ini;
    }

    /// <summary>Builds a profile from an existing OptiScaler.ini. Values the profile cannot represent stay automatic.</summary>
    public static RenderProfile ReadProfileFromIni(string ini, string name)
    {
        var values = ReadIniValues(ini);

        string Choice(string section, string key, string[] options)
        {
            return Get(values, section, key) is { } value && options.Contains(value.ToLowerInvariant())
                ? value.ToLowerInvariant()
                : "auto";
        }

        // "Enabled=false" with no output chosen is frame generation explicitly turned off.
        var output = Choice("FrameGen", "FGOutput", FrameGenOutputOptions);
        if (output == "auto" && ParseFlag(Get(values, "FrameGen", "Enabled")) == false) output = "nofg";

        var profile = new RenderProfile
        {
            Name = name,
            Description = "Imported from OptiScaler.ini",
            Dx11Upscaler = Choice("Upscalers", "Dx11Upscaler", Dx11Options),
            Dx12Upscaler = Choice("Upscalers", "Dx12Upscaler", Dx12Options),
            VulkanUpscaler = Choice("Upscalers", "VulkanUpscaler", VulkanOptions),
            Sharpness = ParseFlag(Get(values, "Sharpness", "OverrideSharpness")) == true
                ? ParseDecimal(Get(values, "Sharpness", "Sharpness")) is { } s && s is >= 0 and <= 1 ? s : null
                : null,
            EnableLogging = ParseFlag(Get(values, "Log", "LogToFile")) == true,
            SpoofDxgi = ParseFlag(Get(values, "Spoofing", "Dxgi")),
            OverlayKey = ParseKey(Get(values, "Menu", "ShortcutKey")),
            FrameGenKey = ParseKey(Get(values, "Menu", "FGShortcutKey")),
            FrameGenInput = Choice("FrameGen", "FGInput", FrameGenInputOptions),
            FrameGenOutput = output,
            DisableOverlays = ParseFlag(Get(values, "Hotfix", "DisableOverlays")),
            LoadReshade = ParseFlag(Get(values, "Plugins", "LoadReshade")) == true,
            LoadSpecialK = ParseFlag(Get(values, "Plugins", "LoadSpecialK")) == true,
            FramerateLimit = ParseDecimal(Get(values, "Framerate", "FramerateLimit")) is { } limit and > 0 and <= 1000
                ? limit
                : null
        };

        // Imported keys may collide (e.g. both shortcuts on one key); fall back to defaults instead of rejecting.
        try
        {
            ValidateProfile(profile);

            return profile;
        }
        catch (InvalidDataException)
        {
            return profile with { OverlayKey = null, FrameGenKey = null };
        }
    }

    /// <summary>A short, human-readable list of the settings a profile overrides.</summary>
    public static string Describe(RenderProfile profile)
    {
        var parts = new List<string>
        {
            $"DX11: {profile.Dx11Upscaler} • DX12: {profile.Dx12Upscaler} • Vulkan: {profile.VulkanUpscaler}"
        };
        if (profile.SpoofDxgi is { } spoof) parts.Add(spoof ? "GPU spoofing on" : "GPU spoofing off");
        if (profile.OverlayKey is { } overlay) parts.Add($"Overlay key: {KeyName(overlay)}");
        if (profile.FrameGenKey is { } fg) parts.Add($"FG key: {KeyName(fg)}");
        if (profile.FrameGenOutput != "auto" || profile.FrameGenInput != "auto")
            parts.Add($"Frame generation: {profile.FrameGenInput} → {profile.FrameGenOutput}");
        if (profile.DisableOverlays is { } overlays) parts.Add(overlays ? "Overlays blocked" : "Overlays allowed");
        if (profile.LoadReshade) parts.Add("Loads ReShade");
        if (profile.LoadSpecialK) parts.Add("Loads Special K");
        if (profile.FramerateLimit is { } limit) parts.Add($"FPS limit: {limit.ToString(CultureInfo.InvariantCulture)}");
        if (profile.Sharpness is { } sharpness)
            parts.Add($"Sharpness: {sharpness.ToString(CultureInfo.InvariantCulture)}");
        if (profile.EnableLogging) parts.Add("File logging");

        return string.Join("\n", parts);
    }

    public static string KeyName(int code)
    {
        return ShortcutKeys.FirstOrDefault(k => k.Code == code).Label ?? $"0x{code:X2}";
    }

    /// <summary>
    ///     Keys whose effective value differs. A missing key means "auto", so adding a key as "auto" is not a change, and
    ///     a customized key that the new file drops is shown reverting to "auto".
    /// </summary>
    public static IReadOnlyList<IniChange> CompareIni(string before, string after)
    {
        var old = ReadIniValues(before);
        var updated = ReadIniValues(after);

        return updated.Keys.Concat(old.Keys.Where(key => !updated.ContainsKey(key)))
            .Select(key => (Key: key, Before: old.GetValueOrDefault(key), After: updated.GetValueOrDefault(key) ?? "auto"))
            .Where(item => !string.Equals(item.Before ?? "auto", item.After, StringComparison.OrdinalIgnoreCase))
            .Select(item => new IniChange(item.Key.Section, item.Key.Key, item.Before, item.After))
            .ToList();
    }

    /// <summary>
    ///     Copies customized (non-"auto") values from the game's current INI into a newer package INI. Only keys the new
    ///     INI still defines are carried over, so settings removed by an OptiScaler update are not reintroduced.
    /// </summary>
    public static string CarryOverSettings(string packageIni, string currentIni, out int carried)
    {
        var target = ReadIniValues(packageIni);
        carried = 0;

        foreach (var ((section, key), value) in ReadIniValues(currentIni))
        {
            if (value.Equals("auto", StringComparison.OrdinalIgnoreCase) ||
                !target.TryGetValue((section, key), out var packaged) ||
                packaged.Equals(value, StringComparison.OrdinalIgnoreCase))
                continue;

            packageIni = SetIniValue(packageIni, section, key, value);
            carried++;
        }

        return packageIni;
    }

    /// <summary>Returns the value of every key, by section; later duplicates win, as when OptiScaler reads the file.</summary>
    internal static Dictionary<(string Section, string Key), string> ReadIniValues(string text)
    {
        var values = new Dictionary<(string, string), string>(SectionKeyComparer.Instance);
        var section = "";

        foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.Trim();

            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#')) continue;

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                section = line[1..^1].Trim();

                continue;
            }

            var separator = line.IndexOf('=');

            if (separator > 0) values[(section, line[..separator].Trim())] = line[(separator + 1)..].Trim();
        }

        return values;
    }

    internal static string SetIniValue(string text, string section, string key, string value)
    {
        var newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = text.Replace("\r\n", "\n").Split('\n').ToList();
        var inSection = false;
        var foundSection = false;
        var insertion = lines.Count;
        var replaced = false;

        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i].Trim();

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                if (inSection) insertion = i;
                inSection = line.Equals($"[{section}]", StringComparison.OrdinalIgnoreCase);
                foundSection |= inSection;
                if (inSection) insertion = i + 1;
            }
            else if (inSection)
            {
                insertion = i + 1;

                if (Regex.IsMatch(line, $"^{Regex.Escape(key)}\\s*=", RegexOptions.IgnoreCase))
                {
                    lines[i] = $"{key}={value}";
                    replaced = true;
                }
            }
        }

        if (!replaced)
        {
            if (!foundSection) lines.Add($"[{section}]");
            lines.Insert(foundSection ? insertion : lines.Count, $"{key}={value}");
        }

        return string.Join(newline, lines);
    }

    private static string? Get(Dictionary<(string, string), string> values, string section, string key)
    {
        return values.GetValueOrDefault((section, key));
    }

    private static string Flag(bool? value) { return value is null ? "auto" : value.Value ? "true" : "false"; }

    private static string Key(int? code)
    {
        return code switch
        {
            null => "auto",
            -1 => "-1",
            _ => $"0x{code.Value:X2}"
        };
    }

    private static bool? ParseFlag(string? value)
    {
        return value?.ToLowerInvariant() switch
        {
            "true" => true,
            "false" => false,
            _ => null
        };
    }

    private static decimal? ParseDecimal(string? value)
    {
        return decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            ? number
            : null;
    }

    private static int? ParseKey(string? value)
    {
        if (value is null) return null;
        if (value == "-1") return -1;

        var hex = value.StartsWith("0x", StringComparison.OrdinalIgnoreCase);

        return int.TryParse(hex ? value[2..] : value, hex ? NumberStyles.HexNumber : NumberStyles.Integer,
                            CultureInfo.InvariantCulture, out var code) && code is >= 1 and <= 0xFE
            ? code
            : null;
    }

    private sealed class SectionKeyComparer : IEqualityComparer<(string Section, string Key)>
    {
        public static readonly SectionKeyComparer Instance = new();

        public bool Equals((string Section, string Key) x, (string Section, string Key) y)
        {
            return string.Equals(x.Section, y.Section, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(x.Key, y.Key, StringComparison.OrdinalIgnoreCase);
        }

        public int GetHashCode((string Section, string Key) obj)
        {
            return HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Section),
                                    StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Key));
        }
    }
}
