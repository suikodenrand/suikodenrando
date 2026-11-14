extern alias GSD2;

using HarmonyLib;
using SuikodenRandomizer.Core.Suikoden2;
using System;
using System.Collections.Generic;
using System.Reflection;
using Il2CppInterop.Runtime.InteropTypes.Arrays;

namespace SuikodenRandomizer.Suikoden2Plugin;

/// <summary>A synchronous, reversible view of native unite requirements; actor IDs are not changed here.</summary>
internal sealed class Gsd2UniteScope : IDisposable
{
    [ThreadStatic] private static int _depth;
    [ThreadStatic] private static Gsd2UniteScope? _outer;
    internal static Action<GSD2.BATTLE_WORK>? BeginProjectionBatch { get; set; }
    internal static Func<int, GSD2.UNITE_DATA, int, byte[]?>? ProjectRequirements { get; set; }
    private readonly List<Mutation>? _mutations;
    private readonly Gsd2Runtime _runtime;
    private readonly long _epoch;
    private readonly IntPtr _work;
    private bool _disposed;
    private sealed class Mutation
    {
        internal Il2CppStructArray<byte> Row = null!;
        internal int Index;
        internal byte Before, After;
    }
    private Gsd2UniteScope(Gsd2Runtime runtime, IntPtr work, List<Mutation>? mutations)
    { _runtime = runtime; _epoch = runtime.ExecutionEpoch; _work = work; _mutations = mutations; }

    internal static Gsd2UniteScope? Begin(GSD2.BattleManager manager)
    {
        if (Gsd2Runtime.Current?.OwnsExecution != true) return null;
        return Begin(manager.battle_work ?? throw new InvalidOperationException("S2 native battle state is unavailable."));
    }
    internal static Gsd2UniteScope? Begin(GSD2.BATTLE_WORK work)
    {
        var runtime = Gsd2Runtime.Current;
        if (runtime?.OwnsExecution != true) return null;
        if (runtime.FaultReason is not null)
            throw new InvalidOperationException("S2 avatar admission failed; unsafe Unite execution is blocked.");
        if (!runtime.IsActive) return null;
        if (_depth != 0)
        {
            if (_outer is null || !ReferenceEquals(runtime, _outer._runtime)
                || !runtime.MatchesExecution(_outer._epoch) || work.Pointer != _outer._work)
                throw new InvalidOperationException("S2 unite scopes crossed a battle or execution owner.");
            _depth++;
            return new Gsd2UniteScope(runtime, work.Pointer, null);
        }
        var table = work.unite_data_table ?? throw new InvalidOperationException("S2 native Unite table is unavailable.");
        var actors = work.b_chara ?? throw new InvalidOperationException("S2 battle actors are unavailable.");
        var present = new HashSet<int>();
        foreach (var actor in actors) if (actor is not null) present.Add(actor.chara_id);
        if (present.Count == 0) throw new InvalidOperationException("S2 Unite preflight has no native battle participants.");
        int absent = -1;
        foreach (int candidate in Gsd2CharacterCatalog.NormalCombatIds)
            if (!present.Contains(candidate)) { absent = candidate; break; }
        if (absent < 1 || absent > 127) throw new InvalidOperationException("No valid absent S2 participant can mask unsafe choreography.");
        BeginProjectionBatch?.Invoke(work);
        var mutations = new List<Mutation>();
        var seen = new Dictionary<(IntPtr, int), Mutation>();
        for (int recipe = 0; recipe < table.Count; recipe++)
        {
            var group = table[recipe];
            if (group is null || group.player_kazu == 0) continue;
            var ids = group.player_id;
            int count = group.player_kazu;
            if (ids is null || count > 6 || count > ids.Count)
                throw new InvalidOperationException("S2 Unite participant requirements are malformed.");
            var projected = ProjectRequirements?.Invoke(recipe, group, absent);
            if (projected is not null && projected.Length != count)
                throw new InvalidOperationException("S2 projected unite requirements changed the member count.");
            int maskedMember = -1;
            if (projected is null)
                for (int p = 0; p < count; p++)
                    if (runtime.MapDisplayId(ids[p] & 0x7f) != (ids[p] & 0x7f)) { maskedMember = p; break; }
            for (int p = 0; p < count; p++)
            {
                byte original = ids[p];
                byte replacement = projected is not null ? projected[p]
                    : p == maskedMember ? checked((byte)((original & 0x80) | absent)) : original;
                if ((replacement & 0x80) != (original & 0x80) || (replacement & 0x7f) == 0)
                    throw new InvalidOperationException("S2 unite projection changed an ordered status exemption or used an invalid identity.");
                if (replacement == original) continue;
                var key = (ids.Pointer, p);
                if (seen.TryGetValue(key, out var prior))
                {
                    if (prior.Before != original || prior.After != replacement)
                        throw new InvalidOperationException("Aliased S2 unite requirements have incompatible projections.");
                    continue;
                }
                var mutation = new Mutation { Row = ids, Index = p, Before = original, After = replacement };
                seen.Add(key, mutation); mutations.Add(mutation);
            }
        }
        int attempted = 0;
        try { foreach (var change in mutations) { attempted++; change.Row[change.Index] = change.After; } }
        catch (Exception error)
        {
            try { Restore(mutations, attempted); }
            catch (Exception rollback) { throw new AggregateException("S2 Unite projection and rollback failed.", error, rollback); }
            throw;
        }
        _depth = 1;
        return _outer = new Gsd2UniteScope(runtime, work.Pointer, mutations);
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        // An already-invalidated outer scope has restored the shared native view.
        if (_mutations is null && _outer is null) return;
        bool misnested = _mutations is not null && (_depth != 1 || !ReferenceEquals(_outer, this));
        try
        {
            if (_mutations is not null) Restore(_mutations, _mutations.Count);
            if (misnested) throw new InvalidOperationException("S2 unite requirement scopes were disposed out of order.");
        }
        catch (Exception error) { Fault("S2 Unite requirements could not be restored", error); throw; }
        finally { _depth--; if (_mutations is not null) { _depth = 0; _outer = null; } }
    }
    internal void Fault(string context, Exception error)
    {
        if (ReferenceEquals(_runtime, Gsd2Runtime.Current) && _runtime.MatchesExecution(_epoch))
            _runtime.Fail(context + ": " + error.Message);
    }
    private static void Restore(List<Mutation> mutations, int count)
    {
        List<Exception>? errors = null;
        for (int i = count - 1; i >= 0; i--)
            try { var change = mutations[i]; change.Row[change.Index] = change.Before; }
            catch (Exception error) { (errors ??= new List<Exception>()).Add(error); }
        if (errors is not null) throw new AggregateException("S2 Unite requirements could not be restored.", errors);
    }
}

