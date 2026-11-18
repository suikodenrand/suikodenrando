# Stars of Destiny 0.14.5

This Windows release updates the combined Suikoden I and II randomizer and includes launcher 1.0.6.

## Campaigns

- Suikoden I retries incomplete party formations while the game is updating them. Fixed rosters reconnect to replacement character records before battle. Joining weapons and equipment can update while S1 owns the game and a battle or overlay scene is active.
- Clean native S1 saves can be loaded and saved with the randomizer installed. A surviving treasure, joining-weapon or joining-equipment file without its matching campaign binding now blocks saving and identifies the missing binding. A rejected randomized save also suppresses the editable mirror write from the supported Suikoden Fix 1.5.2 build. [Save compatibility](UNBOUND-SAVE-COMPATIBILITY.md)
- Suikoden II applies starting equipment independently for each recruit. A full shared bag delays only recruits who need the space. Reloading character graphics picks up replacement assets and reuses existing graphics when the underlying assets match.
- Suikoden II preserves the original campaign binding when two sessions produce the same native save contents with different recruitment settings. An identical repeat save succeeds.
- Character and treasure shuffles retain their existing seed mappings.

## Launcher and recovery

Launcher 1.0.6 validates presets against the settings they will change, preserves pending edits when activating ATB, and checks changes made by another launcher window before applying settings. **Save & launch game** starts the executable in the selected installation. The release builder verifies the published launcher against the included optional S1 ATB 0.10.1 DLL. [Launcher notes](launcher-1.0.6.md)

Restore uses the transaction record and file hashes to recover an interrupted install. After a game update, it can remove matching installed files and keeps incompatible older loader or plugin binaries in the backup. For an interrupted restore, use the transaction path reported by the recovery tool.

## Downloads and upgrades

| Download | Use |
| --- | --- |
| Full Windows setup | Guided install with the pinned BepInEx loader, launcher, plugins and transaction recovery |
| Mod-Only Windows ZIP | Manual game-folder install including the launcher. Install BepInEx separately |
| Plugins-Only Nexus ZIP | Manual plugins, configuration and instructions. Install BepInEx separately |
| Launcher 1.0.6 ZIP | Standalone settings editor for an existing mod installation |

Close the game and launcher before upgrading. Back up `Save` and `BepInEx/config` together. For a manual installation use BepInEx 6 Unity IL2CPP Windows x64 build 697 (`5362580`) and follow the instructions in the archive. Compare downloaded files with `SHA256SUMS.txt` on the release. [Downloads](https://github.com/suikodenrand/suikodenrando/releases/tag/v0.14.5)

Verification used managed campaign, launcher and installer fixtures, generated game interop assemblies, and package inspection. This release has no new in-game playtest.
