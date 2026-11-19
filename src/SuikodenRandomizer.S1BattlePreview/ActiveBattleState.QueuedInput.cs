extern alias GSD1;

using System;
using System.Collections.Generic;
using SuikodenRandomizer.Shared;
using UnityEngine;
using Battle = GSD1::BattleBase;

namespace SuikodenRandomizer.S1BattlePreview;

internal static partial class ActiveBattleState
{
    private readonly record struct QueuedHeroAction(int Slot, IntPtr Identity, NativeCommand Command,
        IntPtr TargetIdentity, bool TargetWasAlive, QueuedResource Resource);

    private readonly record struct QueuedResource(int Id, IntPtr Owner = default, IntPtr Stack = default);

    private static readonly List<QueuedHeroAction> QueuedHeroActions = new(6);
    private static int _inputTurnDepth = -1;
    private static ulong _closingReadySlots;

    private static bool UsesQueuedInput(GSD1.BATTLE_WORK work) => BattlePreviewPlugin.ActiveInputEnabled && UsesReadyCommands(work);
    private static bool CanChargeMeters(GSD1.BATTLE_WORK work)
        => UsesQueuedInput(work) || (!_drainingRound && _heroCommand.Slot == 0);
    private static bool CanActInClosingRound(int slot) => !_drainingRound || (_closingReadySlots & (1UL << slot)) != 0;

    private static void SnapshotClosingMeters(ReadOnlySpan<bool> eligible)
    {
        _closingReadySlots = 0;
        for (int slot = 1; slot < eligible.Length; slot++)
            if (eligible[slot] && Scheduler.IsReadyBefore(slot, 0))
                _closingReadySlots |= 1UL << slot;
    }

    private static void AdvanceQueuedInput(GSD1.BATTLE_WORK work, double now)
    {
        if (!UsesQueuedInput(work) || work.battle_kaisuu != _round || _pausedRound == _round
            || NativeEscapeOwnsRound(_round) || NativeOnlyReason(work) is not null) return;

        if (!HasLiveFormationEnemy(work))
        {
            ClearHeroCommand(cancelReservation: false);
            ReleaseNativeTargetPanels();
            return;
        }

        int slot = _heroCommand.Slot;
        if (slot != 0)
        {
            var actor = ReadNativeTarget(work, slot);
            if (!IsLiving(actor) || actor!.Pointer != _heroCommand.Identity || !CanActInClosingRound(slot))
                ClearHeroCommand(cancelReservation: false, keepTargetPanels: _nativeTargetPanelsHeld);
            else
            {
                if (_nativeMenu is { } menu)
                {
                    TryPumpNativeMenu(menu.Depth - 1, allowPartyCommand: false);
                    if (menu.PartyCommand != 0) return;
                }
                if (_heroCommand.Slot != 0 && !QueueConfirmedHero(work, actor!)) return;
            }
        }

        if (BattlePreviewPlugin.NativeReadyMenusEnabled && !_nativeMenusFailed
            && (_inputTurnDepth < 1 || ReadMenuStep(_inputTurnDepth + 1) != IntPtr.Zero)) return;

        int next = FindReadyHero(work);
        if (next != 0)
            OpenHeroCommand(work, next, work.pm_data[next], now, _inputTurnDepth);
    }

    private static void PruneQueuedHeroes(GSD1.BATTLE_WORK work)
    {
        // A KO or slot replacement invalidates the choice even if the actor returns later.
        for (int i = QueuedHeroActions.Count - 1; i >= 0; i--)
        {
            var queued = QueuedHeroActions[i];
            var actor = ReadNativeTarget(work, queued.Slot);
            if (!IsLiving(actor) || actor!.Pointer != queued.Identity)
                DiscardQueuedHero(i);
        }
    }

    private static int FindReadyHero(GSD1.BATTLE_WORK work)
    {
        if (work.pm_data is not { } rows || work.player_kazu is < 1 or > 6 || work.player_kazu >= rows.Count)
            return 0;
        int selected = 0;
        for (int slot = 1; slot <= work.player_kazu; slot++)
        {
            if (slot == Scheduler.PendingSlot || !CanActInClosingRound(slot) || !Scheduler.IsReadyBefore(slot, selected)
                || IsHeroQueued(slot)) continue;
            var hero = rows[slot];
            if (!IsLiving(hero) || Battle.dead_check(slot) || !Battle.ukeire_check(slot)
                || (hero.finished_flg != 0 && !IsRepeatCandidate(slot, hero, partyMember: true, readyCommands: true))) continue;
            selected = slot;
        }
        return selected;
    }

