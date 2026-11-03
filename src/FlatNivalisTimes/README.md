# Flat Nivalis Times

*Newspaper Readability Mod — public release 1.1*

Read the Nivalis Times in the character's hands or in an enlarged view. This independent mod flattens the newspaper, steadies the reading pose, and adds background and contrast controls.

## Features

- Flat paper with reduced idle movement and an adjusted grip; the original lift/lower animation remains.
- Switch between handheld and enlarged reading views.
- Cycle the original, faded and plain paper backgrounds, and toggle dark text with a consistent warm panel palette.
- On-screen keyboard/controller hints follow the active device and configured bindings, using the game's key/button images when available.
- Background and contrast choices are retained between sessions. The enlarged view closes when the newspaper is put away.
- Enable or disable the mod from **Settings > Gameplay > Improve newspaper readability**. The setting and reading hints follow the game's 11 supported languages.

The mod uses the game's newspaper articles, photographs, fonts and weather display. It applies to the handheld newspaper, not posters. It does not disable the game's lighting or bloom globally.

## Requirements

- Nivalis Nights for Windows x64, **Update 7, Steam build 26295359**.
- BepInEx 6 Unity IL2CPP for Windows x64. Tested loader: **6.0.0-be.788+5b766a3**.

Other game/loader versions are not covered by this check. No other mods are required. Game and dependency files are not bundled.

## Install or update

1. Install the Unity IL2CPP Windows x64 build of BepInEx from its [official builds page](https://builds.bepinex.dev/projects/bepinex_be), launch the game once to generate interop assemblies, then close it. When updating the game, let BepInEx refresh its interop before using the mod.
2. Extract this mod's ZIP into the game folder. The installed DLL must be:

```text
BepInEx/plugins/NivalisNights.FlatNivalisTimes/NivalisNights.FlatNivalisTimes.dll
```

3. Keep only one copy of this mod DLL anywhere under `BepInEx/plugins`. Replace the previous DLL while the game is closed; retain the existing config.

The mod is enabled by default. Use the compiled mod ZIP to install it; the source ZIP is for building.

## Reading controls

Open the newspaper normally, or read it during the wake-up sequence. Extra controls are available after it has fully opened and while gameplay input is active.

| Action | Default keyboard | Default Xbox-style controller |
| --- | --- | --- |
| Enlarge / return to hands | V | RB |
| Cycle background | B | LB |
| Toggle contrast | H | X |

These are defaults; the in-game hints use the current bindings and device. Other normal newspaper controls remain available. Extra controls are suspended during menus, loading, text entry, rebinding, loss of focus and screen transitions. Hints are shortcuts, not mouse buttons.

## Configuration

Settings are stored only in this mod's INI-format file:

```text
BepInEx/config/NivalisNights.FlatNivalisTimes.cfg
```

```ini
[General]
Enabled = true

[Reading]
Background = Original
HighContrast = false

[Keyboard]
View = V
Background = B
Contrast = H

[Gamepad]
View = rightShoulder
Background = leftShoulder
Contrast = buttonWest
```

Background values: `Original`, `Faded`, `Plain`. `HighContrast` accepts `true` or `false`. In-game reading changes save automatically; temporary write failures are retried. To edit the file manually, close the game first and restart afterward.

Bindings accept one short key/button name, case-insensitively. An empty value disables that action for that device. Device prefixes and combinations such as Ctrl+V are unsupported. Conflicting, duplicate, unavailable or synthetic controls may be rejected; check the log.

The preferences apply across game saves. No game settings or save files are edited. Keep the config when updating from the first release, 1.0.0; new entries are added automatically. The enlarged-view state is not saved.

## Remove

Close the game, remove this mod's DLL, then restart. Optionally remove its dedicated config to discard preferences. Leave BepInEx and other mods in place if still needed. Removal does not require a save conversion.

## Troubleshooting

Check `BepInEx/LogOutput.log` for **Flat Nivalis Times 1.1.14**. If hints or controls are absent, check the gameplay option, configured bindings and `Reader controls state` messages. Report the game build, mod version, reproduction steps and relevant log entries. Review logs for personal information before sharing them.

## Changes in 1.1

- Updated for Nivalis Nights Update 7.
- Added enlarged reading with a shortcut to return to the handheld view.
- Added original, faded and plain background choices, plus a contrast toggle with a consistent warm panel palette.
- Added configurable keyboard/controller shortcuts and device-aware hints using the game's original key/button images.
- Background and contrast preferences are saved between sessions.
- Reading controls and saved preferences apply to both wake-up and manually opened newspapers; both routes were checked in game.
- Improved UI readiness, input handling and cleanup safety.

## Build from source

Install the SDK specified by `global.json`. With the supported game and BepInEx installed and interop generated, run from the source ZIP root:

```powershell
dotnet build src/FlatNivalisTimes/NivalisNights.FlatNivalisTimes.csproj -c Release
```

For a different installation folder, add `-p:GameDir="D:\SteamLibrary\steamapps\common\Nivalis Nights"`. Install only `src/FlatNivalisTimes/bin/Release/net6.0/NivalisNights.FlatNivalisTimes.dll`.

## License and third-party rights

The authored mod code, compiled plugin and documentation are licensed under **0BSD**; see [LICENSE](LICENSE). Nivalis Nights, BepInEx, Unity, Harmony, Il2CppInterop and other third-party materials retain their respective licenses and terms. Game assets and third-party binaries are not redistributed; the mod references installed game assets at runtime.

OpenAI Codex assisted development. The author performed in-game testing. Testing does not cover every language, scene, hardware configuration or mod combination.
