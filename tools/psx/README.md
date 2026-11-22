# Stars of Destiny for Suikoden I and II on PlayStation

**Very early alpha (0.1.0-alpha.1). Expect bugs, especially in Suikoden II.** Some scenes can mix the original and shuffled cast. In one reported seed, Gengen's first meeting showed Flik's portrait alongside Gengen's original sprite.

## Generate a seed

1. Extract the ZIP and open `Seed-Randomizer.cmd` on Windows with Python 3.11 or newer and Tk installed. You can also run `python -B seed.py` from the directory containing the launcher.
2. Select the game and browse to your own original U.S. disc image. Both games use a single MODE2/2352 data track. The generator accepts a CUE or a CloneCD CCD.
3. Enter seed text or press **New Seed**. The available roster settings depend on the game. Enable **Save a spoiler log** to see who takes each story role.
4. Press **Generate Seed**, then open the generated CUE in your emulator and choose **New Game**. A fresh game applies the seed's starting stats, inventory, and weapons.

Keep the generated BIN beside its CUE. The seed and settings reproduce the same disc. `seed.json` records the settings and the generated BIN's SHA-256. Each seed gets its own filename so emulators that name memory cards after the disc can identify it separately.

| Supported original | Disc ID | Track SHA-1 |
| --- | --- | --- |
| Suikoden I U.S. v1.1 | `SLUS-00292` | `04f7de4839b8f857b184ceafb52a51c77455cea9` |
| Suikoden II U.S. | `SLUS-00958` | `5a1c6314fb79c2cdab90ccae72f1e7e6fcb89cc4` |

## Suikoden II

The guarded shuffle replaces the 78 combat Stars. Riou and all Jowy variants keep their original story roles and appearances. McDohl, support characters, and other special variants retain their story slots. Abizboah, Feather, Sigfried, and Rulodia share a separate pool so each two-slot role keeps its formation cost.

The seed changes party names, fighter packages, weapons, starting growth and equipment, and character name mentions in verified English dialogue tables. It replaces walking sprites and dialogue portraits in both layouts used by field maps, including maps with a separate portrait bank. Each replacement portrait uses its original palette in dialogue and menus. The story sets each role's recruitment level and weapon sharpening progression.

The revised generator keeps the same assignments for the same seed text and gives the patched disc a new filename. Some custom poses, story text outside the detected tables, and war graphics can still show the original cast. The generator also keeps visuals that exceed the space available in their maps. Unite attacks involving shuffled roles are disabled to avoid running animation scripts tied to specific fighters. Buddy Attack and Double Leader Attack keep their original participants.

A Beetle PSX playtest of `Agilen-PSX-II-01` reached the river scene from New Game. Jowy named Lorelai in Nanami's role, and Kahn and Kasumi showed their own walking sprites and portraits in Viktor's and Flik's roles. The revised disc won a regular encounter replayed from the playtest's world-map checkpoint. Karen and Gadget both attacked, received XP, and returned to the world map. Their battle-result portraits matched the colors on the original disc. Later story battles, duels, and war battles still need playtesting.

```powershell
python -B seed.py "path/to/Suikoden II.ccd" "path/to/new-seed-folder" --seed Agilen-PSX-II-01 --spoiler
```

## Suikoden I guarded combat shuffle

Select **Guarded combat shuffle (75 Stars)** to use the original randomizer's combat roster. Tir, Odessa, Ted, and the support stars occupy their original story roles. The PS1 tool generates its own seed assignments.

```powershell
python -B seed.py "path/to/Suikoden.cue" "path/to/new-guarded-seed-folder" --guarded --seed Agilen-PSX-Guarded-01 --spoiler
```

For a full shuffle, enable **Shuffle the named hero** and **Shuffle support stars**. The equivalent command from the launcher directory is

```powershell
python -B seed.py "path/to/Suikoden.cue" "path/to/new-seed-folder" --seed Agilen-PSX-01 --shuffle-hero --spoiler
```

## Suikoden I changes

The 78 characters with battle packages, including Ted and Odessa, can take fighter roles. The 32 support stars can take support roles. Story IDs drive recruitment, save data, support services, and scripted events. The named hero setting controls who can take the player role. The game uses the name you enter at New Game for that slot.

The seed updates character names, reviewed field sprites and portraits, fighter packages, starting equipment and growth, and the 32 regular Unite recipes. Weapons start at the sharpening level belonging to the story slot. The Soul Eater follows the story IDs used for its transfer.

The full-roster seed `Agilen-PSX-01` was checked through New Game, the palace opening, and a regular field battle started from a test memory card. Two builds of its BIN produced the same SHA-256.

Some custom scenes have separate animations, dialogue, or portraits. Those appearances can still show the original character. Late-game story branches, duels, and the full set of Unite animations need a campaign playtest.

## Package a reviewed version

From the repository root, run the regression checks and package the reviewed commit.

```powershell
python -B -m unittest discover -s tools/psx -v
python -B tools/psx/package_release.py HEAD path/to/release-folder
```

The package contains the standalone Python tool, launcher, setup guide, and MIT license. Its `SOURCE.txt` identifies the source commit. Publish the ZIP alongside the generated `SHA256SUMS.txt`. Changing `VERSION` on the `playstation` branch runs the checks and publishes the tagged GitHub prerelease.

## Original two-character opening pilot

This build puts Tai Ho in Gremio's role and Yam Koo in Pahn's role. It covers
the opening maps, party and battle names, portraits, English dialogue
mentions, native fighter and weapon data, and Fisherman Attack. Both
weapons start at level 1, matching the opening roles. The fighters use their
native starting inventory.

The two assignments are fixed for this playtest. The story and save data use
the original role IDs. Later recruitment and scenes are outside the playtest's
coverage.

### Build and run

With Python 3.11 or newer, run:

```powershell
python -B pilot.py "path/to/Suikoden.cue" "path/to/opening-playtest"
```

The input is the single-track MODE2/2352 `SLUS-00292` image reviewed for this
build. The BIN must have SHA-1 `04f7de4839b8f857b184ceafb52a51c77455cea9`.
The tool creates a new directory with a playable BIN and CUE. The BIN keeps
the original sector layout, and each edited sector has its integrity
fields rebuilt.

Open the generated CUE in a PSX emulator and select **New Game** on a
separate test memory card. After Tai Ho and Yam Koo join, use **Formation**
to move them to the back row. In a regular party battle, select **Unite**
with either fighter and choose **Fisherman Attack**.

### Pilot playtest results

Fisherman Attack defeated a BonBon and returned control to the party in a
standard battle. Both weapons sharpened to level 2 at Lenankamp and retained
their levels after a console reset and a load from the in-game memory card.
