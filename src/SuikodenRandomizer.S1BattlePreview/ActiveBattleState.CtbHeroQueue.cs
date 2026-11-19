extern alias GSD1;

using System;
using Battle = GSD1::BattleBase;

namespace SuikodenRandomizer.S1BattlePreview;

internal static partial class ActiveBattleState
{
    private static bool _ctbEarlyMenuUnavailable;

    private static bool HasCtbHeroQueue(int slot) => _nativeMenu is { Queued: true } menu && menu.Slot == slot;

    private static bool IsCtbHeroQueueConfirmed(int slot) => HasCtbHeroQueue(slot)
        && (_nativeMenu!.Complete || _nativeMenu.PartyCommand != 0);

    private static void ClearCtbHeroQueue()
    {
        if (_nativeMenu is { Queued: true }) ClearHeroCommand(cancelReservation: false, keepTargetPanels: true);
    }

    private static bool CanBufferHero(int slot, GSD1.PM_DATA? actor) => IsLiving(actor)
        && !Battle.dead_check(slot) && (actor!.status & RepeatBlockingStatus) == 0 && actor.unbalance_cnt == 0;

    private static void UpdateCtbHeroQueue(GSD1.BATTLE_WORK work, int nextSlot)
    {
        var actor = ReadNativeTarget(work, nextSlot);
        if (nextSlot <= 0 || nextSlot > work.player_kazu || !CanBufferHero(nextSlot, actor)
            || !UsesReadyCommands(work) || _ctbTurnStep == IntPtr.Zero || _inputTurnDepth < 1
            || ReadMenuStep(_inputTurnDepth) != _ctbTurnStep)
        {
            ClearCtbHeroQueue();
            return;
        }
        if (_nativeMenu is { Queued: true } menu && menu.Slot == nextSlot && menu.Actor == actor!.Pointer)
        {
            if (!menu.Complete && menu.PartyCommand == 0)
                TryPumpNativeMenu(_inputTurnDepth, allowPartyCommand: false);
            return;
        }
        ClearCtbHeroQueue();
        // The same fast hero can be next. Keep the acting command's item receipt
        // until the game has consumed it, before opening a second choice for that slot.
        if (Scheduler.PendingSlot == nextSlot && SharedBattleItemBridge.IsPending(nextSlot)) return;
        if (_ctbEarlyMenuUnavailable || _heroCommand.Slot != 0 || ReadMenuStep(_inputTurnDepth + 1) != IntPtr.Zero
            || !_heroCommand.Open(nextSlot, actor!.Pointer)) return;
        if (!TryOpenNativeReadyMenu(work, nextSlot, actor, _inputTurnDepth, queued: true))
        {
            ClearHeroCommand(cancelReservation: false);
            _ctbEarlyMenuUnavailable = true;
        }
    }

    // Promotion keeps the native child step, including the current submenu and cursor.
    private static bool TryUseCtbHeroQueue(GSD1.BATTLE_WORK work, int slot, GSD1.PM_DATA actor, double now, out int result)
    {
        result = Wait;
        if (_nativeMenu is not { Queued: true } menu) return false;
        if (menu.Slot != slot || menu.Actor != actor.Pointer || !CanBufferHero(slot, actor)
            || _turnDepth + 1 != menu.Depth || (!menu.Complete && menu.PartyCommand == 0
                && (menu.Step == IntPtr.Zero || menu.Step != ReadMenuStep(menu.Depth))))
        {
            ClearCtbHeroQueue();
            return false;
        }
        menu.Queued = false;
        Trace($"CTB handed the full command menu to hero slot {slot} {actor.name}.");
        return ResolveHeroCommand(work, now, canDispatch: true, out result);
    }
}
