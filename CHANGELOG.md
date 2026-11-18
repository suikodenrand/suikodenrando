# Changelog

## [0.14.5](https://github.com/suikodenrand/suikodenrando/releases/tag/v0.14.5) — 2026-11-18

- Fix S1 save admission, editable save mirrors, fixed-roster battle entry and joining loadout updates.
- Process S2 recruitment equipment per recruit, refresh graphics after loading, and preserve an existing campaign binding when multiple saves have the same native contents.
- Include launcher 1.0.6 with fixes for preset import, seeds, concurrent edits, optional ATB activation and launch folder selection.
- Recover interrupted installations, verify launcher contents and check the packaged public ATB against the launcher's reviewed hash. [Release notes](docs/release-0.14.5.md)

## Launcher 1.0.5 — 2026-11-15

- Include launcher 1.0.5 in package revision **0.14.4-r2**, both full setup and manual mod ZIP. Gameplay DLLs, Fix, optional ATB and defaults retain their original 0.14.4 bytes. Nexus plugins-only distribution stays separate.
- Simplify S1 treasure settings to one new-campaign checkbox while retaining the separate saved-policy controls and backups.
- Retire the legacy global chest pause on explicit Apply, preserving each existing save's chest policy. Hide that managed switch in Advanced.
- Keep loading/no-op behavior free of config writes and preserve missing S1 configs on S2-only edits. See [launcher notes](docs/launcher-1.0.5.md).

Available as a separate optional download on the 0.14.4 release. The existing plugin ZIPs and installers retain their original bytes and bundled launcher 1.0.4.

## [0.14.4](https://github.com/suikodenrand/suikodenrando/releases/tag/v0.14.4) — 2026-11-15

- Balance replacements' first joining inventory and equipment in both Suikoden I and II. The default uses the original story role's starting inventory, including consumables such as Medicine, in Shuffle, chosen-companion and seeded random fixed-roster modes.
- Replace incompatible armor and accessories with comparable equipment the replacement can wear. Characters retain their own weapon identities and rune setup; joining weapon-level choices remain separate.
- Offer the previous replacement starting-inventory behavior as an option. Save the policy and first-join history with the campaign so later recruitment, save/load and rejoining preserve acquired items and player equipment changes. Pre-feature campaigns retain legacy behavior without an inventory retrofit.
- Add per-game inventory/equipment choices to launcher 1.0.4, including presets, the advanced editor and configuration backups. The new option requires runtime 0.14.4.

The release provides full setup and mod-only manual downloads with launcher 1.0.4. ATB remains an optional, separate S1 component; the manual package requires BepInEx separately. See [release notes](docs/release-0.14.4.md) for installation requirements, validation and known limits.

A native S1 run verified Meg joining for Viktor with Fur Robe, Boots and weapon level 1, and the resulting save stored that loadout and completed initialization. Reloading the new save was not observed. Wider native coverage remains pending; the managed/static checks are listed in [BUILDING.md](BUILDING.md).

## 0.14.3

Published source and downloads remain available at [v0.14.3](https://github.com/suikodenrand/suikodenrando/releases/tag/v0.14.3). That release added Riou-based first-join weapon levels to S2, matching S1's protagonist-based default, with a seeded -3 to +3 variance. It includes the combined S1/S2 host, Windows launcher, shuffled Unites and dialogue features, plus optional experimental S1 chest shuffle and ATB. See the version tag for its exact source and release contents.
