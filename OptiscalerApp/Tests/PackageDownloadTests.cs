using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using OptiscalerApp.Management;
using OptiscalerApp.Paths;
using Xunit;

namespace Optiscaler.Tests;

public sealed class PackageDownloadTests : TempFolderTest
{
    private AppPaths Paths => new(Path.Combine(Root, "data"));

    private PackageDownloadService Service(HttpMessageHandler http)
    {
        return new PackageDownloadService(Paths, new HttpClient(http));
    }

    private static PackageRelease Release(string? digest = null)
    {
        return new PackageRelease("test", "Optiscaler.zip", "https://github.com/example/release.zip", digest);
    }

    /// <summary>A release archive as GitHub serves it: the package inside one top-level folder.</summary>
    private byte[] Bundle(string? unsafePath = null)
    {
        var pe = Path.Combine(Root, "fixture.dll");
        TestData.WritePe(pe, true);
        using var stream = new MemoryStream();

        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
        {
            // Real archives list their folders as entries too.
            archive.CreateEntry("bundle/OptiScaler/");

            void Add(string name, byte[] bytes)
            {
                using var output = archive.CreateEntry(name).Open();
                output.Write(bytes);
            }

            Add("bundle/OptiScaler.dll", File.ReadAllBytes(pe));
            Add("bundle/OptiScaler.ini", "[Upscalers]\nDx12Upscaler=auto"u8.ToArray());
            Add("bundle/OptiScaler/fakenvapi.dll", File.ReadAllBytes(pe));
            if (unsafePath is not null) Add(unsafePath, [1]);
        }

        return stream.ToArray();
    }

    private string GameExe()
    {
        var exe = Path.Combine(Folder("game"), "game.exe");
        TestData.WritePe(exe, false);

        return exe;
    }

