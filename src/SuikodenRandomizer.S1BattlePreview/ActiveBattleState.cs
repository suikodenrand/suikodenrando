extern alias GSD1;

using System;
using System.Text;
using UnityEngine;

namespace SuikodenRandomizer.S1BattlePreview;

internal static partial class ActiveBattleState
{
    private const int SpeedIndex = 3;
    private const int DexterityIndex = 1;
    private const int Wait = -1;
    private const int Attack = 0;
    private const int Defend = 1;
    // Native round/command checks use these bits for incapacitated actors and balloon removal.
    private const int RepeatBlockingStatus = 0x00E8;
    private const double WaitLimitSeconds = 18;
    private static readonly MeterActionScheduler Scheduler = new(BattlePreviewState.SlotCount, BattlePreviewState.SecondsAtReferenceSpeed);
    private static IntPtr _battle;
    private static int _round;
    private static int _pausedRound;
    private static bool _drainingRound;
    private static bool _fellBack;
    private static double _waitStarted = -1;
    private static double _dispatchedAt;
    private static int _repeatSlot;
    private static IntPtr _repeatActor;
    private static bool _repeatReopened;

    internal static bool ShowingMeters => _battle != IntPtr.Zero && !_fellBack && !NativeAutoOwnsBattle;
    internal static bool FellBack => _fellBack;
    internal static bool WaitingForOpeningCharge => BattlePreviewPlugin.OpeningChargeEnabled && !_fellBack && _round < 1;
    internal static double GetGauge(int slot) => Scheduler.GetGauge(slot);

