extern alias GSD1;

using System;
using System.Collections.Generic;
using System.Linq;
using SuikodenRandomizer.Core;
using SuikodenRandomizer.Core.Combat;
using SuikodenRandomizer.Shared;
using UnityEngine;

namespace SuikodenRandomizer.S1BattlePreview;

internal static partial class ActiveBattleState
{
    private static double _ctbUpkeepInterval;
    private static double _ctbNextUpkeep;
    private static int _ctbSoloBossSlot;
    private static double _ctbOpeningEnemyBreakDelay = CtbTimeline.DefaultBreakDelayFraction;
    private static int _ctbMaintenanceRound = -1;
    private static int _ctbEmptyRounds;
    private static readonly IntPtr[] CtbHpIdentities = new IntPtr[BattlePreviewState.SlotCount];
    private static readonly int[] CtbHpBefore = new int[BattlePreviewState.SlotCount];
    private static readonly IntPtr[] CtbInitialIdentities = new IntPtr[BattlePreviewState.SlotCount];
    private static readonly int[] CtbInitialHp = new int[BattlePreviewState.SlotCount];
    private static readonly CtbReserveSupport CtbSupports = new();
    private static CtbReserveCandidate[] _ctbReserves = Array.Empty<CtbReserveCandidate>();
    private static int _ctbSupportIndex;
    private static string _ctbLastSupport = "Reserve support: waiting for roster";
    private static bool _ctbSupportFailed;
    private static int _ctbSnapshotActor;
    private static int _ctbSnapshotDex;
    private static bool _ctbSupportAction;
    private static readonly CtbEnemyIdentity CtbEnemyBadges = new(BattlePreviewState.SlotCount);
    private static string _ctbRallyMessage = "";
    private static double _ctbRallyUntil;

