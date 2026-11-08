Run this gate against the installed generated interop assembly:

```text
dotnet run --project tests/SuikodenRandomizer.Avatar.Contracts --configuration Release -- "C:\path\to\BepInEx\interop\GSD1.dll" "C:\path\to\BepInEx\interop\UnityEngine.CoreModule.dll" "C:\path\to\BepInEx\interop\GSDShare.dll"
```

The gate reads PE metadata without loading or executing game code. It checks the declared public method, property-accessor and constructor contracts, the `TitleChapter+State.DifficultyInit` enum literal, and the public GSD1 type references in the avatar source embedded at build time. Names preserve namespaces and declaring types, including `PLAYER_BATTLE_SO+ActionCommand`. Constructor matching selects the exact parameter list and accepts the normal additional `.ctor(IntPtr)` overload. Method contracts require a concrete, non-generic method.

Untyped Harmony targets require a unique method name. Save hooks also verify the native parameter names used by Harmony injection: `SaveGameWork` parameter 1 is `callback`; `CopyGameWorkToSaveData` parameter 1 and `CopySaveDataToGameWork` parameter 0 are `save_data`.

The optional second argument verifies Unity's diagnostic log, quitting and scene callbacks, scene identifiers, and the application paths used to collect available crash files. It is a metadata check of the generated Unity interop assembly.

The optional third argument verifies the shared `OPD_DATA.anime_table` getter used to check the battle resource before a reserve-team retry. It is defined in GSDShare rather than GSD1.

The gate does not verify `SystemSave.Save`/`cb`, Unity object cloning, `SOBase`, `Framework.Chapter.activeChapter`, inherited `GlobalWork.Instance`, or `OPDSprite.data`. Native detours, resource ownership, asynchronous execution order, callbacks and gameplay still require runtime validation. The original single-argument GSD1 check remains available.
