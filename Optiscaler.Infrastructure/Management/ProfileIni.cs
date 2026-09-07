using System.Globalization;
using System.Text.RegularExpressions;
using Optiscaler.Core.Management;

namespace Optiscaler.Infrastructure.Management;

/// <summary>Updates a small, validated set of documented keys while preserving unrelated INI lines.</summary>
public static class ProfileIni
{
    public static readonly string[] Dx11Options = ["auto", "fsr22", "fsr31", "xess", "dlss"];
    public static readonly string[] Dx12Options = ["auto", "fsr21", "fsr22", "ffx", "xess", "dlss"];

    public static void Validate(RenderProfile profile)
    {
        if (profile.Id == Guid.Empty || string.IsNullOrWhiteSpace(profile.Name) || profile.Name.Length > 100)
            throw new InvalidDataException("A profile needs a name of 1 to 100 characters and a valid ID.");
        if (!Dx11Options.Contains(profile.Dx11Upscaler) || !Dx12Options.Contains(profile.Dx12Upscaler))
            throw new InvalidDataException("Unsupported upscaler value.");
        if (profile.Sharpness is < 0 or > 1)
            throw new InvalidDataException("Sharpness must be between 0 and 1, or empty for automatic.");
    }

    public static string Apply(string ini, RenderProfile profile)
    {
        Validate(profile);
        ini = Set(ini, "Upscalers", "Dx11Upscaler", profile.Dx11Upscaler);
        ini = Set(ini, "Upscalers", "Dx12Upscaler", profile.Dx12Upscaler);
        ini = Set(ini, "Sharpness", "OverrideSharpness", profile.Sharpness is null ? "auto" : "true");
        ini = Set(ini, "Sharpness", "Sharpness", profile.Sharpness?.ToString(CultureInfo.InvariantCulture) ?? "auto");

        return Set(ini, "Log", "LogToFile", profile.EnableLogging ? "true" : "false");
    }

    private static string Set(string text, string section, string key, string value)
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
}