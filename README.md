# OptiscalerApp

Avalonia desktop application for managing game installations and rendering components.

Currently implemented:

- Add local game folders, display them in the library, and reload the catalog at startup.
- Search by name and sort by name or platform.
- Preserve separate installation paths during discovery while removing repeated results.
- Save JSON through a temporary file and recover malformed documents from a backup.

## Library

- Favorites stay on top; games can be hidden (a rescan does not bring them back) or removed without touching their files.
- Filters: favorites, tested on the OptiScaler wiki, managed by OptiScaler, needs attention, updates available, hidden.
- Each card shows the most useful fact: installed OptiScaler version, "needs attention" when a game update or a launcher's
  file verification changed managed files, or the wiki's compatibility verdict.
- **Check updates** compares every managed game with the latest stable OptiScaler release.

## Manage Game

- **Compatibility list**: the OptiScaler wiki tables are cached for a day and matched by name (exact, then a conservative
  word match that never confuses "Dying Light" with "Dying Light 2"). Status, inputs, OptiPatcher support, notes and a
  link to the game's wiki page are shown.
- **Executable detection**: finds the real game binary as the install guide describes it (Unreal Engine
  `*-Shipping.exe` in `Binaries/Win64` or `WinGDK`, never the `Engine` folder or a launcher stub).
- **Recommended setup**: picks the injection DLL (from the wiki notes, or `winmm.dll` when another mod owns `dxgi.dll`),
  FakeNvapi, OptiPatcher and NukemFG for the detected GPU, and explains every choice. Nothing changes before the preview.
- **Keep my current settings** when updating: customized `OptiScaler.ini` values are copied into the new package's INI.
- The preview lists each `OptiScaler.ini` key that will change, e.g. `[Spoofing] Dxgi: auto → false`.
- Installing OptiPatcher also sets `LoadAsiPlugins=true`, without which OptiScaler ignores it.
- **Copy diagnostic report**: versions, GPU, OS, detected files, operations, verification, custom INI values and the last
  log lines, with the home folder and user name replaced. The copied text is shown before you share it.
- **Launch game** (Steam games through Steam) and, on Linux, the `WINEDLLOVERRIDES` launch options for the chosen DLL.

## Profiles

Besides upscalers, sharpness and logging, profiles cover GPU spoofing, the overlay and frame generation shortcut keys
(with conflict checks against OptiScaler's FPS overlay keys), frame generation input and output, blocking the Steam and
Epic overlays, loading ReShade or Special K, and a frame rate limit. An existing `OptiScaler.ini` can be imported as a
profile.
