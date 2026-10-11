using OptiscalerApp.Models;
using OptiscalerApp.Paths;
using OptiscalerApp.Persistence;
using OptiscalerApp.ViewModels;
using Xunit;

namespace Optiscaler.Tests;

/// <summary>The Profiles page and the editor it opens.</summary>
public sealed class ProfilesViewModelTests : TempFolderTest
{
    private JsonProfileRepository Repository => new(new AppPaths(Path.Combine(Root, "data")));

    private async Task<ProfilesViewModel> Load(IFileDialogs? dialogs = null)
    {
        var vm = new ProfilesViewModel(Repository, dialogs ?? new FakeDialogs());
        await vm.LoadProfilesAsync();

        return vm;
    }

    private static ProfileEditorViewModel Editor(RenderProfile profile, Action<RenderProfile>? onSave = null)
    {
        return new ProfileEditorViewModel(profile, true, saved =>
        {
            onSave?.Invoke(saved);

            return Task.FromResult(true);
        });
    }

    private static ProfileSettingField Field(ProfileEditorViewModel editor, string id)
    {
        return editor.Groups.SelectMany(g => g.Fields).Single(f => f.Setting.Id == id);
    }

    [Fact]
    public async Task DefaultEditedAndDuplicatedProfilesSurviveRestart()
    {
        var vm = await Load();
        Assert.False(vm.CanEdit);
        var profile = TestData.Profile("Quality", ("Upscalers.Dx11Upscaler", "xess"));
        Assert.True(await vm.SaveProfileAsync(profile));
        Assert.Equal(profile, vm.SelectedProfile);
        await vm.SetDefaultProfileCommand.ExecuteAsync(null);
        var edited = profile with
        {
            Settings = new Dictionary<string, string>(profile.Settings) { ["Log.LogToFile"] = "true" }
        };
        Assert.True(await vm.SaveProfileAsync(edited));
        await vm.DuplicateProfileCommand.ExecuteAsync(null);
        var duplicate = vm.SelectedProfile!;
        Assert.NotEqual(profile.Id, duplicate.Id);
        Assert.Equal(edited.Settings, duplicate.Settings);

        var restarted = await Load();
        Assert.Equal(2, restarted.Profiles.Count);
        restarted.SelectedProfile = restarted.Profiles.Single(p => p.Id == profile.Id);
        Assert.Equal(edited.Settings, restarted.SelectedProfile.Settings);
        Assert.Contains("• Default", restarted.SelectionDetails);

        await restarted.DeleteProfileCommand.ExecuteAsync(null);
        Assert.Null(restarted.SelectedProfile);
        var saved = await Repository.LoadProfileCatalogAsync(Ct);
        Assert.Null(saved.DefaultProfileId);
        Assert.Equal(duplicate, Assert.Single(saved.Profiles));
    }

    [Fact]
    public async Task ImportedIniBecomesAUniquelyNamedProfile()
    {
        var vm = await Load();
        const string ini = "[Upscalers]\nDx12Upscaler=xess\n[Spoofing]\nDxgi=false\n";

        Assert.True(await vm.ImportProfileAsync(ini, "Cyberpunk 2077"));
        Assert.True(await vm.ImportProfileAsync(ini, "Cyberpunk 2077"));

        Assert.Equal(["Cyberpunk 2077 (imported 2)", "Cyberpunk 2077 (imported)"],
                     vm.Profiles.Select(p => p.Name).Order(StringComparer.Ordinal));
        Assert.All(vm.Profiles, p => Assert.Equal("false", p.Settings["Spoofing.Dxgi"]));
    }

    [Fact]
    public async Task ImportAndExportGoThroughThePickedFiles()
    {
        var file = Path.Combine(Folder("Elden Ring"), "OptiScaler.ini");
        await File.WriteAllTextAsync(file, "[Upscalers]\nDx12Upscaler=xess\n", Ct);
        var vm = await Load(new FakeDialogs(file: file));

        await vm.ImportProfileCommand.ExecuteAsync(null);
        vm.SelectedProfile = Assert.Single(vm.Profiles);
        Assert.Equal("Elden Ring (imported)", vm.SelectedProfile.Name);

        File.Delete(file);
        await vm.ExportProfileCommand.ExecuteAsync(null);
        Assert.Contains("Dx12Upscaler=xess", await File.ReadAllTextAsync(file, Ct));
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
        Assert.Contains("disk full", vm.StatusMessage);
    }

    [Fact]
    public async Task SearchClearsASelectionItHides()
    {
        var vm = await Load();
        await vm.SaveProfileAsync(new RenderProfile { Name = "Quality" });

        vm.SearchText = "missing";
        Assert.Empty(vm.Profiles);
        Assert.Null(vm.SelectedProfile);
        Assert.False(vm.CanEdit);

        vm.SearchText = "quality";
        Assert.Single(vm.Profiles);
    }

    [Fact]
    public async Task TheEditorSavesAndClosesItself()
    {
        var vm = await Load();

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
    public async Task TheEditorCountsAndSavesOverrides()
    {
        RenderProfile? saved = null;
        var editor = Editor(TestData.Profile("Edited", ("Spoofing.Vulkan", "true")), p => saved = p);
        Assert.Equal("1 option overridden.", editor.OverrideCountText);

        var drawUi = Field(editor, "FrameGen.DrawUIOverFG");
        drawUi.SelectedChoice = drawUi.Choices.Single(c => c.Value == "true");
        await editor.SaveCommand.ExecuteAsync(null);

        Assert.Equal("2 options overridden.", editor.OverrideCountText);
        Assert.Equal(new Dictionary<string, string> { ["Spoofing.Vulkan"] = "true", ["FrameGen.DrawUIOverFG"] = "true" },
                     saved!.Settings);
    }

    [Fact]
    public void EditorFieldsNormalizeNumbersAndRejectInvalidOnes()
    {
        var multiplier = Field(Editor(new RenderProfile()), "OutputScaling.Multiplier");
        Assert.Null(multiplier.ReadValue());

        multiplier.Text = " 1.50 ";
        Assert.Equal("1.50", multiplier.ReadValue());

        // With a thousands separator allowed, "0,3" would be read as 3.
        multiplier.Text = "0,3";
        Assert.Contains("Output scaling multiplier", Assert.Throws<InvalidDataException>(multiplier.ReadValue).Message);
    }

    [Fact]
    public void EditorSearchHidesOptionsAndGroupsThatDoNotMatch()
    {
        var editor = Editor(new RenderProfile());
        var multiplier = Field(editor, "OutputScaling.Multiplier");
        var group = editor.Groups.Single(g => g.Fields.Contains(multiplier));

        editor.SearchText = "output scaling";
        Assert.True(multiplier.IsVisible);
        Assert.True(group.IsVisible);
        Assert.False(Field(editor, "Spoofing.Vulkan").IsVisible);

        editor.SearchText = "no such option";
        Assert.False(group.IsVisible);
        Assert.True(editor.HasNoMatches);
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
