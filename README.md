# Nivalis Nights Mods

Independent mods for [Nivalis Nights](https://store.steampowered.com/app/1488490/Nivalis_Nights/) on Windows x64.

Install only the mods you want. Each mod has its own DLL, configuration file, version and license notice; neither mod requires the other.

| Mod | Purpose | Version | Instructions | Download |
| --- | --- | --- | --- | --- |
| Boat Control Bindings | Keyboard and controller shortcuts for boat controls | 1.1 | [README](src/BoatControlBindings/README.md) | [Releases](https://github.com/KwaiBird/Nivalis-Nights-Mods/releases) |
| Flat Nivalis Times | Steadier handheld and enlarged newspaper reading, background and contrast controls | 1.1 | [README](src/FlatNivalisTimes/README.md) | [Releases](https://github.com/KwaiBird/Nivalis-Nights-Mods/releases) |

The versions listed here are prepared locally; they may not yet be available on the release page. Check the version of each download. Download each mod separately from its release page. Choose the mod ZIP to install it; source ZIPs are for building from source. Compiled DLLs are distributed as release downloads, rather than committed to the source tree.

## Requirements

- [Nivalis Nights on Steam](https://store.steampowered.com/app/1488490/Nivalis_Nights/) for Windows x64. Supported game builds differ by mod; see the table below.
- BepInEx 6 Unity IL2CPP for Windows x64, installed and working with that game version.
- Tested loader: **6.0.0-be.788+5b766a3**. Compatibility with other game or loader versions has not been established.

| Mod version | Supported game build |
| --- | --- |
| Flat Nivalis Times 1.1 | Update 7, Steam build **26295359** |
| Boat Control Bindings 1.1 | Update 7, Steam build **26295359** |

Each mod README includes its complete installation, configuration, update and removal instructions. Game, BepInEx and other dependency files are not included.

## Building from source

Install the .NET SDK specified by `global.json` (**8.0.426**, with patch roll-forward enabled). Launch the game with BepInEx once to generate the IL2CPP interop assemblies, then close the game.

Build only the desired project from the repository root:

```powershell
dotnet build src/BoatControlBindings/NivalisNights.BoatControlBindings.csproj -c Release
dotnet build src/FlatNivalisTimes/NivalisNights.FlatNivalisTimes.csproj -c Release
```

The default game directory is `C:\Program Files (x86)\Steam\steamapps\common\Nivalis Nights`. For another location, add `-p:GameDir="D:\SteamLibrary\steamapps\common\Nivalis Nights"` to the selected command.

The resulting mod DLL is in that project's `bin/Release/net6.0` folder. Install only the mod DLL as described in its README. Referenced game and loader assemblies have `Private=false` and are not bundled. This repository contains no additional package-feed configuration.

## Development and verification

OpenAI Codex was used to assist development. The mod author reviewed the changes and check results, performed in-game testing, and carried out final release verification.

## License

Both mods are licensed under **0BSD (Zero-Clause BSD)**. See the repository [LICENSE](LICENSE), [Boat license](src/BoatControlBindings/LICENSE) and [Flat Nivalis Times license](src/FlatNivalisTimes/LICENSE).

Game and third-party rights remain with their respective rights holders. Original game files, copied or decompiled game code, game assets, and third-party library binaries are not distributed here.
