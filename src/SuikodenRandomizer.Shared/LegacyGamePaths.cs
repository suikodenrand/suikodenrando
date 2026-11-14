using System;
using System.Collections.Generic;

namespace SuikodenRandomizer.Shared;

/// <summary>Relative descriptors only; no config/save read, move, merge or creation.</summary>
public sealed class LegacyGamePathManifest
{
    public ActiveGame Game { get; }
    public string ConfigFileName { get; }
    public string PrimaryBindingDirectory { get; }
    public IReadOnlyList<string> BindingDirectoriesToPreserve { get; }
    public string NativeSaveDirectoryName { get; }
    internal LegacyGamePathManifest(ActiveGame game, string config, string primary, string native, params string[] preserve)
    {
        Game = game; ConfigFileName = config; PrimaryBindingDirectory = primary; NativeSaveDirectoryName = native;
        BindingDirectoriesToPreserve = Array.AsReadOnly((string[])preserve.Clone());
    }
}

public static class LegacyGamePaths
{
    // All config/binding paths below are relative to BepInEx.Paths.ConfigPath.
    // Extra historical S1 stores are preservation inventory, not active-store
    // selection. Each engine continues to choose its own established schema.
    private static readonly LegacyGamePathManifest Gsd1 = new(ActiveGame.Gsd1,
        "agilen.suikoden.recruitment_randomizer.cfg", "SuikodenRandomizer/avatar-shuffle/save-bindings", "gsd1",
        "SuikodenRandomizer/avatar-shuffle/save-bindings",
        "SuikodenRandomizer/avatar-shuffle/fixed-roster-save-bindings",
        "SuikodenRandomizer/avatar-shuffle/nuzlocke-save-bindings",
        "SuikodenRandomizer/avatar-pilot/save-bindings", "SuikodenRandomizer/save-bindings");
    private static readonly LegacyGamePathManifest Gsd2 = new(ActiveGame.Gsd2,
        "agilen.suikoden2.recruitment_randomizer.cfg", "SuikodenRandomizer/gsd2-campaigns", "gsd2",
        "SuikodenRandomizer/gsd2-campaigns");
    public static LegacyGamePathManifest For(ActiveGame game) => game switch
    {
        ActiveGame.Gsd1 => Gsd1, ActiveGame.Gsd2 => Gsd2,
        _ => throw new ArgumentOutOfRangeException(nameof(game), "No persistence owner exists for the launcher or an unknown scene.")
    };
}
