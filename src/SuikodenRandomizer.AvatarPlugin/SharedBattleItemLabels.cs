extern alias GSD1;

using HarmonyLib;
using Il2CppSystem.Collections.Generic;

namespace SuikodenRandomizer.AvatarPlugin;

[HarmonyPatch(typeof(GSD1.UIBattleManager), nameof(GSD1.UIBattleManager.UpdateItemDataUI))]
internal static class SharedBattleItemLabels
{
    [HarmonyPrefix]
    private static void Prefix(List<string> __0)
    {
        if (!SharedBattleItems.Enabled || __0 is null || GSD1.BattleBase.window_work is not { } window) return;
        // make_item_window passes a fresh page of localized names. The native charge
        // column and help text are separate arguments with their own formatting.
        for (int i = 0; i < __0.Count; i++)
        {
            string? owner = SharedBattleItems.OwnerNameForRow(window.item_hyouji_kaishi + i);
            if (!string.IsNullOrEmpty(owner)) __0[i] += " · " + owner;
        }
    }
}