    private static bool TrySelectCtb(out int result)
    {
        result = Wait;
        if (!BattlePreviewPlugin.CtbEnabled || global::SystemObject.mode != global::SystemObject.Mode.GSD1) return false;
        try
        {
            var work = GSD1.BattleBase.battle_work;
            if (work is null || work.battle_phase_no != 4) return false;
            if (_battle != work.Pointer)
            {
                ResetEncounter();
                _battle = work.Pointer;
            }
            if (_fellBack) return false;
            if (_ctbFailedRunDialog != FailedRunDialog.None) return true;
            if (NativeEscapeOwnsRound(work.battle_kaisuu))
            {
                ClearCtbQueues();
                return false;
            }
            if (NativeOnlyReason(work) is { } reason) return Fallback("CTB excludes " + reason);
            var rows = work.pm_data;
            var models = work.b_chara;
            int count = work.pm_kazu;
            int party = work.player_kazu;
            if (rows is null || models is null || party is < 1 or > 6 || party >= count
                || count >= BattlePreviewState.SlotCount || count >= rows.Count || count >= models.Count)
                return Fallback("the CTB combatant roster was incomplete");

            double now = Time.realtimeSinceStartupAsDouble; // Animation watchdog only; never initiative.
            bool running = Application.isFocused && Time.timeScale > 0f;
            int pending = Scheduler.PendingSlot;
            // A fast hero can be editing their next command while their own action finishes.
            if (pending != 0 && (Scheduler.IsAdmitted || pending != _heroCommand.Slot))
            {
                var actor = rows[pending];
                var model = models[pending];
                if (model is null) return Fallback("the CTB action model was lost");
                if (model.action_ukeire_flg != 0)
                {
                    if (!running) _dispatchedAt = now;
                    if (now - _dispatchedAt >= WaitLimitSeconds) return Fallback("a CTB native action stayed busy");
                    return true;
                }
                if (!TryFinish(pending, actor))
                {
                    if (IsLiving(actor)) return Fallback("CTB could not prove native action completion");
                    RestoreRepeatFlag();
                    Scheduler.Observe(pending, IntPtr.Zero, 0, false);
                    _ctbSnapshotActor = 0;
                    Trace($"CTB removed KO'd actor slot {pending} and cleared its action.");
                }
            }
            if (_heroCommand.Slot != 0 && !HasCtbHeroQueue(_heroCommand.Slot))
                return ResolveHeroCommand(work, now, running, out result);

            if (_ctbMaintenanceRound == work.battle_kaisuu)
            {
                result = 0;
                return true;
            }
            if (_round != work.battle_kaisuu)
            {
                _round = work.battle_kaisuu;
                _pausedRound = -1;
                _drainingRound = false;
                Scheduler.BeginRound();
                Trace(FormattableString.Invariant($"CTB native maintenance round {_round}; logical time {Scheduler.Ctb.Now:F1} retained."));
            }

            Span<bool> eligible = Scheduler.CtbEligible;
            eligible.Clear();
            var recoveries = running && _ctbUpkeepInterval == 0 ? new List<double>() : null;
            _ctbTuning ??= LoadCtbTuning();
            if (_ctbUpkeepInterval == 0) ReadOpeningCtbFormation(work, running);
            bool heroesAlive = false, enemiesAlive = false;
            for (int slot = 1; slot <= count; slot++)
            {
                var actor = rows[slot];
                bool alive = IsLiving(actor);
                ObserveSlot(work, slot, actor, alive);
                if (!alive || GSD1.BattleBase.dead_check(slot)) continue;
                if (slot <= party) heroesAlive = true; else enemiesAlive = true;
                if (CtbInitialIdentities[slot] != actor!.Pointer)
                {
                    CtbInitialIdentities[slot] = actor.Pointer;
                    CtbInitialHp[slot] = Math.Max(1, (int)actor.hp);
                }
                recoveries?.Add(Scheduler.Ctb.RecoveryForSpeed(Scheduler.Ctb.GetSpeed(slot)));
                eligible[slot] = models[slot] is not null && CanTakeCtbTurn(slot, actor, slot <= party)
                    && GSD1.BattleBase.ukeire_check(slot);
            }
            if (!heroesAlive || !enemiesAlive || !HasLiveFormationEnemy(work))
            {
                ClearCtbQueues();
                return false;
            }
            if (!running) return true;
            if (recoveries is not null)
            {
                _ctbUpkeepInterval = CtbTimeline.OpeningUpkeepInterval(recoveries);
                _ctbNextUpkeep = _ctbUpkeepInterval;
                RecordCtbPartyReference(work);
                ReadCtbReserves(work);
                Trace(FormattableString.Invariant($"CTB armed. Native upkeep every {_ctbUpkeepInterval:F1} logical units (opening median SPD recovery). Opening enemy Break delay {_ctbOpeningEnemyBreakDelay:0%}."));
                if (_ctbNormalSkipReason is not null) Trace($"CTB normal enemy scaling skipped at opening: {_ctbNormalSkipReason}.");
                if (_ctbSoloBossSlot > 0 && _ctbBlendedBossSpd == 0 && _ctbTuning.SoloBossSpdBonusPercent > 0)
                    Trace(FormattableString.Invariant($"CTB solo boss slot {_ctbSoloBossSlot} {rows[_ctbSoloBossSlot].name}, bonus {_ctbTuning.SoloBossSpdBonusPercent:0.##}%, native SPD {ReadSpeed(rows[_ctbSoloBossSlot])}, effective SPD {Scheduler.Ctb.GetSpeed(_ctbSoloBossSlot)}."));
            }
            double next = Scheduler.Ctb.PeekNextTime(eligible);
            if (double.IsPositiveInfinity(next) || next > _ctbNextUpkeep)
            {
                // Zero is the native end-of-round sentinel. No actor is falsely marked spent.
                // This logical heartbeat is independent of whether everyone has acted.
                if (double.IsPositiveInfinity(next) && ++_ctbEmptyRounds > 8)
                    return Fallback("CTB found no admissible actor after eight native upkeep passes");
                ClearCtbQueues();
                Scheduler.Ctb.AdvanceTo(_ctbNextUpkeep);
                _ctbMaintenanceRound = work.battle_kaisuu;
                _ctbNextUpkeep += _ctbUpkeepInterval;
                Trace(FormattableString.Invariant($"CTB yielded native upkeep before logical event {next:F1}; future actor times retained."));
                result = 0;
                return true;
            }
            _ctbEmptyRounds = 0;
            int selected = Scheduler.Select(eligible);
            if (selected == 0) return true;
            Trace(FormattableString.Invariant($"CTB selected slot {selected} {rows[selected].name} at logical time {Scheduler.Ctb.Now:F1}, SPD {Scheduler.Ctb.GetSpeed(selected)}."));
            if (selected <= party)
            {
                if (!TryUseCtbHeroQueue(work, selected, rows[selected], now, out result))
                    OpenHeroCommand(work, selected, rows[selected], now);
                RefreshCtbPresentation(work);
                return true;
            }
            ClearCtbHeroQueue();
            result = Dispatch(work, selected, rows[selected], now);
            return true;
        }
        catch (Exception error) { return Fallback("CTB: " + error.Message); }
    }

