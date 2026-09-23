using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using OptiscalerApp.Models;
using OptiscalerApp.Paths;

namespace OptiscalerApp.Management;

/// <summary>Everything a support report can include; missing parts are simply left out.</summary>
public sealed record DiagnosticsInput
{
    public required GameRecord Game { get; init; }
    public string? ExecutablePath { get; init; }

    /// <summary>The normalized folder OptiScaler is installed into, used to pick this game's operations.</summary>
    public string? TargetDirectory { get; init; }

    public IReadOnlyList<GpuInfo> Gpus { get; init; } = [];
    public CompatibilityEntry? Compatibility { get; init; }
    public IReadOnlyList<DetectedComponent> Components { get; init; } = [];
    public VerificationResult? Verification { get; init; }
    public IReadOnlyList<OperationJournal> History { get; init; } = [];
    public string? CurrentIni { get; init; }
    public string? LogTail { get; init; }
}

/// <summary>
///     Builds a Markdown report to paste into a GitHub issue or Discord. It answers the questions bug templates ask
///     (versions, GPU, OS, installed files, settings) and replaces the user's home folder and name.
/// </summary>
public static class DiagnosticsReport
{
    private const int MaxIniLines = 60;

    public static string Build(DiagnosticsInput input)
    {
        var text = new StringBuilder();
        var game = input.Game;

        text.AppendLine($"### OptiScaler report: {game.Name}");
        text.AppendLine();
        text.AppendLine($"- App: OptiscalerApp {Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "dev"}");
        text.AppendLine($"- OS: {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})");
        text.AppendLine(input.Gpus.Count == 0
                            ? "- GPU: not detected"
                            : "- GPU: " + string.Join("; ", input.Gpus.Select(DescribeGpu)));
        text.AppendLine($"- Platform: {game.Platform}" + (game.ExternalId is { } id ? $" ({id})" : ""));
        text.AppendLine($"- Executable: {input.ExecutablePath ?? "not selected"}");

        if (input.Compatibility is { } compat)
            text.AppendLine($"- Wiki compatibility: {compat.Status}, inputs {Or(compat.Inputs, "not listed")}, " +
                            $"OptiPatcher {(compat.OptiPatcherSupported ? "supported" : "not listed")}" +
                            (compat.Notes.Length > 0 ? $". Notes: {compat.Notes}" : ""));
        else
            text.AppendLine("- Wiki compatibility: not in the tested list");

        text.AppendLine();
        text.AppendLine("#### Detected files");

        if (input.Components.Count == 0) text.AppendLine("- none");

        foreach (var component in input.Components)
            text.AppendLine($"- {component.DisplayName}: {Relative(game, component.Path)}");

        text.AppendLine();
        text.AppendLine("#### Managed operations");
        var history = input.History.Where(j => input.TargetDirectory is { } target &&
                                               PathUtil.AreSame(j.TargetDirectory, target)).ToList();

        if (history.Count == 0) text.AppendLine("- none");

        foreach (var journal in history.Take(10))
            text.AppendLine($"- {journal.CreatedAtUtc:u} · {journal.Kind} · {journal.State} · {journal.Description}" +
                            (journal.Version is { } version ? $" · {version}" : ""));

        if (input.Verification is { } verification)
            text.AppendLine(verification.IsVerified
                                ? "- Verification: all managed files match"
                                : "- Verification: " + string.Join("; ", verification.Issues));

        if (input.CurrentIni is { } ini)
        {
            var custom = ProfileIni.ReadIniValues(ini)
                .Where(pair => !pair.Value.Equals("auto", StringComparison.OrdinalIgnoreCase))
                .Select(pair => $"[{pair.Key.Section}] {pair.Key.Key}={pair.Value}").ToList();
            text.AppendLine();
            text.AppendLine("#### OptiScaler.ini (values other than auto)");
            text.AppendLine("```ini");
            foreach (var line in custom.Take(MaxIniLines)) text.AppendLine(line);
            if (custom.Count > MaxIniLines) text.AppendLine($"; … {custom.Count - MaxIniLines} more");
            if (custom.Count == 0) text.AppendLine("; every key is auto");
            text.AppendLine("```");
        }

        if (input.LogTail is { Length: > 0 } log)
        {
            text.AppendLine();
            text.AppendLine("#### OptiScaler.log (last lines)");
            text.AppendLine("```");
            text.AppendLine(log.TrimEnd());
            text.AppendLine("```");
        }

        return Redact(text.ToString());
    }

    /// <summary>Hides the home folder and account name, which paths and logs otherwise reveal.</summary>
    public static string Redact(string text)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (home.Length > 1) text = text.Replace(home, "~", StringComparison.OrdinalIgnoreCase);

        var user = Environment.UserName;

        // Very short names would also match ordinary words.
        return user.Length >= 3 ? text.Replace(user, "<user>", StringComparison.OrdinalIgnoreCase) : text;
    }

    /// <summary>The last lines of a log, read without loading a multi-megabyte trace log whole.</summary>
    public static async Task<string?> ReadLogTail_Async(string path, int lines,
                                                        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path)) return null;

        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        const int window = 64 * 1024;
        stream.Seek(Math.Max(0, stream.Length - window), SeekOrigin.Begin);
        using var reader = new StreamReader(stream);
        var tail = (await reader.ReadToEndAsync(cancellationToken)).Replace("\r\n", "\n").Split('\n');

        return string.Join("\n", tail.TakeLast(lines));
    }

    private static string DescribeGpu(GpuInfo gpu)
    {
        return gpu.DedicatedVram is { } vram and > 0 ? $"{gpu.Name} ({vram / (1024 * 1024 * 1024.0):0.#} GB)" : gpu.Name;
    }

    private static string Relative(GameRecord game, string path)
    {
        var root = game.Installations.Select(i => i.RootPath)
            .FirstOrDefault(r => path.StartsWith(r, StringComparison.OrdinalIgnoreCase));

        return root is null ? path : Path.GetRelativePath(root, path);
    }

    private static string Or(string value, string fallback) { return value.Length > 0 ? value : fallback; }
}
