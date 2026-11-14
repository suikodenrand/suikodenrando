extern alias GSD1;

using System;
using SuikodenRandomizer.Core.Combat;
using UnityEngine;

namespace SuikodenRandomizer.S1BattlePreview;

internal static class BattlePreviewState
{
    // Native combatant indices begin at 1. Slot zero is a sentinel.
    internal const int SlotCount = 33;
    internal const double SecondsAtReferenceSpeed = 2;
    private const int SpeedIndex = 3;
    private static readonly AtbReadiness Gauges = new(SlotCount, SecondsAtReferenceSpeed);
    private static readonly Combatant[] Combatants = new Combatant[SlotCount];
    private static readonly bool[] Ready = new bool[SlotCount];
    private static BattleSampleClock _clock;
    private static OneShotActionGate _actionGate;
    private static IntPtr _trialBattle;
    private static IntPtr _trialActor;
    private static int _releasedSlot;
    private static int _round, _phase, _actorSlot, _traceCount;
    private static bool _failed;

    internal readonly record struct Combatant(IntPtr Identity, string Name, int Speed,
        bool PartyMember, bool Alive, bool Finished);

    internal static bool Visible
    {
        get
        {
            double now = Time.realtimeSinceStartupAsDouble;
            return BattlePreviewPlugin.Enabled && !_failed && !ActiveBattleState.WaitingForOpeningCharge
                && !ActiveBattleState.NativeAutoOwnsBattle
                && (_clock.IsFresh(now) || (_clock.Battle == _trialBattle && _actionGate.IsWaiting(now)));
        }
    }

    internal static Combatant GetCombatant(int slot) => Combatants[slot];
    internal static bool RepresentsBattle(IntPtr battle) => _clock.Battle == battle;
    internal static double GetGauge(int slot)
    {
        if (BattlePreviewPlugin.ActiveAtbEnabled && !ActiveBattleState.FellBack)
            return ActiveBattleState.GetGauge(slot);
        return UsesTrialGauge(slot) ? _actionGate.Gauge : Gauges.GetGauge(slot);
    }
    internal static int WaitingSlot => _actionGate.WaitingSlot;
    private static bool UsesTrialGauge(int slot) => slot > 0 && (slot == WaitingSlot || slot == _releasedSlot);