    private static void CaptureCtbAction(GSD1.BATTLE_WORK work, int slot)
    {
        _ctbEarlyMenuUnavailable = false;
        if (work.pm_data is not { } rows) return;
        _ctbSnapshotActor = slot;
        _ctbSnapshotDex = ReadDexterity(rows[slot]);
        _ctbSupportAction = slot <= work.player_kazu
            && (rows[slot].status & RepeatBlockingStatus) == 0 && rows[slot].unbalance_cnt == 0;
        // The confirmed native command uses the same policy as the menu forecast.
        // Resource spending, targeting, animation and damage remain native.
        _ctbCommittedCost = ReadCtbActionCost(work, slot, rows[slot].c_koudou, rows[slot].c_item);
        Scheduler.SetCtbRecovery(_ctbCommittedCost.Multiplier);
        _ctbCommittedReady = Scheduler.Ctb.Now + Scheduler.Ctb.RecoveryForSpeed(Scheduler.Ctb.GetSpeed(slot), _ctbCommittedCost.Multiplier);
        if (slot > work.player_kazu) UpdateCtbEnemyQueue(work, PeekCtbFollowup(work));
        else ClearCtbEnemyQueue();
        for (int target = 1; target <= work.pm_kazu; target++)
        {
            var actor = rows[target];
            CtbHpIdentities[target] = actor?.Pointer ?? IntPtr.Zero;
            CtbHpBefore[target] = actor?.hp ?? 0;
        }
        Trace(FormattableString.Invariant($"CTB committed slot {slot}: {_ctbCommittedCost.Label}, recovery x{_ctbCommittedCost.Multiplier:F2}, admission estimate next {_ctbCommittedReady:F1}, now {Scheduler.Ctb.Now:F1}. Native effects are recorded once after completion."));
    }

    // For unspent actors the game resolves its own incapacitation/unbalance rules.
    private static bool CanTakeCtbTurn(int slot, GSD1.PM_DATA actor, bool hero)
        => Scheduler.Ctb.GetIdentity(slot) == actor.Pointer.ToInt64()
            && (actor.finished_flg == 0 || IsRepeatCandidate(slot, actor, hero, readyCommands: true));

    private static int PeekCtbNextSlot(GSD1.BATTLE_WORK work)
    {
        if (work.pm_data is not { } rows || work.b_chara is not { } models
            || work.pm_kazu >= rows.Count || work.pm_kazu >= models.Count
            || work.pm_kazu >= BattlePreviewState.SlotCount) return 0;

        // Native readiness is sampled by next_chara. An animation may make
        // ukeire_check temporarily false for an actor who will be ready on return.
        Span<bool> eligible = stackalloc bool[BattlePreviewState.SlotCount];
        eligible.Clear();
        for (int slot = 1; slot <= work.pm_kazu; slot++)
        {
            var actor = rows[slot];
            eligible[slot] = Scheduler.CtbEligible[slot] && models[slot] is not null
                && IsLiving(actor) && !GSD1.BattleBase.dead_check(slot)
                && CanTakeCtbTurn(slot, actor!, slot <= work.player_kazu);
        }
        var next = Scheduler.Ctb.PeekOpportunity(eligible, Scheduler.CtbRecoveryMultiplier);
        return next.HasValue && next.Value.ReadyAt <= _ctbNextUpkeep ? next.Value.Slot : 0;
    }

    private static int PeekCtbFollowup(GSD1.BATTLE_WORK work)
    {
        int acting = Scheduler.PendingSlot;
        if (!Scheduler.IsAdmitted || acting == 0 || acting != work.battle_chara_no) return 0;
        int next = PeekCtbNextSlot(work);
        return (acting <= work.player_kazu) == (next <= work.player_kazu) ? next : 0;
    }

    private static void ClearCtbQueues()
    {
        ClearCtbHeroQueue();
        ClearCtbEnemyQueue();
    }

