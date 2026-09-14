using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using OptiscalerApp.DependencyInjection;
using OptiscalerApp.Management;
using OptiscalerApp.Models;
using OptiscalerApp.Paths;
using OptiscalerApp.Persistence;
using OptiscalerApp.ViewModels;
using Xunit;

namespace Optiscaler.Tests;

public sealed class PackageAndArtworkTests : IDisposable
{
    private readonly string _root = Path.Combine(OperatingSystem.IsMacOS() ? "/private/tmp" : Path.GetTempPath(),
                                                 "Optiscaler-packages-" + Guid.NewGuid().ToString("N"));

    public PackageAndArtworkTests() { Directory.CreateDirectory(_root); }

    private AppPaths Paths => new(Path.Combine(_root, "data"));
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() { Directory.Delete(_root, true); }

    private byte[] Bundle(string? unsafePath = null)
    {
        var pe = Path.Combine(_root, "fixture.dll");
        InstallationTests.WritePe(pe, true);
        using var stream = new MemoryStream();

        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
        {
            archive.CreateEntry("bundle/plugins/");

            void Add(string name, byte[] bytes)
            {
                using var output = archive.CreateEntry(name).Open();
                output.Write(bytes);
            }

            Add("bundle/OptiScaler.dll", File.ReadAllBytes(pe));
            Add("bundle/OptiScaler.ini", "[Upscalers]\nDx12Upscaler=auto"u8.ToArray());
            Add("bundle/OptiScaler/fakenvapi.dll", File.ReadAllBytes(pe));
            Add("bundle/plugins/OptiPatcher.asi", File.ReadAllBytes(pe));
            if (unsafePath is not null) Add(unsafePath, [1]);
        }

        return stream.ToArray();
    }

