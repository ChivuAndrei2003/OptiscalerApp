using System.Security.Cryptography;
using System.Text;

namespace Optiscaler.Core.Games;

public sealed record GameId(string Value)
{
    public static GameId Create(GamePlatform platform, string? externalId, string installPath)
    {
        // A launcher ID is preferable because it survives installation-directory moves.
        if (!string.IsNullOrWhiteSpace(externalId))
            return new GameId($"{platform}:{externalId.Trim()}");

        var normalizedPath = NormalizeInstallPath(installPath);
        if (OperatingSystem.IsWindows())
            normalizedPath = normalizedPath.ToUpperInvariant();

        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalizedPath));
        var shortHash = Convert.ToHexString(hashBytes)[..16].ToLowerInvariant();

        return new GameId($"{platform}:path:{shortHash}");
    }

    // Resolve relative segments and trailing separators without changing a filesystem root.
    // Symbolic links are not resolved; on non-Windows systems casing is preserved.
    public static string NormalizeInstallPath(string installPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installPath);

        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(installPath));
    }

    public override string ToString()
    {
        return Value;
    }
}