# Stars of Destiny 0.14.6

**Windows package revision 0.14.6-r4** fixes a Suikoden I ending freeze after Cleo speaks in Gregminster. Hix and Gen use their original cutscene sprites during the ending so their scripted animations can play. A shuffled pre-ending save advanced past both animation commands to the later Liukan dialogue. Existing players can apply the update by running the revision 4 setup against their game folder. [Revision 4 downloads](https://github.com/suikodenrand/suikodenrando/releases/tag/package-v0.14.6-r4)

**Windows setup revision 0.14.6-r3** adds **Repair** and **Uninstall**. Run the same setup again and select your game folder. Repair replaces missing or damaged package files and keeps your mod settings. Uninstall asks whether to keep configuration and campaign bindings, with the option checked by default. It preserves native saves and keeps a shared BepInEx loader available for other mods. [Download 0.14.6-r3 setup](https://github.com/suikodenrand/suikodenrando/releases/download/v0.14.6/Stars-of-Destiny-0.14.6-r3-Setup.exe)

**Windows package revision 0.14.6-r2** ships launcher **1.0.8** with **Join our Discord** in its header. Existing players can use the standalone launcher ZIP. [Launcher update](https://github.com/suikodenrand/suikodenrando/blob/launcher-v1.0.8/docs/launcher-1.0.8.md)

This release adds four optional Suikoden I improvements, launcher 1.0.7, and the first public S1 CTB preview. The four improvements are enabled by default. In the launcher, open **Suikoden I → Quality of Life** to switch them individually or disable all four. CTB has its own switch and starts off.

## Inventory, equipment, and quick start

- **Roc’s Warehouse from Items.** After recruiting Roc, open **Items → Roc’s Warehouse**, directly below Party Items. Deposit or withdraw gear anywhere the field menu is available, then return to Items.
- **Shared battle items.** Each fighter can use battle items carried by a living member of the active party, including an Unbalanced ally. The list identifies the carrier and remaining charges. The acting fighter spends the turn, and the charge comes from the carrier’s stack.
- **Optimize equipment.** Open **Equipment**, select a fighter, and press **Up** from headgear to find **Optimize**. It equips the highest-defense legal loadout, keeps current gear on ties, and respects locked items. Press **Left/Right** on Optimize to choose own pockets, party, own pockets plus warehouse, or party plus warehouse. The last option is the default after recruiting Roc. Other fighters keep all gear they are wearing.
- **Viktor quick-start leveling.** The two companions already with Tir now reach his checkpoint level, normally 7, using each fighter’s own stat growth and recalculated spell charges. This applies once at the start of a new Viktor-skip campaign.

## Optional S1 CTB preview 0.5.2

- SPD determines individual turns. A bottom-center timeline forecasts up to eight actions and shows when the acting fighter should return. Wait, Defend, Item, Attack and Rune tiers have different recovery times.
- On consecutive ally turns, choose the next fighter’s command from the full native menu while the previous animation plays. Queued items reserve an actual charge. Consecutive enemies can act with less delay, and a failed Run returns to CTB turn order.
- Matching enemy markers identify targets on the field, in the target list and on the timeline. The field emblem appears above the full enemy sprite.
- Break lets damage and DEX delay an enemy’s next turn. Reserve Rally lets eligible recruited reserves advance a teammate’s next turn. Both are configurable.
- Tuning editor 0.5.1 supports fixed Star and enemy SPD, separate Break delays, and adaptive balancing. The tested solo-boss setup uses a 75% party / 25% recommended-level blend, a natural-growth ceiling four levels above the recommendation, and 25% Break delay. It aims for one boss attack per three normal attacks across the party. Ordinary encounters have a separate scaling option.

To try CTB, install or activate the optional S1 battle component, disable S1 shuffled Unites and the legacy Talisman preview, then enable CTB in **Suikoden I → Battle & ATB**. Standard turn-based battles are the fresh default. CTB covers regular S1 battles. Duels, army battles and S2 use their usual systems. [CTB guide](https://github.com/suikodenrand/suikodenrando/blob/v0.14.6/docs/CTB-PROTOTYPE.md)

## Downloads and verification

Choose the **Windows setup** for a full installation. The **mod-only ZIP** and **plugins-only ZIP** require BepInEx separately. The **standalone launcher** updates the launcher on an existing installation. The **CTB Preview ZIP** is an optional add-on for an existing 0.14.6 installation. The release includes SHA-256 checksums for all five downloads. [Release assets](https://github.com/suikodenrand/suikodenrando/releases/tag/v0.14.6)

Gameplay tests during development confirmed the two starting companions’ growth, warehouse access, and a shared Mega Medicine charge being deducted from its carrier. Eikei’s optimized equipment defense went from 54 to 84. His Master’s Robe and Goldlet remained equipped after save/reload, with zero of either in storage. CTB balance testing included Zombie Dragon, Venus Shell and early-area encounters. Wider encounter coverage remains in playtesting.
