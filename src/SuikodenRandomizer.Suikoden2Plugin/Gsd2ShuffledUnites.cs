extern alias GSD2;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;

namespace SuikodenRandomizer.Suikoden2Plugin;

/// <summary>
/// S2's matcher/calculator receives canonical requirements. Only the synchronous
/// recipe branch sees displayed PM identities; resource, damage and status
/// helpers execute in canonical islands. No graph, party or save ID is changed.
/// </summary>
internal static class Gsd2ShuffledUnites
{
    internal static bool Enabled { get; private set; }
    [ThreadStatic] private static Snapshot? _batch;
    [ThreadStatic] private static Calculation? _calculation;
    [ThreadStatic] private static Calculation? _playbackIdentity;
    [ThreadStatic] private static RawResourceScope? _rawResources;
    private static IntPtr _validatedWork, _validatedTable;
    private static Gsd2Runtime? _validatedRuntime;
    private static long _validatedEpoch;
    private static readonly List<Calculation> Pending = new();
    private static readonly HashSet<string> Announced = new(StringComparer.Ordinal);

    internal sealed class Actor
    {
        internal int Slot, Role, Donor;
        internal int CountBefore, ActionNumBefore, FinishedBefore;
        internal GSD2.PM_DATA Native = null!;
        internal GSD2.BATTLE_CHARA GraphNative = null!;
        internal IntPtr Graph;
    }
    internal sealed class Snapshot
    {
        internal Gsd2Runtime Runtime = null!;
        internal long Epoch;
        internal GSD2.BATTLE_WORK Work = null!;
        internal IntPtr Rows, Graphs;
        internal Actor[] Actors = Array.Empty<Actor>();
        internal Actor?[] Resolve(Gsd2UniteRecipe recipe)
        {
            var result = new Actor?[recipe.Count];
            for (int p = 0; p < result.Length; p++)
                result[p] = Actors.SingleOrDefault(a => a.Donor == recipe.DonorIds[p]);
            return result;
        }
        internal void Check(bool donorView, bool graphDonors = false)
        {
            if (!ReferenceEquals(Runtime, Gsd2Runtime.Current) || !Runtime.MatchesExecution(Epoch)
                || Runtime.FaultReason is not null)
                throw new InvalidOperationException("S2 unite lost its execution owner.");
            var rows = Work.pm_data; var graphs = Work.b_chara;
            if (rows is null || graphs is null || rows.Pointer != Rows || graphs.Pointer != Graphs
                || Work.player_kazu != Actors.Length)
                throw new InvalidOperationException("S2 unite battle arrays changed.");
            foreach (var a in Actors)
                if (rows[a.Slot]?.Pointer != a.Native.Pointer || graphs[a.Slot]?.Pointer != a.Graph
                    || graphs[a.Slot]!.chara_id != (graphDonors ? a.Donor : a.Role)
                    || a.Native.chara_id != (donorView ? a.Donor : a.Role)
                    || a.Native.battle_work?.Pointer != Work.Pointer
                    || Runtime.MapDisplayId(a.Role) != a.Donor)
                    throw new InvalidOperationException("S2 unite actor identity, slot or displayed fighter changed.");
        }
    }
    private sealed class BeforeActor
    {
        internal GSD2.PM_DATA Actor = null!;
        internal int Slot, Hp, Dead, Unbalance;
        internal uint Status;
    }
    internal sealed class Calculation : IDisposable
    {
        internal Snapshot Party = null!;
        internal GSD2.BattlePlayerCharacter Initiator = null!;
        internal Gsd2UniteRecipe Recipe = null!, EffectiveRecipe = null!;
        internal Actor[]? Members;
        internal Gsd2UniteScope? Requirements;
        internal GSD2.ACTION_DATA? Action;
        internal GSD2.ACTION_DATA? PreviousTail;
        internal int Target;
        internal bool DonorView;
        internal bool IdsMayDiffer;
        internal bool DonorGraphView, GraphIdsMayDiffer;
        internal int IslandDepth;
        private bool _disposed;
        private readonly List<BeforeActor> _before = new();
        internal void Capture()
        {
            var rows = Party.Work.pm_data;
            if (rows is null || Party.Work.pm_kazu < Party.Actors.Length || Party.Work.pm_kazu > rows.Count)
                throw new InvalidOperationException("S2 unite combat dimensions are invalid.");
            for (int p = 0; p < Party.Work.pm_kazu; p++)
                if (rows[p] is { } a) _before.Add(new BeforeActor { Actor = a, Slot = p, Hp = a.hp,
                    Dead = a.dead_flg, Status = a.status, Unbalance = a.unbalance_cnt });
        }
        internal string Changes()
        {
            var text = new StringBuilder();
            foreach (var a in _before)
            {
                if (Party.Work.pm_data[a.Slot]?.Pointer != a.Actor.Pointer) continue;
                if (a.Hp == a.Actor.hp && a.Dead == a.Actor.dead_flg && a.Status == a.Actor.status
                    && a.Unbalance == a.Actor.unbalance_cnt) continue;
                text.Append("; slot=").Append(a.Slot).Append(" HP ").Append(a.Hp).Append(" -> ").Append(a.Actor.hp)
                    .Append(" status ").Append(a.Status).Append(" -> ").Append(a.Actor.status)
                    .Append(" unbalance ").Append(a.Unbalance).Append(" -> ").Append(a.Actor.unbalance_cnt)
                    .Append(" dead ").Append(a.Dead).Append(" -> ").Append(a.Actor.dead_flg);
            }
            return text.Length == 0 ? "; no immediate HP/status change" : text.ToString();
        }
        internal void Fault(Exception error)
        {
            if (ReferenceEquals(Party.Runtime, Gsd2Runtime.Current) && Party.Runtime.MatchesExecution(Party.Epoch))
                Party.Runtime.Fail("S2 shuffled unite stopped: " + error.Message);
        }
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Exception? failure = null;
            // Restore captured PM rows even after an ownership change. Native
            // helpers never receive a lasting alternate character identity.
            try { if (IdsMayDiffer) SetIds(this, false); }
            catch (Exception e) { failure = e; }
            try { if (GraphIdsMayDiffer) SetGraphIds(this, false); }
            catch (Exception e) { failure = Combine(failure, e); }
            try { Requirements?.Dispose(); }
            catch (Exception e) { failure = Combine(failure, e); }
            finally { if (ReferenceEquals(_calculation, this)) _calculation = null; }
            if (failure is not null) { Fault(failure); throw failure; }
        }
    }
    internal sealed class IdentityView : IDisposable
    {
        internal Calculation Calculation = null!;
        private bool _disposed;
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try
            {
                if (Calculation.IdsMayDiffer) SetIds(Calculation, false);
                if (Calculation.IslandDepth != 0)
                    throw new InvalidOperationException("S2 canonical unite helper did not unwind.");
            }
            catch (Exception e) { Calculation.Fault(e); throw; }
        }
    }
    internal sealed class CanonicalIsland : IDisposable
    {
        internal Calculation Calculation = null!;
        internal bool RestoreGraphs;
        private bool _disposed;
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try
            {
                if (--Calculation.IslandDepth == 0)
                {
                    Calculation.Party.Check(false);
                    SetIds(Calculation, true);
                    if (RestoreGraphs) SetGraphIds(Calculation, true);
                }
            }
            catch (Exception e) { Calculation.Fault(e); throw; }
        }
    }
    internal sealed class Playback : IDisposable
    {
        internal Calculation Calculation = null!;
        internal IntPtr BeforeStep;
        private bool _disposed;
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Exception? failure = null;
            try { if (Calculation.IdsMayDiffer) SetIds(Calculation, false); }
            catch (Exception e) { failure = e; }
            try { if (Calculation.GraphIdsMayDiffer) SetGraphIds(Calculation, false); }
            catch (Exception e) { failure = Combine(failure, e); }
            finally { if (ReferenceEquals(_playbackIdentity, Calculation)) _playbackIdentity = null; }
            if (Calculation.IslandDepth != 0)
                failure = Combine(failure, new InvalidOperationException("S2 unite playback helper scopes did not unwind."));
            if (failure is not null) { Calculation.Fault(failure); throw failure; }
        }
    }
    internal sealed class RawResourceScope : IDisposable
    {
        internal Calculation Calculation = null!;
        internal RawResourceScope? Previous;
        private bool _disposed;
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (!ReferenceEquals(_rawResources, this))
            {
                _rawResources = null;
                throw new InvalidOperationException("S2 unite resource scopes were disposed out of order.");
            }
            _rawResources = Previous;
        }
    }

    internal static void Configure(bool enabled)
    {
        Enabled = enabled; Reset();
        Gsd2UniteScope.BeginProjectionBatch = enabled ? BeginBatch : null;
        Gsd2UniteScope.ProjectRequirements = enabled ? Project : null;
        Gsd2Graphics.PreserveRawResourceIds = enabled ? PreserveNativeResourceIds : () => false;
        Gsd2Presentation.PreserveRawResourceIds = enabled ? PreserveNativeResourceIds : () => false;
    }
    internal static void Reset()
    {
        if (_calculation is not null || _playbackIdentity is not null || _rawResources is not null)
            throw new InvalidOperationException("S2 unite calculation cannot cross a battle teardown.");
        _batch = null; Pending.Clear(); Announced.Clear();
        _validatedWork = _validatedTable = IntPtr.Zero; _validatedRuntime = null; _validatedEpoch = 0;
    }
    private static Snapshot Capture(GSD2.BATTLE_WORK work)
    {
        var runtime = Gsd2Runtime.Current ?? throw new InvalidOperationException("No S2 unite owner.");
        if (!runtime.IsActive) throw new InvalidOperationException("S2 character resources are not admitted.");
        var rows = work.pm_data; var graphs = work.b_chara; int count = work.player_kazu;
        if (work.Pointer == IntPtr.Zero || rows is null || graphs is null || count < 1 || count > 6
            || count > rows.Count || count > graphs.Count)
            throw new InvalidOperationException("S2 active battle party dimensions are invalid.");
        var result = new Snapshot { Runtime = runtime, Epoch = runtime.ExecutionEpoch, Work = work,
            Rows = rows.Pointer, Graphs = graphs.Pointer, Actors = new Actor[count] };
        var roles = new HashSet<int>(); var donors = new HashSet<int>(); var identities = new HashSet<IntPtr>();
        for (int slot = 0; slot < count; slot++)
        {
            var row = rows[slot]; var graph = graphs[slot];
            if (row is null || graph is null || row.Pointer == IntPtr.Zero || graph.Pointer == IntPtr.Zero
                || row.chara_no != slot || row.chara_id < 1 || row.chara_id > 83 || graph.chara_id != row.chara_id)
                throw new InvalidOperationException("S2 battle actor does not retain its canonical role and compressed slot.");
            int donor = runtime.MapDisplayId(row.chara_id);
            if (donor < 1 || donor > 83 || !roles.Add(row.chara_id) || !donors.Add(donor) || !identities.Add(row.Pointer))
                throw new InvalidOperationException("S2 battle party has ambiguous displayed fighters or identities.");
            result.Actors[slot] = new Actor { Slot = slot, Role = row.chara_id, Donor = donor, Native = row,
                Graph = graph.Pointer, GraphNative = graph, CountBefore = row.action_kaisu,
                ActionNumBefore = row.action_num, FinishedBefore = row.finished_flg };
        }
        result.Check(false); return result;
    }
    private static void CheckIslandsInstalled()
    {
        foreach (var target in Gsd2UniteCanonicalIslandPatch.Targets())
            if (Harmony.GetPatchInfo(target)?.Prefixes.Any(p => p.PatchMethod.DeclaringType == typeof(Gsd2UniteCanonicalIslandPatch)) != true)
                throw new InvalidOperationException("Required canonical S2 unite helper hook is not installed: " + target.Name);
    }
    private static void BeginBatch(GSD2.BATTLE_WORK work)
    {
        var party = Capture(work);
        var table = work.unite_data_table;
        if (table is null || table.Count != Gsd2UniteRecipes.OriginalCount)
            throw new InvalidOperationException("S2 native unite catalog dimensions changed.");
        bool first = _validatedWork != work.Pointer || _validatedTable != table.Pointer
            || !ReferenceEquals(_validatedRuntime, party.Runtime) || _validatedEpoch != party.Epoch;
        if (first) { CheckIslandsInstalled(); Pending.Clear(); Announced.Clear(); }
        foreach (var recipe in Gsd2UniteRecipes.All)
            // BattleWork may reuse the loaded asset array. Native-derived effect
            // fields can therefore survive teardown; their original defaults
            // are artifact evidence, not an invariant of a later battle.
            if (table[recipe.Index] is not { } native || !recipe.MatchesStable(native))
                throw new InvalidOperationException("S2 native unite catalog changed at recipe " + recipe.Index + ".");
        _validatedWork = work.Pointer; _validatedTable = table.Pointer; _validatedRuntime = party.Runtime;
        _validatedEpoch = party.Epoch; _batch = party;
    }
    private static byte[]? Project(int index, GSD2.UNITE_DATA native, int absent)
    {
        var party = _batch ?? throw new InvalidOperationException("No active S2 unite projection batch.");
        if (!Gsd2UniteRecipes.TryGet(index, out var recipe) || recipe is null || recipe.Count < 2)
            throw new InvalidOperationException("Unknown S2 unite recipe.");
        var members = party.Resolve(recipe); var bytes = new byte[recipe.Count];
        bool complete = members.All(a => a is not null);
        for (int p = 0; p < bytes.Length; p++)
            bytes[p] = complete ? recipe.EncodeForRole(p, members[p]!.Role) : recipe.RawMembers[p];
        if (!complete)
            for (int p = 0; p < bytes.Length; p++) bytes[p] = recipe.EncodeForRole(p, absent);
        string message = "S2 UNITE PREVIEW id=" + index + " " + recipe.Name + (complete
            ? " mapped: " + Describe(members.Select(a => a!).ToArray()) + "; canonical story IDs retained."
            : " unavailable: required displayed fighters are absent.");
        if (Announced.Add(message)) party.Runtime.Trace(message);
        return bytes;
    }
    internal static Calculation? BeforeCalculate(GSD2.BattlePlayerCharacter actor)
    {
        if (!Enabled || Gsd2Runtime.Current?.OwnsExecution != true) return null;
        var runtime = Gsd2Runtime.Current;
        if (runtime.FaultReason is not null) throw new InvalidOperationException(runtime.FaultReason);
        if (!runtime.IsActive) return null;
        if (_calculation is not null || _playbackIdentity is not null)
            throw new InvalidOperationException("S2 unite calculations cannot cross another native unite callback.");
        var scope = Gsd2UniteScope.Begin(actor.battle_work);
        try
        {
            var party = _batch ?? throw new InvalidOperationException("S2 unite calculation has no current party snapshot.");
            if (actor.chara_no < 0 || actor.chara_no >= party.Actors.Length
                || party.Actors[actor.chara_no].Native.Pointer != actor.Pointer || actor.battle_work.Pointer != party.Work.Pointer)
                throw new InvalidOperationException("S2 unite calculator is not the actual active actor.");
            int id = actor.c_item;
            if (!Gsd2UniteRecipes.TryGet(id, out var recipe) || recipe is null || recipe.Count < 2)
                throw new InvalidOperationException("S2 selected an unknown unite recipe.");
            var members = party.Resolve(recipe);
            if (members.Any(a => a is null) || !members.Any(a => a!.Native.Pointer == actor.Pointer))
                throw new InvalidOperationException("S2 queued unite no longer has every required displayed participant.");
            var state = new Calculation { Party = party, Initiator = actor, Recipe = recipe, EffectiveRecipe = recipe,
                Members = members.Select(a => a!).ToArray(),
                Requirements = scope, Target = actor.c_mokuhyo };
            state.Capture(); _calculation = state; return state;
        }
        catch (Exception original)
        {
            try { scope?.Dispose(); }
            catch (Exception cleanup) { throw new AggregateException(original, cleanup); }
            throw;
        }
    }
    internal static IdentityView? BeforeNormal(GSD2.BattlePlayerCharacter actor, byte recipeId, Il2CppStructArray<int> slots)
    {
        var state = _calculation;
        if (state is null) return null;
        state.Party.Check(false); CheckIslandsInstalled();
        if (actor.Pointer != state.Initiator.Pointer || state.Members is null || slots is null || slots.Count != 6)
            throw new InvalidOperationException("S2 native unite calculation passed an invalid participant array.");
        if (!Gsd2UniteRecipes.TryGet(recipeId, out var effective) || effective is null
            || (recipeId != state.Recipe.Index && !(state.Recipe.Index == 3 && recipeId == 40))
            || !effective.DonorIds.SequenceEqual(state.Recipe.DonorIds))
            throw new InvalidOperationException("S2 native unite changed to an unexpected recipe.");
        for (int p = 0; p < state.Members.Length; p++)
            if (slots[p] != state.Members[p].Slot)
                throw new InvalidOperationException("S2 native unite changed participant order or battle slots.");
        if (!state.Members.Any(a => a.Native.Pointer == actor.Pointer))
            throw new InvalidOperationException("S2 unite initiator is outside the required displayed fighters.");
        if (state.Members.Any(a => a.Native.IsDead || (a.Native.status & 0x1f280u) != 0))
            throw new InvalidOperationException("S2 native unite reached calculation with an ineligible participant.");
        state.EffectiveRecipe = effective;
        state.Target = state.Members[0].Native.c_mokuhyo;
        state.PreviousTail = state.Party.Work.action_sp;
        state.Party.Runtime.Trace("S2 UNITE PREVIEW id=" + recipeId + " " + effective.Name + " admitted: "
            + Describe(state.Members) + "; initiator=" + actor.chara_no + "; target=" + state.Target + ".");
        SetIds(state, true);
        return new IdentityView { Calculation = state };
    }
    internal static void AfterNormal(IdentityView? view, int result)
    {
        if (view is null || result != 1) return;
        var state = view.Calculation;
        state.Party.Check(true);
        var action = state.Party.Work.action_sp;
        var members = state.Members!;
        if (action is null || action.Pointer == IntPtr.Zero || action.chara_no != members[0].Slot
            || action.action_type != 7 || action.data is null || action.data.Count < 7)
            throw new InvalidOperationException("S2 native unite did not queue the expected action owner and record.");
        if (state.PreviousTail is not null ? state.PreviousTail.Pointer == action.Pointer
                || state.PreviousTail.next_action_data?.Pointer != action.Pointer
            : state.Party.Work.action_top?.Pointer != action.Pointer)
            throw new InvalidOperationException("S2 native unite did not append a new linked action record.");
        for (int p = 0; p < 7; p++)
            if (action.data[p] != (p < members.Length - 1 ? members[p + 1].Slot : 99))
                throw new InvalidOperationException("S2 queued unite participant slots do not match the actual displayed fighters.");
        state.Action = action;
    }
    internal static void AfterCalculate(Calculation? state, int result)
    {
        if (state is null) return;
        state.Party.Check(false);
        foreach (var a in state.Members!)
        {
            int expectedCount = unchecked((byte)(a.CountBefore + 1));
            int expectedFinish = expectedCount >= a.ActionNumBefore ? 1 : a.FinishedBefore;
            if (a.Native.action_kaisu != expectedCount || a.Native.finished_flg != expectedFinish)
                throw new InvalidOperationException("S2 unite did not consume exactly one native action per participant.");
        }
        if (result != 1)
        {
            state.Party.Runtime.Trace("S2 UNITE PREVIEW id=" + state.Recipe.Index + " " + state.Recipe.Name
                + " not calculated: native eligibility or target check returned " + result + ".");
            return;
        }
        if (state.Members is null || state.Action is null)
            throw new InvalidOperationException("S2 unite succeeded without a validated participant action.");
        if (Pending.Any(p => p.Action?.Pointer == state.Action.Pointer))
            throw new InvalidOperationException("S2 native unite action was queued twice.");
        Pending.Add(state);
        state.Party.Runtime.Trace("S2 UNITE PREVIEW id=" + state.EffectiveRecipe.Index + " " + state.EffectiveRecipe.Name
            + " calculated: " + Describe(state.Members) + "; action-owner=" + state.Action.chara_no
            + "; target=" + state.Target + "; actions=" + string.Join("/", state.Members.Select(a =>
                a.CountBefore + "->" + a.Native.action_kaisu + ":" + a.ActionNumBefore))
            + "; finished=" + Finished(state) + Reactions(state.Action) + state.Changes()
            + "; participant IDs restored; native action record queued.");
    }
    private static void SetIds(Calculation state, bool donors)
    {
        var actors = state.Party.Actors;
        var before = new byte[actors.Length]; int attempted = 0;
        bool previousView = state.DonorView, previousDirty = state.IdsMayDiffer;
        for (int i = 0; i < actors.Length; i++) before[i] = actors[i].Native.chara_id;
        if (donors) state.IdsMayDiffer = true;
        try
        {
            for (int i = 0; i < actors.Length; i++)
            {
                attempted++;
                actors[i].Native.chara_id = checked((byte)(donors ? actors[i].Donor : actors[i].Role));
            }
            state.DonorView = donors;
            state.IdsMayDiffer = donors;
        }
        catch (Exception error)
        {
            Exception? rollback = null;
            for (int i = attempted - 1; i >= 0; i--)
                try { actors[i].Native.chara_id = before[i]; } catch (Exception e) { rollback = Combine(rollback, e); }
            if (rollback is not null)
            {
                state.IdsMayDiffer = true;
                throw new AggregateException("S2 temporary identity view and rollback failed.", error, rollback);
            }
            state.DonorView = previousView; state.IdsMayDiffer = previousDirty;
            throw;
        }
    }
    internal static CanonicalIsland? BeginCanonicalIsland()
    {
        var state = _calculation ?? _playbackIdentity;
        if (state is null || (!state.DonorView && state.IslandDepth == 0)) return null;
        state.Party.Check(state.DonorView, state.DonorGraphView);
        bool restoreGraphs = state.DonorGraphView;
        if (state.IslandDepth == 0)
        {
            if (restoreGraphs) SetGraphIds(state, false);
            SetIds(state, false);
        }
        state.IslandDepth++;
        return new CanonicalIsland { Calculation = state, RestoreGraphs = restoreGraphs };
    }
    internal static Playback? BeforePlayback(GSD2.BattleManager manager)
    {
        if (!Enabled || Gsd2Runtime.Current?.IsActive != true) return null;
        var work = manager.battle_work; int slot = work.battle_chara_no;
        if (slot < 0 || slot >= work.player_kazu) return null;
        var action = work.pm_data[slot]?.action_data;
        var state = Pending.FirstOrDefault(p => p.Party.Work.Pointer == work.Pointer && p.Action?.Pointer == action?.Pointer);
        if (state is null)
        {
            if (action?.action_type == 7)
                throw new InvalidOperationException("S2 native unite playback has no validated calculation record.");
            return null;
        }
        if (work.magic_func is null)
            throw new InvalidOperationException("S2 native unite playback has no native animation controller.");
        state.Party.Check(false);
        CheckIslandsInstalled();
        if (_playbackIdentity is not null || _calculation is not null)
            throw new InvalidOperationException("S2 native unite playback callbacks cannot overlap.");
        var observation = new Playback { Calculation = state, BeforeStep = work.turn_step?.Pointer ?? IntPtr.Zero };
        _playbackIdentity = state;
        try { SetIds(state, true); SetGraphIds(state, true); return observation; }
        catch (Exception original)
        {
            try { observation.Dispose(); }
            catch (Exception cleanup) { throw new AggregateException(original, cleanup); }
            throw;
        }
    }
    internal static void AfterPlayback(Playback? observation)
    {
        if (observation is null) return;
        observation.Dispose();
        var state = observation.Calculation; var work = state.Party.Work;
        state.Party.Check(false);
        if (work.turn_step is null || work.turn_step.Pointer == observation.BeforeStep
            || work.flg_table is null || work.flg_table.Count < 1 || work.flg_table[0] != 1) return;
        Pending.Remove(state);
        state.Party.Runtime.Trace("S2 UNITE PREVIEW id=" + state.EffectiveRecipe.Index + " " + state.EffectiveRecipe.Name
            + " completed: " + Describe(state.Members!) + "; finished=" + Finished(state)
            + "; native unite playback returned control to battle; canonical participant IDs intact.");
    }
    internal static Exception? PlaybackException(Playback? observation, Exception? error)
    {
        try { observation?.Dispose(); } catch (Exception cleanup) { error = Combine(error, cleanup); }
        if (error is not null) observation?.Calculation.Fault(error);
        return error;
    }
    internal static Exception? Finish(IDisposable? scope, Exception? error)
    {
        try { scope?.Dispose(); } catch (Exception e) { error = Combine(error, e); }
        var state = scope switch { Calculation c => c, IdentityView v => v.Calculation,
            CanonicalIsland i => i.Calculation, RawResourceScope r => r.Calculation,
            Playback p => p.Calculation, _ => _calculation ?? _playbackIdentity };
        if (error is not null) state?.Fault(error);
        return error;
    }
    private static void SetGraphIds(Calculation state, bool donors)
    {
        var actors = state.Party.Actors; var before = new int[actors.Length]; int attempted = 0;
        bool previousView = state.DonorGraphView, previousDirty = state.GraphIdsMayDiffer;
        for (int i = 0; i < actors.Length; i++) before[i] = actors[i].GraphNative.chara_id;
        if (donors) state.GraphIdsMayDiffer = true;
        try
        {
            for (int i = 0; i < actors.Length; i++)
            {
                attempted++;
                actors[i].GraphNative.chara_id = donors ? actors[i].Donor : actors[i].Role;
            }
            state.DonorGraphView = donors; state.GraphIdsMayDiffer = donors;
        }
        catch (Exception original)
        {
            Exception? rollback = null;
            for (int i = attempted - 1; i >= 0; i--)
                try { actors[i].GraphNative.chara_id = before[i]; } catch (Exception e) { rollback = Combine(rollback, e); }
            if (rollback is not null)
            {
                state.GraphIdsMayDiffer = true;
                throw new AggregateException("S2 visual identity view and rollback failed.", original, rollback);
            }
            state.DonorGraphView = previousView; state.GraphIdsMayDiffer = previousDirty;
            throw;
        }
    }
    private static bool PreserveNativeResourceIds()
    {
        var state = _rawResources?.Calculation ?? _playbackIdentity;
        return state is not null && state.IslandDepth == 0
            && ReferenceEquals(state.Party.Runtime, Gsd2Runtime.Current) && state.Party.Runtime.MatchesExecution(state.Party.Epoch);
    }
    internal static RawResourceScope? BeginRawResourceScope(IntPtr overlayParent)
    {
        if (!Enabled || overlayParent == IntPtr.Zero || Gsd2Runtime.Current?.IsActive != true) return null;
        var state = Pending.FirstOrDefault(p => ReferenceEquals(p.Party.Runtime, Gsd2Runtime.Current)
            && p.Party.Runtime.MatchesExecution(p.Party.Epoch) && p.Party.Work.magic_func?.Pointer == overlayParent);
        if (state is null) return null;
        var result = new RawResourceScope { Calculation = state, Previous = _rawResources };
        _rawResources = result; return result;
    }
    internal static void FaultCurrent(Exception error)
    {
        if (_calculation is not null) _calculation.Fault(error);
        else if (Gsd2Runtime.Current is { OwnsExecution: true } r) r.Fail("S2 shuffled unite stopped: " + error.Message);
    }
    private static Exception Combine(Exception? first, Exception second) => first is null ? second : new AggregateException(first, second);
    private static string Reactions(GSD2.ACTION_DATA action)
    {
        var rows = action.reaction_data;
        if (rows is null || rows.Count == 0) return "; native reactions=none";
        var result = new StringBuilder("; native reactions=");
        for (int p = 0; p < rows.Count; p++)
        {
            var row = rows[p]; if (row is null) continue;
            if (p != 0) result.Append(", ");
            result.Append("target=").Append(row.mokuhyo).Append(" type=").Append(row.reaction_type)
                .Append(" amount=").Append(row.damage).Append(" status=").Append(row.reaction_status);
        }
        return result.ToString();
    }
    private static string Describe(IEnumerable<Actor> actors) => string.Join(", ", actors.Select(a => "fighter=" + a.Donor + " slot=" + a.Slot + " role=" + a.Role));
    private static string Finished(Calculation state) => string.Join("/", state.Members!.Select(a => a.Native.finished_flg));
}

