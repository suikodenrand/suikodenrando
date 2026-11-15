# Changelog

## [0.14.4](https://github.com/suikodenrand/suikodenrando/releases/tag/v0.14.4) — 2026-11-15

- Balance replacements' first joining inventory and equipment in both Suikoden I and II. The default uses the original story role's starting inventory, including consumables such as Medicine, in Shuffle, chosen-companion and seeded random fixed-roster modes.
- Replace incompatible armor and accessories with comparable equipment the replacement can wear. Characters retain their own weapon identities and rune setup; joining weapon-level choices remain separate.
- Offer the previous replacement starting-inventory behavior as an option. Save the policy and first-join history with the campaign so later recruitment, save/load and rejoining preserve acquired items and player equipment changes. Pre-feature campaigns retain legacy behavior without an inventory retrofit.
- Add per-game inventory/equipment choices to launcher 1.0.4, including presets, the advanced editor and configuration backups. The new option requires runtime 0.14.4.

The release provides full setup and mod-only manual downloads with launcher 1.0.4. ATB remains an optional, separate S1 component; the manual package requires BepInEx separately. See [release notes](docs/release-0.14.4.md) for installation requirements, validation and known limits.

A native S1 run verified Meg joining for Viktor with Fur Robe, Boots and weapon level 1, and the resulting save stored that loadout and completed initialization. Reloading the new save was not observed. Wider native coverage remains pending; the managed/static checks are listed in [BUILDING.md](BUILDING.md).

## 0.14.3

Published source and downloads remain available at [v0.14.3](https://github.com/suikodenrand/suikodenrando/releases/tag/v0.14.3). That release added Riou-based first-join weapon levels to S2, matching S1's protagonist-based default, with a seeded -3 to +3 variance. It includes the combined S1/S2 host, Windows launcher, shuffled Unites and dialogue features, plus optional experimental S1 chest shuffle and ATB. See the version tag for its exact source and release contents.
