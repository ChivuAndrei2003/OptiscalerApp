using System.Text;
using Microsoft.Extensions.DependencyInjection;
using OptiscalerApp.Management;
using OptiscalerApp.Models;
using OptiscalerApp.Paths;
using OptiscalerApp.Persistence;
using OptiscalerApp.ViewModels;
using Xunit;

namespace Optiscaler.Tests;

public sealed class GameArtworkTests : TempFolderTest
{
    private AppPaths Paths => new(Path.Combine(Root, "data"));

    private static async Task<GamesViewModel> AddGame(ServiceProvider provider, string game)
    {
        var vm = await provider.LoadLibraryAsync();
        await vm.AddManualGamesAsync([game], Ct);

        return vm;
    }

    [Fact]
    public async Task LocalArtworkIsPersistedWhenAddingGame()
    {
        var game = Folder("game");
        var cover = Path.Combine(game, "cover.png");
        await File.WriteAllBytesAsync(cover, IconPng, Ct);
        using var provider = TestData.LibraryServices(Paths.RootDirectory);

        var vm = await AddGame(provider, game);

        Assert.Equal(cover, Assert.Single(vm.Games).CoverImage);
        var saved = await provider.GetRequiredService<IGameCatalogRepository>().LoadGameCatalogAsync(Ct);
        Assert.Equal(cover, Assert.Single(saved.Games).CoverImage);
    }

    [Fact]
    public async Task ExecutableWithoutAnIconDoesNotStopGamesBeingAdded()
    {
        // Resources but no icon, like any executable built without one.
        var game = Folder("game");
        await File.WriteAllBytesAsync(Path.Combine(game, "game.exe"), ResourcePe(false), Ct);
        using var provider = TestData.LibraryServices(Paths.RootDirectory);

        var vm = await AddGame(provider, game);

        Assert.Null(Assert.Single(vm.Games).CoverImage);
    }

    [Fact]
    public async Task MalformedExecutableDoesNotPreventArtworkLookup()
    {
        var game = Folder("game");
        await File.WriteAllTextAsync(Path.Combine(game, "game.exe"), "not a PE", Ct);
        var record = TestData.Game(game);

        Assert.False(await new GameArtworkService(Paths).PopulateArtworkAsync([record], Ct));
        Assert.Null(record.CoverImage);
    }

    [Fact]
    public async Task ExtractsEmbeddedExecutableIconIntoPersistentCache()
    {
        var executable = Path.Combine(Folder("game"), "game.exe");
        await File.WriteAllBytesAsync(executable, ResourcePe(true), Ct);
        var record = TestData.Game(Path.GetDirectoryName(executable)!, executable: executable);

        Assert.True(await new GameArtworkService(Paths).PopulateArtworkAsync([record], Ct));

        var icon = await File.ReadAllBytesAsync(record.CoverImage!, Ct);
        Assert.Equal(new byte[] { 0, 0, 1, 0, 1, 0 }, icon[..6]);
        Assert.Equal(IconPng, icon[22..]);
        Assert.StartsWith(Paths.RootDirectory, record.CoverImage);
    }

    [Fact]
    public async Task IconComesFromTheGameAndNotFromHelpersBesideIt()
    {
        var game = Folder("game");
        await File.WriteAllBytesAsync(Path.Combine(game, "Game.exe"), ResourcePe(true), Ct);
        await File.WriteAllBytesAsync(Path.Combine(game, "UnityCrashHandler64.exe"), ResourcePe(false), Ct);
        await File.WriteAllBytesAsync(Path.Combine(game, "unins000.exe"), ResourcePe(false), Ct);
        var record = TestData.Game(game);

        Assert.True(await new GameArtworkService(Paths).PopulateArtworkAsync([record], Ct));
        Assert.EndsWith(".ico", record.CoverImage);
    }

    [Fact]
    public async Task SteamGamesUseTheCoverSteamAlreadyDownloaded()
    {
        var steam = Path.Combine(Root, "Steam");
        var cover = Path.Combine(Folder("Steam", "appcache", "librarycache", "620", "0a1b2c"), "library_capsule.jpg");
        await File.WriteAllBytesAsync(cover, IconPng, Ct);
        var executable = Path.Combine(Folder("game"), "game.exe");
        await File.WriteAllBytesAsync(executable, ResourcePe(true), Ct);
        var record = TestData.Game(Path.GetDirectoryName(executable)!, "Portal 2", GamePlatform.Steam, "620",
                                   executable);

        // Without Steam's art the executable icon stands in, and is replaced once the art is there.
        Assert.True(await new GameArtworkService(Paths, []).PopulateArtworkAsync([record], Ct));
        Assert.EndsWith(".ico", record.CoverImage);
        var artwork = new GameArtworkService(Paths, [steam]);
        Assert.True(await artwork.PopulateArtworkAsync([record], Ct));
        Assert.Equal(cover, record.CoverImage);
        Assert.False(await artwork.PopulateArtworkAsync([record], Ct));
    }

    private static readonly byte[] IconPng =
        Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aGZkAAAAASUVORK5CYII=");

    /// <summary>A minimal PE32+ whose single .rsrc section holds one icon, or only an empty version resource.</summary>
    private static byte[] ResourcePe(bool withIcon)
    {
        var pe = new byte[2048];

        void U16(int offset, ushort value)
        {
            BitConverter.GetBytes(value).CopyTo(pe, offset);
        }

        void U32(int offset, int value)
        {
            BitConverter.GetBytes(value).CopyTo(pe, offset);
        }

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

        if (!withIcon)
        {
            // RT_VERSION (16) is the only resource type.
            Table(0, (16, int.MinValue | 32));

            return pe;
        }

        // RT_ICON (3) holds the image and RT_GROUP_ICON (14) the entry that describes it.
        Table(0, (3, int.MinValue | 32), (14, int.MinValue | 80));
        Table(32, (1, int.MinValue | 56));
        Table(56, (1033, 128));
        Table(80, (1, int.MinValue | 104));
        Table(104, (1033, 144));
        U32(512 + 128, 4096 + 192);
        U32(512 + 132, IconPng.Length);
        U32(512 + 144, 4096 + 160);
        U32(512 + 148, 20);
        U16(512 + 162, 1);
        U16(512 + 164, 1);
        pe[512 + 166] = 1;
        pe[512 + 167] = 1;
        U16(512 + 170, 1);
        U16(512 + 172, 32);
        U32(512 + 174, IconPng.Length);
        U16(512 + 178, 1);
        IconPng.CopyTo(pe, 512 + 192);

        return pe;
    }
}
