# Stars of Destiny - Character Randomizer

Experimental 0.8.8 Nuzlocke mode. See [the test guide](docs/NUZLOCKE_TEST.md) for rules, installation and playtest steps.

Randomize the combat cast of Suikoden I HD Remaster on Windows. The [website](https://suikodenrand.github.io/) generates a config with the selected seed, roster and start point alongside Windows setup.

Character Shuffle uses the 75-hero pool by default. Enable Include Tir to add the protagonist's role and avatar to the pool. His chosen name and story rune stay with his role. Fixed Roster and Random Roster reserve Tir and assign five companions as the story opens party slots. Before Viktor can be used with any mode.

Settings apply to New Game. Existing randomized campaigns restore their saved mappings. Keep your game saves with the matching BepInEx configuration.

## Build

Build the plugin against a working BepInEx 6 IL2CPP installation with interop generated for the supported game build.

```powershell
dotnet build src/SuikodenRandomizer.AvatarPlugin -c Release "-p:GamePath=C:\Games\Suikoden I and II HD Remaster"
```

The output contains SuikodenRandomizer.dll and SuikodenRandomizer.Core.dll. Use those two files to [build the setup with NSIS](packaging/nsis/README.md). Run the standalone test projects under tests with the SDK specified in global.json. The native metadata contract check takes the path to BepInEx/interop/GSD1.dll.
