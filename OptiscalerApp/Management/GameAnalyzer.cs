using OptiscalerApp.Models;
using OptiscalerApp.Paths;

namespace OptiscalerApp.Management;

/// <summary>Inspects filenames and version resources without loading or executing game libraries.</summary>
public sealed class GameAnalyzer : IGameAnalyzer
{
    /// <summary>
    ///     Scans the game installation for executables, anti-cheat files, and supported
    ///     upscaling components without loading or executing any discovered libraries.
    /// </summary>
    public Task<GameAnalysis> AnalyzeGameAsync(string rootPath, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            var analysis = new GameAnalysis();
            var root = PathUtil.Normalize(rootPath);

            if (!Directory.Exists(root)) throw new DirectoryNotFoundException("The game installation is unavailable.");

            var pending = new Stack<(string Path, int Depth)>();
            pending.Push((root, 0));
            var count = 0;

            while (pending.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var (directory, depth) = pending.Pop();

                try
                {
                    foreach (var file in Directory.EnumerateFiles(directory))
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        if (++count > 25000)
                        {
                            analysis.Evidence.Add(CreateEvidence(GameAnalysisEvidence.FileLimit,
                                                                 "Analysis stopped after 25,000 files; select a narrower game folder."));

                            return analysis;
                        }

                        if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) continue;

                        var name = Path.GetFileName(file).ToLowerInvariant();
                        if (name.Contains("easyanticheat") || name.Contains("battleye") ||
                            name is "beclient_x64.dll" or "vgk.sys")
                            analysis.Evidence.Add(CreateEvidence(GameAnalysisEvidence.AntiCheat,
                                                                 "Anti-cheat files detected. Do not install rendering modifications for this game.",
                                                                 file));
                        if (name.EndsWith(".exe", StringComparison.Ordinal))
                            analysis.Evidence.Add(CreateEvidence(GameAnalysisEvidence.Executable,
                                                                 "Executable candidate; select the actual game binary.",
                                                                 file));
                        var kind = name switch
                        {
                            "nvngx_dlss.dll" or "nvngx_dlssd.dll" => ComponentKind.Dlss,
                            "nvngx_dlssg.dll" => ComponentKind.DlssFrameGeneration,
                            "libxess.dll" => ComponentKind.Xess,
                            "optiscaler.dll" or "optiscaler.ini" => ComponentKind.Optiscaler,
                            "fakenvapi.dll" => ComponentKind.Fakenvapi,
                            _ when OptiscalerFiles.FsrNames.Contains(name) => ComponentKind.Fsr,
                            _ when OptiscalerFiles.ProxyNames.Contains(name) => ClassifyProxy(file),
                            _ => (ComponentKind?)null
                        };

                        if (kind is null) continue;

                        analysis.Components.Add(new DetectedComponent
                        {
                            Kind = kind.Value,
                            Path = file,
                            Version = SafeFiles.ReadFileVersion(file)
                        });
                        if (kind is ComponentKind.Optiscaler) analysis.HasOptiscalerFiles = true;
                    }

                    foreach (var child in Directory.EnumerateDirectories(directory))
                    {
                        if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0) continue;

                        if (depth < 12)
                            pending.Push((child, depth + 1));
                        else
                            analysis.Evidence.Add(CreateEvidence(GameAnalysisEvidence.DepthLimit,
                                                                 "Skipped a directory beyond the analysis depth limit.",
                                                                 child));
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    analysis.Evidence.Add(CreateEvidence(GameAnalysisEvidence.Unreadable, ex.Message, directory));
                }
            }

            return analysis;
        }, cancellationToken);
    }

    /// <summary>
    ///     A proxy filename alone says little: games ship Microsoft's own dbghelp.dll or version.dll, which is not a
    ///     mod at all, and OptiScaler is only one of the mods installed under these names.
    /// </summary>
    private static ComponentKind? ClassifyProxy(string file)
    {
        var (original, company) = SafeFiles.ReadLibraryIdentity(file);

        if (string.Equals(original, "OptiScaler.dll", StringComparison.OrdinalIgnoreCase))
            return ComponentKind.Optiscaler;

        return company?.Contains("Microsoft", StringComparison.OrdinalIgnoreCase) == true
            ? null
            : ComponentKind.InjectionProxy;
    }

    private static GameAnalysisEvidence CreateEvidence(string code, string message, string? path = null)
    {
        return new GameAnalysisEvidence { Code = code, Message = message, Path = path };
    }
}