extern alias GSD1;

using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using SuikodenRandomizer.Core.Combat;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace SuikodenRandomizer.AvatarPlugin;

/// <summary>
/// One qualified native choreography only. Translate requirements while native
/// synchronous readers run; retain canonical IDs, native commands, and live bases.
/// ATB group scheduling is deliberately a separate feature.
/// </summary>
internal static class AvatarTalismanPreview
{
    internal const int Recipe = 1;
    private static readonly int[] Donors = { 6, 2 }; // Original .cctor recipe 1: Pahn first, Gremio second.
    private static readonly HashSet<string> Reported = new();
    internal static bool Enabled { get; private set; }
    private static Execution? _execution;

    internal sealed class Execution
    {
        internal readonly AvatarRuntime Runtime;
        internal readonly long Epoch;
        internal readonly IntPtr Battle;
        internal readonly int Leader;
        internal readonly UniteParticipantGroup Group;
        internal bool Prepared;
        internal int DamageTarget, TargetHpBefore;
        internal IntPtr DamageTargetIdentity;
        internal Execution(AvatarRuntime runtime, GSD1.BATTLE_WORK work, int leader, UniteParticipantGroup group)
        { Runtime = runtime; Epoch = runtime.ExecutionEpoch; Battle = work.Pointer; Leader = leader; Group = group; }
        internal bool IsCurrent => ReferenceEquals(Runtime, AvatarRuntime.Current)
            && Runtime.MatchesExecution(Epoch) && Runtime.IsActive
            && GSD1.BattleBase.battle_work?.Pointer == Battle;
    }

    internal sealed class FinishScope : IDisposable
    {
        internal readonly Execution Action;
        private readonly AvatarUniteTableScope? _scope;
        internal FinishScope(Execution action, AvatarUniteTableScope? scope) { Action = action; _scope = scope; }
        public void Dispose() => _scope?.Dispose();
    }

    internal sealed class DamageObservation
    {
        internal readonly Execution Action;
        internal readonly int Target, Before, Multiplier;
        internal DamageObservation(Execution action, int target, int before, int multiplier)
        { Action = action; Target = target; Before = before; Multiplier = multiplier; }
    }

    internal static void Configure(bool enabled)
    {
        Enabled = enabled;
        AvatarUniteTableScope.ProjectRequirements = enabled ? ProjectRequirements : null;
        Reset();
    }

    internal static void Reset() { _execution = null; Reported.Clear(); }

    private static AvatarRuntime? ActiveRuntime()
    {
        var runtime = AvatarRuntime.Current;
        return Enabled && runtime?.IsActive == true && runtime.MatchesExecution(runtime.ExecutionEpoch) ? runtime : null;
    }

    private static void Once(AvatarRuntime runtime, string message)
    { if (Reported.Count < 48 && Reported.Add(message)) runtime.Trace("UNITE PREVIEW " + message); }

