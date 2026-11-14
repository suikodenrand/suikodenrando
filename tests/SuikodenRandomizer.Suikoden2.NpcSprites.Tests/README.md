# Suikoden II NPC sprite boundary tests

The managed harness links the actual `Gsd2NpcSprites.cs` and production
`Gsd2NpcSpriteAliases.cs`. Native object wrappers, campaign admission, and the
already projected graphics accessor are controlled test doubles. The harness
does not start Unity or install Harmony patches.

It exercises actor-reference preservation, art aliases instead of raw `fpno`,
role admission, four-direction animation integrity, HD/shared-bank exclusions,
ambiguous pointers, nested scopes/factories, exceptions, campaign changes,
thread isolation, and verification of the returned native resource before
logging success. Canonical results from ineffective interception remain intact;
null or unexpected resources fail closed without actor or renderer-cache writes.
Missing donor atlases/caches also retain canonical input without initialization.

The Ryube regression uses distinct normal `vb05_14` and temporary `vb05_13`
Millie OPDs in one context. Both select the already projected role24 donor while
the temporary actor retains `fpno=83`, position, animation, motion, flags, and
original OPD/ANIME references. Only the exact scene-bank alias is admitted;
pristine `mi2` and other guest resources remain excluded. These boundary tests
do not exercise the actual game's party/stat-role admission code.

Both source and donor banks require exactly four valid directions. The generated
registry separately approves 72 pristine normal donor banks. Three small custom
donors use separately owned four-row adapters; four large custom donors keep
canonical NPC sprites. Full party/combat banks remain unchanged. The observed
Gengen→Gadget and Tuta→Lorelai cases are within the supported set.

Run with the repository-pinned SDK:

```powershell
$env:DOTNET_CLI_HOME = '<workspace>/work/suikoden2/tools/dotnet-home'
$env:NUGET_PACKAGES = '<workspace>/work/suikoden2/tools/nuget'
& '<workspace>/work/suikoden2/tools/dotnet/dotnet.exe' run --project tests/SuikodenRandomizer.Suikoden2.NpcSprites.Tests -p:NuGetAudit=false
```

`InteropCompile/InteropCompile.csproj` separately links these same production
files against the original generated GSD2/GSDShare interop and real Harmony
assemblies, checking the exact field and method signatures without loading game
code. Pass `-p:BepInExPath='<game>/BepInEx'` when building it.

These checks establish managed boundary behavior and compilation. Live native
argument interception and rendering remain separate qualification: the module
logs `NPC SPRITE native factory confirmed` only after `ANIME.data` has the
expected donor pointer. No test result is reported as a live NPC-rendering pass.
New adapters require separate native qualification. Live observations of prior
installed builds are recorded independently from these managed tests.
