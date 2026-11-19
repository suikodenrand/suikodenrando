extern alias GSD1;

using System;
using HarmonyLib;
using Battle = GSD1.BattleBase;

namespace SuikodenRandomizer.AvatarPlugin;

[HarmonyPatch(typeof(Battle), nameof(Battle.make_item_data))]
internal static class SharedBattleItemListPatch
{
    [HarmonyPrefix]
    private static bool Prefix(ref int __result)
    {
        int count = SharedBattleItems.BuildMenu();
        if (count < 0) return true;
        __result = count;
        return false;
    }
}

[HarmonyPatch(typeof(Battle), nameof(Battle.command_item))]
internal static class SharedBattleItemOpenPatch
{
    [HarmonyPrefix]
    private static void Prefix() => SharedBattleItems.EnterItemList(returning: false);
}

[HarmonyPatch(typeof(Battle), nameof(Battle.command_item01))]
internal static class SharedBattleItemNavigatePatch
{
    [HarmonyPrefix]
    private static void Prefix(out GSD1.PLAYER_DATA? __state)
    {
        if (SharedBattleItems.Enabled && Battle.window_work?.window_step == 0) SharedBattleItems.EnterItemList(returning: true);
        __state = SharedBattleItems.BeginWindowView();
    }

    [HarmonyFinalizer]
    private static void Finalizer(GSD1.PLAYER_DATA? __state) => SharedBattleItems.EndWindowView(__state);
}

[HarmonyPatch(typeof(Battle), nameof(Battle.make_item_window))]
internal static class SharedBattleItemWindowPatch
{
    [HarmonyPrefix]
    private static void Prefix(out GSD1.PLAYER_DATA? __state) => __state = SharedBattleItems.BeginWindowView();
    [HarmonyFinalizer]
    private static void Finalizer(GSD1.PLAYER_DATA? __state) => SharedBattleItems.EndWindowView(__state);
}

[HarmonyPatch(typeof(Battle), nameof(Battle.command_item02))]
internal static class SharedBattleItemConfirmPatch
{
    [HarmonyPrefix]
    private static bool Prefix(int __0, ref int __result)
    {
        if (SharedBattleItems.SelectNativeItem()) return true;
        // A queued action or a KO can invalidate a row after it was displayed.
        __result = Battle.command_item(__0);
        return false;
    }
}

[HarmonyPatch(typeof(Battle), nameof(Battle.command01))]
internal static class SharedBattleItemReselectPatch
{
    [HarmonyPrefix]
    private static void Prefix()
    {
        if (SharedBattleItems.Enabled && Battle.window_work is { window_step: 0 } window)
            SharedBattleItems.ReleaseItem(window.selected_player);
    }
}

[HarmonyPatch(typeof(Battle), nameof(Battle.player_item_sub))]
internal static class SharedBattleItemUsePatch
{
    [HarmonyPrefix]
    private static bool Prefix(int __0, ref int __result, out SharedBattleItems.Use? __state) =>
        SharedBattleItems.BeginUse(__0, out __state, ref __result);
    [HarmonyFinalizer]
    private static void Finalizer(SharedBattleItems.Use? __state, int __result, Exception? __exception) =>
        SharedBattleItems.EndUse(__state, __result, __exception);
}

[HarmonyPatch(typeof(Battle), nameof(Battle.battle_end_free))]
internal static class SharedBattleItemEndPatch
{
    [HarmonyPrefix]
    private static void Prefix() => SharedBattleItems.Reset();
}

// S1 clears every command at this boundary, including skipped actions that never reached player_item_sub.
[HarmonyPatch(typeof(Battle), nameof(Battle.init_player_command))]
internal static class SharedBattleItemClearCommandsPatch
{
    [HarmonyPostfix]
    private static void Postfix() => SharedBattleItems.ClearCommands();
}