    internal static bool TrySelect(out int result)
    {
        if (BattlePreviewPlugin.CtbEnabled) return TrySelectCtb(out result);
        result = Wait;
        if (!BattlePreviewPlugin.ActiveAtbEnabled || global::SystemObject.mode != global::SystemObject.Mode.GSD1)
            return false;

        try
        {
            var work = GSD1.BattleBase.battle_work;
            if (work is null || work.battle_phase_no != 4) return false;
            if (_battle != work.Pointer)
            {
                ResetEncounter();
                _battle = work.Pointer;
            }

            if (_fellBack || NativeAutoOwnsBattle) return false;
            if (_pausedRound == work.battle_kaisuu || NativeEscapeOwnsRound(work.battle_kaisuu)) return false;
            if (NativeOnlyReason(work) is { } reason) return Fallback(reason);
            var rows = work.pm_data;
            var models = work.b_chara;
            int count = work.pm_kazu;
            int party = work.player_kazu;
            if (rows is null || models is null || party < 1 || party > 6 || party >= count
                || count >= BattlePreviewState.SlotCount || count >= rows.Count || count >= models.Count)
                return Fallback("the native combatant roster was incomplete");

            double now = Time.realtimeSinceStartupAsDouble;
            bool charging = Application.isFocused && Time.timeScale > 0f;
            bool newRound = _round != work.battle_kaisuu;
            if (newRound) _drainingRound = false;
            bool queuedInput = UsesQueuedInput(work);
            Scheduler.Tick(now, charging && !newRound && CanChargeMeters(work));
            int pending = Scheduler.PendingSlot;
            if (pending != 0 && (queuedInput || pending != _heroCommand.Slot))
            {
                var actor = pending <= count ? rows[pending] : null;
                var model = pending <= count ? models[pending] : null;
                if (model is null) return Fallback("the dispatched actor model was lost");
                if (model.action_ukeire_flg != 0)
                {
                    if (!charging) _dispatchedAt = now;
                    if (now - _dispatchedAt >= WaitLimitSeconds)
                        return Fallback("the dispatched actor stayed busy");
                    return true;
                }
                if (!TryFinish(pending, actor))
                {
                    if (IsLiving(actor)) return Fallback("the native selector resumed without confirming the dispatched action");
                    RestoreRepeatFlag();
                    Scheduler.Observe(pending, IntPtr.Zero, 0, alive: false);
                    Trace($"ATB cleared the reservation for slot {pending} after KO or removal.");
                }
            }

            if (!queuedInput && _heroCommand.Slot != 0)
                return ResolveHeroCommand(work, now, charging, out result);
            if (queuedInput)
            {
                if (!HasLiveFormationEnemy(work)) return false;
                if (_nativeMenu is { PartyCommand: > 0 }) return true;
            }

            Span<bool> eligible = stackalloc bool[count + 1];
            eligible.Clear();
            bool hasCandidate = false, hasFirstCandidate = false, hasUnspent = false;
            bool partyAlive = false, enemyAlive = false;
            bool readyCommands = UsesReadyCommands(work);
            for (int slot = 1; slot <= count; slot++)
            {
                var actor = rows[slot];
                bool alive = IsLiving(actor);
                ObserveSlot(work, slot, actor, alive);
                if (!alive || GSD1.BattleBase.dead_check(slot)) continue;

                bool partyMember = slot <= party;
                if (partyMember) partyAlive = true; else enemyAlive = true;
                bool spent = actor!.finished_flg != 0;
                if (!spent) hasUnspent = true;
                if (spent && (newRound || !IsRepeatCandidate(slot, actor, partyMember, readyCommands))) continue;
                if (!spent && newRound && partyMember && !readyCommands)
                {
                    int command = actor.c_koudou;
                    if (command is not (Attack or Defend))
                        return Fallback($"round {work.battle_kaisuu}, hero slot {slot} {actor.name} queued unsupported command "
                            + $"{command} (status=0x{actor.status:X4}, unbalance={actor.unbalance_cnt})");
                    // The native action handler owns status and unbalance rules for this command.
                    if (command == Defend || actor.status != 0 || actor.unbalance_cnt != 0)
                        Trace($"ATB accepted hero slot {slot} {actor.name} with command {command}, "
                            + $"status=0x{actor.status:X4}, unbalance={actor.unbalance_cnt} in round {work.battle_kaisuu}.");
                }
                if (models[slot] is null || !GSD1.BattleBase.ukeire_check(slot)) continue;

                eligible[slot] = true;
                hasCandidate = true;
                if (!spent) hasFirstCandidate = true;
            }

            // Preserve the game's own end-of-round and animation-wait decisions.
            if (!hasCandidate || !partyAlive || !enemyAlive)
            {
                _waitStarted = -1;
                return false;
            }

            if (!hasFirstCandidate)
            {
                // Let native wait for an outstanding first action to become admissible.
                if (hasUnspent)
                {
                    _waitStarted = -1;
                    return false;
                }

                // Only meters full at the boundary can act again before native upkeep.
                if (queuedInput && !_drainingRound) SnapshotClosingMeters(eligible);
                _drainingRound = true;
                if (!queuedInput) Scheduler.Tick(now, charging: false);
            }
            else if (_drainingRound)
            {
                _drainingRound = false;
                Scheduler.Tick(now, charging);
            }

            if (newRound)
            {
                if (WaitingForOpeningCharge && work.battle_kaisuu == 1)
                    SeedOpeningCharge(work);
                _round = work.battle_kaisuu;
                Scheduler.BeginRound();
                Trace($"ATB round {_round} armed with {party} hero and {count - party} enemy slots.");
                if (_round > 1) TraceMeters(work, "carry-in");
                Scheduler.Tick(now, charging);
            }

            if (!charging)
            {
                _waitStarted = -1;
                return true;
            }

            bool awaitingHeroInput = false;
            if (queuedInput)
            {
                if (_drainingRound)
                    for (int slot = 1; slot <= count; slot++)
                        eligible[slot] &= CanActInClosingRound(slot);
                if (TryDispatchQueuedHero(work, eligible, now, out result)) return true;
                awaitingHeroInput = _heroCommand.Slot != 0;
                bool hasUnqueuedFirstAction = false;
                for (int slot = party + 1; slot <= count; slot++)
                    hasUnqueuedFirstAction |= eligible[slot] && rows[slot].finished_flg == 0;
                for (int slot = 1; slot <= party; slot++)
                {
                    bool canChoose = eligible[slot] && !IsHeroQueued(slot);
                    awaitingHeroInput |= canChoose && Scheduler.IsReadyBefore(slot, 0);
                    hasUnqueuedFirstAction |= canChoose && rows[slot].finished_flg == 0;
                    eligible[slot] = false;
                }
                // A deferred command must not keep native round upkeep from moving the formation
                // or clearing status effects that can make the command usable again.
                if (!_drainingRound && !awaitingHeroInput && !hasUnqueuedFirstAction && QueuedHeroActions.Count != 0)
                    return false;
            }

            int selected = Scheduler.Select(eligible);
            if (selected == 0)
            {
                if (awaitingHeroInput)
                {
                    _waitStarted = -1;
                    return true;
                }
                if (_drainingRound) return false;
                if (_waitStarted < 0) _waitStarted = now;
                if (now - _waitStarted >= WaitLimitSeconds)
                    return Fallback("no eligible meter reached full before the safety deadline");
                return true;
            }

            if (selected <= party && readyCommands)
            {
                OpenHeroCommand(work, selected, rows[selected], now);
                return true;
            }

            result = Dispatch(work, selected, rows[selected], now);
            return true;
        }
        catch (Exception error) { return Fallback(error.Message); }
    }