[HarmonyPatch]
internal static class Gsd2UniteSafetyPatch
{
    [HarmonyTargetMethods] private static IEnumerable<MethodBase> Targets()
    {
        foreach (string name in new[] { "check_unite", "make_command_data", "make_unite_window", "command_unite",
            "UniteSelectBeforeFunc", "command_unite01", "command_unite99", "make_unite_player_window" })
            yield return AccessTools.Method(typeof(GSD2.BattleManager), name)
                ?? throw new MissingMethodException("BattleManager." + name);
    }
    [HarmonyPrefix] private static void Prefix(GSD2.BattleManager __instance, out Gsd2UniteScope? __state)
    {
        __state = null;
        var runtime = Gsd2Runtime.Current;
        long epoch = runtime?.ExecutionEpoch ?? 0;
        try { __state = Gsd2UniteScope.Begin(__instance); }
        catch (Exception error)
        {
            if (ReferenceEquals(runtime, Gsd2Runtime.Current) && runtime?.MatchesExecution(epoch) == true)
                runtime.Fail("S2 Unite safety preflight failed: " + error.Message);
            throw;
        }
    }
    [HarmonyPostfix] private static void Postfix(Gsd2UniteScope? __state) => Restore(__state);
    [HarmonyFinalizer] private static Exception? Finalizer(Exception? __exception, Gsd2UniteScope? __state)
    {
        try { Restore(__state); }
        catch (Exception error) { __exception = __exception is null ? error : new AggregateException(__exception, error); }
        if (__exception is not null) __state?.Fault("S2 native Unite command failed", __exception);
        return __exception;
    }
    private static void Restore(Gsd2UniteScope? state)
    {
        state?.Dispose();
    }
}
