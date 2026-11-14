# Third-party notices

The root [MIT License](LICENSE) applies to the Stars of Destiny project's own code and documentation. It does not relicense dependencies, the game, locally generated game interop assemblies or third-party content carrying its own notice.

## BepInEx and loader dependencies

The tested prerequisite is **BepInEx 6.0.0-be.697, commit `5362580`, Unity IL2CPP Windows x64**. Obtain it from the [official BepInEx distribution](https://builds.bepinex.dev/projects/bepinex_be). The mod-only manual ZIP does not contain its loader files. The full setup includes the pinned upstream distribution and its original notices and corresponding source folders.

BepInEx, Unity Doorstop, Il2CppInterop, Harmony/HarmonyX, MonoMod and the other loader components remain under their respective upstream licenses. Preserve their supplied notices and source material when redistributing a full loader bundle. Do not apply the project's MIT license to them.

Relevant upstream projects include:

- [BepInEx](https://github.com/BepInEx/BepInEx), LGPL-2.1.
- [Unity Doorstop](https://github.com/NeighTools/UnityDoorstop), LGPL-2.1.
- [Il2CppInterop](https://github.com/BepInEx/Il2CppInterop), LGPL-3.0.
- [HarmonyX](https://github.com/BepInEx/HarmonyX), [Harmony](https://github.com/pardeike/Harmony), [MonoMod](https://github.com/MonoMod/MonoMod), [Mono.Cecil](https://github.com/jbevain/cecil) and [Dobby](https://github.com/jmpews/Dobby); consult their original notices for the versions redistributed by a particular package.

The exact dependency versions, files and hashes are recorded by the release packaging manifests. Package notices describe the files actually included in that package; the mod-only ZIP and full setup have different dependency contents.

## Suikoden Fix

[Suikoden Fix 1.5.2b](https://github.com/d3xMachina/Suikoden-Fix/releases/tag/v1.5.2b), by d3xMachina, is distributed unchanged under its MIT license. Its original `Suikoden-Fix-LICENSE.txt` is retained with the package notices. Its source and ownership are separate from Stars of Destiny.

## Microsoft .NET

The self-contained Windows launcher includes Microsoft .NET 10.0.14 runtime files. Their original notices remain beside the launcher:

- `dotnet10-Core-LICENSE.txt`
- `dotnet10-Core-THIRD-PARTY-NOTICES.txt`
- `dotnet10-WindowsDesktop-LICENSE.txt`

The full setup's BepInEx loader also uses a separate private .NET runtime, supplied by the pinned upstream distribution. That loader runtime is excluded from the mod-only ZIP. Keep the notices appropriate to each redistributed runtime.

## Installer tooling

The full setup is compiled with [NSIS 3.11](https://nsis.sourceforge.io/). Its license and compression-module notices are retained in the full package. NSIS is not used by the mod-only manual ZIP.

## Game references

Suikoden I & II HD Remaster and its assets belong to their respective rights holders. This repository does not grant rights to the game. Building the native-facing plugins requires the user's own supported game installation and locally generated interop assemblies. Do not commit or redistribute those binaries, game assets, private saves or generated runtime caches as project source.
