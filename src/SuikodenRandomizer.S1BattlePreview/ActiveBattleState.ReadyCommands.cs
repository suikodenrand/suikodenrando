extern alias GSD1;

using System;
using System.Collections.Generic;
using UnityEngine;

namespace SuikodenRandomizer.S1BattlePreview;

internal static partial class ActiveBattleState
{
    private const sbyte UnselectedCommand = -1;
    private static int _readyCommandRound = -1;
    private static HeroCommandGate _heroCommand;
    private static HeroCommandPrompt? _heroPrompt;
    private static bool _commandOverlayFailed;

    internal static HeroCommandPrompt? PendingHeroPrompt => _heroCommand.Choice.Kind == HeroCommandKind.None ? _heroPrompt : null;

    internal static bool TrySkipNativeCommandBatch()
    {
        if (!BattlePreviewPlugin.ReadyCommandsEnabled || _commandOverlayFailed
            || global::SystemObject.mode != global::SystemObject.Mode.GSD1) return false;

        try
        {
            var work = GSD1.BattleBase.battle_work;
            var window = GSD1.BattleBase.window_work;
            if (work is null || work.battle_phase_no != 2 || NativeOnlyReason(work) is not null
                || window is null || window.window_step != 0 || window.window_player_index != -1 || window.selected_player != -1
                || work.pm_data is not { } rows || work.player_kazu is < 1 or > 6 || work.player_kazu >= rows.Count)
                return false;
            if (_battle != work.Pointer)
            {
                ResetEncounter();
                _battle = work.Pointer;
            }
            if (_fellBack) return false;

            // The native Fight window initializes ordinary hero commands to -1.
            // Leave this neutral value in place until a hero chooses their command.
            for (int slot = 1; slot <= work.player_kazu; slot++)
            {
                var actor = rows[slot];
                if (IsLiving(actor) && actor!.c_koudou != UnselectedCommand)
                {
                    Trace($"ATB kept native command entry because hero slot {slot} already had command {actor.c_koudou}.");
                    return false;
                }
            }

            _readyCommandRound = work.battle_kaisuu + 1;
            Trace($"ATB skipped the party command batch for round {_readyCommandRound}.");
            return true;
        }
        catch (Exception error) { return Fallback(error.Message); }
    }

    internal static void ChooseHeroCommand(HeroCommandPrompt prompt, HeroCommand choice)
    {
        if (ReferenceEquals(prompt, PendingHeroPrompt))
            _heroCommand.Choose(prompt.Slot, prompt.Identity, choice);
    }

    internal static void CommandOverlayFailed(string reason)
    {
        _commandOverlayFailed = true;
        if (_heroCommand.Slot != 0) Fallback("the hero command prompt was unavailable: " + reason);
    }

    private static bool UsesReadyCommands(GSD1.BATTLE_WORK work)
        => BattlePreviewPlugin.ReadyCommandsEnabled && !_commandOverlayFailed
            && work.Pointer == _battle && work.battle_kaisuu == _readyCommandRound;

    private static void OpenHeroCommand(GSD1.BATTLE_WORK work, int slot, GSD1.PM_DATA actor, double now, int turnDepth = -1)
    {
        if (!HasLiveFormationEnemy(work))
        {
            Scheduler.CancelReservation(slot, actor.Pointer);
            return;
        }
        if (!_heroCommand.Open(slot, actor.Pointer))
            throw new InvalidOperationException("Could not reserve the ready hero command.");

        Scheduler.Tick(now, charging: CanChargeMeters(work));
        _waitStarted = -1;
        string during = Scheduler.PendingSlot != 0 && Scheduler.PendingSlot != slot ? $" while slot {Scheduler.PendingSlot} was acting" : "";
        Trace($"ATB hero slot {slot} {actor.name} ready for a command at 100% in round {_round}{during}.");
        if (!TryOpenNativeReadyMenu(work, slot, actor, turnDepth < 0 ? _turnDepth : turnDepth))
            OpenOverlayPrompt(work, slot, actor);
    }

    private static void OpenOverlayPrompt(GSD1.BATTLE_WORK work, int slot, GSD1.PM_DATA actor)
    {
        if (BattlePreviewPlugin.CtbEnabled)
        {
            Fallback("the CTB native command menu was unavailable; overlay commands are disabled in this prototype");
            return;
        }
        if (!HasLiveFormationEnemy(work))
        {
            ClearHeroCommand(cancelReservation: true);
            return;
        }
        _heroPrompt = new HeroCommandPrompt(slot, actor.Pointer, actor.name, ReadTargets(work, slot, actor),
            ReadRunes(work, slot, actor), ReadItems(work, slot));
    }

