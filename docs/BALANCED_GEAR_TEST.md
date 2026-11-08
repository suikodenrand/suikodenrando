# Starting gear test 0.8.12

Balanced starting gear gives replacement heroes equipment suited to their place in the story. Their weapon level follows the original story slot, and their ordinary armor is chosen from compatible equipment within a similar strength and value range. Special equipment is kept. Character levels, base stats and runes stay as selected by the character randomizer.

Fixed Roster and Random Roster balance their five companions once against Tir's starting equipment. Nuzlocke balances a story slot when it first receives a fighter; later replacements inherit that slot's equipment and upgrades.

## Install

Close the game and back up your saves and BepInEx/config. Extract all files, keep the config beside StarsOfDestiny-Setup.exe, run Setup and select the game folder. BepInEx 6 IL2CPP must already be installed and initialized once. The setup backs up previous plugin files and config under SRR-Backups. It is unsigned.

The included configuration selects Nuzlocke with Balanced gear. Start a New Game to use this setting. Existing campaigns keep their equipment and upgrades. Existing Nuzlocke saves from 0.8.9 through 0.8.11 remain compatible.

## Settings

Edit agilen.suikoden.recruitment_randomizer.cfg beside Setup before installing:

```ini
[Shuffle]
Seed = balanced-gear-test
Mode = Nuzlocke
IncludeTir = false

[Start]
Point = Opening

[Balance]
StartingGear = Balanced
```

Set StartingGear to Original to keep heroes' usual starting equipment. The choice applies to New Game. Set Point to BeforeViktor to start at the later checkpoint.

| Mode | Behavior |
| --- | --- |
| Shuffle | Shuffle the eligible combat cast. |
| FixedRandom | Tir and five randomly selected companions. |
| FixedChosen | Tir and five chosen companions. |
| Nuzlocke | Shuffle with permanent companion retirement after confirmed KOs. |

For FixedChosen, add five different eligible hero IDs under [Shuffle]. This example selects Cleo, Gremio, Pahn, Viktor and Valeria:

```ini
Mode = FixedChosen
Companions = 1,2,6,35,36
```

Tir is protected in Nuzlocke and both fixed roster modes. IncludeTir applies to Shuffle.

## Check the result

Start a new campaign and inspect the first party's weapon levels and armor. Check that the equipment is compatible, then fight a battle. Save, quit and reload; equipment and upgrades should remain. In Nuzlocke, check that a reserve inherits the fallen slot's current equipment after a KO and that a full-party defeat retries the encounter.

If the game still responds during a problem, press F6 to export a report. After a crash, launch the game once more and send the newest stars-crash-report ZIP from BepInEx/StarsOfDestiny-Diagnostics, along with what happened.
