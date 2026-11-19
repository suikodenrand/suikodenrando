extern alias GSD1;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using HarmonyLib;
using SuikodenRandomizer.Core.Combat;

namespace SuikodenRandomizer.S1BattlePreview;

internal static partial class ActiveBattleState
{
    private static CtbActionCost _ctbCommittedCost = CtbActionTiming.Describe(CtbActionKind.Attack);
    private static double _ctbCommittedReady;
    private static bool _ctbPreviewWarned;

    internal static void ObserveCtbCommandPreview(int depth, int result)
        => ObserveCtbPreview("command01", depth, result);

    internal static void ObserveCtbPreview(string nativeMethod, int depth, int result)
    {
        if (!BattlePreviewPlugin.CtbEnabled || !_pumpingNativeMenu) return;
        try
        {
            var menu = _nativeMenu;
            var work = GSD1.BattleBase.battle_work;
            var window = GSD1.BattleBase.window_work;
            if (result != Wait || menu is null || menu.Depth != depth || menu.Complete || menu.PartyCommand != 0
                || work is null || work.Pointer != _battle || window is null || window.Pointer != menu.Window
                || window.selected_player != menu.Slot || _heroCommand.Slot != menu.Slot
                || (Scheduler.PendingSlot != menu.Slot && !HasCtbHeroQueue(menu.Slot)) || menu.Step == IntPtr.Zero
                || ReadMenuStep(menu.Depth) != menu.Step || work.pm_data is not { } rows
                || menu.Slot < 1 || menu.Slot >= rows.Count || rows[menu.Slot]?.Pointer != menu.Actor) return;

            bool resource = nativeMethod is "command_magic01" or "command_item01";
            int activeStep = nativeMethod == "command01" || resource ? 2 : 1;
            if (window.window_step != activeStep) return;
            CtbActionCost? cost;
            string transition = "";
            if (nativeMethod == "command01")
            {
                cost = window.window_index switch
                {
                    0 => CtbActionTiming.Describe(CtbActionKind.Attack),
                    1 => CtbActionTiming.Describe(CtbActionKind.Defend),
                    2 => CtbActionTiming.Describe(CtbActionKind.Rune),
                    3 => CtbActionTiming.Describe(CtbActionKind.Item),
                    CommandRows - 1 => CtbActionTiming.Describe(CtbActionKind.Wait),
                    _ => null
                };
                transition = window.window_index switch
                {
                    RetreatRow => "Run / Let Go · Failed attempt costs one turn",
                    5 => "Bribe: native result",
                    _ => ""
                };
            }
            else if (nativeMethod == "command_magic01")
            {
                // This array contains Rune tiers here, not item IDs or target slots.
                var map = window.item_window_data;
                int index = window.window_index;
                if (map is null || index < 0 || index >= window.magic_kazu || index >= map.Count) return;
                int rank = map[index];
                if (rank is < 0 or > 4) return;
                cost = CtbActionTiming.Describe(CtbActionKind.Rune, ReadCtbRuneRank(work, menu.Slot, rank));
            }
            else if (nativeMethod == "command_item01" || nativeMethod.StartsWith("command_item", StringComparison.Ordinal))
                cost = CtbActionTiming.Describe(CtbActionKind.Item);
            else if (nativeMethod.StartsWith("command_magic", StringComparison.Ordinal))
                cost = CtbActionTiming.Describe(CtbActionKind.Rune, ReadCtbRuneRank(work, menu.Slot, rows[menu.Slot].c_item));
            else cost = CtbActionTiming.Describe(CtbActionKind.Attack);

            menu.CtbPreview = cost;
            menu.CtbTransition = transition;
            string signature = cost.HasValue ? cost.Value.Kind + ":" + cost.Value.Rank : transition;
            if (signature != menu.CtbLoggedPreview)
            {
                menu.CtbLoggedPreview = signature;
                Trace(cost.HasValue
                    ? FormattableString.Invariant($"CTB preview hero slot {menu.Slot}: {cost.Value.Label}, recovery x{cost.Value.Multiplier:F2}; native cursor {nativeMethod}. No resources spent.")
                    : $"CTB preview hero slot {menu.Slot}: {transition}; no CTB return forecast.");
            }
            RefreshCtbPresentation(work);
        }
        catch (Exception error)
        {
            if (_ctbPreviewWarned) return;
            _ctbPreviewWarned = true;
            Trace("CTB command preview unavailable: " + error.Message);
        }
    }

