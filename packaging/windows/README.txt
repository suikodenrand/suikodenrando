SUIKODEN RANDOMIZER 0.8.6 BEFORE VIKTOR TEST
Suikoden I HD Remaster on Windows x64

The optional start point opens in Gregminster after the inn scene, before
Viktor joins for the first time. Recruit him at his bench and play the gate
escape. This build also includes the chosen and random fixed roster modes.

Install
1. Make a backup of your native saves and the BepInEx/config folder. Start this
   test in a separate New Game. The game shares its autosave across campaigns.
2. Have a working BepInEx 6 IL2CPP installation. Launch the game once to let
   BepInEx generate its game references, then close it.
3. Extract this entire ZIP into a fresh folder. Run Install.cmd and select the
   game folder that contains GameAssembly.dll. The installer compiles the plugin
   against your installed references and verifies the supported game revision.
   It downloads Microsoft build tools on first use and backs up any existing
   plugin files before replacing them.
4. Launch to the title screen once, then close the game. Open this file in a
   text editor under the game folder:

   BepInEx/config/agilen.suikoden.recruitment_randomizer.cfg

Start before Viktor
Under [Start], edit the existing Point entry:

   Point = BeforeViktor

Save the config, launch Suikoden I, and choose New Game. Enter your name and
difficulty as usual. Use Point = Opening for the normal start. The setting is
read on launch and applies only to New Game.

The test uses the captured Gregminster street position, a short walk from
Viktor. Tir starts at level 7. Your randomized companions use their usual
starting stats and equipment. The preset leaves out optional opening loot.

Optional fixed roster
Under [Shuffle], edit these entries in the existing section:

   Mode = FixedChosen
   Seed = fixed-roster-test
   Companions = 11,26,35,56,76

The example picks Anji, Luc, Viktor, Lotte, and Meg. Choose five distinct IDs
from docs/COMPANION_IDS.txt. Tir is included automatically. Story joins and
departures determine how many roster fighters are active.

Roll five companions from a seed
Under [Shuffle], set Mode = FixedRandom and enter a Seed. A seed produces the
same five companions when starting another New Game in this mode. Companions
is used only by FixedChosen.

Use Mode = Shuffle for the regular avatar shuffle. Existing bound campaigns
use their stored mapping. Keep native saves and their matching BepInEx/config
data together.

What to test
1. After starting BeforeViktor, check that you can move in Gregminster, Tir has
   the Soul Eater, and three fighters are active. Press F6 to log the checkpoint.
2. Speak to Viktor and accept his help. Check that a fourth fighter joins. In
   fixed roster mode, the fourth fighter should come from your preset. Press
   F6 again when you regain control, then escape through the gate and proceed
   to Lenankamp.
3. At the next available save point, make a manual save, close the game, and
   reload it. Check your party, runes, and story progress.
4. With Point = Opening, start another New Game and check the normal opening.

The skip, duels, army battles, scripted equipment changes, and full campaign
completion still need in-game testing. Some original Unite attacks are
disabled for changed fighters.

If you hit a problem, note the scene and action that caused it. Close the game
and run Collect-Avatar-Diagnostics.cmd. Setup failures print a separate log path.

Rollback.cmd restores the plugin files backed up by this installer. For a first
installation it removes the installed plugin files. Use your backed-up save and
configuration to continue your previous campaign after rollback.