    private static void ReadOpeningCtbFormation(GSD1.BATTLE_WORK work, bool running)
    {
        // The authored count persists across KOs. The sole opening enemy is the first slot after the party.
        var formation = work.monster_party_data;
        int firstEnemy = work.player_kazu + 1;
        int count = formation?.monster_kazu ?? 0;
        bool bossMusic = formation is not null && ((uint)formation.party_flg & GSD1.Game_h.PARTY_BOSS_MUSIC) != 0;
        GSD1.MONSTER_DATA? openingEnemy = null;
        if (count == 1 && work.b_chara[firstEnemy] is { } model && work.monster_data_table is { } monsters)
        {
            int index = model.chara_id - 1;
            if (index >= 0 && index < monsters.Count) openingEnemy = monsters[index];
        }
        // On this build solo Clay Doll and solo Queen Ant use boss music without the monster trait.
        bool bossTrait = openingEnemy is not null && (openingEnemy.seishitsu_flg & GSD1.Game_h.SEISHITSU_BOSS) != 0;
        var tuning = CtbTimeline.ClassifyFormation(count, bossTrait, bossMusic,
            _ctbTuning!.SoloBossBreakPercent / 100d, OtherCtbBreakDelay);
        _ctbSoloBossSlot = tuning.SoloBoss ? firstEnemy : 0;
        _ctbOpeningEnemyBreakDelay = tuning.BreakDelay;
        if (running && tuning.SoloBoss && _ctbTuning.SoloBossBlendEnabled && _ctbBlendParty.Count == 0
            && IsLiving(work.pm_data[firstEnemy]) && !GSD1.BattleBase.dead_check(firstEnemy))
            CaptureCtbBlendParty(work, firstEnemy);
        if (running && _ctbTuning.NormalEnemyScalingEnabled && _ctbNormalFormation is null && !bossMusic && !tuning.SoloBoss
            && formation is not null && CtbTuningNormalScaling.IsOrdinaryFormation(formation.party_flg))
            _ctbNormalSkipReason = CaptureNormalCtbFormation(work, count);
    }

    private static void CompleteCtbAction(int slot, GSD1.PM_DATA actor)
    {
        var work = GSD1.BattleBase.battle_work;
        if (work?.pm_data is not { } rows || work.Pointer != _battle || _ctbSnapshotActor != slot) return;
        _ctbSnapshotActor = 0;
        if (BattlePreviewPlugin.CtbBreakEnabled && slot <= work.player_kazu)
            for (int target = work.player_kazu + 1; target <= work.pm_kazu; target++)
            {
                var enemy = rows[target];
                if (!IsLiving(enemy) || enemy!.Pointer != CtbHpIdentities[target]
                    || enemy.Pointer != CtbInitialIdentities[target]) continue;
                int lost = CtbHpBefore[target] - enemy.hp;
                if (lost <= 0) continue;
                double dexFactor = 0.75 + Math.Clamp(_ctbSnapshotDex, 0, 100) / 200.0;
                int points = (int)Math.Clamp((10.0 + 100.0 * lost / CtbInitialHp[target]) * dexFactor, 5, 45);
                double delayFraction = target == work.player_kazu + 1
                    ? _ctbOpeningEnemyBreakDelay : OtherCtbBreakDelay;
                var broken = Scheduler.Ctb.ApplyBreak(target, enemy.Pointer.ToInt64(), points, delayFraction);
                if (broken.Accepted)
                    Trace(FormattableString.Invariant($"CTB Break target slot {target} {enemy.name}: HP loss {lost}, +{broken.Applied} stagger, remaining {broken.Remaining}/100, triggered={broken.Triggered}, delay {broken.Delay:F1}."));
            }
        if (_ctbSupportAction && BattlePreviewPlugin.CtbSupportEnabled && CtbSupports.RecordHeroAction())
            RallyCtbReserve(work);
        Trace(FormattableString.Invariant($"CTB completed slot {slot} {actor.name}: {_ctbCommittedCost.Label}, recovery x{_ctbCommittedCost.Multiplier:F2}; next turn {Scheduler.Ctb.GetNextReady(slot):F1}, admission estimate {_ctbCommittedReady:F1}, now {Scheduler.Ctb.Now:F1}. Native SPD/status changes can revise the estimate."));
        RefreshCtbPresentation(work);
    }