    private static int Dispatch(GSD1.BATTLE_WORK work, int selected, GSD1.PM_DATA chosen, double now)
    {
        _closingReadySlots &= ~(1UL << selected);
        _waitStarted = -1;
        _dispatchedAt = now;
        string side = selected <= work.player_kazu ? "hero" : "enemy";
        if (BattlePreviewPlugin.CtbEnabled)
        {
            if (chosen.finished_flg == 1)
            {
                _repeatSlot = selected;
                _repeatActor = chosen.Pointer;
            }
            Trace(FormattableString.Invariant($"CTB dispatch {side} slot {selected} {chosen.name}, command {chosen.c_koudou}, target {chosen.c_mokuhyo}, logical time {Scheduler.Ctb.Now:F1}."));
            return selected;
        }
        int speed = ReadSpeed(chosen);
        int dexterity = ReadDexterity(chosen);
        bool modified = selected > work.player_kazu && BattlePreviewPlugin.EnemyDexChargeEnabled;
        double multiplier = modified ? MeterActionScheduler.EnemyDexMultiplier(dexterity) : 1;
        string dex = modified ? FormattableString.Invariant($", DEX {dexterity} ×{multiplier:F3}") : "";
        string timing = FormattableString.Invariant(
            $" Active charge to first full sample: {Scheduler.GetChargeSeconds(selected):F3}s (SPD {speed}{dex}, empty-meter baseline {Scheduler.SecondsToFull(speed, multiplier):F3}s).");
        if (chosen.finished_flg == 1)
        {
            _repeatSlot = selected;
            _repeatActor = chosen.Pointer;
            string closing = _drainingRound ? " (closing ready meters)" : "";
            Trace($"ATB repeat dispatch {side} slot {selected} {chosen.name} at 100% in round {_round}{closing}." + timing);
        }
        else
        {
            Trace($"ATB dispatch {side} slot {selected} {chosen.name} at 100% in round {_round}." + timing);
        }
        return selected;
    }

    internal static void ObserveNativeSelection(int result)
    {
        if (result != 0 || !ShowingMeters || Scheduler.PendingSlot != 0) return;
        try
        {
            var work = GSD1.BattleBase.battle_work;
            if (work is not null && work.Pointer == _battle)
                PauseRound(work, Time.realtimeSinceStartupAsDouble);
        }
        catch (Exception error) { Fallback(error.Message); }
    }

    internal static void ObserveActionEntry()
    {
        if (!ShowingMeters || Scheduler.PendingSlot == 0) return;
        try
        {
            var work = GSD1.BattleBase.battle_work;
            if (work is null || work.Pointer != _battle || work.battle_phase_no != 4) return;
            int slot = work.battle_chara_no;
            if (slot != Scheduler.PendingSlot || work.pm_data is not { } rows || slot >= rows.Count) return;
            var actor = rows[slot];
            if (actor is null || !Scheduler.Admit(slot, actor.Pointer)) return;
            if (slot == _repeatSlot)
            {
                if (work.battle_kaisuu != _round || actor.Pointer != _repeatActor
                    || !IsRepeatCandidate(slot, actor, slot <= work.player_kazu, UsesReadyCommands(work)))
                {
                    Fallback($"the reserved repeat for slot {slot} changed before native action entry");
                    return;
                }

                // Clear only the admitted actor's spent flag. Native resolution sets it again.
                _repeatReopened = true;
                actor.finished_flg = 0;
                Trace($"ATB reopened slot {slot} {actor.name} for its repeat action in round {_round}.");
            }
            Trace($"ATB native action started for slot {slot} {actor.name}.");
            if (BattlePreviewPlugin.CtbEnabled) CaptureCtbAction(work, slot);
        }
        catch (Exception error) { Fallback(error.Message); }
    }

