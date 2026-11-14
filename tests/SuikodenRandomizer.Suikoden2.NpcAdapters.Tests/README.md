# Suikoden II ordinary NPC direction adapter tests

This harness links the production direction adapter, NPC factory boundary and
asset-name registry. Unity/IL2CPP allocation, native wrappers and runtime
admission are controlled doubles; no game code is loaded or executed.

The three fixtures preserve original serialized metadata for Riou (`shu`),
Mukumuku (`muk`) and Chuchura (`kkl`), without sprite pixels. Their prefix
fingerprints were generated independently from the original Unity exports.
The production fingerprint checks the exact name/header, all four copied
animation rows and all twelve copied pattern rows, including timing, collision
lists, sprite names, geometry and mirror flags. Unused extra animation/pattern
rows are intentionally excluded from the view.

Tests cover independent nested/cache ownership, unchanged sources, exact four
animation and twelve pattern bounds, full-bank/name forgery rejection, factory
integration, custom-pose/HD exclusions, pointer reuse, retirement, allocation and
readback failure, safe cleanup/quarantine and optional logging failure. The
production retry gate is tested with a controlled clock: failed automatic
attempts back off for one second per donor/source pointer, while success/reset
clear only timing state. Manual preparation remains independently retryable.

The name registry still contains 143 source aliases and 72 pristine normal
donor names. Three separately verified adapter donors bring ordinary NPC donor
coverage to 75. The four large banks (49/50/72/74) and unsupported source poses
keep canonical NPC visuals. Party graphs retain their full original donor bank.

Run from the repository with the pinned SDK:

```powershell
dotnet run --project tests/SuikodenRandomizer.Suikoden2.NpcAdapters.Tests -p:NuGetAudit=false
dotnet run --project tests/SuikodenRandomizer.Suikoden2.NpcSprites.Tests -p:NuGetAudit=false
```

The sibling `NpcSprites.Tests/InteropCompile` project compiles both production
modules against actual generated GSD2/GSDShare and Harmony assemblies. These
checks establish managed behavior and API compatibility. Native allocation and
visible rendering of the three adapters have not been observed; no live pass is
implied by the test results.