    [Fact]
    public async Task DownloadsBundleThenInstallsVerifiesAndRestoresAllComponents()
    {
        var bytes = Bundle();
        using var client = new HttpClient(new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(bytes)
        }));
        var service = new PackageDownloadService(Paths, client);
        var package = await service.DownloadPackage_Async(new PackageRelease("test", "Optiscaler.zip",
                                                                             "https://github.com/example/release.zip",
                                                                             "sha256:" +
                                                                             Convert
                                                                                 .ToHexString(SHA256.HashData(bytes))),
                                                          cancellationToken: Ct);
        var game = Path.Combine(_root, "game");
        Directory.CreateDirectory(game);
        var executable = Path.Combine(game, "game.exe");
        InstallationTests.WritePe(executable, false);
        var installer = new GameInstallationService(Paths, service);
        var plan = await installer.PreviewInstallation_Async(executable, package, "dxgi.dll", null, Ct);
        Assert.Contains(plan.Files, f => f.RelativePath == Path.Combine("OptiScaler", "fakenvapi.dll"));
        Assert.False(File.Exists(Path.Combine(game, "dxgi.dll")));
        await installer.ExecuteInstallationPlan_Async(plan, Ct);
        Assert.True((await installer.VerifyInstallation_Async(game, Ct)).IsVerified);
        Assert.True(File.Exists(Path.Combine(game, "plugins", "OptiPatcher.asi")));
        await installer.RestoreLatestOperation_Async(game, Ct);
        Assert.False(File.Exists(Path.Combine(game, "OptiScaler", "fakenvapi.dll")));
    }

    [Theory]
    [InlineData("../outside.dll")]
    [InlineData("/absolute.dll")]
    [InlineData("..\\outside.dll")]
    [InlineData("C:\\outside.dll")]
    public async Task RejectsArchiveTraversalAndRemovesPartialDownload(string path)
    {
        var bytes = Bundle(path);
        using var client = new HttpClient(new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(bytes)
        }));
        var service = new PackageDownloadService(Paths, client);
        await Assert.ThrowsAsync<InvalidDataException>(() =>
                                                           service.DownloadPackage_Async(new PackageRelease("test",
                                                             "Optiscaler.zip",
                                                             "https://github.com/example/release.zip", null),
                                                            cancellationToken: Ct));
        Assert.Empty(Directory.EnumerateDirectories(Path.Combine(Paths.RootDirectory, "packages")));
        Assert.False(File.Exists(Path.Combine(_root, "outside.dll")));
    }

    [Fact]
    public async Task RejectsChecksumMismatch()
    {
        using var client = new HttpClient(new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(Bundle())
        }));
        await Assert.ThrowsAsync<InvalidDataException>(() =>
                                                           new PackageDownloadService(Paths, client)
                                                               .DownloadPackage_Async(
                                                                new PackageRelease("test", "Optiscaler.zip",
                                                                 "https://github.com/example/release.zip",
                                                                 "sha256:bad"), cancellationToken: Ct));
        Assert.Empty(Directory.EnumerateDirectories(Path.Combine(Paths.RootDirectory, "packages")));
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task FailedDownloadsLeaveNoPartialPackage(HttpStatusCode status)
    {
        using var client = new HttpClient(new Handler(_ => new HttpResponseMessage(status)));
        await Assert.ThrowsAsync<HttpRequestException>(() =>
                                                           new PackageDownloadService(Paths, client)
                                                               .DownloadPackage_Async(
                                                                new PackageRelease("test", "Optiscaler.zip",
                                                                 "https://github.com/example/release.zip",
                                                                 null), cancellationToken: Ct));
        Assert.Empty(Directory.EnumerateDirectories(Path.Combine(Paths.RootDirectory, "packages")));
    }

    [Fact]
    public async Task CancelledDownloadLeavesNoPartialPackage()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        using var client = new HttpClient(new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(Bundle())
        }));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                                                                    new PackageDownloadService(Paths, client)
                                                                        .DownloadPackage_Async(
                                                                         new PackageRelease("test",
                                                                          "Optiscaler.zip",
                                                                          "https://github.com/example/release.zip",
                                                                          null),
                                                                         cancellationToken: cancellation.Token));
        Assert.Empty(Directory.EnumerateDirectories(Path.Combine(Paths.RootDirectory, "packages")));
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
                    new
                    {
                        name = "Optiscaler.7z",
                        browser_download_url = "https://github.com/test/file.7z",
                        digest = (string?)null
                    },
                    new
                    {
                        name = "source.zip",
                        browser_download_url = "https://github.com/test/source.zip",
                        digest = (string?)null
                    }
                }
            });
        using var client = new HttpClient(new Handler(request =>
        {
            Assert.Contains("api.github.com/repos/", request.RequestUri!.AbsoluteUri);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(releases))
            };
        }));
        var service = new PackageDownloadService(Paths, client);
        Assert.Equal("stable", Assert.Single(await service.GetReleases_Async(false, Ct)).Version);
        Assert.Equal(2, (await service.GetReleases_Async(true, Ct)).Count);
    }


    [Fact]
    public async Task StandaloneOptiPatcherReleaseIsListedPreviewedAndRestored()
    {
        var pe = Path.Combine(_root, "OptiPatcher.asi");
        InstallationTests.WritePe(pe, true);
        var bytes = File.ReadAllBytes(pe);
        using var client = new HttpClient(new Handler(request => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = request.RequestUri!.Host == "api.github.com"
                ? new StringContent(JsonSerializer.Serialize(new[]
                {
                    new
                    {
                        tag_name = "v0.41",
                        draft = false,
                        prerelease = false,
                        assets = new[]
                        {
                            new
                            {
                                name = "OptiPatcher_v0.41.asi",
                                browser_download_url =
                                    "https://github.com/optiscaler/OptiPatcher/releases/download/v0.41/OptiPatcher_v0.41.asi"
                            }
                        }
                    }
                }))
                : new ByteArrayContent(bytes)
        }));
        var service = new PackageDownloadService(Paths, client);
        var release = Assert.Single(await service.GetComponentReleases_Async(DownloadComponent.OptiPatcher, Ct));
        var game = Directory.CreateDirectory(Path.Combine(_root, "component-game")).FullName;
        var plan = new InstallPlan(Guid.NewGuid(), game, OperationKind.InstallOptiscaler, "Test", [],
                                   DateTimeOffset.UtcNow);
        var sources = await service.DownloadComponent_Async(DownloadComponent.OptiPatcher, release,
            cancellationToken: Ct);
        plan = await GameInstallationService.AddComponentFilesToPlan_Async(
            plan, DownloadComponent.OptiPatcher, sources, release.Version, Ct);
        var file = Assert.Single(plan.Files);
        Assert.Equal(Path.Combine("plugins", "OptiPatcher.asi"), file.RelativePath);
        Assert.False(File.Exists(Path.Combine(game, file.RelativePath)));
        var installer = new GameInstallationService(Paths, service);
        await installer.ExecuteInstallationPlan_Async(plan, Ct);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(Path.Combine(game, file.RelativePath), Ct));
        await installer.RestoreLatestOperation_Async(game, Ct);
        Assert.False(File.Exists(Path.Combine(game, file.RelativePath)));
    }

    [Fact]
    public async Task ComponentOverrideReplacesBundleEntryAndRestoresOriginal()
    {
        using var client = new HttpClient();
        var game = Directory.CreateDirectory(Path.Combine(_root, "override-game")).FullName;
        var existing = Path.Combine(game, "fakenvapi.dll");
        await File.WriteAllTextAsync(existing, "original", Ct);
        var source = Path.Combine(_root, "fakenvapi.dll");
        InstallationTests.WritePe(source, true);
        var plan = new InstallPlan(Guid.NewGuid(), game, OperationKind.InstallOptiscaler, "Test",
        [
            new PlannedFile("unused", Path.Combine("OptiScaler", "fakenvapi.dll"), null, "unused")
        ], DateTimeOffset.UtcNow);
        plan = await GameInstallationService.AddComponentFilesToPlan_Async(plan, DownloadComponent.FakeNvapi, [source],
                                                                          "local", Ct);
        Assert.Equal("fakenvapi.dll", Assert.Single(plan.Files).RelativePath);
        var installer = new GameInstallationService(Paths, new PackageDownloadService(Paths, client));
        await installer.ExecuteInstallationPlan_Async(plan, Ct);
        await installer.RestoreLatestOperation_Async(game, Ct);
        Assert.Equal("original", await File.ReadAllTextAsync(existing, Ct));
    }

    [Fact]
    public async Task ComponentOverrideRejectsWrongFileAndDuplicateVariants()
    {
        var game = Directory.CreateDirectory(Path.Combine(_root, "reject-game")).FullName;
        var plan = new InstallPlan(Guid.NewGuid(), game, OperationKind.InstallOptiscaler, "Test", [],
                                   DateTimeOffset.UtcNow);
        var source = Path.Combine(_root, "fakenvapi.dll");
        InstallationTests.WritePe(source, true);
        await Assert.ThrowsAsync<InvalidDataException>(() => GameInstallationService.AddComponentFilesToPlan_Async(
                                                        plan, DownloadComponent.Nukem, [source], "local", Ct));
        await Assert.ThrowsAsync<InvalidDataException>(() => GameInstallationService.AddComponentFilesToPlan_Async(
                                                        plan, DownloadComponent.FakeNvapi, [source, source],
                                                        "local", Ct));
    }

    [Theory]
    [InlineData("bundled")]
    [InlineData("keep")]
    [InlineData("local")]
    [InlineData("release")]
    public async Task PackagePreviewAppliesComponentSelectionAndRestoresOriginalFiles(string choice)
    {
        var bytes = Bundle();
        var downloads = 0;
        using var client = new HttpClient(new Handler(_ =>
        {
            downloads++;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
        }));
        var packages = new PackageDownloadService(Paths, client);
        var release = new PackageRelease("test", "Optiscaler.zip", "https://github.com/test/bundle.zip", null);
        var package = await packages.DownloadPackage_Async(release, cancellationToken: Ct);
        var game = Directory.CreateDirectory(Path.Combine(_root, "selection-game")).FullName;
        var executable = Path.Combine(game, "game.exe");
        InstallationTests.WritePe(executable, false);
        Directory.CreateDirectory(Path.Combine(game, "OptiScaler"));
        var nestedDestination = Path.Combine(game, "OptiScaler", "fakenvapi.dll");
        var rootDestination = Path.Combine(game, "fakenvapi.dll");
        await File.WriteAllTextAsync(nestedDestination, "original nested", Ct);
        await File.WriteAllTextAsync(rootDestination, "original root", Ct);
        var local = Path.Combine(_root, "fakenvapi.dll");
        InstallationTests.WritePe(local, true);
        await File.AppendAllTextAsync(local, "local version", Ct);
        var selection = choice switch
        {
            "keep" => new ComponentInstallSelection(DownloadComponent.FakeNvapi, KeepExisting: true),
            "local" => new ComponentInstallSelection(DownloadComponent.FakeNvapi, LocalPath: local),
            "release" => new ComponentInstallSelection(DownloadComponent.FakeNvapi, Release: release),
            _ => new ComponentInstallSelection(DownloadComponent.FakeNvapi)
        };
        var installer = new GameInstallationService(Paths, packages);
        var plan = await installer.PreviewPackageInstallation_Async(
            executable, package, "winmm.dll", new RenderProfile { Dx12Upscaler = "xess" },
            [selection, new ComponentInstallSelection(DownloadComponent.OptiPatcher, KeepExisting: true)],
            cancellationToken: Ct);

        Assert.Equal(choice == "release" ? 2 : 1, downloads);
        Assert.DoesNotContain(plan.Files, file => file.RelativePath.EndsWith("OptiPatcher.asi"));
        Assert.False(File.Exists(Path.Combine(game, "winmm.dll")));
        Assert.Equal("original root", await File.ReadAllTextAsync(rootDestination, Ct));
        Assert.Equal("original nested", await File.ReadAllTextAsync(nestedDestination, Ct));
        var componentFiles = plan.Files.Where(file => Path.GetFileName(file.RelativePath) == "fakenvapi.dll").ToList();
        if (choice == "keep")
        {
            Assert.Empty(componentFiles);
        }
        else
        {
            var componentFile = Assert.Single(componentFiles);
            Assert.Equal(choice == "bundled" ? Path.Combine("OptiScaler", "fakenvapi.dll") : "fakenvapi.dll",
                componentFile.RelativePath);
            var expectedSource = choice == "local" ? local : Path.Combine(package, "OptiScaler", "fakenvapi.dll");
            Assert.Equal(Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(expectedSource, Ct))),
                componentFile.AfterHash);
        }

        await installer.ExecuteInstallationPlan_Async(plan, Ct);
        Assert.True((await installer.VerifyInstallation_Async(game, Ct)).IsVerified);
        Assert.Contains("Dx12Upscaler=xess", await File.ReadAllTextAsync(Path.Combine(game, "OptiScaler.ini"), Ct));
        if (choice == "keep")
        {
            Assert.Equal("original root", await File.ReadAllTextAsync(rootDestination, Ct));
            Assert.Equal("original nested", await File.ReadAllTextAsync(nestedDestination, Ct));
        }
        await installer.RestoreLatestOperation_Async(game, Ct);
        Assert.Equal("original root", await File.ReadAllTextAsync(rootDestination, Ct));
        Assert.Equal("original nested", await File.ReadAllTextAsync(nestedDestination, Ct));
        Assert.False(File.Exists(Path.Combine(game, "winmm.dll")));
    }

    [Fact]
    public async Task LocalArtworkIsPersistedWhenAddingGameAndSurvivesReload()
    {
        var game = Path.Combine(_root, "game");
        Directory.CreateDirectory(game);
        var cover = Path.Combine(game, "cover.png");
        await File.WriteAllBytesAsync(cover,
                                      Convert
                                          .FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aGZkAAAAASUVORK5CYII="),
                                      Ct);
        using var provider = new ServiceCollection().AddOptiscalerServices().AddSingleton<IAppPaths>(Paths)
            .AddSingleton<ProfilesViewModel>().AddSingleton<MainWindowViewModel>().BuildServiceProvider();
        var vm = provider.GetRequiredService<MainWindowViewModel>();
        await vm.LoadGameLibrary_Async(Ct);
        await vm.AddManualGames_Async([game], Ct);
        Assert.Equal(cover, Assert.Single(vm.Games).CoverImage);
        var saved = await provider.GetRequiredService<IGameCatalogRepository>().LoadGameCatalog_Async(Ct);
        Assert.Equal(cover, Assert.Single(saved.Games).CoverImage);
    }

    [Fact]
    public async Task MalformedExecutableDoesNotPreventArtworkLookup()
    {
        var game = Path.Combine(_root, "game");
        Directory.CreateDirectory(game);
        await File.WriteAllTextAsync(Path.Combine(game, "game.exe"), "not a PE", Ct);
        var record = new GameRecord
        {
            Id = GameId.Create(GamePlatform.Manual, null, game),
            Name = "Test",
            Platform = GamePlatform.Manual,
            Installations = [new GameInstallation { RootPath = game }]
        };
        Assert.False(await new GameArtworkService(Paths).PopulateArtwork_Async([record], Ct));
        Assert.Null(record.CoverImage);
    }

    [Fact]
    public async Task ExtractsEmbeddedExecutableIconIntoPersistentCache()
    {
        var game = Path.Combine(_root, "icon-game");
        Directory.CreateDirectory(game);
        var executable = Path.Combine(game, "game.exe");

        // Minimal PE32+ with RT_GROUP_ICON and RT_ICON resources in one .rsrc section.
        var pe = new byte[2048];

        void U16(int offset, ushort value) { BitConverter.GetBytes(value).CopyTo(pe, offset); }

        void U32(int offset, int value) { BitConverter.GetBytes(value).CopyTo(pe, offset); }

        U16(0, 0x5a4d);
        U32(0x3c, 128);
        U32(128, 0x4550);
        U16(132, 0x8664);
        U16(134, 1);
        U16(148, 240);
        U16(150, 2);
        U16(152, 0x20b);
        U32(152 + 32, 4096);
        U32(152 + 36, 512);
        U32(152 + 56, 8192);
        U32(152 + 60, 512);
        U32(152 + 108, 16);
        U32(152 + 112 + 16, 4096);
        U32(152 + 112 + 20, 512);
        var section = 392;
        Encoding.ASCII.GetBytes(".rsrc").CopyTo(pe, section);
        U32(section + 8, 512);
        U32(section + 12, 4096);
        U32(section + 16, 512);
        U32(section + 20, 512);

        void Table(int offset, params (int Id, int Target)[] entries)
        {
            U16(512 + offset + 14, (ushort)entries.Length);

            for (var i = 0; i < entries.Length; i++)
            {
                U32(512 + offset + 16 + i * 8, entries[i].Id);
                U32(512 + offset + 20 + i * 8, entries[i].Target);
            }
        }

        Table(0, (3, int.MinValue | 32), (14, int.MinValue | 80));
        Table(32, (1, int.MinValue | 56));
        Table(56, (1033, 128));
        Table(80, (1, int.MinValue | 104));
        Table(104, (1033, 144));
        var png =
            Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aGZkAAAAASUVORK5CYII=");
        U32(512 + 128, 4096 + 192);
        U32(512 + 132, png.Length);
        U32(512 + 144, 4096 + 160);
        U32(512 + 148, 20);
        U16(512 + 162, 1);
        U16(512 + 164, 1);
        pe[512 + 166] = 1;
        pe[512 + 167] = 1;
        U16(512 + 170, 1);
        U16(512 + 172, 32);
        U32(512 + 174, png.Length);
        U16(512 + 178, 1);
        png.CopyTo(pe, 512 + 192);
        await File.WriteAllBytesAsync(executable, pe, Ct);
        var record = new GameRecord
        {
            Id = GameId.Create(GamePlatform.Manual, null, game),
            Name = "Icon test",
            Platform = GamePlatform.Manual,
            Installations = [new GameInstallation { RootPath = game, PrimaryExecutablePath = executable }]
        };
        Assert.True(await new GameArtworkService(Paths).PopulateArtwork_Async([record], Ct));
        var icon = await File.ReadAllBytesAsync(record.CoverImage!, Ct);
        Assert.Equal(new byte[] { 0, 0, 1, 0, 1, 0 }, icon[..6]);
        Assert.Equal(png, icon[22..]);
        Assert.StartsWith(Paths.RootDirectory, record.CoverImage);
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
                                                               CancellationToken cancellationToken)
        {
            return Task.FromResult(respond(request));
        }
    }
}