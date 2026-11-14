# Suikoden I joining equipment audit

Audit date: 2026-11-14. This records static native evidence and managed adapter checks, not a native gameplay pass.

## Capture and first-join boundary

Shuffle captures each role's complete initialized `PLAYER_BASE.item` array and `item_kazu` immediately before the existing eager donor-base transaction. It does not use the raw Unity `PLAYER_DATA.player_base` placeholders: sampled original assets for Cleo, Gremio, Pahn, Tir, Kasim and Kai have level/count/items zero before native initialization. The existing readiness check requires complete live New Game master records.

The detached role inventories persist with each saved campaign. They are applied only when their canonical role first becomes an active, bound resident. Fixed rosters capture the current unprojected native role immediately before the fighter's first assignment; later role changes keep the fighter's earned state.

The native `P_player_c.AddPlayer`, `LoadPlayer`, and active-party change postfixes resolve the first join immediately. The active-party change hook covers equipment `MatchRole` regardless of the selected weapon policy. This avoids replacing a later event reward at the next background tick. Save preparation and battle admission also resolve pending initialization.

The inspected native `Event_c.AddPartyMember` at `0x1813bc000`, `PARTY.AddPlayer2` at `0x181255570`, and `PARTY.AddPlayer` at `0x1812556b0` change membership/order and bind the canonical live base. No inventory initializer was found there. `Event_c.eventLevelChange` at `0x1813cec40` changes level/experience/magic capacity, not personal equipment. `eventGamenSplSub` has story rune/weapon edits; those are separate fields and remain outside this policy.

The retained event corpus contains 4,847 decoded records: 4,713 complete reachable CFGs and 134 partial records. Across decoded nodes it contains 326 `EVENTITEM` and 17 `ITEMSPL` operations. `Event_c.eventItem` at `0x1813ce400` grants ordinary items to the active party or removes a matching item from active inventories. `Event_c.special_item` at `0x1813db8a0` modes 0/1 alter the separate Party Items list; mode 2 strips a selected personal inventory into storage. The only decoded mode-2 site is `vb7d:0:5`, byte offset 101, target 6 (Pahn). Its local English dialogue IDs `1033076025..1033076030` identify the fatal branch after Pahn's Teo duel, well after Pahn's initial party join.

No pre-first-join equipment/inventory edit for a later recruit was found in these reviewed native helpers and decoded event paths. Partial CFGs and unreviewed custom callbacks remain outside that claim; this is not a proof that every possible execution path lacks additional edits.

## Compatible armor evidence

The retained original `G_item_c_data` static initializer at `0x18114be90` provides all 179 canonical item IDs. Its exact x64 object-field/array writes independently match the original item constants. `BOUGU_DATA` metadata type 4809 has `soubi_type` at `0x20`, `func_no` at `0x21`, and `equip_data` at `0x28`. Its managed byte array is zero-initialized and explicit writes supply the eight bonuses.

All 58 shipped armor entries use function 0 and a positive price (minimum 50). The largest bonus in indices 0 through 6 is 20; defense at index 7 reaches 63. No bonus byte is 128 or greater. Signed/unsigned byte interpretation therefore yields the same comparison for every armor in this shipped catalog. The register-backed bonus writes use `BPL = 8`, established by the initializer's sole `MOV EBP,0x8`; its `R12W` equipment writes use `0x80`, established by the sole `MOV R12D,0x80` before the epilogue.

The planner retains exact compatible role armor. Otherwise it prefers ordinary compatible armor in the same slot category, with defense and positive shop price bounded by the role piece. It ranks native bonus distance, then price distance, then canonical item ID. If no bounded item exists, the closest legal basic item is used and logged. If the fighter cannot wear any legal basic item in that category, the original item stays owned and unequipped, with equipped/lock flags cleared. A hypothetical source without a shop price uses bounded defense/stat proximity before price and logs that exceptional case; no shipped armor currently needs this path.

## Derived equipment stats

`BattleBase.calc_noryoku` at `0x1810042c0` rebuilds its output from the current
`PLAYER_BASE.noryoku` and equipped items in the current personal inventory,
zero-extending each of the eight armor bonus bytes. `battle_main_initialize` at
`0x180ffcfd0` calls it for every active fighter after the mod's battle-entry
equipment boundary. `make_tsuyosa_window` at `0x1810d2930` calls it with a fresh
eight-element array for the inspected status display. An additional speculative
cache-refresh hook is therefore unnecessary at this recruitment boundary. These
observations do not establish every menu's behavior or replace native testing.

The retained local traces are `work/suikoden2/qol-research/s1-battle/`
`calc_noryoku.c`, `battle_main_initialize.c`, and
`work/suikoden2/qol-research/s1-battle-followup/make_tsuyosa_window.c`.

## Evidence identity

The retained research artifacts are local inspection inputs, not redistributed game files. Paths below are relative to the research workspace containing `work/all-recruits`.

| Artifact | SHA-256 |
| --- | --- |
| Original `GameAssembly.dll` snapshot | `e093f175eaf3734d293ca0a236c652dd7fcc72ea5fa10d7b21103edf12f11c3a` |
| Original `global-metadata.dat` | `abb6489ca1ef1728f9de74f64a64d9c109f5ddd5ba9046226ea964b15b62f964` |
| `work/all-recruits/decoded-commands.jsonl` | `d712bb500a5872e77e9bed0fac86956f78668c810f140caf49afea73f5c44ed7` |
| `work/all-recruits/native/native-functions.json` | `625a1edbdd833e3e95be37f34ccf8a267c094002462651d06cbe2cb6137edc48` |
| `work/all-recruits/item-native/native-functions.json` | `de137778d4de56895c14dd4bbcb1b60e6589f04e5e7612123930cc55363cd699` |
| `work/all-recruits/corpus/original-items.json` | `9f6302e2e92bfcd298481745196e416f274727f2a05c2c325e6862e90d638df2` |

Managed tests link the production runtime, native inventory adapter, planner, binding serializer and lifecycle patch. They cover the three modes, complete medicine/armor state, compatibility/gender/category fallback, policy persistence, legacy saves, rejoin/reload, immutable metadata, no mutable aliases, and transactional rollback. The combined plugin also compiles against both installed game interop assemblies. Native menu, defense recalculation and later story gameplay still need a test run.
