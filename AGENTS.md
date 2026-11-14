# Source and releases

Keep changes to Stars of Destiny in this repository, including runtime,
launcher, optional components, tests, packaging and build documentation.
For each release, commit the corresponding source and release notes, run
the relevant checks, and create the version tag from that reviewed commit.
Publish setup/manual assets and their actual checksums against the same tag.
Do not prepare an undocumented binary-only release from uncommitted source.

The project's own code is MIT licensed. Preserve third-party copyright,
license notices and any corresponding-source requirements separately.
Do not commit game binaries, generated game interop assemblies, personal
saves/config sidecars, credentials, local build caches or release binaries.

Use BUILDING.md and packaging/Build-Release.ps1 for release builds. Keep
custom ATB binaries paired with their reviewed hash in the matching launcher.
Keep manual BepInEx requirements explicit; setup and manual distributions
have different dependency contents. State the limits of native playtesting.

Publishing a GitHub release does not by itself authorize a website update.
