using System;
using System.Collections.Generic;
using SuikodenRandomizer.AvatarPlugin;
using UnityEngine;

namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class)] internal sealed class HarmonyPatch(Type type, string method) : Attribute
    {
        internal Type Target { get; } = type;
        internal string Method { get; } = method;
    }
    internal sealed class HarmonyPrefix : Attribute { }
    internal sealed class HarmonyPostfix : Attribute { }
}
namespace SuikodenRandomizer.AvatarPlugin
{
    internal sealed class AvatarRuntime
    {
        internal static AvatarRuntime Current = new();
        internal bool OwnsExecution = true;
        internal void Trace(string message) { }
    }
}

internal static class Program
{
    private static int _checks;
    private static void Check(bool passed, string message)
    {
        if (!passed) throw new InvalidOperationException(message);
        _checks++;
    }

    private static bool Input(uint direction = 0, uint button = 0)
    {
        OldSrcBase.sys_work.PadRTrig = direction;
        OldSrcBase.sys_work.PadTrig = button;
        int result = 0;
        bool runNative = WarehouseItemsPickerPatch.Prefix(ref result);
        if (!runNative) Check(result == -1, "A handled frame must keep the Items picker open.");
        return runNative;
    }

    private static void CheckEquipmentStock()
    {
        var game = OldSrcBase.game_work;
        game.member_flag[85] = 0;
        Check(EquipmentWarehouse.Capture() == null, "Optimize reached storage before Roc joined.");
        game.member_flag[85] = 8;
        var buckets = game.shiro_data.azukari_kantei;
        G_item_c_data.item_data_table[2] = new BOUGU_DATA();
        G_item_c_data.item_data_table[3] = new BOUGU_DATA();
        G_item_c_data.item_data_table[4] = new ITEM_BASE { type = 4 };
        buckets[2][0] = 2;
        buckets[4][0] = 237;
        buckets[10][1] = 1;
        var stock = EquipmentWarehouse.Capture()!;
        Check(stock.Counts.Count == 1 && stock.Counts[2] == 2 && !stock.CanStore(2, 5)
            && !stock.CanStore(4, 0), "Optimize exposed a consumable, an antique, or gear with instance data.");
        var swap = new Dictionary<ushort, int> { [2] = -1, [3] = 1 };
        Check(stock.CanApply(swap) && !stock.CanApply(new Dictionary<ushort,int> { [3] = 1 })
            && !stock.CanApply(new Dictionary<ushort,int> { [2] = -3 }), "Stock limits broke on the full warehouse.");
        stock.Apply(swap);
        Check(buckets[2][0] == 1 && buckets[3][0] == 1 && buckets[4][0] == 237 && buckets[10][1] == 1,
            "An exchange changed the wrong stock record.");
        stock.Restore();
        stock.Restore();
        Check(buckets[2][0] == 2 && buckets[3][0] == 0, "Rollback did not restore the original stock.");

        buckets[2][0] = 1;
        buckets[3][0] = 1; // Same total; selected stack differs from the capture.
        bool rejected = false;
        try { stock.Apply(swap); }
        catch (InvalidOperationException) { rejected = true; }
        Check(rejected && buckets[2][0] == 1 && buckets[3][0] == 1, "A stale warehouse snapshot overwrote live inventory.");
        game.member_flag[85] = 0;
    }

    public static void Main()
    {
        EquipmentMenuChecks.Run(Check);
        CheckEquipmentStock();
        var work = G_cmd_c.command_work;
        var menu = WindowManager.Instance.menuWindow.menuTool;
        var row = menu.partyItemList[7];
        var group = WindowManager.Instance.menuWindow.gameObject.AddComponent<CanvasGroup>();
        group.alpha = 0.75f;
        int original = row.transform.GetSiblingIndex();
        WarehouseItemsEntryPatch.Postfix();
        Check(!row.gameObject.Active && Input(0x4000), "The warehouse appeared before recruitment.");

        OldSrcBase.game_work.member_flag[85] = 8;
        S1QualityOfLife.WarehouseMenu = false;
        WarehouseItemsEntryPatch.Postfix();
        Check(!row.gameObject.Active && WarehouseMenu.RocRecruited, "Disabled field access must leave Roc recruited and the row hidden.");
        S1QualityOfLife.WarehouseMenu = true;
        WarehouseItemsEntryPatch.Postfix();
        Check(row.gameObject.Active && row.leftText.text == "Roc's Warehouse", "A previously recruited Roc did not unlock the warehouse.");
        Check(row.transform.GetSiblingIndex() == menu.partyItemList[6].transform.GetSiblingIndex() + 1,
            "Warehouse did not appear immediately below Party Items.");
        Check(!Input(0x4000) && menu.Cursor == 7 && row.Selected && work.index == 6, "Down from empty Party Items missed the warehouse.");
        Check(Input(0x4000) && !row.Selected && work.index == 6, "Down from warehouse did not delegate party-member navigation.");
        work.index = 0; // Native Down wraps from Party Items to the first hero.
        Check(!Input(0x1000) && menu.Cursor == 7, "Up from the first hero missed the warehouse.");
        Check(Input(0x1000) && work.index == 0, "Up from warehouse did not delegate back to Party Items.");
        work.index = 6;
        Check(!Input(0x4000) && Input(button: 0x40), "Cancel did not stay with the native Items menu.");

        OldSrcBase.game_work.member_flag[85] = 9;
        WarehouseItemsEntryPatch.Postfix();
        Check(!Input(0x4000) && !Input(button: 0x20) && D_azukar_c.Opened == 1 && group.alpha == 0,
            "Confirm failed to enter the native warehouse and hide the underlying menu.");
        Check(work.exit_kinshi_flg == 1 && !Input() && group.alpha == 0.75f && work.exit_kinshi_flg == 0 &&
            menu.Cursor == 7, "Leaving warehouse failed to restore the menu and its cursor.");
        D_azukar_c.FailNext = true;
        Check(!Input(button: 0x20) && !Input() && OldSrcBase.global_work.event_c.uiAzukari == null &&
            group.alpha == 0.75f && work.exit_kinshi_flg == 0, "A native error stranded the player in storage.");
        WarehouseFieldExitPatch.Prefix();
        Check(!row.gameObject.Active && row.transform.GetSiblingIndex() == original, "Field-menu exit left a reordered row.");
        OldSrcBase.game_work.member_flag[85] = 0;
        WarehouseItemsEntryPatch.Postfix();
        Check(!row.gameObject.Active && Input(0x4000), "Loading a pre-recruitment save retained warehouse access.");
        OldSrcBase.game_work.member_flag[85] = 9;
        AvatarRuntime.Current.OwnsExecution = false;
        WarehouseItemsEntryPatch.Postfix();
        Check(!row.gameObject.Active && Input(0x4000), "A foreign campaign gained the warehouse entry.");
        Console.WriteLine($"{_checks} warehouse stock, picker, recruitment, navigation and cleanup checks passed.");
    }
}
