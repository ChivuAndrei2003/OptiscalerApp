using OptiscalerApp.Models;
using OptiscalerApp.Paths;
using OptiscalerApp.Persistence;
using OptiscalerApp.Management;
using OptiscalerApp.ViewModels;
using Xunit;

namespace Optiscaler.Tests;

public sealed class ProfileUiTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "Optiscaler-profile-ui-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    [Fact]
    public async Task ProfileActionsSurviveRestartAndExportEditedOverrides()
    {
        var vm = new ProfilesViewModel(new JsonProfileRepository(new AppPaths(_root)));
        await vm.LoadProfiles_Async();
        Assert.False(vm.CanEdit);
        var profile = new RenderProfile { Name = "Quality", Description = "Test", Dx11Upscaler = "xess", Sharpness = 0.4m };
        Assert.True(await vm.SaveProfile_Async(profile));
        Assert.Equal(profile, vm.SelectedProfile);
        Assert.True(await vm.SetDefaultProfile_Async());
        Assert.True(await vm.SaveProfile_Async(profile with { Dx12Upscaler = "dlss", EnableLogging = true }));
        Assert.True(await vm.DuplicateProfile_Async());
        var duplicate = vm.SelectedProfile!;
        Assert.NotEqual(profile.Id, duplicate.Id);
        Assert.Equal("dlss", duplicate.Dx12Upscaler);
        Assert.True(duplicate.EnableLogging);
        Assert.Equal(profile.Id, vm.DefaultProfile!.Id);

        var restarted = new ProfilesViewModel(new JsonProfileRepository(new AppPaths(_root)));
        await restarted.LoadProfiles_Async();
        Assert.Equal(2, restarted.Profiles.Count);
        Assert.Equal(profile.Id, restarted.DefaultProfile!.Id);
        restarted.SelectedProfile = restarted.Profiles.Single(p => p.Id == profile.Id);
        var ini = ProfileIni.ApplyProfileToIni("", restarted.SelectedProfile);
        Assert.Contains("Dx12Upscaler=dlss", ini);
        Assert.Contains("Sharpness=0.4", ini);
        Assert.Contains("LogToFile=true", ini);
        Assert.True(await restarted.DeleteProfile_Async());
        Assert.Null(restarted.SelectedProfile);
        Assert.Null(restarted.DefaultProfile);
        var saved = await new JsonProfileRepository(new AppPaths(_root)).LoadProfileCatalog_Async(TestContext.Current.CancellationToken);
        Assert.Null(saved.DefaultProfileId);
        Assert.Equal(duplicate, Assert.Single(saved.Profiles));
    }

    [Fact]
    public async Task FailedSavePreservesSelectionDefaultAndCatalog()
    {
        var repository = new FailingRepository();
        var vm = new ProfilesViewModel(repository);
        await vm.LoadProfiles_Async();
        vm.SelectedProfile = Assert.Single(vm.Profiles);
        var original = vm.SelectedProfile;
        Assert.False(await vm.DeleteProfile_Async());
        Assert.Same(original, vm.SelectedProfile);
        Assert.Equal(original, Assert.Single(vm.Profiles));
        Assert.Equal(original, vm.DefaultProfile);
        Assert.True(vm.CanEdit);
        Assert.Contains("disk full", vm.StatusMessage);
    }

    [Fact]
    public async Task SearchClearsHiddenSelectionAndInvalidDefaultIsRejected()
    {
        var repository = new JsonProfileRepository(new AppPaths(_root));
        var vm = new ProfilesViewModel(repository);
        await vm.LoadProfiles_Async();
        await vm.SaveProfile_Async(new RenderProfile { Name = "Quality" });
        vm.SearchText = "missing";
        Assert.Empty(vm.Profiles);
        Assert.Null(vm.SelectedProfile);
        Assert.False(vm.CanEdit);
        vm.SearchText = "quality";
        Assert.Single(vm.Profiles);
        await Assert.ThrowsAsync<InvalidDataException>(() => repository.SaveProfileCatalog_Async(new ProfileCatalog { DefaultProfileId = Guid.NewGuid() }, TestContext.Current.CancellationToken));
    }

    private sealed class FailingRepository : IProfileRepository
    {
        private readonly RenderProfile _profile = new() { Name = "Saved" };
        public Task<ProfileCatalog> LoadProfileCatalog_Async(CancellationToken cancellationToken = default) =>
            Task.FromResult(new ProfileCatalog { Profiles = [_profile], DefaultProfileId = _profile.Id });
        public Task SaveProfileCatalog_Async(ProfileCatalog catalog, CancellationToken cancellationToken = default) =>
            throw new IOException("disk full");
    }
}