    // The optional scheduler is another DLL. Inspect its actual loaded settings
    // on each admission, including when it loads after the combined host.
    private static bool NativeTurnBased()
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            var type = assembly.GetType("SuikodenRandomizer.S1BattlePreview.BattlePreviewPlugin", false);
            if (type is null) continue;
            foreach (string name in new[] { "ActiveAtbEnabled", "OneShotAttackEnabled" })
            {
                var property = type.GetProperty(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (property?.PropertyType != typeof(bool) || property.GetValue(null) is not bool value || value) return false;
            }
        }
        return true;
    }

    private static bool TryGroup(AvatarRuntime runtime, out UniteParticipantGroup? group, out string reason)
    {
        group = null;
        if (!NativeTurnBased()) { reason = "requires native turn-based combat; ATB/one-shot scheduling is active"; return false; }
        var work = GSD1.BattleBase.battle_work;
        if (work is null || work.player_kazu is < 1 or > 6 || work.pm_data is not { } pm
            || work.p_battle_data is not { } players || work.b_chara is not { } characters
            || pm.Count <= work.player_kazu || players.Count <= work.player_kazu || characters.Count <= work.player_kazu)
        { reason = "active native battle arrays are incomplete"; return false; }
        var actors = new List<UniteActor>();
        for (int slot = 1; slot <= work.player_kazu; slot++)
        {
            var actor = pm[slot];
            var player = players[slot]?.player_data;
            var character = characters[slot];
            if (actor is null || player is null || character is null || character.chara_id != player.chara_id)
            { reason = "canonical actor IDs disagree at slot " + slot; return false; }
            int role = player.chara_id;
            int donor = runtime.MapDisplayId(role);
            bool eligible = actor.hp > 0 && actor.dead_flg == 0 && (actor.status & 0x61) == 0
                && !GSD1.BattleBase.dead_check(slot);
            actors.Add(new UniteActor(role, donor, slot, actor.Pointer, eligible));
        }
        if (!UniteParticipantSnapshot.TryCreate(actors, out var snapshot, out var error)
            || !snapshot!.TryResolve(Donors, out group, out error))
        { reason = error ?? "participant resolution failed"; return false; }

        foreach (var member in group!.Members)
        {
            var rows = characters[member.NativeSlot].action_data_table;
            int last = member.DisplayedDonorId == Donors[1] ? 15 : 9;
            if (rows is null || rows.Count <= last || rows[9] is not { Count: > 0 }
                || rows[last] is not { Count: > 0 })
            { reason = "fighter " + member.DisplayedDonorId + " lacks native Talisman action rows"; group = null; return false; }
        }
        reason = "";
        return true;
    }

    private static byte[]? ProjectRequirements(int index, GSD1.KYORYOKU_DATA recipe)
    {
        var runtime = ActiveRuntime();
        if (runtime is null || index != Recipe) return null;
        if (recipe.player_kazu != 2 || recipe.player_id is not { Count: >= 2 } ids
            || ids[0] != Donors[0] || ids[1] != Donors[1] || recipe.func_no != 1 || recipe.type != 3)
            throw new InvalidOperationException("Talisman recipe shape differs from the qualified native build.");
        if (!TryGroup(runtime, out var group, out string reason))
        { Once(runtime, "Talisman unavailable: " + reason); return null; }
        var first = group!.Members[0]; var second = group.Members[1];
        Once(runtime, "Talisman available: Pahn slot=" + first.NativeSlot + " role=" + first.CanonicalRoleId
            + "; Gremio slot=" + second.NativeSlot + " role=" + second.CanonicalRoleId + "; story IDs retained.");
        return new[] { checked((byte)first.CanonicalRoleId), checked((byte)second.CanonicalRoleId) };
    }

    private static bool Selected(int slot)
    {
        var work = GSD1.BattleBase.battle_work;
        return work is not null && slot > 0 && slot <= work.player_kazu
            && work.pm_data is { } rows && slot < rows.Count && rows[slot] is { c_koudou: 4, c_item: Recipe };
    }

    internal static bool BeforeAdmission(int slot, out Execution? action)
    {
        action = null;
        var runtime = ActiveRuntime();
        if (runtime is null || !Selected(slot)) return true;
        if (_execution?.IsCurrent == true)
        { Once(runtime, "ignored duplicate admission while Talisman is executing"); return false; }
        _execution = null;
        try
        {
            if (!TryGroup(runtime, out var group, out string reason))
            { CancelCommands(runtime, reason); return false; }
            bool leader = false;
            var work = GSD1.BattleBase.battle_work!;
            foreach (var member in group!.Members)
            {
                if (member.NativeSlot == slot) leader = true;
                if (!Selected(member.NativeSlot))
                { CancelCommands(runtime, "a partner no longer has this Unite queued"); return false; }
                if (work.pm_data[member.NativeSlot].finished_flg != 0)
                { CancelCommands(runtime, "a participant has already spent their action this round"); return false; }
            }
            if (!leader) { CancelCommands(runtime, "the selected leader is not Gremio or Pahn"); return false; }
            action = new Execution(runtime, work, slot, group);
            return true;
        }
        catch (Exception error) { CancelCommands(runtime, error.Message); return false; }
    }

    internal static void AfterAdmission(Execution? action, int result)
    {
        if (action is null) return;
        if (result > 0 && action.IsCurrent)
        {
            _execution = action;
            action.Runtime.Trace("UNITE PREVIEW Talisman admitted: leader=" + action.Leader
                + "; Pahn=" + action.Group.Members[0].NativeSlot + "; Gremio=" + action.Group.Members[1].NativeSlot + ".");
        }
        else if (result < 0)
        { CancelCommands(action.Runtime, "native eligibility rejected the queued group"); }
    }

    private static void CancelCommands(AvatarRuntime runtime, string reason)
    {
        // Called before native sequence admission. Native battle_turn02 receives
        // -1 and calls set_player_finished for the leader; surviving partners
        // defend instead of retrying a now-invalid group. No item/MP is spent.
        var work = GSD1.BattleBase.battle_work;
        if (work?.pm_data is { } rows)
            for (int slot = 1; slot <= work.player_kazu && slot < rows.Count; slot++)
                if (rows[slot] is { c_koudou: 4, c_item: Recipe } actor)
                { actor.c_koudou = 1; actor.c_item = 0; actor.c_mokuhyo = checked((sbyte)slot); }
        _execution = null;
        runtime.Trace("UNITE PREVIEW Talisman canceled before execution: " + reason + "; partners released to Defend.");
    }

    internal static FinishScope? BeforeFinish(int slot, Il2CppStructArray<int> participants)
    {
        var runtime = ActiveRuntime();
        if (runtime is null || !Selected(slot)) return null;
        var action = _execution;
        if (action is null || !action.IsCurrent || action.Leader != slot || participants is null || participants.Count < 2
            || GSD1.BattleBase.kmagic01_work?.p_id?.Pointer != participants.Pointer)
            throw Stop(runtime, "Talisman execution has no matching admitted native group.");
        if (!TryGroup(runtime, out var current, out string reason)
            || !SameMembers(action.Group, current!))
            throw Stop(runtime, "Talisman participants changed before animation preparation: " + reason);
        return new FinishScope(action, AvatarUniteTableScope.Begin());
    }

    private static bool SameMembers(UniteParticipantGroup left, UniteParticipantGroup right)
    {
        for (int i = 0; i < left.Members.Count; i++)
        {
            var a = left.Members[i]; var b = right.Members[i];
            if (a.NativeSlot != b.NativeSlot || a.CanonicalRoleId != b.CanonicalRoleId || a.Identity != b.Identity
                || a.DisplayedDonorId != b.DisplayedDonorId) return false;
        }
        return true;
    }

    private static Exception Stop(AvatarRuntime runtime, string reason)
    { runtime.Fail("Unite preview stopped: " + reason); return new InvalidOperationException(reason); }

    internal static void ObserveSequenceException(Exception? error)
    {
        if (error is null || !Enabled || _execution is not { } action
            || !ReferenceEquals(action.Runtime, AvatarRuntime.Current) || !action.Runtime.MatchesExecution(action.Epoch)
            || GSD1.BattleBase.battle_work?.Pointer != action.Battle || action.Runtime.FaultReason is not null) return;
        action.Runtime.Fail("Native Talisman sequence failed; unsafe retries stopped: " + error.GetType().Name + ": " + error.Message);
    }

    internal static bool CanStepSequence(bool preparingAnimation, int slot)
    {
        var runtime = AvatarRuntime.Current;
        if (!Enabled || runtime is null || !runtime.MatchesExecution(runtime.ExecutionEpoch)) return true;
        if (runtime.FaultReason is not null) return false;
        if (!preparingAnimation || !runtime.IsActive || !Selected(slot)) return true;
        try
        {
            var action = _execution;
            if (action?.IsCurrent != true || action.Leader != slot
                || GSD1.BattleBase.kmagic01_work?.p_id is not { Count: >= 2 }
                || !TryGroup(runtime, out var current, out string reason) || !SameMembers(action.Group, current!))
            {
                runtime.Fail("Unite preview stopped before animation: admitted Talisman participants or resources changed.");
                return false;
            }
            return true;
        }
        catch (Exception error) { runtime.Fail("Unite preview animation preflight failed: " + error.Message); return false; }
    }

    internal static void AfterFinish(FinishScope? state, Il2CppStructArray<int> participants)
    {
        if (state is null) return;
        try
        {
            var action = state.Action;
            if (!action.IsCurrent) throw Stop(action.Runtime, "battle ownership changed during Talisman preparation.");
            var work = GSD1.BattleBase.battle_work!;
            foreach (var member in action.Group.Members)
            {
                int index = member.DisplayedDonorId == Donors[0] ? 0 : 1;
                if (participants[index] != member.NativeSlot || work.pm_data[member.NativeSlot].finished_flg != 1
                    || work.pm_data[member.NativeSlot].c_mokuhyo != work.pm_data[action.Leader].c_mokuhyo)
                    throw Stop(action.Runtime, "native participant slots/action consumption disagree with the resolved group.");
            }
            action.Prepared = true;
            action.Runtime.Trace("UNITE PREVIEW Talisman prepared: Pahn slot=" + participants[0] + ", Gremio slot="
                + participants[1] + "; both finished_flg=1; target=" + work.pm_data[action.Leader].c_mokuhyo + ".");
        }
        finally { state.Dispose(); }
    }

    internal static DamageObservation? BeforeDamage(int leader, int count, int multiplier, Il2CppStructArray<int> participants)
    {
        var action = _execution;
        if (action?.IsCurrent != true || !action.Prepared || action.Leader != leader) return null;
        if (count != 2 || participants is null || participants.Count < 2
            || participants[0] != action.Group.Members[0].NativeSlot || participants[1] != action.Group.Members[1].NativeSlot)
            throw Stop(action.Runtime, "Talisman damage did not retain both prepared actor slots.");
        var work = GSD1.BattleBase.battle_work!;
        int target = work.pm_data[leader].c_mokuhyo;
        if (target < 1 || target > work.pm_kazu || target >= work.pm_data.Count || work.pm_data[target] is null)
            throw Stop(action.Runtime, "Talisman damage target is invalid.");
        if (action.DamageTargetIdentity == IntPtr.Zero)
        {
            action.DamageTarget = target;
            action.DamageTargetIdentity = work.pm_data[target].Pointer;
            action.TargetHpBefore = work.pm_data[target].hp;
        }
        return new DamageObservation(action, target, work.pm_data[target].hp, multiplier);
    }

    internal static void AfterDamage(DamageObservation? state)
    {
        if (state?.Action.IsCurrent != true) return;
        var target = GSD1.BattleBase.battle_work!.pm_data[state.Target];
        state.Action.Runtime.Trace("UNITE PREVIEW Talisman damage queued: target=" + state.Target + "; damage=" + target.damage
            + "; immediate HP " + state.Before + " -> " + target.hp + "; native multiplier=" + state.Multiplier
            + "; HP application may finish during playback; live actor stats retained.");
    }

    internal static void AfterSequence(int result)
    {
        var action = _execution;
        if (action?.IsCurrent != true || !action.Prepared || result != 0 || GSD1.BattleBase.kmagic01_work is not null) return;
        var work = GSD1.BattleBase.battle_work!;
        string damage = "";
        if (action.DamageTargetIdentity != IntPtr.Zero && action.DamageTarget < work.pm_data.Count
            && work.pm_data[action.DamageTarget] is { } target && target.Pointer == action.DamageTargetIdentity)
            damage = "; target=" + action.DamageTarget + "; final HP " + action.TargetHpBefore + " -> " + target.hp;
        action.Runtime.Trace("UNITE PREVIEW Talisman completed: Pahn slot=" + action.Group.Members[0].NativeSlot
            + ", Gremio slot=" + action.Group.Members[1].NativeSlot + "; finished="
            + work.pm_data[action.Group.Members[0].NativeSlot].finished_flg + "/"
            + work.pm_data[action.Group.Members[1].NativeSlot].finished_flg + damage + "; native sequence released.");
        _execution = null;
    }
}

