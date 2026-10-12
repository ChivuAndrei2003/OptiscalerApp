using OptiscalerApp.Management;

namespace OptiscalerApp.ViewModels;

public sealed record HelpTopic(string Title, string Text);

public sealed record HelpSection(string Title, string Intro, IReadOnlyList<HelpTopic> Topics);

/// <summary>
///     Every explanation the app shows, written once: the "?" beside an option and the Help page read the same text.
/// </summary>
public static class HelpText
{
    // Manage Game: identity.
    public const string EditDetails =
        "Renames the game in your library. Saving also stores the selected executable for this installation.";

    public const string Installation =
        "The folder being managed. A game found in more than one place lists each installation, and every one is " +
        "installed, verified and restored on its own.";

    public const string LaunchGame =
        "Starts the game to try your changes. Steam games start through Steam; others start their executable.";

    // Manage Game: installation.
    public const string Status =
        "Whether OptiScaler is in this game's folder. \"Managed files verified\" means every file this app installed " +
        "is unchanged. \"Installation needs attention\" means a game update or the launcher's file verification " +
        "changed some of them. \"Untracked rendering files\" means OptiScaler or a similar mod was copied in by " +
        "hand. Verify reads the folder again.";

    public const string Components =
        "Upscaler and frame generation libraries found in the game folder: DLSS, FSR, XeSS, OptiScaler and other " +
        "injected DLLs. Hover a name to see its path. A file being present does not prove the game uses it.";

    public const string Channel =
        "Stable lists the official OptiScaler releases. Beta lists preview builds with newer features and less " +
        "testing. Each channel remembers the package you chose for it.";

    public const string Version =
        "The OptiScaler build to install. Pick a release to download it, or choose a local package you extracted " +
        "yourself. If you pick nothing, installing downloads the channel's latest release. Refresh versions asks " +
        "GitHub for the current list.";

    public const string ComponentChoices =
        "\n\n\"Use package bundle\" takes the version shipped inside the OptiScaler package. \"Skip\" leaves the " +
        "files already in the game folder alone. A version number downloads that release. \"Choose local file\" " +
        "uses a file of your own.";

    public const string Fsr =
        "AMD's FSR 4 upscaler libraries, which OptiScaler loads when you select FSR 4. The INT8 build also runs on " +
        "GPUs without official FSR 4 support, at some performance cost." + ComponentChoices;

    public const string FakeNvapi =
        "Imitates NVIDIA's NVAPI on AMD and Intel GPUs so the game offers DLSS and Reflex; Reflex is converted to " +
        "Anti-Lag 2 or LatencyFlex. Not needed on NVIDIA GPUs." + ComponentChoices;

    public const string Injection =
        "The file name OptiScaler.dll is installed under, which is what makes the game load it. dxgi.dll works for " +
        "most games. Pick another, such as winmm.dll, when a mod like ReShade already uses that name or the wiki " +
        "recommends one. An update keeps the installed name; uninstall first to switch.";

    public const string OptiPatcher =
        "A plugin that unlocks DLSS inputs in supported games without pretending the GPU is an NVIDIA one. Only " +
        "for AMD and Intel GPUs, and only for games the wiki lists. Installing it also sets LoadAsiPlugins=true, " +
        "without which OptiScaler ignores it." + ComponentChoices;

    public const string Nukem =
        "dlssg-to-fsr3: turns the game's DLSS Frame Generation into FSR 3 frame generation on GPUs that cannot run " +
        "DLSS-G. Only useful when the game ships nvngx_dlssg.dll." + ComponentChoices;

    public const string Profile =
        "A saved set of OptiScaler.ini overrides from the Profiles page, written into OptiScaler.ini when you " +
        "install. Your default profile is selected for you. With none selected, the package's own settings are used.";

    public const string KeepCurrentSettings =
        "When updating, values you changed in the existing OptiScaler.ini are copied into the new one, so in-game " +
        "tuning survives the update. A selected profile still wins for the keys it sets.";

    public const string CurrentSettings =
        "What this game's OptiScaler.ini changes from OptiScaler's defaults. The in-game menu's Save Settings writes " +
        "to the same file, so what you tune while playing shows up here after Verify. Save as profile turns it into " +
        "a profile you can apply to other games.";

