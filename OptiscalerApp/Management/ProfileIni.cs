using System.Globalization;
using System.Text.RegularExpressions;
using OptiscalerApp.Models;

namespace OptiscalerApp.Management;

/// <summary>Updates a small, validated set of documented keys while preserving unrelated INI lines.</summary>
public static class ProfileIni
{
    public static readonly string[] Dx11Options = ["auto", "fsr22", "fsr31", "xess", "dlss"];
    public static readonly string[] Dx12Options = ["auto", "fsr21", "fsr22", "ffx", "xess", "dlss"];

    public static void ValidateProfile(RenderProfile profile)
    {
        if (profile.Id == Guid.Empty || string.IsNullOrWhiteSpace(profile.Name) || profile.Name.Length > 100)
            throw new InvalidDataException("A profile needs a name of 1 to 100 characters and a valid ID.");
        if (!Dx11Options.Contains(profile.Dx11Upscaler) || !Dx12Options.Contains(profile.Dx12Upscaler))
            throw new InvalidDataException("Unsupported upscaler value.");
        if (profile.Sharpness is < 0 or > 1)
            throw new InvalidDataException("Sharpness must be between 0 and 1, or empty for automatic.");
        if (profile.Settings is null) throw new InvalidDataException("Profile settings are missing.");

        foreach (var (id, value) in profile.Settings)
        {
            // Exact IDs keep one entry per key, so two spellings cannot write conflicting values.
            if (ProfileSettings.Find(id) is not { } setting || setting.Id != id)
                throw new InvalidDataException($"Unsupported profile setting {id}.");
            if (setting.Normalize(value) != value)
                throw new InvalidDataException($"Invalid value for {setting.Label}.");
        }
    }

    /// <param name="resetOmitted">
    ///     Sets existing keys the profile leaves on default back to auto. Use it when the INI is an installed copy that
    ///     may still carry another profile's overrides, not a package's original file.
    /// </param>
    public static string ApplyProfileToIni(string ini, RenderProfile profile, bool resetOmitted = false)
    {
        ValidateProfile(profile);
        ini = SetIniValue(ini, "Upscalers", "Dx11Upscaler", profile.Dx11Upscaler);
        ini = SetIniValue(ini, "Upscalers", "Dx12Upscaler", profile.Dx12Upscaler);
        ini = SetIniValue(ini, "Sharpness", "OverrideSharpness", profile.Sharpness is null ? "auto" : "true");
        ini = SetIniValue(ini, "Sharpness", "Sharpness",
                          profile.Sharpness?.ToString(CultureInfo.InvariantCulture) ?? "auto");

        ini = SetIniValue(ini, "Log", "LogToFile", profile.EnableLogging ? "true" : "false");

        foreach (var setting in ProfileSettings.All)
            if (profile.Settings.TryGetValue(setting.Id, out var value))
                ini = SetIniValue(ini, setting.Section, setting.Key, value);
            else if (resetOmitted)
                ini = SetIniValue(ini, setting.Section, setting.Key, "auto", false);

        return ini;
    }

    /// <summary>Reads the supported, non-auto values of an existing OptiScaler.ini into a new profile.</summary>
    public static RenderProfile ReadProfileFromIni(string ini, string name)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var section = "";

        foreach (var raw in ini.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.Trim();

            if (line.Length == 0 || line[0] is ';' or '#') continue;

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                section = line[1..^1].Trim();

                continue;
            }

            var separator = line.IndexOf('=');
            if (separator > 0) values[$"{section}.{line[..separator].Trim()}"] = line[(separator + 1)..].Trim();
        }

        string? Read(string id) { return values.GetValueOrDefault(id); }

        // Values are matched without case, like the advanced settings below.
        bool IsTrue(string id) { return string.Equals(Read(id), "true", StringComparison.OrdinalIgnoreCase); }

        string ReadOption(string[] options, string id)
        {
            return options.FirstOrDefault(o => o.Equals(Read(id), StringComparison.OrdinalIgnoreCase)) ?? "auto";
        }

        var sharpness = IsTrue("Sharpness.OverrideSharpness") &&
                        decimal.TryParse(Read("Sharpness.Sharpness"), ProfileSetting.NumberStyle,
                                         CultureInfo.InvariantCulture, out var value) && value is >= 0 and <= 1
            ? value
            : (decimal?)null;

        return new RenderProfile
        {
            Name = name.Length > 100 ? name[..100] : name,
            Dx11Upscaler = ReadOption(Dx11Options, "Upscalers.Dx11Upscaler"),
            Dx12Upscaler = ReadOption(Dx12Options, "Upscalers.Dx12Upscaler"),
            Sharpness = sharpness,
            EnableLogging = IsTrue("Log.LogToFile"),
            Settings = ProfileSettings.All
                .Select(s => (s.Id, Value: s.Normalize(Read(s.Id))))
                .Where(s => s.Value is not null)
                .ToDictionary(s => s.Id, s => s.Value!)
        };
    }

    private static string SetIniValue(string text, string section, string key, string value, bool addMissing = true)
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

        if (!replaced && addMissing)
        {
            if (!foundSection) lines.Add($"[{section}]");
            lines.Insert(foundSection ? insertion : lines.Count, $"{key}={value}");
        }

        return string.Join(newline, lines);
    }
}