    private static int ReadCtbRuneRank(GSD1.BATTLE_WORK work, int slot, int resource)
    {
        if (slot > work.player_kazu || resource is < 1 or > 4
            || GetHeroBase(work, slot)?.monsyo_data is not { monsyo_id: > 0 } equipped
            || GSD1.G_monsyo_c_data.monsyo_data_table is not { } table || equipped.monsyo_id >= table.Count
            || table[equipped.monsyo_id] is not { type: 0, magic: not null } rune
            || resource >= rune.magic.Count || rune.magic[resource] == 0
            || GSD1.BattleBase.magic_data_table is not { } spells || rune.magic[resource] >= spells.Count
            || spells[rune.magic[resource]] is not { magic_func: not null }) return 0;
        return resource;
    }

    private static CtbActionCost ReadCtbActionCost(GSD1.BATTLE_WORK work, int slot, int kind, int resource)
    {
        // Enemy command encodings outside Rune are not classified as hero menu
        // choices. Keep their ordinary recovery rather than guessing from IDs.
        if (slot > work.player_kazu)
            return CtbActionTiming.Describe(kind == Rune ? CtbActionKind.Rune : CtbActionKind.EnemyAttack);
        return kind switch
        {
            Defend => CtbActionTiming.Describe(CtbActionKind.Defend),
            Item => CtbActionTiming.Describe(CtbActionKind.Item),
            Rune => CtbActionTiming.Describe(CtbActionKind.Rune, ReadCtbRuneRank(work, slot, resource)),
            _ => CtbActionTiming.Describe(CtbActionKind.Attack)
        };
    }

    private static CtbActionCost? ReadCtbPendingCost(GSD1.BATTLE_WORK work)
    {
        int slot = Scheduler.PendingSlot;
        if (slot < 1 || work.pm_data is not { } rows || slot >= rows.Count || rows[slot] is not { } actor) return null;
        if (Scheduler.IsAdmitted) return _ctbCommittedCost;
        if (_nativeMenu is { } menu && menu.Slot == slot && actor.Pointer == menu.Actor)
            return menu.Complete ? ReadCtbActionCost(work, slot, menu.Draft.Kind, menu.Draft.Resource) : menu.CtbPreview;
        return ReadCtbActionCost(work, slot, actor.c_koudou, actor.c_item);
    }

    private static string CtbCostCaption(CtbActionCost? cost, int returnIndex)
    {
        if (_nativeMenu is { } menu && !menu.Complete && cost is null) return menu.CtbTransition;
        if (cost is null || Scheduler.PendingSlot < 1) return "";
        var work = GSD1.BattleBase.battle_work;
        if (work is null || Scheduler.PendingSlot > work.player_kazu) return "";
        string mode = Scheduler.IsAdmitted ? "Acting" : "Preview";
        string next = returnIndex >= 0 ? "#" + (returnIndex + 1).ToString(CultureInfo.InvariantCulture) : ">8";
        return FormattableString.Invariant($"{mode}: {cost.Value.Label} | {cost.Value.Multiplier * 100:0}% | Next {next}");
    }
}

[HarmonyPatch]
internal static class CtbResourcePreviewProbe
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(GSD1.BattleBase), nameof(GSD1.BattleBase.command_magic01));
        yield return AccessTools.Method(typeof(GSD1.BattleBase), nameof(GSD1.BattleBase.command_item01));
    }
    [HarmonyPostfix]
    private static void Postfix(MethodBase __originalMethod, int __0, int __result)
        => ActiveBattleState.ObserveCtbPreview(__originalMethod.Name, __0, __result);
}

[HarmonyPatch]
internal static class CtbTargetActionPreviewProbe
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(GSD1.BattleBase), nameof(GSD1.BattleBase.command_attack));
        yield return AccessTools.Method(typeof(GSD1.BattleBase), nameof(GSD1.BattleBase.command_magic10));
        yield return AccessTools.Method(typeof(GSD1.BattleBase), nameof(GSD1.BattleBase.command_magic20));
        yield return AccessTools.Method(typeof(GSD1.BattleBase), nameof(GSD1.BattleBase.command_item10));
        yield return AccessTools.Method(typeof(GSD1.BattleBase), nameof(GSD1.BattleBase.command_item20));
    }
    [HarmonyPostfix]
    private static void Postfix(MethodBase __originalMethod, int __0, int __result)
        => ActiveBattleState.ObserveCtbPreview(__originalMethod.Name, __0, __result);
}
