using System.Diagnostics;
using Optiscaler.Core.Abstractions;
using Optiscaler.Core.Analysis;
using Optiscaler.Core.Games;

namespace Optiscaler.Infrastructure.Management;

/// <summary>Inspects filenames and version resources without loading or executing game libraries.</summary>
public sealed class GameAnalyzer : IGameAnalyzer
{
    /// <summary>
    /// Scans the game installation for executables, anti-cheat files, and supported
    /// upscaling components without loading or executing any discovered libraries.
    /// </summary>
    public Task<GameAnalysis> AnalyzeGame_Async(GameId gameId, GameInstallation installation,
                                                CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            var analysis = new GameAnalysis { GameId = gameId, InstallState = InstallState.NotInstalled };
            var root = SafeFiles.NormalizeAndValidateAbsolutePath(installation.RootPath);

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
                            analysis.Evidence.Add(CreateEvidence("analysis.limit",
                                                                 "Analysis stopped after 25,000 files; select a narrower game folder."));

                            return analysis;
                        }

                        if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) continue;

                        var name = Path.GetFileName(file).ToLowerInvariant();
                        if (name.Contains("easyanticheat") || name.Contains("battleye") ||
                            name is "beclient_x64.dll" or "vgk.sys")
                            analysis.Evidence.Add(CreateEvidence("game.anticheat",
                                                                 "Anti-cheat files detected. Do not install rendering modifications for this game.",
                                                                 file));
                        if (name.EndsWith(".exe", StringComparison.Ordinal))
                            analysis.Evidence.Add(CreateEvidence("game.executable",
                                                                 "Executable candidate; select the actual game binary.",
                                                                 file));
                        var kind = name switch
                        {
                            "nvngx_dlss.dll" or "nvngx_dlssd.dll" => ComponentKind.Dlss,
                            "nvngx_dlssg.dll" => ComponentKind.DlssFrameGeneration,
                            "libxess.dll" => ComponentKind.Xess,
                            "amd_fidelityfx_upscaler_dx12.dll" => ComponentKind.Fsr,
                            "optiscaler.dll" or "optiscaler.ini" => ComponentKind.Optiscaler,
                            "fakenvapi.dll" => ComponentKind.Fakenvapi,
                            _ when GameInstallationService.ProxyNames.Contains(name) => ComponentKind.InjectionProxy,
                            _ => (ComponentKind?)null
                        };

                        if (kind is null) continue;

                        string? version = null;

                        try
                        {
                            version = FileVersionInfo.GetVersionInfo(file).FileVersion;
                        }
                        catch (Exception ex) when (ex is IOException or ArgumentException)
                        {
                        }

                        analysis.Components.Add(new DetectedComponent
                        {
                            Kind = kind.Value, Path = file, Version = version, Origin = ComponentOrigin.Untracked,
                            Evidence =
                            [
                                CreateEvidence("component.filename",
                                               "Filename evidence only; not proof of origin, compatibility, or runtime activation.",
                                               file)
                            ]
                        });
                        if (kind is ComponentKind.Optiscaler or ComponentKind.InjectionProxy)
                            analysis.InstallState = InstallState.UntrackedInstallation;
                    }

                    foreach (var child in Directory.EnumerateDirectories(directory))
                    {
                        if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0) continue;

                        if (depth < 12) pending.Push((child, depth + 1));
                        else
                            analysis.Evidence.Add(CreateEvidence("analysis.depth",
                                                                 "Skipped a directory beyond the analysis depth limit.",
                                                                 child));
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    analysis.Evidence.Add(CreateEvidence("analysis.unreadable", ex.Message, directory));
                }
            }

            return analysis;
        }, cancellationToken);
    }

    private static GameAnalysisEvidence CreateEvidence(string code, string message, string? path = null)
    {
        return new GameAnalysisEvidence
            { Code = code, Message = message, Path = path, Confidence = EvidenceConfidence.Low };
    }
}