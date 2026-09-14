using System.Security.Cryptography;
using System.Text.Json;
using OptiscalerApp.Models;
using OptiscalerApp.Paths;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace OptiscalerApp.Management;

public sealed record DownloadComponent(string Name, string Repository, string AssetPrefix, string[] FileNames)
{
    // Same release repositories and file locations as Optiscaler-Client/config.json.
    public static readonly DownloadComponent Fsr = new("FSR 4 / INT8", "Optiscaler-Client/OptiScaler-Extras", "FSR",
    [
        "amd_fidelityfx_upscaler_dx12.dll", "amdxcffx64.dll", "amdxc64.dll"
    ]);

    public static readonly DownloadComponent FakeNvapi =
        new("FakeNvapi", "optiscaler/fakenvapi", "fakenvapi", ["fakenvapi.dll", "fakenvapi.ini"]);

    public static readonly DownloadComponent OptiPatcher =
        new("OptiPatcher", "optiscaler/OptiPatcher", "OptiPatcher", ["OptiPatcher.asi"]);

    public static readonly DownloadComponent Nukem =
        new("NukemFG", "Nukem9/dlssg-to-fsr3", "dlssg-to-fsr3", ["dlssg_to_fsr3_amd_is_better.dll"]);

    public string Destination(string fileName)
    {
        if (fileName.Equals("OptiPatcher.asi", StringComparison.OrdinalIgnoreCase))
            return Path.Combine("plugins", fileName);
        if (fileName.Equals("amdxc64.dll", StringComparison.OrdinalIgnoreCase))
            return Path.Combine("OptiScaler", fileName);

        return fileName;
    }
}

public sealed record PackageRelease(string Version, string AssetName, string DownloadUrl, string? Digest)
{
    public override string ToString() { return Version; }
}

/// <summary>Downloads release bundles into app storage before the existing installer previews game changes.</summary>
public sealed class PackageDownloadService(IAppPaths paths, HttpClient client)
{
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

    private async Task<IReadOnlyList<PackageRelease>> GetReleases_Async(string repository, string prefix, bool beta,
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

                releases.Add(new PackageRelease(release.GetProperty("tag_name").GetString()!, name,
                                                asset.GetProperty("browser_download_url").GetString()!,
                                                asset.TryGetProperty("digest", out var digest)
                                                    ? digest.GetString()
                                                    : null));
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

    private async Task<string> DownloadArchive_Async(PackageRelease release, bool isOptiscaler,
                                                     IProgress<string>? progress,
                                                     CancellationToken cancellationToken)
    {
        var uri = new Uri(release.DownloadUrl);

        if (uri.Scheme != "https" || uri.Host != "github.com")
            throw new InvalidDataException("Expected a GitHub release download.");

        var root = SafeFiles.NormalizeAndValidateAbsolutePath(
                                                              Path.Combine(paths.RootDirectory, "packages",
                                                                           Guid.NewGuid().ToString("N")));
        var archivePath = Path.Combine(root, "download.archive");
        var extracted = Path.Combine(root, "files");
        Directory.CreateDirectory(root);

        try
        {
            progress?.Report($"Downloading {release.Version}…");
            await DownloadFile_Async(uri, archivePath, cancellationToken);
            await VerifyChecksum_Async(archivePath, release.Digest, cancellationToken);

            if (!isOptiscaler && release.AssetName.EndsWith(".asi", StringComparison.OrdinalIgnoreCase))
            {
                Directory.CreateDirectory(extracted);
                File.Move(archivePath, Path.Combine(extracted, "OptiPatcher.asi"));
            }
            else
            {
                progress?.Report($"Extracting {release.Version}…");
                await Task.Run(() => ExtractArchive_Async(archivePath, extracted, cancellationToken),
                               cancellationToken);
                File.Delete(archivePath);
            }

            return isOptiscaler ? FindOptiscalerPackage(extracted) : extracted;
        }
        catch
        {
            Directory.Delete(root, true);

            throw;
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

            if (name.Split('/').Any(part => part is ".." || part.Contains(':')))
                throw new InvalidDataException("Unsafe archive path.");

            var destination = SafeFiles.ResolveSafeChildPath(folder, name);

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

    public async Task<InstallPlan> AddComponentToPlan_Async(InstallPlan plan, DownloadComponent component,
                                                            PackageRelease release, IProgress<string>? progress = null,
                                                            CancellationToken cancellationToken = default)
    {
        var folder = await DownloadArchive_Async(release, false, progress, cancellationToken);
        var sources = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
            .Where(f => component.FileNames.Contains(Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)).ToList();

        return await AddComponentFilesToPlan_Async(plan, component, sources, release.Version, cancellationToken);
    }
    
    public static async Task<InstallPlan> AddComponentFilesToPlan_Async(InstallPlan plan, DownloadComponent component,
                                                                        IReadOnlyList<string> sources, string version,
                                                                        CancellationToken cancellationToken = default)
    {
        var names = sources.Select(Path.GetFileName).ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (names.Any(name => !component.FileNames.Contains(name, StringComparer.OrdinalIgnoreCase)))
            throw new InvalidDataException(
                                           $"Select a supported {component.Name} file: {string.Join(", ", component.FileNames)}.");

        if (component == DownloadComponent.Fsr &&
            !names.Contains("amd_fidelityfx_upscaler_dx12.dll") && !names.Contains("amdxcffx64.dll"))
            throw new InvalidDataException(
                                           "The FSR release must include an upscaler binary, not only the driver companion.");

        if (!names.Any(name => Path.GetExtension(name)?.ToLowerInvariant() is ".dll" or ".asi"))
            throw new InvalidDataException($"{component.Name} does not contain its expected binary.");

        if (names.Count != sources.Count)
            throw new InvalidDataException(
                                           "Release contains multiple variants of the same component. Use a local package to choose a variant.");

        var files = plan.Files
            .Where(f => !component.FileNames.Contains(Path.GetFileName(f.RelativePath),
                                                      StringComparer.OrdinalIgnoreCase)).ToList();

        foreach (var source in sources)
        {
            SafeFiles.NormalizeAndValidateAbsolutePath(source);
            if (!source.EndsWith(".ini", StringComparison.OrdinalIgnoreCase)) SafeFiles.RequireX64PeFile(source, true);
            var relative = component.Destination(Path.GetFileName(source));
            var target = SafeFiles.ResolveSafeChildPath(plan.TargetDirectory, relative);

            var hash = await HashFile_Async(source, cancellationToken);
            files.Add(new PlannedFile(source, relative,
                                      File.Exists(target) ? await HashFile_Async(target, cancellationToken) : null,
                                      hash,
                                      SourceHash: hash));
        }

        return plan with
        {
            Files = files.AsReadOnly(), Description = plan.Description + $" · {component.Name} {version}"
        };
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