# Suikoden Randomizer 0.8.4 — Story Avatars 75 Shuffle

This experimental patch blocks unsafe native Unite attacks involving changed combat avatars. The engine still recognizes original story IDs such as Gremio and Pahn, but their assigned avatars may lack the animation data that the original Unite sequence expects. The reported Talisman softlock repeatedly throws an array-bounds exception in its native battle routine.

**Unites involving changed combat avatars are blocked. Donor-based Unites are not implemented. Unchanged native combinations remain eligible subject to the game's own conditions.** Canonical story identities are retained.

**Existing bound 0.8.0/0.8.1/0.8.2/0.8.3 shuffle saves are supported. No restart is required for this Unite safety fix.** Save the current run before closing if needed, keep its matching config/bindings, install the update and reload. Unbound saves, the 0.7.x pilot and older ID-swapping campaigns are not converted. An already softlocked battle must be abandoned by closing and reloading a prior save.

The 0.8.3 portrait correction, 0.8.2 staged-resident startup correction and 0.8.1 performance improvements remain. Non-combat Stars, Tir, Ted and Odessa remain excluded from both sides of the 75-hero shuffle. Default seed: `agilen-080`; bound saves restore their own mapping.

Read [docs/AVATAR_SHUFFLE_0.8.4.md](docs/AVATAR_SHUFFLE_0.8.4.md) for installation, focused battle retesting, diagnostics and rollback. Confirm the Gremio/Pahn replacements cannot select their original Talisman attack, check ordinary attacks and runes, then save/full quit/reload. No full playthrough or repeated opening is requested.

The AutoInstall package verifies the supported native revision, checks installed API contracts, compiles against installed BepInEx/game references and runs all relevant managed suites before replacing either DLL. This source contains no proprietary binaries or precompiled plugin. The pinned SDK is .NET 10.0.403; Core targets netstandard2.1 and the game plugin targets net6.0.

Managed suites use actual linked production code with explicit native-model doubles. They do not execute native hooks or compile the complete plugin against real external wrappers. `RELEASE_VALIDATION.json` records completed checks and limits. The corrected Unite behavior still needs the focused in-game retest.

Example local build:

```powershell
dotnet build src/SuikodenRandomizer.AvatarPlugin/SuikodenRandomizer.AvatarPlugin.csproj --configuration Release -p:GamePath="C:\path\to\Suikoden I and II HD Remaster"
```

Use the installer for backed-up DLL replacement. Building only reads installed references.
