using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OptiscalerApp.Paths;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace OptiscalerApp.Management;

/// <summary>Downloads release bundles into app storage before the existing installer previews game changes.</summary>
public sealed class PackageDownloadService(IAppPaths paths, HttpClient client)
{
    // GitHub allows 60 anonymous API requests per hour, so release lists are reused across channel switches and pages.
    private static readonly TimeSpan ReleaseListLifetime = TimeSpan.FromMinutes(15);

    private readonly ConcurrentDictionary<string, (DateTime FetchedAtUtc, IReadOnlyList<PackageRelease> Releases)>
        _releaseLists = new();

    public string CacheDirectory { get; } = Path.Combine(paths.RootDirectory, "packages");

    public static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("OptiscalerApp/1.0");

        return client;
    }

    public Task<IReadOnlyList<PackageRelease>> GetReleases_Async(
        bool beta, CancellationToken cancellationToken = default)
    {
        return GetReleases_Async(beta ? "Optiscaler-Client/OptiScaler-Betas" : "optiscaler/OptiScaler",
                                 "Optiscaler", beta, cancellationToken);
    }

    public Task<IReadOnlyList<PackageRelease>> GetComponentReleases_Async(
        DownloadComponent component, CancellationToken cancellationToken = default)
    {
        return GetReleases_Async(component.Repository, component.AssetPrefix, true, cancellationToken);
    }

    /// <summary>Makes the next release lookups query GitHub again instead of reusing recent lists.</summary>
    public void ClearReleaseLists() { _releaseLists.Clear(); }

    private async Task<IReadOnlyList<PackageRelease>> GetReleases_Async(string repository, string prefix, bool beta,
                                                                        CancellationToken cancellationToken)
    {
        var key = $"{repository}|{prefix}|{beta}";

        if (_releaseLists.TryGetValue(key, out var cached) &&
            DateTime.UtcNow - cached.FetchedAtUtc < ReleaseListLifetime)
            return cached.Releases;

        var releases = await FetchReleases_Async(repository, prefix, beta, cancellationToken);
        _releaseLists[key] = (DateTime.UtcNow, releases);

        return releases;
    }

    private async Task<IReadOnlyList<PackageRelease>> FetchReleases_Async(string repository, string prefix, bool beta,
                                                                         CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync($"https://api.github.com/repos/{repository}/releases?per_page=30",
                                                   cancellationToken);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var releases = new List<PackageRelease>();

        foreach (var release in json.RootElement.EnumerateArray())
        {
            if (release.GetProperty("draft").GetBoolean() ||
                (!beta && release.GetProperty("prerelease").GetBoolean()))
                continue;

            foreach (var asset in release.GetProperty("assets").EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString() ?? "";

                if (!IsSupportedAsset(repository, prefix, name)) continue;

                var version = release.GetProperty("tag_name").GetString()!;
                var url = asset.GetProperty("browser_download_url").GetString()!;
                var checksum = asset.TryGetProperty("digest", out var digest) ? digest.GetString() : null;
                releases.Add(new PackageRelease(version, name, url, checksum));
            }
        }

        return releases;
    }

    private static bool IsSupportedAsset(string repository, string prefix, string name)
    {
        if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
        if (repository == DownloadComponent.Nukem.Repository &&
            name.Contains("DLSSTweaks", StringComparison.OrdinalIgnoreCase))
            return false;

        return Path.GetExtension(name).ToLowerInvariant() switch
        {
            ".zip" or ".7z" => true,
            ".asi" => repository == DownloadComponent.OptiPatcher.Repository,
            _ => false
        };
    }

    public Task<string> DownloadPackage_Async(PackageRelease release, IProgress<string>? progress = null,
                                              CancellationToken cancellationToken = default)
    {
        return DownloadArchive_Async(release, true, progress, cancellationToken);
    }

    public async Task<IReadOnlyList<string>> DownloadComponent_Async(
        DownloadComponent component, PackageRelease release, IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var folder = await DownloadArchive_Async(release, false, progress, cancellationToken);

        return Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
            .Where(file => component.FileNames.Contains(Path.GetFileName(file), StringComparer.OrdinalIgnoreCase))
            .ToList();
    }

    private async Task<string> DownloadArchive_Async(PackageRelease release, bool isOptiscaler,
                                                     IProgress<string>? progress,
                                                     CancellationToken cancellationToken)
    {
        var uri = new Uri(release.DownloadUrl);

        if (uri.Scheme != "https" || uri.Host != "github.com")
            throw new InvalidDataException("Expected a GitHub release download.");

        var cache = CacheDirectory;
        PruneCache(cache);

        // One folder per release URL: repeated installs reuse it, and it only appears once fully extracted.
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(release.DownloadUrl)))[..16];
        var extracted = Path.Combine(cache, key);

        if (!Directory.Exists(extracted))
        {
            var staging = Path.Combine(cache, $"{key}.{Guid.NewGuid():N}.tmp");
            var archivePath = Path.Combine(staging, "download.archive");
            var files = Path.Combine(staging, "files");
            Directory.CreateDirectory(staging);

            try
            {
                progress?.Report($"Downloading {release.Version}…");
                await DownloadFile_Async(uri, archivePath, cancellationToken);
                await VerifyChecksum_Async(archivePath, release.Digest, cancellationToken);

                if (!isOptiscaler && release.AssetName.EndsWith(".asi", StringComparison.OrdinalIgnoreCase))
                {
                    Directory.CreateDirectory(files);
                    File.Move(archivePath, Path.Combine(files, "OptiPatcher.asi"));
                }
                else
                {
                    progress?.Report($"Extracting {release.Version}…");
                    await Task.Run(() => ExtractArchive_Async(archivePath, files, cancellationToken),
                                   cancellationToken);
                }

                if (isOptiscaler) FindOptiscalerPackage(files);
                if (!Directory.Exists(extracted)) Directory.Move(files, extracted);
            }
            finally
            {
                Directory.Delete(staging, true);
            }
        }

        Directory.SetLastWriteTimeUtc(extracted, DateTime.UtcNow);

        return isOptiscaler ? FindOptiscalerPackage(extracted) : extracted;
    }

    /// <summary>Total size of downloaded and extracted packages.</summary>
    public long GetCacheSize()
    {
        if (!Directory.Exists(CacheDirectory)) return 0;

        return new DirectoryInfo(CacheDirectory).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
    }

    /// <summary>Deletes downloaded packages; returns how many could not be removed because they are in use.</summary>
    public int ClearCache()
    {
        if (!Directory.Exists(CacheDirectory)) return 0;

        var failures = 0;

        // Snapshot the list: folders are renamed inside the directory being enumerated.
        foreach (var directory in Directory.GetDirectories(CacheDirectory))
            try
            {
                // A package folder counts as complete while it exists, so a delete that fails halfway must not leave
                // it under its key. Renaming first fails cleanly when files are in use; a partly deleted staging
                // folder is pruned later.
                var doomed = directory.EndsWith(".tmp", StringComparison.Ordinal)
                    ? directory
                    : $"{directory}.{Guid.NewGuid():N}.tmp";
                if (doomed != directory) Directory.Move(directory, doomed);
                Directory.Delete(doomed, true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failures++;
            }

        return failures;
    }

    /// <summary>Removes packages unused for a month and staging folders left by an interrupted download.</summary>
    private static void PruneCache(string cache)
    {
        if (!Directory.Exists(cache)) return;

        foreach (var directory in Directory.EnumerateDirectories(cache))
        {
            var age = DateTime.UtcNow - Directory.GetLastWriteTimeUtc(directory);

            if (age < (directory.EndsWith(".tmp", StringComparison.Ordinal)
                    ? TimeSpan.FromDays(1)
                    : TimeSpan.FromDays(30)))
                continue;

            try
            {
                Directory.Delete(directory, true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Another instance may still be using it; try again next time.
            }
        }
    }

    private async Task DownloadFile_Async(Uri uri, string destination, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = File.Create(destination);
        await CopyLimited_Async(input, output, 512L * 1024 * 1024, cancellationToken);
    }

    private static async Task VerifyChecksum_Async(string file, string? digest, CancellationToken cancellationToken)
    {
        if (digest is null) return;

        if (!digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Unsupported release checksum.");

        var hash = await HashFile_Async(file, cancellationToken);

        if (!hash.Equals(digest[7..], StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The downloaded package does not match its release checksum.");
    }

    private static async Task ExtractArchive_Async(string archivePath, string folder,
                                                   CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(folder);
        using var archive = ArchiveFactory.OpenArchive(archivePath);

        // Read sequentially so solid 7z archives are decompressed only once.
        using var stream = File.OpenRead(archivePath);
        using var reader = archive.Type == ArchiveType.SevenZip
            ? archive.ExtractAllEntries()
            : ReaderFactory.OpenReader(stream);
        var remainingBytes = 2L * 1024 * 1024 * 1024;
        var entryCount = 0;

        while (reader.MoveToNextEntry())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = reader.Entry;

            if (++entryCount > 2000 || entry.LinkTarget is not null)
                throw new InvalidDataException("Unsupported package contents.");

            var name = (entry.Key ?? "").Replace('\\', '/');
            if (entry.IsDirectory) name = name.TrimEnd('/');

            // Rejects traversal, rooted, and drive-qualified entry names.
            var destination = PathUtil.ResolveChild(folder, name);

            if (entry.IsDirectory)
            {
                Directory.CreateDirectory(destination);

                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            using var input = reader.OpenEntryStream();
            await using var output = new FileStream(destination, FileMode.CreateNew);
            remainingBytes -= await CopyLimited_Async(input, output, remainingBytes, cancellationToken);
        }
    }

    private static string FindOptiscalerPackage(string folder)
    {
        var packages = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
            .Where(file => Path.GetFileName(file).Equals("OptiScaler.dll", StringComparison.OrdinalIgnoreCase))
            .Select(file => Path.GetDirectoryName(file)!)
            .Where(directory => File.Exists(Path.Combine(directory, "OptiScaler.ini")))
            .ToList();

        if (packages.Count != 1)
            throw new InvalidDataException("The release must contain one OptiScaler package with its INI.");

        SafeFiles.RequireX64PeFile(Path.Combine(packages[0], "OptiScaler.dll"), true);

        return packages[0];
    }

    private static async Task<string> HashFile_Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);

        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
    }

    private static async Task<long> CopyLimited_Async(Stream input, Stream output, long limit,
                                                      CancellationToken cancellationToken)
    {
        var buffer = new byte[65536];
        long total = 0;
        int read;

        while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
        {
            total += read;

            if (total > limit) throw new InvalidDataException("Package exceeds the download or extraction size limit.");

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        return total;
    }
}