    public const string Executable =
        "The game's real binary; OptiScaler is installed into its folder. A launcher stub does not count: Unreal " +
        "Engine games run a *-Shipping.exe inside Binaries/Win64. Detect finds it for you, Browse lets you pick it.";

    public const string PackageFolder =
        "A folder holding an extracted OptiScaler release (OptiScaler.dll and OptiScaler.ini). It is filled in after " +
        "a download; browse to use a build you extracted yourself.";

    public const string ProfileActions =
        "\"Use package settings\" clears the selected profile. \"Apply profile…\" previews writing the selected " +
        "profile into the installed OptiScaler.ini, without reinstalling anything.";

    public const string InstallActions =
        "Preview lists every file that would be created or replaced and each OptiScaler.ini key that would change. " +
        "Nothing is written until you choose Apply changes, and every replaced file is backed up first. Uninstall " +
        "undoes every change this app made in the folder and puts the original files back.";

    // Manage Game: maintenance.
    public const string NativeSwap =
        "Swaps a library the game ships, such as nvngx_dlss.dll or libxess.dll, for another version. You pick the " +
        "DLL in the game folder, then its replacement. The change is previewed and the original is backed up.";

    public const string UndoLatest =
        "Reverts only the most recent change in this folder (an install, update, profile or DLL swap) and restores " +
        "the files it replaced. Uninstall reverts all of them.";

    public const string History =
        "Lists every change recorded for this folder with its date, result and OptiScaler version, under Analysis " +
        "& activity.";

    public const string Diagnostics =
        "Copies a report for bug reports: versions, GPU, operating system, detected files, changes, verification, " +
        "custom OptiScaler.ini values and the last log lines. Your home folder and user name are replaced, and the " +
        "text is shown before you share it.";

    public const string Activity =
        "The raw result of the last check: every detected file with its path, verification problems, the " +
        "operation history and the diagnostic report.";

    // Manage Game: guidance.
    public const string Compatibility =
        "The verdict from the community-maintained OptiScaler wiki, matched by game name and cached for a day. A " +
        "game missing from the list is untested, not broken. Anti-cheat found in the folder is shown here instead.";

    public const string Inputs =
        "The upscalers the game offers: those found in its folder, plus the ones the wiki lists. OptiScaler takes " +
        "over one of them (DLSS, FSR 2 or later, XeSS), so a game with none usually cannot use it.";

    public const string OptiPatcherSupport =
        "Whether the wiki lists this game as supported by OptiPatcher, which exposes DLSS inputs without GPU spoofing.";

    public const string Gpu =
        "The graphics card games render on. Recommendations depend on it: FakeNvapi, OptiPatcher and NukemFG are " +
        "suggested for AMD and Intel GPUs only.";

    public const string Recommended =
        "A suggested injection name and set of components for this game and GPU, each with its reason. Apply " +
        "recommended only selects them in the options; nothing changes until you preview and apply.";

    public const string LinuxLaunch =
        "Under Proton or Wine the game ignores an injected DLL unless told to prefer it. Paste this text into the " +
        "game's launch options, for example in Steam's game properties.";

    public const string Notes = "What to do next for this installation, and warnings such as detected anti-cheat.";

    // Profile editor.
    public const string ProfileGeneral =
        "The name is what you pick in Manage Game. The description is a note for yourself, shown in the profile list.";

    public const string ProfileOptions =
        "Each option is one OptiScaler.ini key. \"Default (auto)\" or an empty box leaves the key out, so OptiScaler " +
        "decides. Options you set are shown in bold and counted above. Reset to defaults clears all of them.";

    // Settings.
    public const string AutoScan = "Looks for newly installed games in the enabled launchers every time the app starts.";

    public const string Platforms =
        "The launchers Scan Games reads. Turn off the ones you do not use to scan faster. Games you added by hand " +
        "are not affected.";

    public const string CustomFolders =
        "Extra folders to search for games no launcher knows about, such as DRM-free or portable installs. One " +
        "absolute path per line. They are scanned only while the \"Custom folder\" platform above is enabled.";

    public const string AllowedRoots =
        "Limits scanning to these drives or folders: games installed anywhere else are skipped, whichever launcher " +
        "reported them. Leave empty to allow every location.";

    public const string DefaultChannel =
        "The release channel selected when you open a game. You can still switch between Stable and Beta per game.";

