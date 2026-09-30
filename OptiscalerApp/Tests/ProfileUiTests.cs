using OptiscalerApp.Management;
using OptiscalerApp.Models;
using OptiscalerApp.Paths;
using OptiscalerApp.Persistence;
using OptiscalerApp.ViewModels;
using Xunit;

namespace Optiscaler.Tests;

public sealed class ProfileUiTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "Optiscaler-profile-ui-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private ProfilesViewModel Create(IFileDialogs? dialogs = null)
    {
        return new ProfilesViewModel(new JsonProfileRepository(new AppPaths(_root)), dialogs ?? new FakeDialogs());
    }

    [Fact]
    public async Task ProfileActionsSurviveRestartAndExportEditedOverrides()
    {
        var vm = Create();
        await vm.LoadProfilesAsync();
        Assert.False(vm.CanEdit);
        var profile = TestData.Profile("Quality", ("Upscalers.Dx11Upscaler", "xess"),
                                       ("Sharpness.OverrideSharpness", "true"), ("Sharpness.Sharpness", "0.4"));
        Assert.True(await vm.SaveProfileAsync(profile));
        Assert.Equal(profile, vm.SelectedProfile);
        await vm.SetDefaultProfileCommand.ExecuteAsync(null);
        Assert.Contains("• Default", vm.SelectionDetails);
        var edited = profile with
        {
            Settings = new Dictionary<string, string>(profile.Settings)
            {
                ["Upscalers.Dx12Upscaler"] = "dlss", ["Log.LogToFile"] = "true"
            }
        };
        Assert.True(await vm.SaveProfileAsync(edited));
        await vm.DuplicateProfileCommand.ExecuteAsync(null);
        var duplicate = vm.SelectedProfile!;
        Assert.NotEqual(profile.Id, duplicate.Id);
        Assert.Equal(edited.Settings, duplicate.Settings);

        var restarted = Create();
        await restarted.LoadProfilesAsync();
        Assert.Equal(2, restarted.Profiles.Count);
        restarted.SelectedProfile = restarted.Profiles.Single(p => p.Id == profile.Id);
        Assert.Contains("• Default", restarted.SelectionDetails);
        var ini = ProfileIni.ApplyProfileToIni("", restarted.SelectedProfile);
        Assert.Contains("Dx12Upscaler=dlss", ini);
        Assert.Contains("Sharpness=0.4", ini);
        Assert.Contains("LogToFile=true", ini);
        await restarted.DeleteProfileCommand.ExecuteAsync(null);
        Assert.Null(restarted.SelectedProfile);
        var saved =
            await new JsonProfileRepository(new AppPaths(_root)).LoadProfileCatalogAsync(TestContext.Current
                .CancellationToken);
        Assert.Null(saved.DefaultProfileId);
        Assert.Equal(duplicate, Assert.Single(saved.Profiles));
    }

    [Fact]
    public async Task ImportedIniBecomesAUniquelyNamedProfile()
    {
        var vm = Create();
        await vm.LoadProfilesAsync();
        const string ini = "[Upscalers]\nDx12Upscaler=xess\n[Spoofing]\nDxgi=false\n";

        Assert.True(await vm.ImportProfileAsync(ini, "Cyberpunk 2077"));
        Assert.True(await vm.ImportProfileAsync(ini, "Cyberpunk 2077"));

        Assert.Equal(["Cyberpunk 2077 (imported 2)", "Cyberpunk 2077 (imported)"],
                     vm.Profiles.Select(p => p.Name).Order(StringComparer.Ordinal));
        Assert.All(vm.Profiles, p => Assert.Equal("false", p.Settings["Spoofing.Dxgi"]));
        Assert.Contains("Spoof GPU as NVIDIA (DirectX): Off", vm.SelectionDetails);
    }

    [Fact]
    public async Task ImportAndExportGoThroughThePickedFiles()
    {
        var game = Directory.CreateDirectory(Path.Combine(_root, "Elden Ring")).FullName;
        var file = Path.Combine(game, "OptiScaler.ini");
        await File.WriteAllTextAsync(file, "[Upscalers]\nDx12Upscaler=xess\n", TestContext.Current.CancellationToken);
        var vm = Create(new FakeDialogs(file: file));
        await vm.LoadProfilesAsync();

        await vm.ImportProfileCommand.ExecuteAsync(null);
        vm.SelectedProfile = Assert.Single(vm.Profiles);
        Assert.Equal("Elden Ring (imported)", vm.SelectedProfile.Name);

        File.Delete(file);
        await vm.ExportProfileCommand.ExecuteAsync(null);
        Assert.Contains("Dx12Upscaler=xess", await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken));
        Assert.StartsWith("Exported", vm.StatusMessage);
    }

    [Fact]
    public async Task TheEditorSavesAndClosesItself()
    {
        var vm = Create();
        await vm.LoadProfilesAsync();

        vm.NewProfileCommand.Execute(null);
        var editor = Assert.IsType<ProfileEditorViewModel>(vm.Editor);
        editor.Name = "  Handheld ";
        await editor.SaveCommand.ExecuteAsync(null);

        Assert.False(vm.IsEditing);
        Assert.Equal("Handheld", Assert.Single(vm.Profiles).Name);

        vm.SelectedProfile = vm.Profiles[0];
        vm.EditProfileCommand.Execute(null);
        vm.Editor!.CancelCommand.Execute(null);
        Assert.Null(vm.Editor);
    }

    [Fact]
    public async Task FailedSavePreservesSelectionDefaultAndCatalog()
    {
        var vm = new ProfilesViewModel(new FailingRepository(), new FakeDialogs());
        await vm.LoadProfilesAsync();
        vm.SelectedProfile = Assert.Single(vm.Profiles);
        var original = vm.SelectedProfile;
        await vm.DeleteProfileCommand.ExecuteAsync(null);
        Assert.Same(original, vm.SelectedProfile);
        Assert.Equal(original, Assert.Single(vm.Profiles));
        Assert.Contains("• Default", vm.SelectionDetails);
        Assert.True(vm.CanEdit);
        Assert.Contains("disk full", vm.StatusMessage);
    }

    [Fact]
    public async Task SearchClearsHiddenSelectionAndInvalidDefaultIsRejected()
    {
        var repository = new JsonProfileRepository(new AppPaths(_root));
        var vm = new ProfilesViewModel(repository, new FakeDialogs());
        await vm.LoadProfilesAsync();
        await vm.SaveProfileAsync(new RenderProfile { Name = "Quality" });
        vm.SearchText = "missing";
        Assert.Empty(vm.Profiles);
        Assert.Null(vm.SelectedProfile);
        Assert.False(vm.CanEdit);
        vm.SearchText = "quality";
        Assert.Single(vm.Profiles);
        await Assert.ThrowsAsync<InvalidDataException>(() =>
                                                           repository.SaveProfileCatalogAsync(new ProfileCatalog
                                                            {
                                                                DefaultProfileId = Guid.NewGuid()
                                                            },
                                                            TestContext.Current.CancellationToken));
    }

    private sealed class FailingRepository : IProfileRepository
    {
        private readonly RenderProfile _profile = new() { Name = "Saved" };

        public Task<ProfileCatalog> LoadProfileCatalogAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ProfileCatalog { Profiles = [_profile], DefaultProfileId = _profile.Id });
        }

        public Task SaveProfileCatalogAsync(ProfileCatalog catalog, CancellationToken cancellationToken = default)
        {
            throw new IOException("disk full");
        }
    }
}
