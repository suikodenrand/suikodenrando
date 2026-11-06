# Suikoden I HD Remaster Randomizer 0.8.5 Fixed Roster Test

The fixed roster has Tir and five distinct companions. Choose five hero IDs or roll five from a seed. As story roles join and leave the active party, available fighters move into those roles. A returning fighter carries their saved progress and equipment. Recruit normally for story progression.

The Windows test package builds the plugin on the player's PC against the installed game and BepInEx references. See [the installation and playtest guide](packaging/windows/README.txt) and [companion IDs](packaging/windows/docs/COMPANION_IDS.txt).

Use a separate New Game for the test. The prototype targets ordinary battles and party changes. Check the opening with Ted and the Ted-only Queen Ant sequence, a departure and return, and a full quit and reload. Duels, army battles, quest-specific inventory and rune changes, and the full campaign need native playtesting.

## Source and packaging

The parent commit is the published 0.8.4 source snapshot. The fixed roster profile, active assignments, and fighter records are stored in a separate versioned save binding. Keep the game's saves and its BepInEx configuration together.

Run the standalone managed projects under `tests/` using the SDK in `global.json`. The native metadata contract check also takes the path to the installed `BepInEx/interop/GSD1.dll`.

To reproduce the Windows archive, run the following from this checkout. The script generates the installer payload and its SHA-256 from this source.

```sh
python3 packaging/package.py --source . --staging packaging/windows --output SuikodenRandomizer-0.8.5-FixedRoster-Test-AutoInstall.zip
```