    internal static void ObserveFrame(GSD1.BATTLE_WORK work, double now, bool running)
    {
        if (!ShowingMeters || _battle != work.Pointer) return;
        try
        {
            bool resolving = work.battle_phase_no == 4;
            Scheduler.Tick(now, resolving && running && _round == work.battle_kaisuu
                && _pausedRound != _round && !NativeEscapeOwnsRound(_round) && CanChargeMeters(work) && NativeOnlyReason(work) is null);
            var rows = work.pm_data;
            if (rows is null || work.pm_kazu >= rows.Count) return;

            // A battle can go straight to its closing phase after a decisive hit.
            int pending = Scheduler.PendingSlot;
            if (!resolving)
            {
                bool keepPanels = _nativeTargetPanelsHeld && HasLiveFormationEnemy(work)
                    && !NativeEscapeOwnsRound(work.battle_kaisuu);
                ClearHeroCommand(cancelReservation: true, keepTargetPanels: keepPanels);
                if (!keepPanels) ReleaseNativeTargetPanels();
                pending = Scheduler.PendingSlot;
            }
            if (!resolving && pending != 0
                && (pending >= rows.Count || !TryFinish(pending, rows[pending])))
            {
                Fallback("the native action phase ended before completion could be confirmed");
                return;
            }

            for (int slot = 1; slot <= work.pm_kazu; slot++)
            {
                var actor = rows[slot];
                ObserveSlot(work, slot, actor, IsLiving(actor));
            }
            PruneQueuedHeroes(work);
            if (_repeatSlot != 0 && Scheduler.PendingSlot == 0) RestoreRepeatFlag();
            if (!resolving) PauseRound(work, now);
            else if (running && !BattlePreviewPlugin.CtbEnabled) AdvanceQueuedInput(work, now);
            if (BattlePreviewPlugin.CtbEnabled)
            {
                if (!resolving || work.battle_kaisuu != _round || NativeEscapeOwnsRound(_round)
                    || NativeOnlyReason(work) is not null || !HasLiveFormationEnemy(work))
                    ClearCtbQueues();
                else if (running && Scheduler.IsAdmitted)
                {
                    int next = PeekCtbFollowup(work);
                    if (Scheduler.PendingSlot <= work.player_kazu) UpdateCtbHeroQueue(work, next);
                    else UpdateCtbEnemyQueue(work, next);
                }
                RefreshCtbPresentation(work);
            }
        }
        catch (Exception error) { Fallback(error.Message); }
    }

    internal static void ResetEncounter()
    {
        ResetNativeReadyState();
        Scheduler.Reset();
        _battle = IntPtr.Zero;
        _round = -1;
        _pausedRound = -1;
        _drainingRound = false;
        _closingReadySlots = 0;
        _inputTurnDepth = -1;
        DiscardQueuedHeroes();
        _fellBack = false;
        _waitStarted = -1;
        _dispatchedAt = 0;
        ClearRepeat();
        ClearHeroCommand(cancelReservation: false);
        _readyCommandRound = -1;
        ResetCtb();
    }

    private static string? NativeOnlyReason(GSD1.BATTLE_WORK work)
    {
        if (work.is_auto) return "auto battle is active";
        if (work.party_attack_flg != 0) return $"the party attack override is {work.party_attack_flg}";
        if (work.phase_func_flg != 0 || work.phase_func is not null) return $"a scripted phase is active (flag {work.phase_func_flg})";
        if (work.extern_func is not null) return "an external battle callback is active";
        if (work.flg_table is not { Count: > 6 } flags) return "the native battle flags were unavailable";
        return flags[6] ? "the native forced-selection flag is set" : null;
    }

    private static void PauseRound(GSD1.BATTLE_WORK work, double now)
    {
        if (_round < 1 || _round != work.battle_kaisuu || _pausedRound == _round) return;
        Scheduler.Tick(now, charging: false);
        _pausedRound = _round;
        TraceMeters(work, "paused");
    }

    private static void TraceMeters(GSD1.BATTLE_WORK work, string transition)
    {
        var rows = work.pm_data;
        if (rows is null || work.pm_kazu >= rows.Count) return;
        var message = new StringBuilder($"ATB round {_round} {transition} meters:");
        for (int slot = 1; slot <= work.pm_kazu; slot++)
        {
            var actor = rows[slot];
            if (!IsLiving(actor)) continue;
            string side = slot <= work.player_kazu ? "hero" : "enemy";
            message.Append(FormattableString.Invariant($" {side} {slot} {actor!.name}={Scheduler.GetGauge(slot) * 100:F2}%;"));
        }
        Trace(message.ToString());
    }

    private static bool IsLiving(GSD1.PM_DATA? actor) => actor is not null && actor.hp > 0 && actor.dead_flg == 0;

    private static void SeedOpeningCharge(GSD1.BATTLE_WORK work)
    {
        var random = System.Random.Shared;
        var rows = work.pm_data;
        for (int slot = 1; slot <= work.pm_kazu; slot++)
        {
            var actor = rows[slot];
            if (!IsLiving(actor) || GSD1.BattleBase.dead_check(slot)) continue;
            int speed = ReadSpeed(actor);
            int dex = ReadDexterity(actor);
            double fraction = MeterActionScheduler.OpeningCharge(speed, dex,
                random.NextDouble(), random.NextDouble(), random.NextDouble());
            Scheduler.SeedOpeningCharge(slot, fraction);
            string side = slot <= work.player_kazu ? "hero" : "enemy";
            Trace(FormattableString.Invariant($"ATB opening charge for {side} slot {slot} {actor.name}: {fraction * 100:F2}% (SPD {speed}, DEX {dex})."));
        }
    }

