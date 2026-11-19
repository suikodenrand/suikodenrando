extern alias GSD1;

using System;
using UnityEngine;
using Battle = GSD1::BattleBase;

namespace SuikodenRandomizer.S1BattlePreview;

internal static partial class ActiveBattleState
{
    private readonly record struct CtbEnemyFollowup(int ActingSlot, IntPtr ActingActor, int NextSlot, IntPtr NextActor);
    private static CtbEnemyFollowup _ctbQueuedEnemy;

    private static void ClearCtbEnemyQueue() => _ctbQueuedEnemy = default;

    private static void UpdateCtbEnemyQueue(GSD1.BATTLE_WORK work, int next)
    {
        int acting = Scheduler.PendingSlot;
        if (!Scheduler.IsAdmitted || acting <= work.player_kazu || next <= work.player_kazu
            || ReadNativeTarget(work, acting) is not { } source || ReadNativeTarget(work, next) is not { } target
            || !IsLiving(source) || !IsLiving(target))
        {
            ClearCtbEnemyQueue();
            return;
        }

        var followup = new CtbEnemyFollowup(acting, source.Pointer, next, target.Pointer);
        if (followup == _ctbQueuedEnemy) return;
        _ctbQueuedEnemy = followup;
        Trace($"CTB queued enemy slot {next} after enemy slot {acting}.");
    }

    // g1_battle_turn counts down chara_timer before it calls next_chara. Once
    // the native action has returned, a queued follow-up can use this frame.
    private static void TryAdvanceCtbEnemyQueue()
    {
        if (!Scheduler.IsAdmitted || !Application.isFocused || Time.timeScale <= 0f || _fellBack
            || Battle.battle_work is not { battle_phase_no: 4 } work
            || work.Pointer != _battle || work.battle_kaisuu != _round
            || NativeEscapeOwnsRound(_round) || NativeOnlyReason(work) is not null) return;

        int acting = Scheduler.PendingSlot;
        if (acting != work.battle_chara_no || acting <= 0 || work.b_chara is not { } models
            || acting >= models.Count || models[acting] is not { action_ukeire_flg: 0 }
            || ReadNativeTarget(work, acting) is not { finished_flg: > 0 } actor) return;

        int next = PeekCtbFollowup(work);
        if (next == 0) return;
        if (acting > work.player_kazu)
        {
            var intent = _ctbQueuedEnemy;
            if (intent.ActingSlot != acting || intent.ActingActor != actor.Pointer || intent.NextSlot != next
                || ReadNativeTarget(work, next)?.Pointer != intent.NextActor) return;
            ClearCtbEnemyQueue();
        }
        else if (!IsCtbHeroQueueConfirmed(next)) return;
        if (!TryFinish(acting, actor)) return;

        // Break or Rally can change the order at completion. The regular
        // selector still admits the next actor and the game chooses enemy AI.
        if (PeekCtbNextSlot(work) != next || !Battle.ukeire_check(next)) return;
        if (work.chara_timer > 1)
        {
            Trace($"CTB chained slot {acting} to slot {next}, skipping {work.chara_timer - 1} idle ticks.");
            work.chara_timer = 1;
        }
    }
}