    private static void PerformCtbWait(GSD1.BATTLE_WORK work, NativeHeroMenu menu)
    {
        if (Scheduler.PendingSlot != menu.Slot || _heroCommand.Slot != menu.Slot
            || !Scheduler.SpendCtbMenuTurn(menu.Slot, menu.Actor, CtbActionTiming.Describe(CtbActionKind.Wait).Multiplier))
            throw new InvalidOperationException("the CTB Wait reservation changed");
        ClearHeroCommand(cancelReservation: false);
        Trace(FormattableString.Invariant($"CTB Wait slot {menu.Slot}; recovery x{CtbActionTiming.Describe(CtbActionKind.Wait).Multiplier:F2}, next opportunity {Scheduler.Ctb.GetNextReady(menu.Slot):F1}. No attack, item, MP or support credit consumed."));
        RefreshCtbPresentation(work);
    }

    private static void RefreshCtbPresentation(GSD1.BATTLE_WORK work)
    {
        CtbPresentation.Visible = BattlePreviewPlugin.CtbEnabled && work.Pointer == _battle
            && work.battle_phase_no is >= 2 and <= 4;
        CtbPresentation.Support = Time.realtimeSinceStartupAsDouble < _ctbRallyUntil ? _ctbRallyMessage : "";
        if (_fellBack)
        {
            CtbPresentation.Turns = Array.Empty<CtbTurnView>();
            CtbPresentation.Enemies = Array.Empty<CtbEnemyView>();
            CtbPresentation.PreviewLabel = "";
            return;
        }
        Span<bool> eligible = stackalloc bool[BattlePreviewState.SlotCount];
        eligible.Clear();
        var rows = work.pm_data;
        var models = work.b_chara;
        if (rows is null || work.pm_kazu >= rows.Count) return;
        // Bind to the native actor, not its name, list order or formation row.
        // Keep a KO'd actor's pointer so revival retains the same badge.
        for (int slot = 1; slot < BattlePreviewState.SlotCount; slot++)
        {
            var enemy = slot > work.player_kazu && slot <= work.pm_kazu ? rows[slot] : null;
            CtbEnemyBadges.Observe(slot, enemy?.Pointer.ToInt64() ?? 0,
                IsLiving(enemy) && !GSD1.BattleBase.dead_check(slot));
        }
        int targeted = ReadCtbTarget(work);
        var enemies = new List<CtbEnemyView>();
        int pending = Scheduler.PendingSlot;
        IntPtr pendingActor = IntPtr.Zero;
        for (int slot = 1; slot <= work.pm_kazu; slot++)
        {
            var actor = rows[slot];
            if (IsLiving(actor) && !GSD1.BattleBase.dead_check(slot)
                && models is not null && slot < models.Count && models[slot] is not null)
            {
                eligible[slot] = CanTakeCtbTurn(slot, actor!, slot <= work.player_kazu);
                if (slot == pending) pendingActor = actor!.Pointer;
            }
            if (slot > work.player_kazu && actor is not null
                && CtbEnemyBadges.Get(slot, actor.Pointer.ToInt64()) is { } badge)
                enemies.Add(new CtbEnemyView(slot, actor.Pointer, actor.name, ReadCtbEnemySource(work, slot), badge,
                    BattlePreviewPlugin.CtbBreakEnabled ? (100 - Scheduler.Ctb.GetBreakRemaining(slot)) / 100f : 0,
                    BattlePreviewPlugin.CtbBreakEnabled && Scheduler.Ctb.IsBroken(slot), slot == targeted));
        }
        var cost = ReadCtbPendingCost(work);
        double recovery = Scheduler.IsAdmitted ? Scheduler.CtbRecoveryMultiplier : cost?.Multiplier ?? 1;
        var projected = Scheduler.ForecastCtb(8, eligible, pendingActor, recovery);
        int returnIndex = -1;
        if (cost.HasValue && pending > 0 && pending <= work.player_kazu)
            for (int index = 1; index < projected.Length; index++)
                if (projected[index].Slot == pending && projected[index].Identity == pendingActor.ToInt64())
                { returnIndex = index; break; }
        CtbPresentation.PreviewLabel = projected.Length == 0 ? "" : CtbCostCaption(cost, returnIndex);
        CtbPresentation.Enemies = enemies.ToArray();
        CtbPresentation.Turns = projected.Select((entry, index) =>
        {
            var actor = rows[entry.Slot];
            bool hero = entry.Slot <= work.player_kazu;
            return new CtbTurnView(entry.Slot, new IntPtr(entry.Identity), actor?.name ?? "Unknown", hero,
                entry.Current, hero ? "" : ReadCtbEnemySource(work, entry.Slot),
                hero || actor is null ? null : CtbEnemyBadges.Get(entry.Slot, actor.Pointer.ToInt64()),
                hero || !BattlePreviewPlugin.CtbBreakEnabled ? 0 : (100 - Scheduler.Ctb.GetBreakRemaining(entry.Slot)) / 100f,
                !hero && BattlePreviewPlugin.CtbBreakEnabled && Scheduler.Ctb.IsBroken(entry.Slot), entry.Slot == targeted,
                index == returnIndex);
        }).ToArray();
    }