    private static bool IsHeroQueued(int slot)
    {
        foreach (var queued in QueuedHeroActions)
            if (queued.Slot == slot) return true;
        return false;
    }

    private static void DiscardQueuedHero(int index)
    {
        var queued = QueuedHeroActions[index];
        if (queued.Command.Kind == Item) SharedBattleItemBridge.Release(queued.Slot);
        QueuedHeroActions.RemoveAt(index);
    }

    private static void DiscardQueuedHeroes()
    {
        for (int i = QueuedHeroActions.Count - 1; i >= 0; i--) DiscardQueuedHero(i);
    }

    private static bool QueueConfirmedHero(GSD1.BATTLE_WORK work, GSD1.PM_DATA actor)
    {
        NativeCommand command;
        IntPtr targetIdentity;
        if (_nativeMenu is { Complete: true } menu)
        {
            command = menu.Draft;
            targetIdentity = menu.TargetIdentity;
        }
        else
        {
            var choice = _heroCommand.Choice;
            if (choice.Kind == HeroCommandKind.None) return false;
            command = new NativeCommand(ToNativeCommand(choice.Kind), checked((sbyte)(choice.Resource?.Index ?? 0)),
                checked((sbyte)choice.TargetSlot));
            targetIdentity = choice.TargetIdentity;
        }

        int slot = _heroCommand.Slot;
        bool overlayItem = _nativeMenu is null && command.Kind == Item;
        var resource = ReadQueuedResource(work, slot, command, reservedItem: !overlayItem);
        if (resource.Id >= 0 && !IsHeroQueued(slot) && (!overlayItem
            || (_heroCommand.Choice.Resource is { } selected && selected.Id == resource.Id
                && selected.ItemStack == resource.Stack && SharedBattleItemBridge.Reserve(slot, command.Resource))))
        {
            QueuedHeroActions.Add(new QueuedHeroAction(slot, actor.Pointer, command, targetIdentity,
                IsLiving(ReadNativeTarget(work, command.Target)), resource));
            if (_nativeMenu is { } accepted) accepted.ItemHandedToQueue = command.Kind == Item;
            string during = Scheduler.PendingSlot != 0 ? $" while slot {Scheduler.PendingSlot} was acting" : "";
            Trace($"ATB queued hero slot {slot} {actor.name}'s command {command.Kind} targeting slot {command.Target}{during}.");
        }
        else
        {
            Trace($"ATB discarded hero slot {slot}'s command because its resource was unavailable.");
        }
        ClearHeroCommand(cancelReservation: false);
        return true;
    }

    private static bool TryDispatchQueuedHero(GSD1.BATTLE_WORK work, ReadOnlySpan<bool> eligible, double now, out int result)
    {
        result = Wait;
        for (int index = 0; index < QueuedHeroActions.Count;)
        {
            var queued = QueuedHeroActions[index];
            int slot = queued.Slot;
            var actor = ReadNativeTarget(work, slot);
            if (!IsLiving(actor) || actor!.Pointer != queued.Identity)
            {
                DiscardQueuedHero(index);
                continue;
            }
            if (slot >= eligible.Length || !eligible[slot] || !Scheduler.IsReadyBefore(slot, 0)
                || (queued.Command.Kind == Rune && (actor.status & RuneCommandBlocked) != 0))
            {
                // A busy actor keeps their place. Another ready action can pass while they wait.
                index++;
                continue;
            }

            var command = queued.Command;
            HeroCommandTarget replacement = default;
            if (command.Kind == Attack)
            {
                var targets = ReadTargets(work, slot, actor);
                replacement = HeroCommandTarget.SelectAttackTarget(targets, command.Target, queued.TargetIdentity);
                if (replacement.Slot == 0)
                {
                    // Native round setup may move a surviving enemy into range.
                    index++;
                    continue;
                }
                command = command with { Target = checked((sbyte)replacement.Slot) };
            }
            else if (!QueuedTargetStillMatches(work, queued)
                || ReadQueuedResource(work, slot, command, reservedItem: true) != queued.Resource)
            {
                DiscardQueuedHero(index);
                Trace($"ATB discarded hero slot {slot}'s queued command because its target or resource was no longer valid.");
                continue;
            }

            if (!Scheduler.TryReserveReady(slot, queued.Identity)) return false;
            QueuedHeroActions.RemoveAt(index);
            if (command.Kind == Attack
                && (replacement.Slot != queued.Command.Target || replacement.Identity != queued.TargetIdentity))
                Trace($"ATB retargeted hero slot {slot} {actor.name}'s queued Attack from slot {queued.Command.Target} to slot {replacement.Slot} {replacement.Name}.");
            command.Apply(actor);
            Trace($"ATB confirmed native command {command.Kind} for hero slot {slot} {actor.name} "
                + $"in round {_round} (resource index {command.Resource}, target slot {command.Target}).");
            result = Dispatch(work, slot, actor, now);
            return true;
        }
        return false;
    }