    private static bool IsRepeatCandidate(int slot, GSD1.PM_DATA actor, bool partyMember, bool readyCommands)
    {
        if (!BattlePreviewPlugin.RepeatTurnsEnabled || actor.finished_flg != 1 || !IsLiving(actor)
            || !Scheduler.CanRepeat(slot, actor.Pointer) || (actor.status & RepeatBlockingStatus) != 0
            || actor.unbalance_cnt != 0) return false;
        if (!partyMember) return true;
        // During a native cursor callback the actor contains the menu's temporary draft.
        var command = _pumpingNativeMenu && _nativeMenu is { } menu && menu.Actor == actor.Pointer
            ? menu.StockCommand.Kind : actor.c_koudou;
        return command is Attack or Defend || (readyCommands && command is Rune or Item);
    }

    private static void ObserveSlot(GSD1.BATTLE_WORK work, int slot, GSD1.PM_DATA? actor, bool alive)
    {
        bool enemy = slot > work.player_kazu;
        int speed = ReadSpeed(actor);
        if (Scheduler.UseCtb && alive && actor is not null)
            speed = ReadTunedCtbSpeed(work, slot, actor, speed, enemy);
        double multiplier = enemy && BattlePreviewPlugin.EnemyDexChargeEnabled
            ? MeterActionScheduler.EnemyDexMultiplier(ReadDexterity(actor)) : 1;
        Scheduler.Observe(slot, actor?.Pointer ?? IntPtr.Zero, speed, alive, multiplier);
    }

    private static int ReadDexterity(GSD1.PM_DATA? actor)
    {
        var stats = actor?.noryoku;
        return stats is not null && stats.Count > DexterityIndex ? stats[DexterityIndex] : 0;
    }

    private static int ReadSpeed(GSD1.PM_DATA? actor)
    {
        var stats = actor?.noryoku;
        return stats is not null && stats.Count > SpeedIndex ? stats[SpeedIndex] : 0;
    }

    private static bool TryFinish(int slot, GSD1.PM_DATA? actor)
    {
        if (actor is null || actor.finished_flg == 0 || !Scheduler.Finish(slot, actor.Pointer)) return false;
        if (BattlePreviewPlugin.CtbEnabled) CompleteCtbAction(slot, actor);
        if (slot == _repeatSlot) ClearRepeat();
        if (!BattlePreviewPlugin.CtbEnabled) Trace($"ATB completed slot {slot} {actor.name}; meter reset to 0%.");
        RefreshHeldNativePartyStatus();
        return true;
    }

    private static bool Fallback(string reason)
    {
        RestoreRepeatFlag();
        DiscardQueuedHeroes();
        ClearHeroCommand(cancelReservation: true);
        ReleaseNativeTargetPanels();
        _fellBack = true;
        if (BattlePreviewPlugin.CtbEnabled)
        {
            ClearCtbQueues();
            CtbPresentation.Turns = Array.Empty<CtbTurnView>();
            CtbPresentation.Enemies = Array.Empty<CtbEnemyView>();
            CtbPresentation.Support = "";
            CtbPresentation.PreviewLabel = "";
            ClearCtbTarget();
        }
        Trace($"ATB returned to native timing for this encounter because {reason}.");
        return false;
    }

    private static void RestoreRepeatFlag()
    {
        try
        {
            if (!_repeatReopened) return;
            var work = GSD1.BattleBase.battle_work;
            if (work is null || work.Pointer != _battle || work.battle_phase_no != 4 || work.battle_kaisuu != _round
                || work.pm_data is not { } rows || _repeatSlot >= rows.Count)
                return;

            var actor = rows[_repeatSlot];
            if (actor is not null && actor.Pointer == _repeatActor && actor.finished_flg == 0)
                actor.finished_flg = 1;
        }
        catch (Exception error)
        {
            BattlePreviewPlugin.PreviewLog?.LogWarning("Could not restore the S1 repeat actor's spent flag. " + error.Message);
        }
        finally { ClearRepeat(); }
    }

    private static void ClearRepeat()
    {
        _repeatSlot = 0;
        _repeatActor = IntPtr.Zero;
        _repeatReopened = false;
    }

    private static void Trace(string message) => BattlePreviewPlugin.PreviewLog?.LogInfo(
        (BattlePreviewPlugin.CtbEnabled ? "[S1 CTB] " : "[S1 ATB] ") + message);
}
