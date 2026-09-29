using System.Text.RegularExpressions;
using OptiscalerApp.Models;

namespace OptiscalerApp.Management;

/// <summary>Writes profile settings into OptiScaler.ini while preserving unrelated lines and comments.</summary>
public static class ProfileIni
{
    // OptiScaler's defaults: Insert opens the overlay and End toggles frame generation.
    private const string DefaultOverlayKey = "0x2D";
    private const string DefaultFrameGenKey = "0x23";

    public static void ValidateProfile(RenderProfile profile)
    {
        if (profile.Id == Guid.Empty || string.IsNullOrWhiteSpace(profile.Name) || profile.Name.Length > 100)
            throw new InvalidDataException("A profile needs a name of 1 to 100 characters and a valid ID.");
        if (profile.Settings is null) throw new InvalidDataException("Profile settings are missing.");

        foreach (var (id, value) in profile.Settings)
        {
            // Exact IDs keep one entry per key, so two spellings cannot write conflicting values.
            if (ProfileSettings.Find(id) is not { } setting || setting.Id != id)
                throw new InvalidDataException($"Unsupported profile setting {id}.");
            if (setting.Normalize(value) != value)
                throw new InvalidDataException($"Invalid value for {setting.Label}.");
        }

        // Compare the keys OptiScaler will actually use, so a default can still collide with an override.
        var overlay = profile.Settings.GetValueOrDefault(ProfileSettings.OverlayKeyId, DefaultOverlayKey);
        var frameGen = profile.Settings.GetValueOrDefault(ProfileSettings.FrameGenKeyId, DefaultFrameGenKey);

        if (overlay != "-1" && overlay == frameGen)
            throw new InvalidDataException("The overlay and frame generation shortcuts must be different keys.");
    }

    /// <param name="overridesOnly">
    ///     Writes only the settings the profile changes, leaving every other key as it is in <paramref name="ini" />.
    ///     Otherwise unset keys that the file has are reset to "auto", undoing earlier edits to them.
    /// </param>
    public static string ApplyProfileToIni(string ini, RenderProfile profile, bool overridesOnly = false)
    {
        ValidateProfile(profile);

        foreach (var setting in ProfileSettings.All)
            if (profile.Settings.TryGetValue(setting.Id, out var value))
                ini = SetIniValue(ini, setting.Section, setting.Key, value);
            else if (!overridesOnly)
                ini = SetIniValue(ini, setting.Section, setting.Key, "auto", false);

        // Choosing an FG output is a request to run frame generation, not only to configure it.
        if (profile.Settings.TryGetValue(ProfileSettings.FrameGenOutputId, out var output))
            ini = SetIniValue(ini, "FrameGen", "Enabled", output == "nofg" ? "false" : "true");
        else if (!overridesOnly)
            ini = SetIniValue(ini, "FrameGen", "Enabled", "auto", false);

        return ini;
    }

    /// <summary>Builds a profile from an existing OptiScaler.ini. Values the profile cannot represent stay automatic.</summary>
    public static RenderProfile ReadProfileFromIni(string ini, string name)
    {
        var values = ReadIniValues(ini);
        var settings = ProfileSettings.All
            .Select(setting => (setting.Id, Value: setting.Normalize(values.GetValueOrDefault((setting.Section, setting.Key)))))
            .Where(setting => setting.Value is not null)
            .ToDictionary(setting => setting.Id, setting => setting.Value!);

        // "Enabled=false" with no output chosen is frame generation explicitly turned off.
        if (!settings.ContainsKey(ProfileSettings.FrameGenOutputId) &&
            values.GetValueOrDefault(("FrameGen", "Enabled"))?.Equals("false", StringComparison.OrdinalIgnoreCase) == true)
            settings[ProfileSettings.FrameGenOutputId] = "nofg";

        var profile = new RenderProfile { Name = name, Description = "Imported from OptiScaler.ini", Settings = settings };

        // Imported shortcuts may collide; fall back to the defaults instead of rejecting the file.
        try
        {
            ValidateProfile(profile);

            return profile;
        }
        catch (InvalidDataException)
        {
            return profile with
            {
                Settings = settings.Where(s => s.Key is not (ProfileSettings.OverlayKeyId or ProfileSettings.FrameGenKeyId))
                    .ToDictionary()
            };
        }
    }

    /// <summary>A short, human-readable list of the settings a profile overrides.</summary>
    public static string Describe(RenderProfile profile)
    {
        var overrides = ProfileSettings.All.Where(s => profile.Settings.ContainsKey(s.Id))
            .Select(s => $"{s.Label}: {s.Display(profile.Settings[s.Id])}").ToList();

        return overrides.Count == 0 ? "Uses OptiScaler's defaults." : string.Join("\n", overrides);
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

    internal static string SetIniValue(string text, string section, string key, string value,
                                        bool addMissing = true)
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