    private static bool QueuedTargetStillMatches(GSD1.BATTLE_WORK work, QueuedHeroAction queued)
    {
        if (queued.TargetIdentity == IntPtr.Zero) return true;
        var target = ReadNativeTarget(work, queued.Command.Target);
        return target?.Pointer == queued.TargetIdentity && IsLiving(target) == queued.TargetWasAlive;
    }

    private static QueuedResource ReadQueuedResource(GSD1.BATTLE_WORK work, int slot, NativeCommand command, bool reservedItem)
    {
        if (command.Kind is Attack or Defend) return default;
        int index = command.Resource;
        if (command.Kind == Rune)
        {
            var hero = GetHeroBase(work, slot);
            if (index < 0 || hero?.monsyo_data is not { monsyo_id: > 0 } rune) return new(-1);
            if (index != 0 && (hero.magic_point is not { } charges || index >= charges.Count || charges[index] == 0))
                return new(-1);
            return new(rune.monsyo_id);
        }
        if (command.Kind != Item || !S1BattleItemAddress.TryDecode(slot, index, out int ownerSlot, out int pocket))
            return new(-1);
        if (SharedBattleItemBridge.Enabled)
        {
            // A depleted neighbour can move the reserved stack. The token sent to
            // the game stays the same; the host tracks the stack's current pocket.
            if (reservedItem) pocket = SharedBattleItemBridge.ReservedPocket(slot, index);
        }
        else if (ownerSlot != slot) return new(-1);
        if (pocket < 0 || GetLivingItemOwner(work, ownerSlot) is not { item: { } items } owner
            || pocket >= owner.item_kazu || pocket >= items.Count || items[pocket] is not { item_id: > 0 } item
            || GSD1.G_item_c_data.item_data_table is not { } table
            || item.item_id >= table.Count || table[item.item_id]?.TryCast<GSD1.ITEM_DATA>() is not { } effect
            || (effect.type & BattleItemFlag) == 0 || effect.item_func is null
            || (item.data == 0 && effect.times != 0)) return new(-1);
        return new(item.item_id, owner.Pointer, item.Pointer);
    }

    private static GSD1.PLAYER_BASE? GetLivingItemOwner(GSD1.BATTLE_WORK work, int slot)
        => slot is >= 1 and <= S1BattleItemAddress.PartySlots && slot <= work.player_kazu
            && ReadNativeTarget(work, slot) is { hp: > 0, dead_flg: 0, IsAway: false }
            ? GetHeroBase(work, slot) : null;

    private static void ObserveQueuedTurnReturn()
    {
        int slot = Scheduler.PendingSlot;
        if (slot == 0 || !Scheduler.IsAdmitted || Battle.battle_work is not { } work || work.battle_phase_no != 4
            || !UsesQueuedInput(work) || work.battle_kaisuu != _round || work.battle_chara_no != slot
            || work.b_chara is not { } models || slot >= models.Count || models[slot] is not { action_ukeire_flg: 0 })
            return;

        // The action handler has returned to g1_battle_turn. Reset before the native inter-action timer.
        Scheduler.Tick(Time.realtimeSinceStartupAsDouble,
            Application.isFocused && Time.timeScale > 0f && NativeOnlyReason(work) is null);
        TryFinish(slot, ReadNativeTarget(work, slot));
    }
}