    internal static void ObserveFrame()
    {
        if (_failed) return;
        try
        {
            if (!BattlePreviewPlugin.Enabled || global::SystemObject.mode != global::SystemObject.Mode.GSD1)
            {
                EndBattle();
                return;
            }

            var work = GSD1.BattleBase.battle_work;
            if (work is null || work.battle_phase_no < 2 || work.battle_phase_no > 5
                || work.phase_func_flg != 0 || work.phase_func is not null || work.extern_func is not null)
            {
                EndBattle();
                return;
            }

            var rows = work.pm_data;
            int count = work.pm_kazu;
            int partyCount = work.player_kazu;
            if (rows is null || partyCount < 1 || partyCount > 6 || partyCount >= count
                || count >= SlotCount || count >= rows.Count)
            {
                EndBattle();
                return;
            }

            int frame = Time.frameCount;
            bool running = Application.isFocused && Time.timeScale > 0f;
            double now = Time.realtimeSinceStartupAsDouble;
            ActiveBattleState.ObserveFrame(work, now, running);
            var sample = _clock.Sample(work.Pointer, frame, now, running, out double elapsed);
            if (sample == BattleSampleKind.Duplicate) return;
            if (sample == BattleSampleKind.NewBattle)
            {
                ClearCombatants();
                Trace("Battle opened with " + partyCount + " party slots and " + (count - partyCount) + " enemy slots.");
            }

            int phase = work.battle_phase_no;
            int round = work.battle_kaisuu;
            if (phase != 4 || round != 1 || work.Pointer != _trialBattle)
                _releasedSlot = 0;
            if (phase != _phase || round != _round)
            {
                _phase = phase;
                _round = round;
                Trace("Round " + round + " phase " + phase + " frame " + frame + ".");
            }

            for (int slot = 1; slot < SlotCount; slot++)
            {
                var native = slot <= count ? rows[slot] : null;
                var stats = native?.noryoku;
                Combatant previous = Combatants[slot];
                if (native is null || stats is null || stats.Count <= SpeedIndex)
                {
                    Combatants[slot] = default;
                    Ready[slot] = false;
                    Gauges.Observe(slot, 0, false);
                    continue;
                }

                bool alive = native.hp > 0 && native.dead_flg == 0;
                var current = new Combatant(native.Pointer, native.name ?? "Slot " + slot,
                    stats[SpeedIndex], slot <= partyCount, alive, native.finished_flg != 0);
                if (current.Identity != previous.Identity)
                {
                    Gauges.ObserveNativeAction(slot);
                    Ready[slot] = false;
                    Trace("Slot " + slot + " " + current.Name + " SPD=" + current.Speed + ".");
                }
                else if (current.Finished && !previous.Finished && alive && phase == 4)
                {
                    if (!ActiveBattleState.ShowingMeters)
                        Trace("Native finished flag set on slot " + slot + " at gauge " + GaugePercent(slot) + "%.");
                    if (work.Pointer == _trialBattle && slot == _releasedSlot && current.Identity == _trialActor)
                    {
                        Trace("One-shot SPD attack finished for slot " + slot + " " + current.Name + ".");
                        _releasedSlot = 0;
                    }
                    Gauges.ObserveNativeAction(slot);
                    Ready[slot] = false;
                }
                Combatants[slot] = current;
                Gauges.Observe(slot, current.Speed, alive);
            }

            int actorSlot = phase == 4 ? work.battle_chara_no : 0;
            if (actorSlot != _actorSlot && actorSlot > 0 && actorSlot <= count
                && Combatants[actorSlot].Identity != IntPtr.Zero)
                Trace("Native active actor changed to slot " + actorSlot + " at gauge " + GaugePercent(actorSlot) + "%.");
            _actorSlot = actorSlot;

            // Native battle callbacks can span several render frames.
            Gauges.Advance(elapsed, running);
            for (int slot = 1; slot <= count; slot++)
            {
                if ((BattlePreviewPlugin.ActiveAtbEnabled && !ActiveBattleState.FellBack) || UsesTrialGauge(slot)) continue;
                bool ready = Gauges.IsReady(slot);
                if (ready && !Ready[slot])
                    Trace("Meter ready for slot " + slot + " " + Combatants[slot].Name + ".");
                Ready[slot] = ready;
            }
        }
        catch (Exception error) { StopPreview(error); }
    }

    internal static void ObserveSelection(int slot)
    {
        // -1 is the native retry sentinel and can repeat enough to exhaust the encounter trace.
        if (slot == -1 || !BattlePreviewPlugin.Enabled || _failed || _clock.Battle == IntPtr.Zero) return;
        try
        {
            if (GSD1.BattleBase.battle_work?.Pointer != _clock.Battle
                || !_clock.IsFresh(Time.realtimeSinceStartupAsDouble)) return;
            string gauge = slot > 0 && slot < SlotCount ? " gauge=" + GaugePercent(slot) + "%" : "";
            Trace("Native next_chara returned " + slot + " in round " + _round + gauge + ".");
        }
        catch (Exception error) { StopPreview(error); }
    }

