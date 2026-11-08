# Stars of Destiny - Character Randomizer

Version 0.8.12 adds balanced starting equipment for new campaigns. Choose Balanced to give replacement heroes suitable starting weapons and armor, or Original to keep their usual starting equipment. See [the starting gear guide](docs/BALANCED_GEAR_TEST.md) for installation and settings.

Randomize the combat cast of Suikoden I HD Remaster on Windows. The [website](https://suikodenrand.github.io/) generates a config with the selected seed, roster and start point alongside Windows setup.

Character Shuffle uses the 75-hero pool by default. Enable Include Tir to add the protagonist's role and avatar to the pool. His chosen name and story rune stay with his role. Fixed Roster and Random Roster reserve Tir and assign five companions as the story opens party slots. Before Viktor can be used with any mode.

Starting gear is set once for a new campaign; existing campaigns keep their current equipment and upgrades. Nuzlocke retains Tir's protection, reserve-team retries and crash diagnostics. Version 0.8.9 through 0.8.11 Nuzlocke saves remain compatible; 0.8.8 Nuzlocke campaigns are not supported. Keep game saves with their matching BepInEx configuration. See [the Nuzlocke guide](docs/NUZLOCKE_TEST.md) for its rules.

## Build

Build the plugin against a working BepInEx 6 IL2CPP installation with interop generated for the supported game build.

```powershell
dotnet build src/SuikodenRandomizer.AvatarPlugin -c Release "-p:GamePath=C:\Games\Suikoden I and II HD Remaster"
```

The output contains SuikodenRandomizer.dll and SuikodenRandomizer.Core.dll. Use those two files to [build the setup with NSIS](packaging/nsis/README.md). Run the standalone test projects under tests with the SDK specified in global.json. The native metadata contract check takes the path to BepInEx/interop/GSD1.dll.
