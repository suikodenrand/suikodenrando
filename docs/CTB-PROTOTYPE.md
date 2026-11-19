# Suikoden I CTB preview 0.5.2

CTB gives each living combatant an individual turn based on SPD. While you choose a command, the battle clock pauses. The bottom-center strip shows up to eight upcoming actions. Highlight a command and the cyan **Next** marker shows when that hero is projected to act again.

The preview is optional in Stars of Destiny **0.14.6**. It uses battle plugin **0.15.2**, launcher **1.0.8** in the revised Windows downloads, and tuning editor **0.5.1**. Fresh installations leave CTB off.

## Enable CTB

1. Close the game. Open the **Stars of Destiny Launcher** for your game installation. The 0.14.6 setup and manual mod package include the preview and the tuning editor. If using the separate CTB download, follow the add-on instructions below first.
2. In **Suikoden I → Battle & ATB**, turn off shuffled Unites. If you enabled the legacy Talisman preview under **Advanced**, turn it off there. If the component is dormant, select **Activate ATB / CTB preview**.
3. Turn on **Enable battle preview component** and **Enable CTB (overrides ATB)**. Choose whether to enable **Break** and **Reserve Rally**, then apply your settings and start the game.
4. To switch back to ordinary combat, close the game and turn off the battle preview component and CTB in the launcher. To use ATB, disable CTB and enable the ATB options you want. Applying battle settings affects your next game launch.

### Installing the separate CTB download

The CTB ZIP adds the preview to an existing Stars of Destiny 0.14.6 installation for the Windows Steam remaster. It includes the matched launcher, tuning editor, desktop runtime, and battle plugin. [Main downloads and installation instructions](https://github.com/suikodenrand/suikodenrando/releases/tag/v0.14.6)

1. Close the game and launcher. Open the installation folder containing `Suikoden I and II HD Remaster.exe`. Keep a backup of your current launcher and any existing battle-preview DLL outside the game folder.
2. Extract **BepInEx** and **StarsOfDestiny Launcher** from the ZIP into that game folder. When upgrading an older launcher, replace its folder as a complete set. You can restore your own `launcher-preferences.json` afterwards.
3. The supplied DLL belongs directly under `BepInEx` until activation. If an older battle preview is already active anywhere under `BepInEx/plugins`, move that old DLL to your backup first. Open the matching launcher and activate the new preview as described above.

## Commands and turn timing

- Every hero opens the full command menu. If allies act back to back, the next hero can choose a command during the first animation. CTB queues one action ahead, validates the target and resources again, and reopens selection if the choice expires. Consecutive enemies can follow one another as soon as their native animations finish.
- **Wait** takes the place of Auto. Run uses the native escape check. If escape fails, dismiss the message to continue in CTB order. Bribe hands the rest of that encounter to native combat.
- The local game's portraits identify heroes. Enemy cards use an available complete native sprite or a large colored shape. Its color, shape, and number match the enemy list and the emblem above the enemy's sprite. Previously generated local thumbnail sets remain supported.
- When **Shared battle items** is enabled in the S1 settings, each living active fighter can use an ally's item. CTB reserves the selected stack and deducts from the carrier once the command succeeds.

| Action | Time until next turn, relative to Attack |
| --- | ---: |
| Wait | 50% |
| Defend | 75% |
| Item | 85% |
| Attack or failed Run | 100% |
| Rune I / II / III / IV | 115% / 135% / 155% / 175% |

A special Rune of unknown rank uses 135%. Ordinary or unclassified enemy actions use 100%. Damage, healing, KO, Break, and support can change the forecast once actions resolve.

## Break and Rally

**Break** builds on confirmed HP damage and the attacking hero’s DEX. At 100 points it delays the enemy’s next turn. The default delay is 25% of a solo boss’s base interval and 50% for ordinary or grouped enemies. The encounter’s opening formation determines its category. The gauge resets when the enemy acts.

**Reserve Rally** lets up to two eligible recruited reserves alternate after every four qualifying hero actions. Each Rally advances the living eligible ally whose next turn is furthest away by up to 15% of that ally's normal interval. The launcher accepts specific reserve role IDs or automatic selection.

## Tuning SPD

Choose **Open CTB tuning editor** in the launcher. Settings saved in the editor apply to each newly opened battle. Star overrides use the displayed fighter’s identity in shuffled campaigns. Clear an override to return to the game’s SPD, including natural growth and equipment. The editor shows the latest recorded opening SPD for a Star who has appeared in battle.

- **Fixed speeds.** Set a combat Star to 1–255 SPD or an enemy to 1–400 SPD. The solo boss SPD bonus adds a percentage to native speed. For example, +131% multiplies 65 SPD by 2.31 and rounds to 150. Individual enemy overrides take priority over this fixed bonus.
- **Adaptive solo bosses.** Target roughly one boss Attack per three normal Attacks across the party. The blend combines the living party’s opening total SPD with the same number of average combat Stars at that fight’s recommended level. The party contribution is capped at the fighters’ natural average SPD four levels above the recommendation, with equipment bonuses. Party weight and one-time fight variation are configurable. At 75% party weight, the recommended-level reference contributes 25%. For supported fights the blend takes priority over fixed enemy SPD. Break and action choice affect actual turn counts.
- **Supported solo bosses.** Clay Doll (level 6), Zombie Dragon (17), Gigantes (24), Dragon (26), castle Neclord (47), Crystal Core (51), Venus Shell (55), Sonya Shulen (56), and Ain Gide (57).
- **Normal encounters.** Optional scaling compares the party’s average SPD with the average of 76 combat Stars at the formation’s median enemy level. Each enemy gains or loses half the percentage difference, bounded at −10% and +25%. The multiplier stays fixed for that encounter. Individual enemy overrides take precedence.

### Use the playtested adaptive settings

Fresh CTB profiles leave both forms of adaptive SPD off. To reproduce the balance tested with Zombie Dragon, Venus Shell and the starting-area encounters, enable **Break** in the launcher. In the editor, check **Blend solo boss SPD** and **Adapt enemy SPD to the opening party**. Set party weight to **75%**, variation to **0%**, solo boss Break delay to **25%**, and other Break delay to **50%**. Leave the fixed solo boss SPD bonus at **0%** and clear individual SPD overrides. Select **Save changes** before the next encounter.

The separate preview ZIP includes the same settings in `Stars of Destiny CTB Preview/Examples/s1-ctb-tuning.json`. To use the example, close the editor, back up any existing `BepInEx/config/s1-ctb-tuning.json`, and copy the example there. Enable CTB and Break in the launcher.

CTB covers regular S1 encounters. Forced and scripted phases can return to native timing, and duels, army battles, and S2 continue using their own battle systems. Save outcomes still include the usual HP, consumable use, rewards and progress. Earlier local tests covered normal encounters, Zombie Dragon, Venus Shell and shared item use. The public build’s native portrait fallback still needs visual confirmation across the enemy roster.

## Building the add-on

Build the 0.14.6 release binaries following [BUILDING.md](../BUILDING.md), then package their exact battle plugin, launcher and editor together:

```powershell
./packaging/Build-CtbPreview.ps1 -ReleaseBuildDirectory ./artifacts/0.14.6 -OutputDirectory ./artifacts/ctb-public
```

The packager checks the component versions, the battle DLL hash embedded in the launcher, the Windows runtime notices, and every final archive member. Use the release tag to obtain the corresponding source. The published ZIP contains project binaries, runtime dependencies, instructions, and licenses.