[HarmonyPatch(typeof(GSD1.BattleBase), "command_kyoryoku99")]
internal static class AvatarTalismanCommitPatch
{
    [HarmonyPrepare] private static bool Prepare() => AvatarTalismanPreview.Enabled;
    [HarmonyPrefix] private static bool Prefix(ref int __result, out AvatarUniteTableScope? __state)
    {
        __state = null;
        var runtime = AvatarRuntime.Current;
        if (runtime is null || !runtime.MatchesExecution(runtime.ExecutionEpoch)) return true;
        if (runtime.FaultReason is not null) { __result = 0; return false; }
        try { __state = AvatarUniteTableScope.Begin(); return true; }
        catch (Exception error) { runtime.Fail("Talisman command projection failed: " + error.Message); __result = 0; return false; }
    }
    [HarmonyPostfix] private static void Postfix(AvatarUniteTableScope? __state) => __state?.Dispose();
    [HarmonyFinalizer] private static Exception? Finalizer(Exception? __exception, AvatarUniteTableScope? __state)
    { __state?.Dispose(); return __exception; }
}

[HarmonyPatch(typeof(GSD1.BattleBase), "player_kyoryoku_sub")]
internal static class AvatarTalismanAdmissionPatch
{
    [HarmonyPrepare] private static bool Prepare() => AvatarTalismanPreview.Enabled;
    [HarmonyPrefix] private static bool Prefix(int __0, ref int __result, out AvatarTalismanPreview.Execution? __state)
    {
        if (AvatarTalismanPreview.BeforeAdmission(__0, out __state)) return true;
        __result = -1; return false; // Stock failure/skip, not the native wait sentinel 0.
    }
    [HarmonyPostfix] private static void Postfix(int __result, AvatarTalismanPreview.Execution? __state)
        => AvatarTalismanPreview.AfterAdmission(__state, __result);
}

