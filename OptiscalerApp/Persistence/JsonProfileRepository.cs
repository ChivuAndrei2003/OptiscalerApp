using System.Globalization;
using System.Text.Json.Nodes;
using OptiscalerApp.Paths;
using OptiscalerApp.Models;
using OptiscalerApp.Management;

namespace OptiscalerApp.Persistence;

/// <summary>Persists validated profiles with the same backup recovery as settings and the game catalog.</summary>
public sealed class JsonProfileRepository(IAppPaths paths) : IProfileRepository
{
    private readonly AtomicJsonFile<ProfileCatalog> _store =
        new(paths.ProfilesFilePath, OptiscalerJsonContext.Default.ProfileCatalog, ValidateProfileCatalog,
            UpgradeProfileCatalog);

    public async Task<ProfileCatalog> LoadProfileCatalogAsync(CancellationToken cancellationToken = default)
    {
        return await _store.LoadJsonFileAsync(cancellationToken).ConfigureAwait(false) ?? new ProfileCatalog();
    }

    public Task SaveProfileCatalogAsync(ProfileCatalog catalog, CancellationToken cancellationToken = default)
    {
        return _store.SaveJsonFileAsync(catalog, cancellationToken);
    }

    private static void ValidateProfileCatalog(ProfileCatalog catalog)
    {
        if (catalog.SchemaVersion != ProfileCatalog.CurrentSchemaVersion || catalog.Profiles is null ||
            catalog.Profiles.Any(p => ReferenceEquals(p, null)))
            throw new InvalidDataException("Invalid or unsupported profile catalog.");

        if (catalog.DefaultProfileId is { } id && catalog.Profiles.All(p => p.Id != id))
            throw new InvalidDataException("The default profile must exist in the catalog.");

        foreach (var profile in catalog.Profiles) ProfileIni.ValidateProfile(profile);

        if (catalog.Profiles.Select(p => p.Id).Distinct().Count() != catalog.Profiles.Count)
            throw new InvalidDataException("Profile IDs must be unique.");
    }

    /// <summary>Version 1 stored the common options as typed properties; version 2 keeps every option in settings.</summary>
    internal static JsonNode UpgradeProfileCatalog(JsonNode document)
    {
        try
        {
            if (document["schemaVersion"]?.GetValue<int>() != 1 || document["profiles"] is not JsonArray profiles)
                return document;

            foreach (var profile in profiles.OfType<JsonObject>()) UpgradeProfile(profile);
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException)
        {
            throw new InvalidDataException("profiles.json contains an unreadable version 1 profile.", ex);
        }

        document["schemaVersion"] = ProfileCatalog.CurrentSchemaVersion;

        return document;
    }

    private static void UpgradeProfile(JsonObject profile)
    {
        var settings = profile["settings"] as JsonObject ?? [];
        profile["settings"] = settings;

        void Move(string property, string id, Func<JsonNode, string?> convert)
        {
            if (profile[property] is { } node && convert(node) is { } value) settings[id] = value;
            profile.Remove(property);
        }

        static string? Choice(JsonNode node) { return node.GetValue<string>() is var value and not "auto" ? value : null; }
        static string Flag(JsonNode node) { return node.GetValue<bool>() ? "true" : "false"; }
        static string? OnlyTrue(JsonNode node) { return node.GetValue<bool>() ? "true" : null; }
        static string Number(JsonNode node) { return node.GetValue<decimal>().ToString(CultureInfo.InvariantCulture); }
        static string Key(JsonNode node) { return node.GetValue<int>() is -1 ? "-1" : $"0x{node.GetValue<int>():X2}"; }

        // A version 1 sharpness value was also the switch that overrides the game's own sharpness.
        if (profile["sharpness"] is not null) settings["Sharpness.OverrideSharpness"] = "true";
        Move("sharpness", "Sharpness.Sharpness", Number);
        Move("dx11Upscaler", "Upscalers.Dx11Upscaler", Choice);
        Move("dx12Upscaler", "Upscalers.Dx12Upscaler", Choice);
        Move("vulkanUpscaler", "Upscalers.VulkanUpscaler", Choice);
        Move("spoofDxgi", "Spoofing.Dxgi", Flag);
        Move("overlayKey", ProfileSettings.OverlayKeyId, Key);
        Move("frameGenKey", ProfileSettings.FrameGenKeyId, Key);
        Move("frameGenInput", "FrameGen.FGInput", Choice);
        Move("frameGenOutput", ProfileSettings.FrameGenOutputId, Choice);
        Move("disableOverlays", "Hotfix.DisableOverlays", Flag);
        Move("loadReshade", "Plugins.LoadReshade", OnlyTrue);
        Move("loadSpecialK", "Plugins.LoadSpecialK", OnlyTrue);
        Move("framerateLimit", "Framerate.FramerateLimit", Number);
        Move("enableLogging", "Log.LogToFile", OnlyTrue);
    }
}
