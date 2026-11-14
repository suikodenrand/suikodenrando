# Linked shuffled unite verification

This executable links the actual production `AvatarShuffledUnites`, `AvatarUniteRecipes`, `AvatarUniteTableScope`, and `AvatarUnitePatches` source plus the production Core project. Harmony attributes, IL2CPP arrays, the runtime owner, and native GSD1 objects are managed test doubles. The original game executable is never loaded or executed.

`expected-native.json` is a separate expected-data fixture extracted from the original native initializer catalog and reviewed choreography contract. It supplies the 32 ordered member sets, immutable metadata/opcodes and participant-specific action rows. The fixture does not read expected values from the production catalog.

The matrix covers every recipe in all six formation rotations on unrelated canonical roles, every participant as initiator, stock and shuffled Tir, two/three/four-member groups, alias dispatch to work01, native targeting/turn consumption, resource qualification, isolation of canceled groups, multiple groups in one round, owner/battle boundaries and native exception cleanup. Explicit NativeModel helpers simulate canonical requirement matching, command commitment and FinishFunc participant/target/finished-flag effects. Separately modeled AoE/self/status changes exercise production observation without supplying any damage formula.

Passing checks establish managed adapter behavior only. They do not validate Harmony installation, native indirect calls, animations, damage formulas, sound, camera behavior or an in-game unite. Those require the preview's game tests. `verification.json` records the exact production/test source and assembly hashes from the successful run.