    private static string ReadCtbEnemySource(GSD1.BATTLE_WORK work, int slot)
    {
        if (work.b_chara is not { } models || slot < 1 || slot >= models.Count || models[slot] is not { } model) return "";
        // The live OPD is authoritative, including transformed monsters. Catalog is a startup fallback.
        if (model.anime_sp?.opd_data is { } opd && !string.IsNullOrEmpty(opd.id)) return opd.id;
        var monsters = GSD1.BattleBase.vil_data?.monster_data;
        int index = model.chara_id - 1;
        return monsters is not null && index >= 0 && index < monsters.Count
            ? monsters[index]?.opd_data?.id ?? "" : "";
    }

    private static void ReadCtbReserves(GSD1.BATTLE_WORK work)
    {
        _ctbReserves = Array.Empty<CtbReserveCandidate>();
        if (!BattlePreviewPlugin.CtbSupportEnabled || _ctbSupportFailed)
        {
            _ctbLastSupport = "Reserve support: disabled";
            return;
        }
        try
        {
            var game = GSD1.OldSrcBase.game_work;
            if (game?.member_flag is not { } flags || game.player_base is not { } bases
                || work.p_battle_data is not { } party)
            {
                _ctbLastSupport = "Reserve support: roster unavailable";
                return;
            }
            var activeRoles = new HashSet<int>();
            var activeDisplays = new HashSet<int>();
            for (int slot = 1; slot <= work.player_kazu; slot++)
            {
                if (slot >= party.Count) throw new InvalidOperationException("active party data incomplete");
                var player = party[slot]?.player_data ?? throw new InvalidOperationException("active party identity missing");
                if (work.b_chara is not { } models || slot >= models.Count
                    || models[slot] is not { } model || model.chara_id != player.chara_id
                    || player.player_base?.chara_no != player.chara_id)
                    throw new InvalidOperationException("active party role and battle identity disagree");
                activeRoles.Add(player.chara_id);
                activeDisplays.Add(CtbDisplayId(player.chara_id));
            }
            var candidates = new List<CtbReserveCandidate>();
            var seen = new HashSet<int>();
            for (int index = 0; index < bases.Count; index++)
            {
                var data = bases[index];
                if (data is null || data.level == 0 || data.max_hp == 0) continue;
                int role = data.chara_no;
                if (role >= flags.Count || !CombatCatalog.IsNormalCombat(role) || (flags[role] & 7) != 1) continue;
                if (!seen.Add(role)) throw new InvalidOperationException("duplicate reserve identity");
                int display = CtbDisplayId(role);
                candidates.Add(new CtbReserveCandidate(role, display, CombatCatalog.GetName(display),
                    recruitedAlive: data.hp > 0, battleCapable: CombatCatalog.IsNormalCombat(display) || display == CombatCatalog.TirId,
                    initialized: data.level > 0 && data.max_hp > 0));
            }
            int[]? preferences = null;
            if (!string.IsNullOrWhiteSpace(BattlePreviewPlugin.CtbReserveRoles))
                preferences = BattlePreviewPlugin.CtbReserveRoles.Split(',').Select(value =>
                    int.TryParse(value.Trim(), out int id) && id >= 0 && id < 108 ? id
                    : throw new InvalidOperationException("ReserveRoleIds must contain canonical IDs 0..107")).ToArray();
            _ctbReserves = CtbReserveSupport.Select(candidates, activeRoles, activeDisplays, preferences);
            _ctbLastSupport = _ctbReserves.Length == 0 ? "Reserve support: no eligible recruited reserves"
                : "Rally reserves: " + string.Join(", ", _ctbReserves.Select(candidate => candidate.Name));
            Trace(_ctbLastSupport);
        }
        catch (Exception error)
        {
            _ctbSupportFailed = true;
            _ctbReserves = Array.Empty<CtbReserveCandidate>();
            _ctbLastSupport = "Reserve support unavailable (see log)";
            Trace("CTB reserve support skipped: " + error.Message);
        }
    }