    public const string MenuButton =
        "OptiScaler itself only listens to the keyboard. With a button chosen here, this app presses Insert, " +
        "OptiScaler's menu key, for the game whenever you press that button, so keep the app open while you play. " +
        "Set button waits for what you press on the controller: one button such as a back paddle or Share, or " +
        "several held together such as View + Menu. Pick something the game does not use, then Save settings. It " +
        "stops working if a profile moves \"Open OptiScaler menu\" away from Insert. On a Steam Deck the back " +
        "buttons only reach this app when Steam Input passes them on; you can instead bind one to the Insert key " +
        "in the game's controller layout in Steam, which needs nothing here.";

    public const string DefaultProxy =
        "The injection file name selected for new installs. dxgi.dll suits most games; a game's recommendation or " +
        "an existing installation overrides it.";

    public const string DataFolder =
        "Where the app keeps your library, profiles, settings, downloaded packages and the backups used to undo " +
        "changes. Deleting it removes the ability to restore games to their original files.";

    public const string Downloads =
        "OptiScaler packages and components downloaded so far. Clearing them frees disk space; they are downloaded " +
        "again when needed. Backups are not touched.";

    private static readonly IReadOnlyDictionary<string, string> ProfileGroupIntros = new Dictionary<string, string>
    {
        ["Upscaling"] = "Which upscaler replaces the one the game asks for, and how its image is sharpened and scaled.",
        ["Frame generation"] = "Extra frames inserted between rendered ones, and how the game's HUD is kept clean.",
        ["Overlay & shortcuts"] = "OptiScaler's in-game menu, its shortcut keys and the FPS overlay.",
        ["Inputs & GPU spoofing"] =
            "Which upscaler requests OptiScaler answers, and whether the GPU is reported as an NVIDIA one so games " +
            "offer DLSS.",
        ["Other mods, performance & logging"] = "Loading other mods next to OptiScaler, a frame rate cap and log files."
    };

