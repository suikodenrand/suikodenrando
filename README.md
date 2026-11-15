# Stars of Destiny

A seeded character randomizer for the Windows Steam release of **Suikoden I & II HD Remaster**. Both games share one plugin host and interface, with separate settings and saved campaign mappings. The current release is **0.14.4**, with launcher **1.0.4**.

Download the [0.14.4 release](https://github.com/suikodenrand/suikodenrando/releases/tag/v0.14.4). Developers and reviewers can build the source using [BUILDING.md](BUILDING.md); see [CHANGELOG.md](CHANGELOG.md) for release history. The earlier [0.14.3 release](https://github.com/suikodenrand/suikodenrando/releases/tag/v0.14.3) remains available with its original source and assets.

## Features

- Character-role shuffle, chosen companions and seeded random fixed rosters, with optional protagonist shuffle.
- Separate seeds, quick starts and settings for Suikoden I and II. Recruitment requirements and story progression retain their native roles.
- Shuffled names, portraits and party appearances, plus supported English dialogue-name replacements.
- Native Unite eligibility based on the actual shuffled fighters in both games.
- Joining-weapon options: follow Tir in S1 or Riou in S2 with seeded -3 to +3 variance, match the original role, or start at weapon level 1. Saved campaigns retain their policy and earned sharpening.
- Joining inventory and equipment for both games: use the original role's starting inventory, including consumables, with comparable wearable substitutes for incompatible equipment. Available in Shuffle, chosen companions and random fixed rosters; keeping the replacement's starting inventory remains an option.
- A Windows launcher for settings, seeds, presets, configuration backups and optional features.
- Optional experimental S1 chest shuffle and S1 Active Time Battle preview. ATB uses native command menus, targeting and SPD gauges.
- One text-only F6 panel, hidden at launch by default.

Suikoden II ATB and chest shuffle, Nuzlocke, shared-inventory changes and extra rune slots are not included. Some custom scene animations retain native handling. The English dialogue replacement is curated; it does not rewrite every mention in every language.

## Choose a download

| Asset | Installation | BepInEx |
| --- | --- | --- |
| `Stars-of-Destiny-0.14.4-Setup.exe` | Guided full installation, optional ATB component and transaction recovery | Included |
| `Stars-of-Destiny-0.14.4-Mod-Only-Windows.zip` | Manual extraction into the game folder | Required separately |

Both include the combined randomizer, launcher and Suikoden Fix. The launcher includes its own .NET runtime; players do not need a separate .NET SDK or runtime installation. The release also provides SHA-256 checksums and the project's MIT license. Existing users should read the upgrade instructions before replacing files.

### Full setup

Close the game and launcher, then run the setup EXE and select the game folder. Fresh ATB is optional, unchecked and disabled. Setup verifies its supported game build and package, preserves existing configuration files and saves, and records recovery copies for changed files. Read the included instructions and keep the transaction backup path.

The full setup includes upstream BepInEx files. The reported detections for `BepInEx/core/dobby.dll` and `.doorstop_version` match unmodified files in the tested upstream package. This identifies their origin; it is not an antivirus false-positive ruling or a guarantee of platform approval. The manual mod-only ZIP does not redistribute the BepInEx loader.

### Manual installation

1. Close the game and launcher. In Steam, use **Manage > Browse local files** to find the folder containing `Suikoden I and II HD Remaster.exe`.
2. Install **BepInEx 6 Unity IL2CPP Windows x64, build 697 (`5362580`)** from the [official download](https://builds.bepinex.dev/projects/bepinex_be/697/BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.697%2B5362580.zip). Extract its contents into the game folder first. Do not substitute BepInEx 5, a Mono build or an x86 build.
3. Extract the mod-only ZIP into the same game folder. Its `BepInEx` folder contains mod plugins and settings, not the BepInEx loader.
4. Open `StarsOfDestiny Launcher/StarsOfDestiny.Launcher.exe`, choose settings and use **Apply settings** or **Save & launch game**.
5. Allow time and internet access for the first BepInEx launch to obtain Unity reference files and generate interop assemblies. Start a New Game for a new randomized campaign.

For an existing modded installation, first back up `Save` and `BepInEx/config` together outside the game folder. Follow `StarsOfDestiny Manual/MANUAL UPGRADE.txt` in the ZIP to replace binaries while preserving existing configs and campaign sidecars. Manual copying does not run installer conflict checks, create backups or create restore transactions. Do not remove the external loader as part of a mod-only rollback.

## Defaults and optional features

Fresh 0.14.4 installations use character Shuffle, the original role's starting inventory, normal openings, English dialogue replacement and shuffled Unites. Protagonist shuffle and chest shuffle are off. F6 starts Hidden and cycles Full, Compact and Hidden text views for the current session.

In Characters, **Joining inventory & equipment** defaults to **Original role's starting inventory** (`Shuffle/JoiningEquipment=MatchRole`). At first join, replacements receive the original role's inventory and equipped items; incompatible armor and accessories use comparable equipment they can wear. The replacement's own weapon identity and rune setup remain intact. **Replacement's starting inventory** (`Donor`) keeps the previous behavior. The choice applies to new campaigns in all three modes, independently of the joining-weapon-level option. Later rejoining and reloading retain acquired items and equipment changes.

Campaigns saved before this feature keep the legacy replacement inventory behavior. Upgrading or changing the launcher does not retrofit those saves or reset their inventory. Keep saves and their campaign sidecars together.

First joining weapons follow Tir or Riou with seeded -3 to +3 variance, including a possible exact match. These are new-campaign policies. Changing launcher settings does not rewrite the seed, mapping or weapon policy already bound to a saved campaign.

ATB is inactive by default. In the manual ZIP its reviewed DLL is dormant directly under `BepInEx`, outside the active plugins tree. With the game closed, disable S1 shuffled Unites, choose **Activate ATB preview** in the launcher's S1 Battle & ATB tab, then enable ATB and apply settings. Activation alone does not enable it. S1 ATB and shuffled S1 Unites must not be enabled together.

## Source, builds and license

The project's own code and documentation are under the [MIT License](LICENSE). Dependencies retain their existing licenses; see [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md). A legal copy of the game is required. Game binaries, generated game interop assemblies and personal saves are not supplied with the source.

[BUILDING.md](BUILDING.md) explains the pinned SDK, locally generated game references, plugin and launcher builds, tests and release packaging. [CONTRIBUTING.md](CONTRIBUTING.md) records the source-first release process for future versions.

## Testing status and support

Representative shuffled parties, save/load transitions, dialogue, Unite attacks and F6 behavior from earlier builds have been tested in-game; every campaign, recipe and scripted encounter has not been played through. S1 chest shuffle and ATB remain experimental. For the new joining-inventory policy, a native S1 run verified Meg joining in Viktor's role with Fur Robe, Boots and weapon level 1, and inspection confirmed that loadout in the new save. Reloading that save was not observed. Wider native recruitment, compatibility and save/rejoin checks remain useful in both games; see the recorded coverage and remaining checklist in [BUILDING.md](BUILDING.md).

Report the game, version, seed/mode, scene and steps that reproduce a problem. Include relevant `BepInEx/LogOutput.log` lines and screenshots after reviewing them for personal paths or other private data. Keep saves and their configuration sidecars paired when backing up or sharing a campaign.
