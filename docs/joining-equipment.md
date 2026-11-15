# Recruitment inventory and equipment

The 0.14.4 release adds `Shuffle/JoiningEquipment` to both games. `MatchRole`
is the default for a new campaign in Shuffle, Fixed Roster and Random Roster.
`Donor` retains the replacement fighter's own initial inventory.

`MatchRole` copies the original story role's complete carried inventory on a
fighter's first active party assignment. It preserves raw consumable counts and
item flags, then substitutes incompatible armor with compatible equipment of the
same category. The ranking prefers ordinary equipment within the original
defense and price tier, then the closest native stat profile, price and item ID.
If that bounded pool is empty, the closest legal ordinary item is used and the
fallback is logged. Items without a shop price are ranked by their native stats.

Weapon identity, the separately configured joining weapon level, runes, level
and other progression are outside this inventory policy. Subsequent recruitment,
role changes, saves and reloads must preserve earned equipment.

## Campaign state

The campaign stores the selected policy and its completed first assignments.
Shuffle also stores the original role inventories captured after native New Game
initialization and before eager character projection. Fixed/Random Roster capture
the live original role inventory immediately before its first fighter projection.
Inventory initialization is verified before its history is committed. Partial
writes are rolled back; missing catalog data is an error rather than permission
to force illegal equipment.

Existing saves without the added campaign state retain `Donor`. Changing the
launcher setting does not reset an existing campaign's inventory.

In S1, an item with no legal equivalent remains carried and unequipped. S2 has
separate head/body/shield slots, so such an item must move to the shared bag.
The S2 transaction requires enough bag space and retries pending initialization
when space becomes available; it must never discard the item or overwrite an
occupied bag cell. If the fighter's inventory changes while that transaction is
waiting, initialization is skipped with a diagnostic so later earned items or
player equipment changes cannot be overwritten.

## Native compatibility reference

The supported Windows GameAssembly.dll SHA-256 is
`e093f175eaf3734d293ca0a236c652dd7fcc72ea5fa10d7b21103edf12f11c3a`.
Static analysis establishes the following S2 rules for that exact binary:

| Method | Native address | Relevant observation |
| --- | --- | --- |
| `G2_bogu_seigen` | `0x181a28f90` | Returns 0 for a legal selection and -1 for an illegal selection. |
| `G2_CharaEquip_seigen` | `0x181a27b40` | Decodes head/body/shield capability from `soubi_kanou`. |
| `CIT_KIND` | `0x181a275f0` | Carried item kind is `(use_cnt >> 4) & 7`. |
| `G2_kind_no_get` | `0x181a32ff0` | Shared bag records use the same item-number/kind encoding. |
| `G2_data_get` | `0x181a2d190` | Kind 1 selects the armor catalog. |
| `G2_def_val` | `0x181a2d5c0` | Armor defense is a signed byte. |

For head, body and shield, armor `info[1]` must contain slot bit 1, 2 or 4.
The fighter's capability class is respectively `mask & 3`, `(mask >> 2) & 7`,
or `(mask >> 5) & 1`; zero disallows the slot. Armor `info[0]` must contain
`1 << (class - 1)`.

Accessories require `info[1] & 8`. The fighter's native class nibble maps
0/1/2/3/4/5/7 to masks 64/16/4/32/8/2/1. Other values disallow accessories.
The native checks also limit boots (`info[1] & 16`) and gloves (`info[1] & 32`)
to two of each. Armor carried in the shared bag has kind 1, encoded as `0x10`
in `use_cnt`; unrelated carried items retain their original raw byte.

S1 uses the native armor equipment mask and item sex restrictions. Its inventory
stores item ID, equipped/locked flags and per-item data separately. Its category
masks are head 3, body 60, shield 64 and accessory 128.

## Native validation recorded for release

A new S1 campaign verified Meg joining in Viktor's role with Fur Robe, Boots
and weapon level 1. Inspection of the resulting slot 18 save confirmed that
loadout, `MatchRole` and one completed initialization for role 35. Reloading
that save was not observed; this is not a native reload/rejoin pass.

The static findings and managed regressions do not replace wider in-game tests
of recruitment, incompatible gear, full inventory, save/reload and later story
scripts in both games. See [BUILDING.md](../BUILDING.md) for the remaining
playtest checklist. No proprietary game binary, extracted master table or
decompiled game body is distributed here.
