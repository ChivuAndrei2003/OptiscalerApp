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
                ["FrameGen.DrawUIOverFG"] = "true", ["Spoofing.Vulkan"] = "false", ["OutputScaling.Multiplier"] = "1.5"
            }
        };
        var repository = new JsonProfileRepository(new AppPaths(_root));
        await repository.SaveProfileCatalog_Async(new ProfileCatalog { Profiles = [profile] }, Ct);
        var saved = Assert.Single((await new JsonProfileRepository(new AppPaths(_root)).LoadProfileCatalog_Async(Ct))
                                      .Profiles);
        Assert.Equal(profile, saved);

        var ini = ProfileIni.ApplyProfileToIni("[FrameGen]\nDrawUIOverFG=auto\n; keep\n[Spoofing]\nVulkan=auto\n", saved);
        Assert.Contains("[FrameGen]\nDrawUIOverFG=true\n; keep", ini);
        Assert.Contains("Vulkan=false", ini);
        Assert.Contains("[OutputScaling]\nMultiplier=1.5", ini);
    }

    [Theory]
    [InlineData("Unknown.Key", "true")]
    [InlineData("framegen.drawuioverfg", "true")]
    [InlineData("FrameGen.DrawUIOverFG", "auto")]
    [InlineData("OutputScaling.Multiplier", "5")]
    [InlineData("Menu.Scale", "3.0")]
    // Keys modeled as typed profile properties have a single source.
    [InlineData("Spoofing.Dxgi", "false")]
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
                           [Sharpness]
                           OverrideSharpness=true
                           Sharpness=0.3
                           [Menu]
                           Scale=1.5
                           ShowFps=auto
                           ; ShowFps=true
                           [Spoofing]
                           Vulkan=False
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
                         ["Menu.Scale"] = "1.5", ["Spoofing.Vulkan"] = "false"
                     }, profile.Settings);
        ProfileIni.ValidateProfile(profile);
    }

    [Fact]
    public void EditorFieldsNormalizeValuesAndReportInvalidInput()
    {
        var groups = ProfileSettingGroup.Create(new Dictionary<string, string> { ["Spoofing.Vulkan"] = "true" });
        var fields = groups.SelectMany(g => g.Fields).ToDictionary(f => f.Setting.Id);
        Assert.True(fields["Spoofing.Vulkan"].IsOverridden);
        Assert.Equal("true", fields["Spoofing.Vulkan"].ReadValue());
        Assert.Null(fields["FrameGen.DrawUIOverFG"].ReadValue());

        var multiplier = fields["OutputScaling.Multiplier"];
        multiplier.Text = " 1.50 ";
        Assert.Equal("1.50", multiplier.ReadValue());
        // With a thousands separator allowed, "0,3" would be read as 3.
        multiplier.Text = "0,3";
        Assert.Throws<InvalidDataException>(multiplier.ReadValue);
        multiplier.Text = "fast";
        Assert.Contains("Output scaling multiplier", Assert.Throws<InvalidDataException>(multiplier.ReadValue).Message);

        fields["Spoofing.Vulkan"].Load(null);
        Assert.False(fields["Spoofing.Vulkan"].IsOverridden);

        var group = groups.Single(g => g.Fields.Contains(multiplier));
        group.ApplySearch("output scaling");
        Assert.True(multiplier.IsVisible);
        Assert.True(group.IsVisible);
        Assert.All(group.Fields.Where(f => !f.Matches("output scaling")), f => Assert.False(f.IsVisible));
        group.ApplySearch("no such option");
        Assert.False(group.IsVisible);
    }

    [Fact]
    public void ApplyingAProfileResetsAdvancedOverridesItLeavesOnDefault()
    {
        var previous = new RenderProfile
        {
            Name = "Previous", Settings = new Dictionary<string, string> { ["Spoofing.Vulkan"] = "false" }
        };
        var installed = ProfileIni.ApplyProfileToIni("[Spoofing]\nVulkan=auto\n", previous);
        Assert.Contains("Vulkan=false", installed);

        var next = ProfileIni.ApplyProfileToIni(installed, new RenderProfile { Name = "Next" });
        Assert.Contains("Vulkan=auto", next);
        Assert.DoesNotContain("DrawUIOverFG", next);

        // Keeping the game's settings writes only the profile's own overrides.
        Assert.Contains("Vulkan=false", ProfileIni.ApplyProfileToIni(installed, new RenderProfile { Name = "Next" }, true));
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
                Name = "Edited", Settings = new Dictionary<string, string> { ["Spoofing.Vulkan"] = "true" }
            }, true);
            var window = new Window { Width = 1100, Height = 900, Content = editor };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var texts = editor.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
            Assert.Contains("Edit Profile", texts);
            Assert.Contains("1 option overridden.", texts);
            Assert.Contains("Frame generation", texts);
            Assert.Contains("Spoof GPU as NVIDIA (Vulkan)", texts);

            var groups = (IReadOnlyList<ProfileSettingGroup>)editor.FindControl<ItemsControl>("GroupsList")!.ItemsSource!;
            var drawUi = groups.SelectMany(g => g.Fields).Single(f => f.Setting.Id == "FrameGen.DrawUIOverFG");
            drawUi.SelectedChoice = drawUi.Choices.Single(c => c.Value == "true");
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
        Assert.Equal(new Dictionary<string, string> { ["Spoofing.Vulkan"] = "true", ["FrameGen.DrawUIOverFG"] = "true" },
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
            var multiplier = groups.SelectMany(g => g.Fields).Single(f => f.Setting.Id == "OutputScaling.Multiplier");
            multiplier.Text = "0,3";
            var search = editor.FindControl<TextBox>("SettingsSearchBox")!;
            search.Text = "spoof";
            Dispatcher.UIThread.RunJobs();
            Assert.False(multiplier.IsVisible);

            editor.FindControl<Button>("SaveButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("", search.Text);
            Assert.True(multiplier.IsVisible);
            Assert.Contains("Output scaling multiplier", editor.FindControl<TextBlock>("ValidationText")!.Text);
            Assert.True(editor.GetVisualDescendants().OfType<TextBox>().Single(t => t.DataContext == multiplier).IsFocused);
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
