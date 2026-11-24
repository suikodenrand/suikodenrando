# Contributing and releasing

Keep changes to the game's two engines isolated where their native behavior differs. Shared UI, settings and serialization code should retain separate S1 and S2 campaign data. Test that old saves and configuration sidecars keep their existing policy before changing defaults.

Contributions to the project's own code are made under the repository's [MIT License](LICENSE). Preserve third-party copyright and license notices. Do not add proprietary game assemblies, assets, saves, credentials or generated interop files to a contribution.

## Before a pull request

1. Follow [BUILDING.md](BUILDING.md) to use the pinned SDK and required local game references.
2. Run `pwsh -File eng/Check-Quality.ps1`. For changes that are already committed, pass the comparison commit with `-BaseRef`. Native-facing changes also need a clearly described in-game check when available.
3. Document behavior, defaults and compatibility changes. State which checks passed and which native scenarios remain untested.
4. Keep generated binaries, caches, test logs and local machine settings out of the source commit. Supply sanitized diagnostic excerpts when they help review.

## Quality and security checks

`.editorconfig` defines repository C# spacing and targeted correctness rules. The SDK's .NET analyzers run during builds, with warnings treated as errors. Keep fixes focused on the files you edit. To format one file, run `dotnet format whitespace . --folder --include path/to/file.cs` from the repository root. Document the reason for any analyzer exception at the narrowest applicable scope.

GitHub Actions runs the **Managed quality** job on pull requests and pushes to `main`. Once the workflow has reported a successful check on GitHub, the repository owner can require **Managed quality** in the `main` branch ruleset. The separate CodeQL workflow scans C# source for security findings. Dependabot watches NuGet manifests and GitHub Actions. The release dependency manifest in `packaging/release/dependencies.json` uses its own hash pins and needs separate review when an upstream binary changes.

## Release policy

Future releases should publish their corresponding source and build instructions with the release, rather than adding source after distributing binaries.

1. Commit the complete source for the runtime, launcher, optional components and packaging scripts before creating a version tag.
2. Update versions, install/upgrade instructions, release notes, dependency pins and applicable notices. Keep the project's MIT scope separate from dependency licenses.
3. Build from a clean checkout with the documented commands. Run relevant managed tests and package installation/upgrade/undo checks. Record any limits of native playtesting.
4. Inspect the source tree and Git history being made public for credentials, personal settings, saves, proprietary game files and build caches.
5. Produce the setup and manual artifacts from the reviewed source. Keep BepInEx external in the mod-only manual ZIP; preserve its complete notices and corresponding source when distributing it in a full setup.
6. Generate SHA-256 checksums, verify archive extraction and file paths, and check that the shipped binaries correspond to the reviewed source and dependency versions. Record the build environment; do not promise identical hashes on other machines unless that has been demonstrated.
7. Tag the reviewed source as `v<version>` and attach the release assets, checksums, installation requirements, changes and known limitations to that tag.
8. Verify the published source links and downloadable assets. A local antivirus result does not replace a host's security review or a vendor's analysis of a reported detection.

Do not retarget an existing release tag or silently replace an already published asset. Correct a release through a documented new revision or version so users can identify the source and checksums that match their download.