    private static void RallyCtbReserve(GSD1.BATTLE_WORK work)
    {
        ReadCtbReserves(work); // Revalidate membership, life, active roles and shuffled identities.
        if (_ctbReserves.Length == 0 || work.pm_data is not { } rows || !HasLiveFormationEnemy(work)) return;
        var allies = new List<(int Slot, long Identity)>();
        for (int slot = 1; slot <= work.player_kazu; slot++)
        {
            var ally = rows[slot];
            if (!IsLiving(ally) || (ally!.status & RepeatBlockingStatus) != 0 || ally.unbalance_cnt != 0) continue;
            allies.Add((slot, ally.Pointer.ToInt64()));
        }
        var (selected, advance) = CtbReserveSupport.Rally(Scheduler.Ctb, allies);
        if (advance <= 0) return;
        var supporter = _ctbReserves[_ctbSupportIndex++ % _ctbReserves.Length];
        _ctbLastSupport = supporter.Name + " rallied " + rows[selected].name;
        _ctbRallyMessage = _ctbLastSupport;
        _ctbRallyUntil = Time.realtimeSinceStartupAsDouble + 4;
        Trace(FormattableString.Invariant($"CTB reserve role {supporter.RoleId}, donor {supporter.DisplayId} {supporter.Name} rallied slot {selected} {rows[selected].name} by {advance:F1} logical units. No MP spent."));
    }

    private static void ResetCtb()
    {
        _ctbTuning = null;
        _ctbSoloBossSlot = 0;
        _ctbBlendedBossSpd = 0;
        _ctbBlendParty = default;
        _ctbBlendGuide = default;
        _ctbNormalFormation = null;
        _ctbNormalSkipReason = null;
        _ctbDisplayMapper = null;
        _ctbStarLookupFailed = false;
        Array.Clear(CtbSpeedBindings);
        ClearCtbQueues();
        Scheduler.UseCtb = BattlePreviewPlugin.CtbEnabled;
        _ctbUpkeepInterval = 0;
        _ctbOpeningEnemyBreakDelay = CtbTimeline.DefaultBreakDelayFraction;
        _ctbNextUpkeep = 0;
        _ctbMaintenanceRound = -1;
        _ctbEmptyRounds = 0;
        _ctbSnapshotActor = 0;
        _ctbSupportAction = false;
        _ctbSupportIndex = 0;
        _ctbSupportFailed = false;
        CtbSupports.Reset();
        _ctbReserves = Array.Empty<CtbReserveCandidate>();
        _ctbLastSupport = "Reserve support: waiting for roster";
        Array.Clear(CtbInitialIdentities, 0, CtbInitialIdentities.Length);
        Array.Clear(CtbInitialHp, 0, CtbInitialHp.Length);
        Array.Clear(CtbHpIdentities, 0, CtbHpIdentities.Length);
        Array.Clear(CtbHpBefore, 0, CtbHpBefore.Length);
        CtbPresentation.Visible = false;
        NativeCtbWindow.ResetEncounter();
        NativeEnemyIndicators.ResetEncounter();
        CtbVisualAssets.ResetEncounter();
        CtbEnemyBadges.Reset();
        ClearCtbTarget();
        CtbPresentation.Turns = Array.Empty<CtbTurnView>();
        CtbPresentation.Enemies = Array.Empty<CtbEnemyView>();
        CtbPresentation.Support = "";
        _ctbRallyMessage = "";
        _ctbRallyUntil = 0;
        _ctbCommittedCost = CtbActionTiming.Describe(CtbActionKind.Attack);
        _ctbCommittedReady = 0;
        _ctbPreviewWarned = false;
        CtbPresentation.PreviewLabel = "";
    }
}
