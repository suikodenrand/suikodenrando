# Stars of Destiny 0.14.4

Shuffled recruits can now receive the original story role's starting inventory instead of bringing their replacement character's starting gear. The new **Joining inventory & equipment** option defaults to **Original role's starting inventory** in both games and works in Shuffle, chosen-companion and seeded random fixed-roster modes.

## Changes

- Copy the original role's first-join inventory, including consumables, item counts and equipped state.
- Substitute incompatible armor and accessories with comparable equipment the replacement can wear. Where no legal substitute exists, retain the item unequipped in S1 or transfer it to S2's shared bag when space permits; do not discard it.
- Preserve the replacement's own weapon identity and rune setup. Joining weapon-level choices remain separate, including Tir/Riou +/-3, original role and level 1.
- Save the inventory policy and completed first-join history with the campaign. Later joins, equipment changes and acquired items are not intended to be reset by this initialization.
- Keep **Replacement's starting inventory** (`Donor`) as an option. Campaigns created before the feature retain that legacy policy without an inventory retrofit.
- Add independent S1/S2 inventory choices to launcher **1.0.4**, including presets, the advanced editor and configuration backups. This setting requires runtime **0.14.4**.

## Downloads

- **`Stars-of-Destiny-Launcher-1.0.5-Windows.zip`** — latest separate optional launcher, compatible with runtime 0.14.4. Simplifies S1 chest settings to one new-campaign checkbox while keeping saved-policy controls. On Apply, clears the older global pause; saved campaign policies remain separate. See [launcher 1.0.5 notes](launcher-1.0.5.md). Its checksum is in `Stars-of-Destiny-Launcher-1.0.5-Windows.zip.sha256`.
- **`Stars-of-Destiny-0.14.4-Plugins-Only-Nexus.zip`** — manual plugins, defaults, instructions, Suikoden Fix and dormant optional S1 ATB. No EXEs, launcher/runtime, loader or install scripts. Install the tested BepInEx separately; the launcher is optional.
- **`Stars-of-Destiny-0.14.4-Optional-Launcher-Windows.zip`** — original separate launcher **1.0.4**, retained unchanged. Prefer the 1.0.5 download above for the simplified treasure settings. Both include their complete private .NET runtime and require plugins/BepInEx separately.
- **`SHA256SUMS-Split-0.14.4.txt`** — hashes for the two split downloads above.
- **`Stars-of-Destiny-0.14.4-Setup.exe`** — full Windows setup, including the tested BepInEx loader, launcher, Suikoden Fix and optional S1 ATB.
- **`Stars-of-Destiny-0.14.4-Mod-Only-Windows.zip`** — manual mod package with launcher, Suikoden Fix and dormant optional S1 ATB. Install **BepInEx 6 Unity IL2CPP Windows x64 build 697 (`5362580`) separately first**.
- **`SHA256SUMS.txt`** — hashes for the actual release assets.
- **`LICENSE.txt`** — MIT license for the project's own code. Dependencies retain their original licenses.

For manual installation, use the [tested official BepInEx download](https://builds.bepinex.dev/projects/bepinex_be/697/BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.697%2B5362580.zip), then extract the plugins ZIP into the same game folder. The manual plugin downloads do not include BepInEx core, its root `dotnet` runtime, `winhttp.dll` or Doorstop files. Read `START HERE.txt` and `REQUIREMENTS.txt` in the package. Existing users should follow `StarsOfDestiny Manual/MANUAL UPGRADE.txt` to preserve configurations and campaign sidecars before replacing binaries.

For Nexus, use **Plugins-Only-Nexus**. Edit the supplied CFGs with the game closed and launch through Steam; its `SETTINGS.txt` covers seeds, toggles and optional ATB without requiring the launcher. The original Mod-Only-Windows ZIP includes the launcher; the new plugins-only ZIP does not. This distribution split retains the exact released plugin/config and launcher binary bytes. It changes packaging and documentation, not gameplay. The original assets and `v0.14.4` source tag remain unchanged. Compiled DLL plugins can still require host review; no Nexus or antivirus approval is claimed.

The full setup includes the upstream loader distribution. Previously reported detections for `dobby.dll` and `.doorstop_version` matched unmodified upstream files; their origin alone does not establish an antivirus false-positive ruling or host approval.

## Defaults and existing campaigns

Fresh defaults are Shuffle, **Original role's starting inventory** (`Shuffle/JoiningEquipment=MatchRole`), normal openings, English dialogue replacement and shuffled Unites. Protagonist shuffle, chest shuffle and ATB remain off. F6 starts Hidden. Joining weapon levels follow Tir in S1 or Riou in S2 with seeded -3 to +3 variance by default.

The inventory and weapon policies are separate. Changing a launcher option does not rewrite the policy of an existing saved campaign. Saves predating joining-equipment metadata continue to use `Donor`; upgrading does not reset or replace their items. Keep saves and all corresponding configuration sidecars together.

S1 ATB remains optional and experimental. Disable shuffled S1 Unites before activating or enabling ATB; the two previews must not run together. S2 ATB/chest shuffle, Nuzlocke, shared inventory and extra rune slots are not included.

## Verification and limits

Managed/static checks passed: **109 S1 runtime, 69 S2 runtime, 13 dialogue, 25 launcher settings, 11 launcher UI, 38 compiled-host PE contracts, 165 native catalog/contracts, 5 ATB build-pairing and 6 package metadata/default checks**. These cover the new policy, per-game launcher settings, all three roster modes, compatibility planning and saved initialization history.

In the recorded native S1 check, Meg replaced Viktor and joined with **Fur Robe, Boots and weapon level 1**. Inspection of the new slot 18 save confirmed that loadout, `MatchRole` and the completed first initialization. **Reloading the save was not observed**, so this is first-join and stored-save evidence, not a native reload/rejoin pass.

Every recruitment script, incompatible loadout and later campaign transition has not been played through in both games. The [build guide](../BUILDING.md) records the remaining native checklist. S1 chest shuffle and ATB continue to need wider testing. Local checks do not establish acceptance by Nexus or an individual antivirus vendor.

The [0.14.3 source and release](https://github.com/suikodenrand/suikodenrando/releases/tag/v0.14.3) remain available. This release's own source is tagged [v0.14.4](https://github.com/suikodenrand/suikodenrando/tree/v0.14.4); see [BUILDING.md](../BUILDING.md) and [THIRD_PARTY_NOTICES.md](../THIRD_PARTY_NOTICES.md).