[HarmonyPatch(typeof(GSD1.BattleBase), "FinishFunc")]
internal static class AvatarTalismanFinishPatch
{
    [HarmonyPrepare] private static bool Prepare() => AvatarTalismanPreview.Enabled;
    [HarmonyPrefix] private static void Prefix(int __0, Il2CppStructArray<int> __1, out AvatarTalismanPreview.FinishScope? __state)
        => __state = AvatarTalismanPreview.BeforeFinish(__0, __1);
    [HarmonyPostfix] private static void Postfix(Il2CppStructArray<int> __1, AvatarTalismanPreview.FinishScope? __state)
        => AvatarTalismanPreview.AfterFinish(__state, __1);
    [HarmonyFinalizer] private static Exception? Finalizer(Exception? __exception, AvatarTalismanPreview.FinishScope? __state)
    {
        try { __state?.Dispose(); }
        catch (Exception cleanup) { __exception = __exception is null ? cleanup : new AggregateException(__exception, cleanup); }
        AvatarTalismanPreview.ObserveSequenceException(__exception);
        return __exception;
    }
}

[HarmonyPatch(typeof(GSD1.BattleBase), "DamageFunc")]
internal static class AvatarTalismanDamagePatch
{
    [HarmonyPrepare] private static bool Prepare() => AvatarTalismanPreview.Enabled;
    [HarmonyPrefix] private static void Prefix(int __0, int __1, int __2, Il2CppStructArray<int> __3,
        out AvatarTalismanPreview.DamageObservation? __state) => __state = AvatarTalismanPreview.BeforeDamage(__0, __1, __2, __3);
    [HarmonyPostfix] private static void Postfix(AvatarTalismanPreview.DamageObservation? __state) => AvatarTalismanPreview.AfterDamage(__state);
}

