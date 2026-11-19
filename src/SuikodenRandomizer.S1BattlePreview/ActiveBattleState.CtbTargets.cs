extern alias GSD1;

using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace SuikodenRandomizer.S1BattlePreview;

internal static partial class ActiveBattleState
{
    private static int _ctbTargetFrame = -1;
    private static int _ctbTargetSlot;
    private static IntPtr _ctbTargetActor;
    private static bool _ctbTargetWarned;

    private static void ClearCtbTarget()
    {
        _ctbTargetFrame = -1;
        _ctbTargetSlot = 0;
        _ctbTargetActor = IntPtr.Zero;
    }

    internal static void ObserveCtbEnemyTarget(int result)
    {
        if (!BattlePreviewPlugin.CtbEnabled || !_pumpingNativeMenu) return;
        try { ObserveCtbEnemyTargetCore(result); }
        catch (Exception error)
        {
            ClearCtbTarget();
            if (_ctbTargetWarned) return;
            _ctbTargetWarned = true;
            BattlePreviewPlugin.PreviewLog?.LogWarning("CTB target highlight paused: " + error.Message);
        }
    }

    private static void ObserveCtbEnemyTargetCore(int result)
    {
        ClearCtbTarget();
        var work = GSD1.BattleBase.battle_work;
        var window = GSD1.BattleBase.window_work;
        var menu = _nativeMenu;
        // The native target cursor can belong to the reserved hero or the next ally choosing early.
        // selected_monster is deliberately not used: Rune/Item leave that cache stale.
        if (result != Wait || menu is null || menu.Complete || work is null || work.Pointer != _battle
            || window is null || window.Pointer != menu.Window || window.selected_player != menu.Slot
            || window.window_step != 1
            || menu.Step == IntPtr.Zero || ReadMenuStep(menu.Depth) != menu.Step
            || _heroCommand.Slot != menu.Slot || (Scheduler.PendingSlot != menu.Slot && !HasCtbHeroQueue(menu.Slot))
            || work.pm_data is not { } actors || menu.Slot >= actors.Count
            || actors[menu.Slot]?.Pointer != menu.Actor
            || window.monster_window_data is not { } map) return;
        int index = window.window_index;
        if (index < 0 || index >= work.nokori_monster || index >= map.Count) return;
        int slot = map[index];
        if (slot <= work.player_kazu || slot > work.pm_kazu || slot >= actors.Count
            || !IsLiving(actors[slot]) || GSD1.BattleBase.dead_check(slot)) return;
        _ctbTargetFrame = Time.frameCount;
        _ctbTargetSlot = slot;
        _ctbTargetActor = actors[slot].Pointer;
        RefreshCtbPresentation(work);
    }

    private static int ReadCtbTarget(GSD1.BATTLE_WORK work)
    {
        int slot = _ctbTargetSlot;
        return _ctbTargetFrame == Time.frameCount && _nativeMenu is { Complete: false } menu
            && menu.Slot == _heroCommand.Slot && work.pm_data is { } actors
            && slot > work.player_kazu && slot <= work.pm_kazu && slot < actors.Count
            && actors[slot]?.Pointer == _ctbTargetActor
            && CtbEnemyBadges.Get(slot, _ctbTargetActor.ToInt64()).HasValue ? slot : 0;
    }

    internal static int CtbEnemyListCount(GSD1.BATTLE_WORK work, GSD1.BattleBase.WINDOW_WORK? window)
    {
        // The native-menu bridge temporarily substitutes this prefix count while
        // pumping selectors, then restores the stock count before LateUpdate.
        // Array capacity includes stale tail entries and is never a row count.
        var menu = _nativeMenu;
        if (work.Pointer == _battle && window is not null && menu is not null && !menu.Complete
            && menu.Window == window.Pointer && window.selected_player == menu.Slot
            && work.pm_data is { } actors && menu.Slot > 0 && menu.Slot < actors.Count
            && actors[menu.Slot]?.Pointer == menu.Actor)
            return Math.Max(0, menu.EnemyCount);
        return Math.Max(0, (int)work.nokori_monster);
    }
}

// These are three separate native selector functions; item and rune do not share
// Attack's selected_monster cache. The postfix never changes commands or targets.
[HarmonyPatch]
internal static class CtbEnemyTargetProbe
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(GSD1.BattleBase), nameof(GSD1.BattleBase.command_attack));
        yield return AccessTools.Method(typeof(GSD1.BattleBase), nameof(GSD1.BattleBase.command_item10));
        yield return AccessTools.Method(typeof(GSD1.BattleBase), nameof(GSD1.BattleBase.command_magic10));
    }

    [HarmonyPostfix]
    private static void Postfix(int __result) => ActiveBattleState.ObserveCtbEnemyTarget(__result);
}
