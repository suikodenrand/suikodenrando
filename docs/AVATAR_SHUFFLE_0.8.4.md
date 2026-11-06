# Suikoden Randomizer 0.8.4 — Story Avatars 75 Shuffle

This experimental patch blocks **unsafe native Unite attacks** involving changed combat avatars. The engine retains original story IDs, so it can still offer Gremio and Pahn's Talisman attack to their replacements. The supplied 0.8.3 diagnostic records 3,308 repeated array-bounds exceptions in `BattleBase.kmagic01_func02`, called by `battle_turn_kyoryoku`, matching the reported softlock.

**Existing bound 0.8.0/0.8.1/0.8.2/0.8.3 shuffle saves remain compatible. No restart is required for this Unite safety fix.** The seed, mapping, profile, save-binding format and progression policy are unchanged. Keep the matching `BepInEx/config` binding data with the native save. Unbound saves, the 0.7.x pilot and older ID-swapping campaigns are not converted. This update does not resume an already softlocked battle; close the game and reload a prior save.

## Unite behavior

Unites involving changed combat avatars are blocked. Donor-based Unites are not implemented. A native combination remains eligible only when every required participant retains that role's original combat avatar, subject to the game's own conditions, including party membership and battle state. A safe alternative can remain available even when another combination for the same character is blocked. The fix does not change canonical character IDs to force eligibility.

The same safety policy applies to pair, triple and five-member combinations, including alternate native combinations. Matching an assigned avatar's displayed name is not enough to make a donor-based Unite safe: native eligibility and animation execution still use original roles.

With the default seed `agilen-080`, the affected opening assignments are:

| Original role | Assigned avatar | Expected original Unite behavior |
| --- | --- | --- |
| Gremio | Anji | Talisman unavailable |
| Pahn | Lotte | Talisman unavailable |
| Cleo | Meg | Native Beauty combinations unavailable when changed participants are involved |

## Existing behavior retained

- **75 combat roles** shuffle one-to-one. A role may occasionally map to itself.
- Non-combat Stars, Tir McDohl, Ted and Odessa remain outside both sides of the shuffle.
- Native story/recruitment conditions, membership, active party order and canonical role/base IDs remain unchanged.
- The 0.8.3 portrait correction remains: native static/instance character-to-face converters share one nesting guard; original donor assets are available throughout conversion and final sprite IDs stay native.
- The 0.8.2 staged-resident startup correction remains: a valid bound eligible resident can anchor initialization before formal active-party joining. Inactive reservations are separate from the active `narabi`/`player_kazu` prefix.
- All 75 pristine donor assets and initialized live records still pass preflight before a new campaign copies donor starting state once. Bound reload reattaches templates without repeating that copy.
- The 0.8.1 performance controls remain: bounded background retries, no event-command polling, one-pass native-base indexing and fast existing character-cache hits.

The latest 0.8.3 diagnostic records native portrait conversion routes for Gremio -> Anji, Cleo -> Meg and Pahn -> Lotte. It also records battle proxies retaining canonical IDs while levels and EXP advance. These observations do not establish a full campaign playthrough or successful native save/reload for that capture.

## Install without restarting the campaign

1. If you want to keep the current running campaign and the game responds, make a normal manual save first and confirm it succeeds. Then close the game. If battle is already softlocked, close and use the last valid save. Back up native saves and the entire `BepInEx/config` folder together.
2. Extract the entire **0.8.4 AutoInstall ZIP into a fresh folder**. Run `Install.cmd`, select the existing game folder, and confirm the **0.8.4** banner and successful completion.
3. Setup verifies the supported native revision and installed API contracts, compiles against installed BepInEx/game references, and runs managed checks before replacing either DLL. Missing SDK/targeting packs may download. No source editing is required.
4. Reload your bound shuffle save with its matching config. **Keep the existing seed and mapping.** Do not start over merely to test this fix.

A separate New Game is needed only if you have never initialized the shuffle or have no valid bound shuffle snapshot. A New Game shares the native autosave. The default seed remains `agilen-080`; `[Shuffle]` -> `Seed` in `BepInEx/config/agilen.suikoden.recruitment_randomizer.cfg` affects future New Games only. Loading a bound campaign always restores its saved mapping.

## Focused battle retest

1. Enter an ordinary battle with Gremio and Pahn's assigned replacements. Confirm their original Talisman Unite cannot be selected. A changed avatar must not execute the original role's Unite sequence.
2. Complete a turn using normal attacks and runes. Check that battle finishes and earned progress remains. Unchanged native combinations, where available, should still follow normal native eligibility rules.
3. Press **F6** for a checkpoint. It records party slots, active order/count, readiness and performance counters. Preserve any Unite safety trace when reporting a problem.
4. Make a new manual save, record HP/level/EXP/equipment/items/runes, fully quit, relaunch and reload. Confirm the same mapping and earned progress remain. Press F6 again.
5. If an unsafe Unite is still selectable or a battle stalls, close the game and run **Collect-Avatar-Diagnostics.cmd**. Send the Desktop ZIP and identify the original roles, assigned avatars, selected command and what happened.

The optional `BepInEx/config/SuikodenRandomizer/avatar-shuffle/mapping-spoiler.csv` lists the full mapping after initialization. No full playthrough or repeated opening is required for this patch.

## Diagnostics, rollback and limits

The collector includes logs, up to 200 shuffle binding metadata files, installed-plugin metadata and the latest runtime startup. It excludes native save contents and full config files. If setup fails, send the installer log printed by setup before gameplay testing; compilation/check failures stop before DLL replacement.

With the game closed, `Rollback.cmd` restores the DLLs saved by this 0.8.4 update. It does not restore native saves or config. Keep each campaign with its matching native saves and binding data, especially when returning to the 0.7.x pilot or older ID-swapping randomizer.

This patch guards native Unite eligibility; it does not implement donor-based Unite attacks or certify every native battle sequence. The fix still needs the focused in-game retest. Other remaining limits include custom poses/expressions, original raw dialogue/voice, range behavior, Pahn's duel, Viktor's forced weapon upgrade, scripted battles, native object lifetime and full campaign compatibility.

`RELEASE_VALIDATION.json` records exact completed checks and outstanding work. Managed suites link production code to explicit native-model doubles; they do not execute native detours or certify game behavior. Complete generated external wrappers are not supplied here, so setup compiles the full plugin against installed references.