    private static bool ResolveHeroCommand(GSD1.BATTLE_WORK work, double now, bool canDispatch, out int result)
    {
        result = Wait;
        int slot = _heroCommand.Slot;
        var rows = work.pm_data;
        var actor = rows is not null && slot <= work.player_kazu && slot < rows.Count ? rows[slot] : null;
        if (!UsesReadyCommands(work) || work.battle_kaisuu != _round || Scheduler.PendingSlot != slot
            || actor is null || actor.Pointer != _heroCommand.Identity || !IsLiving(actor)
            || !GSD1.BattleBase.ukeire_check(slot)
            || (actor.finished_flg != 0 && !IsRepeatCandidate(slot, actor, partyMember: true, readyCommands: true)))
        {
            Trace($"ATB canceled the command prompt for slot {slot} after its native eligibility changed.");
            ClearHeroCommand(cancelReservation: true);
            return true;
        }

        if (TryDispatchNativeReadyCommand(work, actor, slot, now, canDispatch, out result)) return true;

        HeroCommand choice = _heroCommand.Choice;
        if (!canDispatch || choice.Kind == HeroCommandKind.None) return true;

        var targets = Array.Empty<HeroCommandTarget>();
        HeroResourceOption? resource = null;
        if (choice.Kind == HeroCommandKind.Attack) targets = ReadTargets(work, slot, actor);
        if (choice.Kind is HeroCommandKind.Rune or HeroCommandKind.Item)
        {
            var options = choice.Kind == HeroCommandKind.Rune ? ReadRunes(work, slot, actor) : ReadItems(work, slot);
            foreach (var option in options)
            {
                if (option.Resource != choice.Resource) continue;
                resource = option;
                targets = option.Targets;
                break;
            }
        }

        string? targetName = null;
        foreach (var target in targets)
            if (target.Slot == choice.TargetSlot && target.Identity == choice.TargetIdentity)
                targetName = target.Name;
        bool targetUnavailable = targets.Length > 0 ? targetName is null : choice.TargetSlot != 0;
        if ((choice.Kind is HeroCommandKind.Rune or HeroCommandKind.Item && resource is null) || targetUnavailable
            || (choice.Kind == HeroCommandKind.Item && !SharedBattleItemBridge.Reserve(slot, resource!.Resource.Index)))
        {
            ClearHeroCommand(cancelReservation: false);
            OpenHeroCommand(work, slot, actor, now);
            Trace($"ATB refreshed slot {slot}'s command prompt after its resource or target became unavailable.");
            return true;
        }

        actor.c_item = checked((sbyte)(resource?.Resource.Index ?? 0));
        actor.c_mokuhyo = checked((sbyte)choice.TargetSlot);
        actor.c_koudou = ToNativeCommand(choice.Kind);
        Trace($"ATB chose {choice.Kind} for hero slot {slot} {actor.name} in round {_round}."
            + (resource is null ? "" : $" Resource {resource.Name} (index {resource.Resource.Index}, id {resource.Resource.Id}"
                + (resource.Uses is int uses ? $", uses before action {uses})." : ")."))
            + (targetName is null ? "" : $" Target slot {choice.TargetSlot} {targetName}."));
        ClearHeroCommand(cancelReservation: false);
        Scheduler.Tick(now, charging: !_drainingRound);
        result = Dispatch(work, slot, actor, now);
        return true;
    }

    private static sbyte ToNativeCommand(HeroCommandKind kind) => kind switch
    {
        HeroCommandKind.Attack => Attack,
        HeroCommandKind.Defend => Defend,
        HeroCommandKind.Rune => Rune,
        HeroCommandKind.Item => Item,
        _ => throw new InvalidOperationException("Unexpected ready-hero command.")
    };

    private static HeroCommandTarget[] ReadTargets(GSD1.BATTLE_WORK work, int slot, GSD1.PM_DATA actor)
    {
        if ((actor.status & RepeatBlockingStatus) != 0 || actor.unbalance_cnt != 0
            || work.p_battle_data is not { } partyData || slot >= partyData.Count
            || partyData[slot]?.player_data?.player_base?.buki_data is not { } equipped
            || GSD1.G_buki_c_data.buki_data_table is not { } weapons || equipped.buki_id >= weapons.Count
            || weapons[equipped.buki_id] is not { } weapon || work.pm_data is not { } rows)
            return Array.Empty<HeroCommandTarget>();

        const int BackRow = 4;
        const int ShortRange = 1;
        const int LongRange = 2;
        if (weapon.type == ShortRange && actor.kougeki_position >= BackRow) return Array.Empty<HeroCommandTarget>();
        var targets = new List<HeroCommandTarget>();
        for (int enemySlot = work.player_kazu + 1; enemySlot <= work.pm_kazu && enemySlot < rows.Count; enemySlot++)
        {
            var enemy = rows[enemySlot];
            if (enemy is null || !IsLivingFormationEnemy(enemySlot, enemy)
                || (weapon.type != LongRange && enemy.kougeki_position >= BackRow)) continue;
            targets.Add(new HeroCommandTarget(enemySlot, enemy.Pointer, enemy.name ?? $"Enemy {enemySlot}"));
        }
        return targets.ToArray();
    }

    private static void ClearHeroCommand(bool cancelReservation, bool keepTargetPanels = false)
    {
        ClearNativeReadyMenu(keepTargetPanels);
        if (cancelReservation && _heroCommand.Slot != 0)
            Scheduler.CancelReservation(_heroCommand.Slot, _heroCommand.Identity);
        _heroCommand.Reset();
        _heroPrompt = null;
    }
}
