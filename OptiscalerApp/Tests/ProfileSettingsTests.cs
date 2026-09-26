using System.Net;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OptiscalerApp.Management;
using OptiscalerApp.Models;
using OptiscalerApp.Paths;
using OptiscalerApp.Persistence;
using OptiscalerApp.ViewModels;
using OptiscalerApp.Views;
using Xunit;

namespace Optiscaler.Tests;

public sealed class ProfileSettingsTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "Optiscaler-profile-settings-" + Guid.NewGuid().ToString("N"));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    [Fact]
    public async Task AdvancedSettingsSurviveRestartAndAreWrittenToTheirSections()
    {
        var profile = new RenderProfile
        {
            Name = "Frame gen",
            Settings = new Dictionary<string, string>
            {
                ["FrameGen.FGOutput"] = "xefg", ["Spoofing.Dxgi"] = "false", ["Framerate.FramerateLimit"] = "117.5"
            }
        };
        var repository = new JsonProfileRepository(new AppPaths(_root));
        await repository.SaveProfileCatalog_Async(new ProfileCatalog { Profiles = [profile] }, Ct);
        var saved = Assert.Single((await new JsonProfileRepository(new AppPaths(_root)).LoadProfileCatalog_Async(Ct))
                                      .Profiles);
        Assert.Equal(profile, saved);

        var ini = ProfileIni.ApplyProfileToIni("[FrameGen]\nFGOutput=auto\n; keep\n[Spoofing]\nDxgi=auto\n", saved);
        Assert.Contains("[FrameGen]\nFGOutput=xefg\n; keep", ini);
        Assert.Contains("Dxgi=false", ini);
        Assert.Contains("[Framerate]\nFramerateLimit=117.5", ini);
    }

    [Theory]
    [InlineData("Unknown.Key", "true")]
    [InlineData("framegen.fgoutput", "xefg")]
    [InlineData("FrameGen.FGOutput", "auto")]
    [InlineData("Framerate.FramerateLimit", "5000")]
    [InlineData("Menu.ShortcutKey", "Insert")]
    [InlineData("Spoofing.SpoofedGPUName", "RTX\n[Log]")]
    public void InvalidSettingsAreRejected(string id, string value)
    {
        var profile = new RenderProfile { Settings = new Dictionary<string, string> { [id] = value } };
        Assert.Throws<InvalidDataException>(() => ProfileIni.ValidateProfile(profile));
    }

    [Fact]
    public void ImportReadsSupportedNonAutoValues()
    {
        const string ini = """
                           [Upscalers]
                           Dx11Upscaler=auto
                           Dx12Upscaler=XeSS
                           VulkanUpscaler=fsr31
                           [Sharpness]
                           OverrideSharpness=true
                           Sharpness=0.3
                           [Menu]
                           ShortcutKey=0x2D
                           ShowFps=auto
                           ; ShowFps=true
                           [Log]
                           LogToFile=True
                           LogLevel=9
                           """;
        var profile = ProfileIni.ReadProfileFromIni(ini.Replace("\n", "\r\n"), "Imported");
        Assert.Equal("Imported", profile.Name);
        Assert.Equal("auto", profile.Dx11Upscaler);
        Assert.Equal("xess", profile.Dx12Upscaler);
        Assert.Equal(0.3m, profile.Sharpness);
        Assert.True(profile.EnableLogging);
        Assert.Equal(new Dictionary<string, string>
                     {
                         ["Upscalers.VulkanUpscaler"] = "fsr31", ["Menu.ShortcutKey"] = "0x2D"
                     }, profile.Settings);
        ProfileIni.ValidateProfile(profile);
    }

    [Fact]
    public void EditorFieldsNormalizeValuesAndReportInvalidInput()
    {
        var groups = ProfileSettingGroup.Create(new Dictionary<string, string> { ["Spoofing.Dxgi"] = "true" });
        var fields = groups.SelectMany(g => g.Fields).ToDictionary(f => f.Setting.Id);
        Assert.True(fields["Spoofing.Dxgi"].IsOverridden);
        Assert.Equal("true", fields["Spoofing.Dxgi"].ReadValue());
        Assert.Null(fields["FrameGen.FGOutput"].ReadValue());

        var limit = fields["Framerate.FramerateLimit"];
        limit.Text = " 60.0 ";
        Assert.Equal("60.0", limit.ReadValue());
        limit.Text = "90,5";
        Assert.Throws<InvalidDataException>(limit.ReadValue);
        limit.Text = "fast";
        Assert.Contains("Frame rate limit", Assert.Throws<InvalidDataException>(limit.ReadValue).Message);

        fields["Spoofing.Dxgi"].Load(null);
        Assert.False(fields["Spoofing.Dxgi"].IsOverridden);

        var group = groups.Single(g => g.Fields.Contains(limit));
        group.ApplySearch("frame rate");
        Assert.True(limit.IsVisible);
        Assert.True(group.IsVisible);
        Assert.All(group.Fields.Where(f => f != limit && !f.Matches("frame rate")), f => Assert.False(f.IsVisible));
        group.ApplySearch("no such option");
        Assert.False(group.IsVisible);
    }

    [Fact]
    public void ApplyingAProfileToAnInstalledIniResetsOverridesItLeavesOnDefault()
    {
        var previous = new RenderProfile
        {
            Name = "Previous", Settings = new Dictionary<string, string> { ["Spoofing.Dxgi"] = "false" }
        };
        var installed = ProfileIni.ApplyProfileToIni("[Spoofing]\nDxgi=auto\n", previous);
        Assert.Contains("Dxgi=false", installed);

        var next = ProfileIni.ApplyProfileToIni(installed, new RenderProfile { Name = "Next" }, true);
        Assert.Contains("Dxgi=auto", next);
        Assert.DoesNotContain("FGOutput", next);

        // A package's original INI keeps its shipped values for options left on default.
        Assert.Contains("Dxgi=false", ProfileIni.ApplyProfileToIni(installed, new RenderProfile { Name = "Next" }));
    }

    [Fact]
    public void ClearingTheCacheRemovesPackagesAndStagingFolders()
    {
        using var client = new HttpClient();
        var service = new PackageDownloadService(new AppPaths(_root), client);
        Directory.CreateDirectory(Path.Combine(service.CacheDirectory, "0123456789ABCDEF", "files"));
        File.WriteAllText(Path.Combine(service.CacheDirectory, "0123456789ABCDEF", "files", "OptiScaler.dll"), "x");
        Directory.CreateDirectory(Path.Combine(service.CacheDirectory, "0123456789ABCDEF.abc.tmp"));
        Assert.True(service.GetCacheSize() > 0);

        Assert.Equal(0, service.ClearCache());
        Assert.Empty(Directory.EnumerateFileSystemEntries(service.CacheDirectory));
        Assert.Equal(0, service.GetCacheSize());
    }

    [Fact]
    public async Task ReleaseListsAreReusedUntilCleared()
    {
        var requests = 0;
        using var client = new HttpClient(new CountingHandler(() => requests++));
        var service = new PackageDownloadService(new AppPaths(_root), client);
        await service.GetReleases_Async(false, Ct);
        await service.GetReleases_Async(false, Ct);
        Assert.Equal(1, requests);
        await service.GetReleases_Async(true, Ct);
        Assert.Equal(2, requests);
        service.ClearReleaseLists();
        await service.GetReleases_Async(false, Ct);
        Assert.Equal(3, requests);
    }

    [Fact]
    public async Task EditorRendersAdvancedOptionsAndSavesThem()
    {
        RenderProfile? saved = null;

        await ManageGameViewTests.Session.Value.Dispatch(() =>
        {
            var editor = new NewProfileDialog
            {
                SaveProfile = p =>
                {
                    saved = p;

                    return Task.FromResult(true);
                }
            };
            editor.SetProfile(new RenderProfile
            {
                Name = "Edited", Settings = new Dictionary<string, string> { ["Spoofing.Dxgi"] = "true" }
            }, true);
            var window = new Window { Width = 1100, Height = 900, Content = editor };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var texts = editor.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
            Assert.Contains("Edit Profile", texts);
            Assert.Contains("1 option overridden.", texts);
            Assert.Contains("Frame generation", texts);
            Assert.Contains("Spoof GPU as NVIDIA (DXGI)", texts);

            var groups = (IReadOnlyList<ProfileSettingGroup>)editor.FindControl<ItemsControl>("GroupsList")!.ItemsSource!;
            var fgOutput = groups.SelectMany(g => g.Fields).Single(f => f.Setting.Id == "FrameGen.FGOutput");
            fgOutput.SelectedChoice = fgOutput.Choices.Single(c => c.Value == "xefg");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("2 options overridden.", editor.FindControl<TextBlock>("OverrideCountText")!.Text);

            editor.FindControl<TextBox>("SettingsSearchBox")!.Text = "no such option";
            Dispatcher.UIThread.RunJobs();
            Assert.True(editor.FindControl<TextBlock>("NoMatchesText")!.IsVisible);

            editor.FindControl<Button>("SaveButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            window.Close();

            return Task.CompletedTask;
        }, Ct);

        Assert.NotNull(saved);
        Assert.Equal(new Dictionary<string, string> { ["Spoofing.Dxgi"] = "true", ["FrameGen.FGOutput"] = "xefg" },
                     saved.Settings);
    }

    [Fact]
    public async Task SavingRevealsAnInvalidOptionHiddenBySearch()
    {
        var saved = false;

        await ManageGameViewTests.Session.Value.Dispatch(() =>
        {
            var editor = new NewProfileDialog
            {
                SaveProfile = _ =>
                {
                    saved = true;

                    return Task.FromResult(true);
                }
            };
            editor.SetProfile(new RenderProfile { Name = "Invalid" }, true);
            var window = new Window { Width = 1100, Height = 900, Content = editor };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var groups = (IReadOnlyList<ProfileSettingGroup>)editor.FindControl<ItemsControl>("GroupsList")!.ItemsSource!;
            var limit = groups.SelectMany(g => g.Fields).Single(f => f.Setting.Id == "Framerate.FramerateLimit");
            limit.Text = "90,5";
            var search = editor.FindControl<TextBox>("SettingsSearchBox")!;
            search.Text = "spoof";
            Dispatcher.UIThread.RunJobs();
            Assert.False(limit.IsVisible);

            editor.FindControl<Button>("SaveButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("", search.Text);
            Assert.True(limit.IsVisible);
            Assert.Contains("Frame rate limit", editor.FindControl<TextBlock>("ValidationText")!.Text);
            Assert.True(editor.GetVisualDescendants().OfType<TextBox>().Single(t => t.DataContext == limit).IsFocused);
            window.Close();

            return Task.CompletedTask;
        }, Ct);

        Assert.False(saved);
    }

    internal sealed class CountingHandler(Action onRequest) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
                                                               CancellationToken cancellationToken)
        {
            onRequest();

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]") });
        }
    }
}
