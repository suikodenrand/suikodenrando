# Release packaging

Package revision 0.14.6-r2 pairs launcher 1.0.8 with the reviewed 0.14.6 gameplay binaries.
The release builds battle preview 0.15.2 and pairs its hash with the launcher. Use the same release outputs when packaging the optional CTB 0.5.2 download.
The release tag and published checksums identify the source and downloads.

`build.py` assembles packages from already-built project binaries. It never
installs into or launches the game. Start with the repository's
[BUILDING.md](../../BUILDING.md) and `packaging/Build-Release.ps1`.

Use the original 0.14.6 plugins-only download for r2. The standalone 1.0.8 launcher and 0.5.2-r2 CTB downloads use `package_launcher.py` and `package_ctb.py`. The [release build instructions](../../BUILDING.md#produce-the-public-downloads) include the commands and component source tags.

Requirements are Python 3.12 or later, the .NET SDK pinned in global.json, the four combined runtime DLLs, the
optional ATB DLL, and a complete self-contained `win-x64` launcher publish.
NSIS 3.11 is additionally required to compile the full setup.

```powershell
python packaging/release/build.py --mode all `
  --runtime-dir artifacts/0.14.6/runtime `
  --atb-dll artifacts/0.14.6/atb/SuikodenRandomizer.S1BattlePreview.dll `
  --launcher-dir artifacts/0.14.6/launcher `
  --reviewed-atb-sha256 <reviewed-ATB-SHA256-used-to-publish-that-launcher> `
  --dependencies artifacts/dependencies `
  --download-dependencies `
  --output artifacts/0.14.6-r2/packages `
  --makensis C:/Tools/NSIS/makensis.exe
```

Use `--mode manual` to create only the mod-only manual ZIP. `--mode all`
creates that ZIP and the full setup. `--stage-only` builds both staging trees
and the manual ZIP without invoking NSIS. Use a fresh output directory:
the recipe refuses to replace an existing staging tree.

`--runtime-dir` must contain exactly the required four file names (other
files in that directory are not copied):

- `SuikodenRandomizer.dll`
- `SuikodenRandomizer.Core.dll`
- `SuikodenRandomizer.Suikoden2.Core.dll`
- `SuikodenRandomizer.Shared.dll`

For a custom rebuild, pass the same reviewed ATB SHA-256 to the launcher's
`ReviewedAtbSha256` MSBuild property and this script's
`--reviewed-atb-sha256` argument. Both packagers inspect the published launcher
assembly and require its compiled hash to match the ATB bytes. Use `--dotnet`
to select the SDK executable. Omitting the hash uses the original reviewed
release hash for the combined package.
Do not disable that check to accept a different DLL.

## Dependency cache

`dependencies.json` lists the exact filenames, official URLs and SHA-256
hashes. Supply these archives in `--dependencies`, or explicitly opt into
downloading missing archives with `--download-dependencies`. Mismatched
existing or downloaded files fail; they are not silently accepted.

The manual package needs only the pinned Suikoden Fix archive. It includes
the project plugins, defaults, complete launcher/runtime and notices, with
ATB dormant outside the plugins tree. Users must install BepInEx separately.
No BepInEx core, root `dotnet`, `winhttp.dll` or Doorstop files are bundled.

The full setup additionally needs the BepInEx archive and three matching
upstream source archives. The recipe reconstructs and hash-checks all 478
source files against `licenses/setup/source-manifest.json`, and places the
extracted sources alongside their notices in the installed support folder.
Source ZIPs are never nested inside the distributed packages.

The `.NET 10` runtime notices must be present in the launcher publish folder;
the project's publish target supplies them. The loader's `.NET 6` runtime
and its notices belong to the upstream BepInEx distribution, not the launcher.

## Outputs and verification

Native S1 testing checked Meg replacing Viktor at first join and a subsequent
save; reload was not observed in that test. Broader S1/S2 join and rejoin
coverage remains limited. Managed tests cover saved-policy persistence.

Fresh per-game configs use `Shuffle/JoiningEquipment=MatchRole`; both package
manifests record that default. This is the original role's full starting
inventory, with comparable wearable gear where needed. Legacy saved campaigns
retain Donor behavior without an inventory retrofit. Weapon-level policies
remain separate, and optional ATB stays disabled. Run the static metadata
checks with `python -m unittest discover -s packaging/release/tests -v`.

- `Stars-of-Destiny-0.14.6-r2-Mod-Only-Windows.zip` is an ordinary Deflate ZIP
  with game-folder-relative paths. It has no setup executable or product
  install/restore scripts.
- `Stars-of-Destiny-0.14.6-r2-Setup.exe` includes BepInEx and uses the unchanged
  transactional install/restore implementation. ATB remains an optional
  setup component and is installed disabled when selected.
- SHA-256 sidecars and `build-verification.json` record the local outputs.
  The manual ZIP also contains a file list and internal checksum list.

Input paths and bytes are verified; links, archive traversal, nested
archives, duplicate names, game assemblies, logs and personalized launcher
settings are rejected. NSIS receives a hash-verified short temporary copy
because its compiler has legacy source-path length limits.

These checks do not imply a native gameplay test, an automatic install test
or acceptance by Nexus/antivirus vendors. Rebuilt assemblies and archives
can differ from published assets because source paths, compiler metadata and
ZIP timestamps affect hashes. Preserve the SHA-256 sidecars for the exact
assets being reviewed.

The repository's MIT license applies to project-owned code. Files under
`licenses/` and downloaded components retain their original terms.