    [Fact]
    public async Task ADownloadedBundleInstallsWithItsBundledComponentsAndReleaseTag()
    {
        var bytes = Bundle();
        var service = Service(StubHttpHandler.Bytes(bytes));
        var package = await service.DownloadPackageAsync(
                          Release("sha256:" + Convert.ToHexString(SHA256.HashData(bytes))), cancellationToken: Ct);
        var exe = GameExe();
        var installer = new GameInstallationService(Paths, service);

        var plan = await installer.PreviewInstallationAsync(exe, package, "dxgi.dll", null, Ct);
        await installer.ExecuteInstallationPlanAsync(plan, Ct);

        // The release tag identifies the package even where DLL file versions are unreadable.
        Assert.Equal("test", plan.Version);
        Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(exe)!, "OptiScaler", "fakenvapi.dll")));
        Assert.True((await installer.VerifyInstallationAsync(Path.GetDirectoryName(exe)!, Ct)).IsVerified);
    }

    [Fact]
    public async Task AComponentReleaseOverridesTheBundledCopy()
    {
        var http = StubHttpHandler.Bytes(Bundle());
        var service = Service(http);
        var package = await service.DownloadPackageAsync(Release(), cancellationToken: Ct);

        var plan = await new GameInstallationService(Paths, service).PreviewPackageInstallationAsync(
                       GameExe(), package, "dxgi.dll", null,
                       [new ComponentInstallSelection(DownloadComponent.FakeNvapi, Release())], cancellationToken: Ct);

        // The component release is the bundle already downloaded above, so it comes from the cache.
        Assert.Equal(1, http.Requests);
        Assert.Equal("fakenvapi.dll",
                     Assert.Single(plan.Files, f => f.RelativePath.EndsWith("fakenvapi.dll")).RelativePath);
    }

    [Fact]
    public async Task AStandaloneComponentAssetIsListedAndDownloadedUnderItsInstallName()
    {
        const string url = "https://github.com/optiscaler/OptiPatcher/releases/download/v0.41/OptiPatcher_v0.41.asi";
        var releases = JsonSerializer.Serialize(new[]
        {
            new
            {
                tag_name = "v0.41", draft = false, prerelease = false,
                assets = new[] { new { name = "OptiPatcher_v0.41.asi", browser_download_url = url } }
            }
        });
        byte[] asset = [1, 2, 3];
        var service = Service(new StubHttpHandler(request => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = request.RequestUri!.Host == "api.github.com"
                ? new StringContent(releases)
                : new ByteArrayContent(asset)
        }));

        var release = Assert.Single(await service.GetComponentReleasesAsync(DownloadComponent.OptiPatcher, Ct));
        var file = Assert.Single(await service.DownloadComponentAsync(DownloadComponent.OptiPatcher, release,
                                     cancellationToken: Ct));

        Assert.Equal("OptiPatcher.asi", Path.GetFileName(file));
        Assert.Equal(asset, await File.ReadAllBytesAsync(file, Ct));
    }

    [Theory]
    [InlineData("../outside.dll")]
    [InlineData("/absolute.dll")]
    [InlineData("..\\outside.dll")]
    [InlineData("C:\\outside.dll")]
    public async Task RejectsArchiveTraversalAndRemovesPartialDownload(string path)
    {
        var service = Service(StubHttpHandler.Bytes(Bundle(path)));

        await Assert.ThrowsAsync<InvalidDataException>(
            () => service.DownloadPackageAsync(Release(), cancellationToken: Ct));
        Assert.Empty(Directory.EnumerateDirectories(service.CacheDirectory));
        Assert.False(File.Exists(Path.Combine(Root, "outside.dll")));
    }

    [Fact]
    public async Task RejectsChecksumMismatch()
    {
        var service = Service(StubHttpHandler.Bytes(Bundle()));

        await Assert.ThrowsAsync<InvalidDataException>(
            () => service.DownloadPackageAsync(Release("sha256:bad"), cancellationToken: Ct));
        Assert.Empty(Directory.EnumerateDirectories(service.CacheDirectory));
    }

    [Fact]
    public async Task AFailedDownloadLeavesNoPartialPackage()
    {
        var service = Service(new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)));

        await Assert.ThrowsAsync<HttpRequestException>(
            () => service.DownloadPackageAsync(Release(), cancellationToken: Ct));
        Assert.Empty(Directory.EnumerateDirectories(service.CacheDirectory));
    }

    [Fact]
    public async Task RepeatedDownloadReusesCachedPackage()
    {
        var http = StubHttpHandler.Bytes(Bundle());
        var service = Service(http);

        var first = await service.DownloadPackageAsync(Release(), cancellationToken: Ct);
        var second = await service.DownloadPackageAsync(Release(), cancellationToken: Ct);

        Assert.Equal(first, second);
        Assert.Equal(1, http.Requests);
    }

    [Fact]
    public async Task DownloadPrunesStalePackagesWithTheirTags()
    {
        var service = Service(StubHttpHandler.Bytes(Bundle()));
        var stale = Directory.CreateDirectory(Path.Combine(service.CacheDirectory, "0123456789ABCDEF")).FullName;
        await File.WriteAllTextAsync(stale + ".release", "v0.1", Ct);
        Directory.SetLastWriteTimeUtc(stale, DateTime.UtcNow.AddDays(-40));

        await service.DownloadPackageAsync(Release(), cancellationToken: Ct);

        Assert.False(Directory.Exists(stale));
        Assert.False(File.Exists(stale + ".release"));
    }

    [Fact]
    public void ClearingTheCacheRemovesPackagesAndStagingFolders()
    {
        var service = Service(StubHttpHandler.Offline());
        Directory.CreateDirectory(Path.Combine(service.CacheDirectory, "0123456789ABCDEF", "files"));
        File.WriteAllText(Path.Combine(service.CacheDirectory, "0123456789ABCDEF", "files", "OptiScaler.dll"), "x");
        Directory.CreateDirectory(Path.Combine(service.CacheDirectory, "0123456789ABCDEF.abc.tmp"));
        Assert.True(service.GetCacheSize() > 0);

        service.ClearCache();

        Assert.Empty(Directory.EnumerateFileSystemEntries(service.CacheDirectory));
        Assert.Equal(0, service.GetCacheSize());
    }

    [Fact]
    public async Task ReleaseListFiltersDraftsPrereleasesAndSourceArchives()
    {
        var releases = new[] { ("stable", false, false), ("beta", false, true), ("draft", true, false) }
            .Select(r => new
            {
                tag_name = r.Item1,
                draft = r.Item2,
                prerelease = r.Item3,
                assets = new[]
                {
                    new { name = "Optiscaler.7z", browser_download_url = "https://github.com/test/file.7z" },
                    new { name = "source.zip", browser_download_url = "https://github.com/test/source.zip" }
                }
            });
        var service = Service(StubHttpHandler.Text(JsonSerializer.Serialize(releases)));

        Assert.Equal("stable", Assert.Single(await service.GetReleasesAsync(false, Ct)).Version);
        Assert.Equal(["stable", "beta"], (await service.GetReleasesAsync(true, Ct)).Select(r => r.Version));
    }

    [Theory]
    [InlineData("{\"message\":\"API rate limit exceeded\"}")]
    [InlineData("[{\"tag_name\":\"v1\"}]")]
    [InlineData("<html>Sign in to the network</html>")]
    public async Task UnexpectedReleaseListIsReportedAsInvalidData(string body)
    {
        var service = Service(StubHttpHandler.Text(body));

        await Assert.ThrowsAsync<InvalidDataException>(() => service.GetReleasesAsync(false, Ct));
    }

    [Fact]
    public async Task ReleaseListsAreReusedUntilCleared()
    {
        var http = StubHttpHandler.Text("[]");
        var service = Service(http);

        await service.GetReleasesAsync(false, Ct);
        await service.GetReleasesAsync(false, Ct);
        Assert.Equal(1, http.Requests);

        await service.GetReleasesAsync(true, Ct);
        Assert.Equal(2, http.Requests);

        service.ClearReleaseLists();
        await service.GetReleasesAsync(false, Ct);
        Assert.Equal(3, http.Requests);
    }

    [Fact]
    public async Task ReleaseListRequestsStillInFlightAreShared()
    {
        var body = new TaskCompletionSource<string>();
        var http = new SlowHttpHandler(body.Task);
        var service = Service(http);

        var first = service.GetReleasesAsync(false, Ct);
        var second = service.GetReleasesAsync(false, Ct);
        body.SetResult("[]");
        await Task.WhenAll(first, second);

        Assert.Equal(1, http.Requests);
    }
}