    internal static bool WaitForAttack()
    {
        if (_failed || _actionGate.Consumed) return false;
        try
        {
            var work = GSD1.BattleBase.battle_work;
            double now = Time.realtimeSinceStartupAsDouble;
            int slot = work?.next_chara_no ?? 0;
            var rows = work?.pm_data;
            var actor = rows is not null && slot > 0 && slot < rows.Count ? rows[slot] : null;
            string? blocked = AttackBlockReason(work, slot, actor);
            int speed = 0;
            if (blocked is null && WaitingSlot == 0)
            {
                var stats = actor!.noryoku;
                if (stats is not null && stats.Count > SpeedIndex) speed = stats[SpeedIndex];
                if (speed <= 0) blocked = "the selected actor had no positive live SPD";
            }

            int waitingSlot = WaitingSlot;
            double duration = speed > 0 ? Gauges.SecondsToFull(speed) : 0;
            var decision = _actionGate.Evaluate(slot, blocked is null, duration, now);
            switch (decision)
            {
                case ActionGateDecision.Armed:
                    _trialBattle = work!.Pointer;
                    _trialActor = actor!.Pointer;
                    Gauges.ObserveNativeAction(slot);
                    Ready[slot] = false;
                    Trace($"One-shot SPD attack queued for slot {slot} {actor.name} at 0% (SPD={speed}, expected {duration:F2}s).");
                    return true;
                case ActionGateDecision.Wait:
                    return true;
                case ActionGateDecision.Release:
                    _releasedSlot = slot;
                    Trace($"One-shot SPD attack released at 100% for slot {slot} {actor!.name} after {_actionGate.ElapsedSeconds:F2}s.");
                    break;
                case ActionGateDecision.TimedOut:
                    TraceFallback("the 15s safety deadline elapsed");
                    break;
                case ActionGateDecision.ActorChanged:
                    TraceFallback($"native selection changed from slot {waitingSlot} to {slot}");
                    break;
                case ActionGateDecision.Ineligible:
                    TraceFallback(blocked ?? "the native attack became ineligible");
                    break;
            }
        }
        catch (Exception error) { StopPreview(error); }
        return false;
    }

    private static string? AttackBlockReason(GSD1.BATTLE_WORK? work, int slot, GSD1.PM_DATA? actor)
    {
        if (!BattlePreviewPlugin.OneShotAttackEnabled || global::SystemObject.mode != global::SystemObject.Mode.GSD1)
            return "the S1 one-shot trial is inactive";
        IntPtr battle = WaitingSlot == 0 ? _clock.Battle : _trialBattle;
        if (work is null || work.Pointer != battle) return "the native battle changed";
        if (!Application.isFocused) return "the game lost focus";
        if (Time.timeScale <= 0f) return "the game paused";
        if (work.battle_phase_no != 4 || work.battle_kaisuu != 1) return "the first action phase ended";
        if (work.is_auto) return "auto battle is active";
        if (work.party_attack_flg != 0 || work.phase_func_flg != 0 || work.phase_func is not null || work.extern_func is not null
            || work.flg_table is not { Count: > 6 } flags || flags[6])
            return "the battle entered a special action path";
        if (slot <= 0 || slot >= SlotCount || slot > work.player_kazu || actor is null)
            return "the native actor is not a party member";
        if (WaitingSlot != 0 && actor.Pointer != _trialActor) return "the native actor changed";
        if (actor.hp == 0 || actor.dead_flg != 0 || actor.finished_flg != 0) return "the actor is KO or finished";
        if (actor.status != 0 || actor.unbalance_cnt != 0) return "the actor has a status effect";
        return actor.c_koudou == 0 ? null : "the selected command is not a normal attack";
    }

    private static void TraceFallback(string reason)
        => Trace($"One-shot SPD attack bypassed after {_actionGate.ElapsedSeconds:F2}s (last charge {(int)(_actionGate.Gauge * 100)}%). Reason: {reason}.");

    internal static void ResetEncounter()
    {
        NativeSpdMeters.ResetEncounter();
        EndBattle();
        ActiveBattleState.ResetEncounter();
        _actionGate.Reset();
        _trialBattle = IntPtr.Zero;
        _trialActor = IntPtr.Zero;
        _releasedSlot = 0;
    }

    private static void EndBattle()
    {
        if (_clock.Battle == IntPtr.Zero) return;
        _clock.Reset();
        ClearCombatants();
    }

    private static void ClearCombatants()
    {
        Gauges.Reset();
        Array.Clear(Combatants, 0, Combatants.Length);
        Array.Clear(Ready, 0, Ready.Length);
        _phase = _round = _actorSlot = _traceCount = 0;
    }

    private static int GaugePercent(int slot) => (int)(GetGauge(slot) * 100);

    private static void Trace(string message)
    {
        if (_traceCount >= 100) return;
        _traceCount++;
        BattlePreviewPlugin.PreviewLog?.LogInfo("[S1 ATB] " + message);
    }

    private static void StopPreview(Exception error)
    {
        EndBattle();
        _failed = true;
        BattlePreviewPlugin.PreviewLog?.LogWarning("S1 battle preview stopped sampling. " + error.Message);
    }
}
