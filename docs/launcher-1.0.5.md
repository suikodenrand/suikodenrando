# Launcher 1.0.5

This launcher update simplifies Suikoden I treasure settings and remains compatible with randomizer 0.14.4. It does not change plugin binaries, chest mappings or the saved-policy format.

- Replace the two Treasure-page switches with **Enable chest shuffle for new campaigns**.
- Keep the saved campaign list, refresh and **Reset selected save's chest settings** controls, including their backups and running-game checks.
- Explain the saved-policy workflow: choose checked for shuffled rewards or unchecked for original rewards, apply settings, select the saved snapshot and reset it. The next load adopts the selected default; acquired items and opened chest flags stay intact.
- Remove the legacy chest master switch from Advanced. On explicit Apply or Save & launch, an existing global pause (`Treasure/Enabled=false`) is changed to `true`, so an existing shuffled campaign can use its stored policy even when the new-campaign checkbox is unchecked. This resumes previously paused saved shuffles.
- Opening the launcher does not migrate or write settings. Missing S1 config files remain absent on no-op and S2-only applies. Presets containing the old pause flag are handled when applied. Manual CFG editing can still use the runtime's old pause flag; applying settings through this launcher clears it.

## Install the optional launcher

Close the launcher. Back up its entire existing folder and preferences/presets. Extract the complete `StarsOfDestiny Launcher` folder beside the game EXE, replacing the old launcher as a complete set. Keep its bundled runtime and Microsoft notices together. No separate .NET runtime, SDK or compiler is needed by players. Copying the launcher does not change game settings; review options before applying them.

Install the mod plugins and external BepInEx separately. The launcher-only ZIP supplies neither. Existing saves keep their chest policies until explicitly reset through the saved-campaign controls. The new-campaign checkbox alone does not change them. S2 chest randomization remains unavailable.

## Build and package

Use the SDK pinned in `global.json`. From the repository root, set `$atbDll` to the reviewed `SuikodenRandomizer.S1BattlePreview.dll` supplied with the target 0.14.4 installation, and compute its hash without rebuilding it:

```powershell
$atbSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $atbDll).Hash.ToLowerInvariant()
dotnet publish src/StarsOfDestiny.Launcher/StarsOfDestiny.Launcher.csproj -c Release -r win-x64 --self-contained true -p:RuntimeFrameworkVersion=10.0.14 "-p:ReviewedAtbSha256=$atbSha256" --disable-build-servers -m:1 -p:BuildInParallel=false -p:RestoreDisableParallel=true -p:NuGetAudit=false -o artifacts/launcher-1.0.5/publish
python packaging/release/package_launcher.py --launcher-dir artifacts/launcher-1.0.5/publish --atb-dll $atbDll --source-commit <reviewed-source-commit> --output artifacts/launcher-1.0.5/package
```

The packager records the source commit, reviewed ATB hash and actual archive checksum. Keep the complete self-contained runtime and notices. Do not overwrite historical release downloads or retag their source for this launcher update. Public release, website publication and live installation are separate actions.

## Verification

Regression checks cover the one-checkbox form, preserved saved-policy controls, hidden Advanced gate, explicit legacy-pause migration, exact configuration backups, unchanged native saves/sidecars, presets and missing-file behavior. The change requires no battle or recruitment gameplay changes; no new native gameplay coverage is claimed.
