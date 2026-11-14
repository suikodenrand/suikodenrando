# Stars of Destiny 0.14.3

Suikoden II now offers the same protagonist-based joining-weapon policy as Suikoden I: eligible replacement characters' first weapon levels follow Riou's current weapon level with seeded -3 to +3 variance, including a possible exact match. This is the fresh S2 default. Saved campaigns keep their existing policy and earned sharpening.

This release also publishes the source and build instructions for the combined randomizer, launcher and optional S1 ATB preview. The project's own code is licensed under MIT; dependencies retain their original licenses.

## Downloads

- **`Stars-of-Destiny-0.14.3-Setup.exe`** — full Windows setup with launcher, BepInEx, Suikoden Fix and optional S1 ATB. Setup preserves existing configs and saves, validates its supported game/package and records transaction recovery copies.
- **`Stars-of-Destiny-0.14.3-Mod-Only-Windows.zip`** — manual mod package with launcher, Suikoden Fix and dormant optional S1 ATB. Install the tested **BepInEx 6 Unity IL2CPP Windows x64 build 697 (`5362580`) separately first**. Read its `START HERE.txt` and `REQUIREMENTS.txt`; existing users must follow the manual upgrade instructions to preserve configs and campaign sidecars.
- **`SHA256SUMS.txt`** — checksums for the release assets.
- **`LICENSE.txt`** — MIT license for the project's own code. Third-party component licenses remain in their respective packages.

The manual ZIP excludes the BepInEx loader, its root `dotnet` runtime, `winhttp.dll`, Doorstop files and loader source bundles. Its `BepInEx` folder contains the mod's plugins and settings. The full setup includes the upstream loader distribution. The two reported loader-file detections match unmodified upstream files; this release does not claim an antivirus false-positive ruling or automatic Nexus approval.

## Changes since the S1-only 0.8.x releases

- Added Suikoden II character randomization through one combined S1/S2 host, one shared F6 interface, and separate game configurations and saved campaign mappings.
- Added a self-contained Windows launcher for seeds, modes, companions, starts, joining weapons, presets, config backups and optional features.
- Added supported English dialogue-name replacement for both games, preserving native custom-name tokens and event commands.
- Added shuffled-character support for native Unite attacks in both games, including characters filling unrelated story roles.
- Added experimental S1 seeded chest rewards, disabled by default, with protected progression/recruitment rewards retained.
- Added the optional experimental S1 ATB preview: native menus and targeting, SPD gauges, DEX-influenced charge, ready-character commands, queued actions, retargeting and Auto.
- Changed the F6 panel to start hidden and display text without its black background when opened.
- Added Riou-based first-join weapon variance for S2, matching the Tir-based option available in S1. Match-original-role and weapon-level-1 policies remain available.
- Prepared separate full-setup and mod-only manual distributions, with instructions, file manifests and checksums.

## Defaults and compatibility

Fresh defaults are Shuffle, normal opening, English dialogue replacement on, shuffled Unites on, protagonist shuffle off, chest shuffle off and ATB off. F6 starts Hidden. S1 joining weapons use Tir's current level +/-3; S2 uses Riou's current level +/-3. Existing campaign policies are retained.

ATB is an S1-only optional preview and must not run with shuffled S1 Unites. In the manual ZIP it is dormant outside the active plugins tree; activation through the launcher does not enable its config by itself. Suikoden II keeps normal turn-based battles. Nuzlocke, S2 chest shuffle/ATB, shared inventory and extra rune slots are not part of this release.

## Verification and limits

The release payloads retain the previously tested 0.14.3 runtime and 1.0.3 launcher files. Managed runtime and launcher regressions, compiled-host contracts, full-installer fixture checks, manual two-step dependency installation, configuration-preserving upgrades, undo and archive checksum/extraction checks have been exercised. Package verification files describe the exact checks and scope for each asset.

Representative native game scenarios have passed, but every campaign, Unite recipe and scripted encounter has not been played through. S1 chest shuffle and ATB remain experimental. Riou-based joining weapons have managed regression coverage; native first-join playtesting of the new policy remains pending. Local Defender scans reported no threats; GitHub, Nexus and individual antivirus vendors make their own security decisions.

See [BUILDING.md](../BUILDING.md) for source-build instructions and [THIRD_PARTY_NOTICES.md](../THIRD_PARTY_NOTICES.md) for dependency licensing.