    public static IReadOnlyList<HelpSection> Sections { get; } =
    [
        new("Getting started",
            "OptiScaler is a mod that replaces a game's upscaler (DLSS, FSR or XeSS) with another one and can add " +
            "frame generation. This app installs it into your games, keeps backups and can undo every change.",
            [
                new("1. Add your games",
                    "On the Games page, Scan Games finds games from your launchers and Add Games adds folders by hand."),
                new("2. Open a game",
                    "Manage game shows what the game ships, what the OptiScaler wiki says about it and a recommended " +
                    "setup for your GPU."),
                new("3. Choose what to install",
                    "Apply recommended selects sensible options. You can change the OptiScaler version, the injection " +
                    "name, the extra components and a profile."),
                new("4. Preview, then apply",
                    "Preview install lists the exact file changes. Apply changes writes them after backing up every " +
                    "file it replaces."),
                new("5. Play",
                    "Start the game, select DLSS, FSR or XeSS in its graphics settings and press Insert to open the " +
                    "OptiScaler menu. An update can keep the settings you save there."),
                new("6. Undo any time",
                    "Uninstall OptiScaler restores the game's original files. Undo latest change reverts just the last " +
                    "step."),
                new("Anti-cheat",
                    "Injecting OptiScaler into online games with anti-cheat can get your account banned. The app warns " +
                    "when it finds anti-cheat files; never play online with OptiScaler installed in such a game."),
                new("Controller and keyboard",
                    "The D-pad, the left stick or the arrow keys move between controls. A or Enter activates the " +
                    "highlighted one, B or Escape closes a menu or returns from Manage game, LB and RB switch pages " +
                    "and the right stick scrolls. On a Steam Deck in Gaming Mode, add the app to Steam as a " +
                    "non-Steam game.")
            ]),
        new("Games library", "Every game the app knows about, one card each.",
            [
                new("Scan Games",
                    "Reads the platforms enabled in Settings (Steam, Epic, GOG, Xbox, EA, Battle.net, Ubisoft, Lutris " +
                    "and your custom folders) and adds the games it finds."),
                new("Add Games",
                    "Adds game folders you pick yourself, for games no launcher reports."),
                new("Check updates",
                    "Compares every managed game with the latest stable OptiScaler release and refreshes the wiki " +
                    "compatibility list."),
                new("Search, filter and sort",
                    "Search matches game names. The filter shows favorites, games tested on the wiki, games managed " +
                    "by OptiScaler, games that need attention, games with an update, or hidden games. Sort by name " +
                    "or platform, ascending or descending."),
                new("Card status",
                    "A card shows its most useful fact: the installed OptiScaler version, an available update, \"needs " +
                    "attention\" when a game update or file verification changed managed files, or the wiki's verdict."),
                new("Favorites, hiding and removing",
                    "The ⋯ menu on a card adds it to favorites (kept on top), hides it (a rescan does not bring it " +
                    "back) or removes it from the library. None of these touch the game's files.")
            ]),
        new("Manage Game: installing", "The options in the middle of the Manage Game page.",
            [
                new("OptiScaler Status", Status),
                new("Detected components", Components),
                new("Stable / Beta", Channel),
                new("OptiScaler version", Version),
                new("FSR 4 / INT8", Fsr),
                new("FakeNvapi", FakeNvapi),
                new("Injection", Injection),
                new("OptiPatcher", OptiPatcher),
                new("NukemFG", Nukem),
                new("Profile (.ini)", Profile),
                new("Keep my current OptiScaler.ini settings", KeepCurrentSettings),
                new("Settings in this game's OptiScaler.ini", CurrentSettings),
                new("Game executable", Executable),
                new("Extracted OptiScaler package", PackageFolder),
                new("Use package settings / Apply profile…", ProfileActions),
                new("Preview install, Apply changes and Uninstall", InstallActions)
            ]),
        new("Manage Game: game and guidance", "The game's identity on the left and the advice on the right.",
            [
                new("Edit game details", EditDetails),
                new("Installation", Installation),
                new("Launch game", LaunchGame),
                new("Compatibility", Compatibility),
                new("Upscaler Inputs", Inputs),
                new("OptiPatcher Support", OptiPatcherSupport),
                new("GPU", Gpu),
                new("Recommended setup", Recommended),
                new("Linux launch options", LinuxLaunch),
                new("Notes", Notes)
            ]),
        new("Manage Game: maintenance & recovery", "Tools for fixing or reverting an installation.",
            [
                new("Replace a native DLL…", NativeSwap),
                new("Undo latest change", UndoLatest),
                new("Operation history", History),
                new("Copy diagnostic report", Diagnostics),
                new("Analysis & activity", Activity)
            ]),
        new("Profiles",
            "A profile is a reusable set of OptiScaler.ini settings. Create it once and apply it to any game from " +
            "Manage Game.",
            [
                new("New Profile", "Opens the editor with every option on Default (auto)."),
                new("Import .ini",
                    "Creates a profile from an existing OptiScaler.ini, such as one from a game you already tuned."),
                new("Set Default", "Makes the selected profile the one preselected when you open a game."),
                new("Edit and Duplicate",
                    "Edit changes the selected profile. Duplicate copies it, to try a variation without losing the " +
                    "original."),
                new("Export .ini", "Saves the profile as an OptiScaler.ini file you can share or copy by hand."),
                new("Delete", "Removes the profile. Games it was applied to keep their settings."),
                new("Name and description", ProfileGeneral),
                new("Options", ProfileOptions)
            ]),
        ..ProfileSettings.All.GroupBy(setting => setting.Group).Select(group => new HelpSection(
            $"Profile options: {group.Key}", ProfileGroupIntros.GetValueOrDefault(group.Key, ""),
            group.Select(setting => new HelpTopic(setting.Label, ForProfileSetting(setting))).ToList())),
        new("Settings", "App-wide choices. Save settings stores the discovery and installation defaults.",
            [
                new("Scan automatically on startup", AutoScan),
                new("Enabled platforms", Platforms),
                new("Custom scan folders", CustomFolders),
                new("Allowed drive roots", AllowedRoots),
                new("Release channel", DefaultChannel),
                new("Proxy DLL", DefaultProxy),
                new("Button that opens OptiScaler's menu in a game", MenuButton),
                new("Data folder", DataFolder),
                new("Downloaded packages", Downloads)
            ])
    ];

    /// <summary>A profile option's description with the OptiScaler.ini key it writes.</summary>
    public static string ForProfileSetting(ProfileSetting setting)
    {
        var values = setting.Kind == ProfileSettingKind.Number ? " Decimals use a point, not a comma." : "";

        return $"{setting.Description}{values}\n\nOptiScaler.ini: [{setting.Section}] {setting.Key}";
    }
}
