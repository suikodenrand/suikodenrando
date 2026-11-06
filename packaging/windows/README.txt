SUIKODEN RANDOMIZER 0.8.5 FIXED ROSTER TEST
Suikoden I HD Remaster on Windows x64

Tir and five companions make up your campaign roster. Story joins and departures
determine how many fighters are active. An incoming story character uses an
available roster fighter. A fighter who sits out can return with their progress
and equipment. Recruit normally whenever the story requires it.

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

Choose your five companions
Under [Shuffle], edit these entries in the existing section:

   Mode = FixedChosen
   Seed = fixed-roster-test
   Companions = 11,26,35,56,76

The example picks Anji, Luc, Viktor, Lotte, and Meg. Choose five distinct IDs
from docs/COMPANION_IDS.txt. Tir is included automatically.

Roll five companions from a seed
Under [Shuffle], set Mode = FixedRandom and enter a Seed. A seed produces the
same five companions when starting another New Game in this mode. Companions
is used only by FixedChosen.

Start a New Game after saving the config. Use Mode = Shuffle for a new campaign
with the original avatar shuffle. Existing bound avatar-shuffle saves use their
stored mapping. Keep native saves and their matching BepInEx/config data together.

What to test
1. Check names, portraits, and ordinary battle animations during the opening
   joins, including when Ted becomes active. You should see only Tir and the
   chosen or rolled companions as active fighters. A short party is expected.
   The Ted-only Queen Ant sequence is a separate priority test. If it stalls,
   keep the save from before the encounter and collect the diagnostic ZIP.
2. Check a story departure and the next join. Make sure the incoming role uses
   an available fighter. A returning fighter should keep earned experience and
   gear. Reordering the formation should keep each active fighter's assignment.
3. Make a manual save, close the game completely, then reload that save. Compare
   the lineup, level, HP, and equipped items.

This package is for testing standard parties and ordinary battles. Duels, army
battles, scripted equipment changes, and full campaign completion still need
in-game testing. Some original Unite attacks are disabled for changed fighters.
Optional recruitment still runs the game's normal story checks and menus.

If you hit a problem, note the scene and action that caused it. Close the game
and run Collect-Avatar-Diagnostics.cmd. Setup failures print a separate log path.

Rollback.cmd restores the plugin files backed up by this installer. For a first
installation it removes the installed plugin files. Use your backed-up save and
configuration to continue your previous campaign after rollback.