[HarmonyPatch(typeof(GSD1.BattleBase), "kmagic01_func03")]
internal static class AvatarTalismanCompletionPatch
{
    [HarmonyPrepare] private static bool Prepare() => AvatarTalismanPreview.Enabled;
    [HarmonyPostfix] private static void Postfix(int __result) => AvatarTalismanPreview.AfterSequence(__result);
    [HarmonyFinalizer] private static Exception? Finalizer(Exception? __exception)
    { AvatarTalismanPreview.ObserveSequenceException(__exception); return __exception; }
}

[HarmonyPatch]
internal static class AvatarTalismanSequenceGuardPatch
{
    [HarmonyPrepare] private static bool Prepare() => AvatarTalismanPreview.Enabled;
    [HarmonyTargetMethods] private static IEnumerable<MethodBase> Targets()
    {
        foreach (string name in new[] { "kmagic01_func02", "battle_turn_kyoryoku" })
            yield return AccessTools.Method(typeof(GSD1.BattleBase), name) ?? throw new MissingMethodException("BattleBase." + name);
    }
    [HarmonyPrefix] private static bool Prefix(MethodBase __originalMethod, int __0, ref int __result)
    {
        if (AvatarTalismanPreview.CanStepSequence(__originalMethod.Name == "kmagic01_func02", __0)) return true;
        __result = -1; return false; // Stop this sequence on an explicit runtime fault; never repeat unsafe native reads.
    }
    [HarmonyFinalizer] private static Exception? Finalizer(Exception? __exception)
    { AvatarTalismanPreview.ObserveSequenceException(__exception); return __exception; }
}

[HarmonyPatch]
internal static class AvatarTalismanBoundaryPatch
{
    [HarmonyPrepare] private static bool Prepare() => AvatarTalismanPreview.Enabled;
    [HarmonyTargetMethods] private static IEnumerable<MethodBase> Targets()
    {
        foreach (string name in new[] { "initialize_battle", "battle_end_free" })
            yield return AccessTools.Method(typeof(GSD1.BattleBase), name) ?? throw new MissingMethodException("BattleBase." + name);
    }
    [HarmonyPrefix] private static void Prefix() => AvatarTalismanPreview.Reset();
}