[HarmonyPatch(typeof(GSD2.BattlePlayerCharacter), "PlayerCalcUnite")]
internal static class Gsd2ShuffledUniteCalculationPatch
{
    [HarmonyPrepare] private static bool Prepare() => Gsd2ShuffledUnites.Enabled;
    [HarmonyPrefix] private static void Prefix(GSD2.BattlePlayerCharacter __instance, out Gsd2ShuffledUnites.Calculation? __state)
    {
        __state = null;
        var runtime = Gsd2Runtime.Current; long epoch = runtime?.ExecutionEpoch ?? 0;
        try { __state = Gsd2ShuffledUnites.BeforeCalculate(__instance); }
        catch (Exception e)
        {
            if (ReferenceEquals(runtime, Gsd2Runtime.Current) && runtime?.MatchesExecution(epoch) == true)
                runtime.Fail("S2 shuffled unite stopped: " + e.Message);
            throw;
        }
    }
    [HarmonyPostfix] private static void Postfix(int __result, Gsd2ShuffledUnites.Calculation? __state)
        => Gsd2ShuffledUnites.AfterCalculate(__state, __result);
    [HarmonyFinalizer] private static Exception? Finalizer(Exception? __exception, Gsd2ShuffledUnites.Calculation? __state)
        => Gsd2ShuffledUnites.Finish(__state, __exception);
}
[HarmonyPatch(typeof(GSD2.BattlePlayerCharacter), "UniteNormalFunc")]
internal static class Gsd2ShuffledUniteNormalPatch
{
    [HarmonyPrepare] private static bool Prepare() => Gsd2ShuffledUnites.Enabled;
    [HarmonyPrefix] private static void Prefix(GSD2.BattlePlayerCharacter __instance, byte __0, Il2CppStructArray<int> __1,
        out Gsd2ShuffledUnites.IdentityView? __state)
        => __state = Gsd2ShuffledUnites.BeforeNormal(__instance, __0, __1);
    [HarmonyPostfix] private static void Postfix(int __result, Gsd2ShuffledUnites.IdentityView? __state)
        => Gsd2ShuffledUnites.AfterNormal(__state, __result);
    [HarmonyFinalizer] private static Exception? Finalizer(Exception? __exception, Gsd2ShuffledUnites.IdentityView? __state)
        => Gsd2ShuffledUnites.Finish(__state, __exception);
}
[HarmonyPatch]
internal static class Gsd2UniteCanonicalIslandPatch
{
    [HarmonyPrepare] private static bool Prepare() => Gsd2ShuffledUnites.Enabled;
    [HarmonyTargetMethods] internal static IEnumerable<MethodBase> Targets()
    {
        foreach (var request in new (Type Type, string Name, Type[] Args)[] {
            (typeof(GSD2.BattlePlayerCharacter), "DoguEqpCheck", new[]{typeof(byte)}),
            (typeof(GSD2.BattlePlayerCharacter), "CalcPlayerDamage", new[]{typeof(int)}),
            (typeof(GSD2.PM_DATA), "DamageSetReActionData", new[]{typeof(GSD2.ACTION_DATA),typeof(int),typeof(short),typeof(byte),typeof(byte)}),
            (typeof(GSD2.PM_DATA), "RecoverSetReActionData", new[]{typeof(GSD2.ACTION_DATA),typeof(int),typeof(short),typeof(byte)}),
            (typeof(GSD2.PM_DATA), "StatusSet", new[]{typeof(int)}) })
            yield return AccessTools.Method(request.Type, request.Name, request.Args)
                ?? throw new MissingMethodException(request.Type.Name + "." + request.Name);
    }
    [HarmonyPrefix] private static void Prefix(out Gsd2ShuffledUnites.CanonicalIsland? __state)
        => __state = Gsd2ShuffledUnites.BeginCanonicalIsland();
    [HarmonyFinalizer] private static Exception? Finalizer(Exception? __exception, Gsd2ShuffledUnites.CanonicalIsland? __state)
        => Gsd2ShuffledUnites.Finish(__state, __exception);
}
[HarmonyPatch(typeof(GSD2.BattleManager), "battle_turn_unite01")]
internal static class Gsd2ShuffledUnitePlaybackPatch
{
    [HarmonyPrepare] private static bool Prepare() => Gsd2ShuffledUnites.Enabled;
    [HarmonyPrefix] private static void Prefix(GSD2.BattleManager __instance, out Gsd2ShuffledUnites.Playback? __state)
    {
        __state = null;
        var runtime = Gsd2Runtime.Current; long epoch = runtime?.ExecutionEpoch ?? 0;
        try { __state = Gsd2ShuffledUnites.BeforePlayback(__instance); }
        catch (Exception e)
        {
            if (ReferenceEquals(runtime, Gsd2Runtime.Current) && runtime?.MatchesExecution(epoch) == true)
                runtime.Fail("S2 unite playback stopped: " + e.Message);
            throw;
        }
    }
    [HarmonyPostfix] private static void Postfix(Gsd2ShuffledUnites.Playback? __state) => Gsd2ShuffledUnites.AfterPlayback(__state);
    [HarmonyFinalizer] private static Exception? Finalizer(Exception? __exception, Gsd2ShuffledUnites.Playback? __state)
        => Gsd2ShuffledUnites.PlaybackException(__state, __exception);
}
[HarmonyPatch]
internal static class Gsd2ShuffledUniteResetPatch
{
    [HarmonyPrepare] private static bool Prepare() => Gsd2ShuffledUnites.Enabled;
    [HarmonyTargetMethods] private static IEnumerable<MethodBase> Targets()
    {
        foreach (string name in new[] { "Release", "BattleFree", "BattleEnd" })
            yield return AccessTools.Method(typeof(GSD2.BattleManager), name) ?? throw new MissingMethodException(name);
    }
    [HarmonyPrefix] private static void Prefix()
    { if (Gsd2Runtime.Current?.OwnsExecution == true) Gsd2ShuffledUnites.Reset(); }
}
