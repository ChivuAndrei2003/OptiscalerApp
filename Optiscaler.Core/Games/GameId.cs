using System.Security.Cryptography;
using System.Text;

namespace Optiscaler.Core.Games;

public sealed record GameId(string Value)
{
    public static GameId Create(GamePlatform platform, string? externalId, string installPath)
    {
        if (!string.IsNullOrEmpty(externalId)) return new GameId($"{platform}:{externalId.Trim()}.");

        if (string.IsNullOrEmpty(installPath))
            throw new ArgumentException("EXternal ID or install path missing", nameof(installPath));

        var normalizedPath = installPath
            .Trim()
            .TrimEnd('/', '\\')
            .Replace('\\', '/')
            .ToUpperInvariant();

        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalizedPath));
        var shortHash = Convert.ToHexString(hashBytes)[..16].ToLowerInvariant();

        return new GameId($"{platform}:path:{normalizedPath}");
    }

    public override string ToString()
    {
        return Value;
    }
}