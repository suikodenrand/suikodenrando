Run this gate against the installed generated interop assembly:

```text
dotnet run --project tests/SuikodenRandomizer.Avatar.Contracts --configuration Release -- "C:\path\to\BepInEx\interop\GSD1.dll"
```

The gate reads PE metadata without loading or executing game code. It checks 146 public method, property-accessor and zero-argument constructor contracts, the `TitleChapter+State.DifficultyInit` enum literal, and the public GSD1 type references in the avatar source embedded at build time. Names preserve namespaces and declaring types, including `PLAYER_BATTLE_SO+ActionCommand`. Constructor matching selects the exact parameter list and accepts the normal additional `.ctor(IntPtr)` overload.

Untyped Harmony targets require a unique method name. Save hooks also verify the native parameter names used by Harmony injection: `SaveGameWork` parameter 1 is `callback`; `CopyGameWorkToSaveData` parameter 1 and `CopySaveDataToGameWork` parameter 0 are `save_data`.

This gate does not verify external assemblies: `SystemSave.Save`/`cb`, Unity object cloning, `SOBase`, `Framework.Chapter.activeChapter`, inherited `GlobalWork.Instance`, and `OPDSprite.data`. Native detours, resource ownership, asynchronous execution order and gameplay still require runtime validation.

The supplied GSD1 assembly passed all 146 method/property/constructor contracts, the enum contract and 27 source type-reference checks. Two isolated negative checks also rejected a nonexistent nested `ActionCommand` type and an incorrect `TitleMain` return type.
