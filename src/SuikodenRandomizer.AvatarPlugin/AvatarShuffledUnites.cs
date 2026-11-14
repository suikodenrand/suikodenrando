extern alias GSD1;

using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using SuikodenRandomizer.Core.Combat;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace SuikodenRandomizer.AvatarPlugin;

/// <summary>Native S1 unites follow active displayed fighters; story IDs and live bases are never changed.</summary>
internal static class AvatarShuffledUnites
{
    internal static bool Enabled { get; private set; }
    private static Execution? _execution;
    [ThreadStatic] private static PartySnapshot? _batch;
    [ThreadStatic] private static string? _batchFailure;
    [ThreadStatic] private static bool _preparingProjection;
    private static readonly HashSet<string> Reported = new();
    private static readonly Dictionary<int, PropertyInfo> WorkGetters = new();
    private static readonly Dictionary<Type, PropertyInfo> ParticipantGetters = new();

    internal sealed class PartySnapshot
    {
        internal readonly AvatarRuntime Runtime;
        internal readonly long Epoch;
        internal readonly GSD1.BATTLE_WORK Work;
        internal readonly UniteParticipantSnapshot Actors;
        internal PartySnapshot(AvatarRuntime runtime, GSD1.BATTLE_WORK work, UniteParticipantSnapshot actors)
        { Runtime=runtime; Epoch=runtime.ExecutionEpoch; Work=work; Actors=actors; }
        internal bool IsCurrent => ReferenceEquals(Runtime, AvatarRuntime.Current) && Runtime.MatchesExecution(Epoch)
            && Runtime.IsActive && GSD1.BattleBase.battle_work?.Pointer == Work.Pointer;
    }

    internal readonly struct ActorBefore
    {
        internal readonly int Slot, Hp, Status, Unbalance, Dead;
        internal readonly IntPtr Identity;
        internal ActorBefore(int slot, GSD1.PM_DATA actor)
        { Slot=slot; Identity=actor.Pointer; Hp=actor.hp; Status=actor.status; Unbalance=actor.unbalance_cnt; Dead=actor.dead_flg; }
    }

    internal sealed class Execution
    {
        internal readonly AvatarRuntime Runtime;
        internal readonly long Epoch;
        internal readonly IntPtr Battle;
        internal readonly int Leader;
        internal readonly ShuffledUniteRecipe Recipe;
        internal readonly UniteParticipantGroup Group;
        internal readonly List<ActorBefore> Before = new();
        internal readonly Dictionary<int, int> LastPendingDamage = new();
        internal bool Prepared;
        internal IntPtr ParticipantArray;
        internal Execution(AvatarRuntime runtime, GSD1.BATTLE_WORK work, int leader,
            ShuffledUniteRecipe recipe, UniteParticipantGroup group)
        {
            Runtime=runtime; Epoch=runtime.ExecutionEpoch; Battle=work.Pointer; Leader=leader; Recipe=recipe; Group=group;
            if(work.pm_data is not { } rows || work.pm_kazu < work.player_kazu || work.pm_kazu >= rows.Count || work.pm_kazu > 127)
                throw new InvalidOperationException("Unite observation has an invalid native actor array.");
            for(int slot=1;slot<=work.pm_kazu;slot++)
                if(rows[slot] is { } actor) Before.Add(new ActorBefore(slot, actor));
        }
        internal bool IsCurrent => ReferenceEquals(Runtime, AvatarRuntime.Current) && Runtime.MatchesExecution(Epoch)
            && Runtime.IsActive && GSD1.BattleBase.battle_work?.Pointer == Battle;
        internal string Label => "id=" + Recipe.Id + " " + Recipe.Name;
    }

    internal sealed class FinishScope : IDisposable
    {
        internal readonly Execution Action;
        private readonly AvatarUniteTableScope? _scope;
        internal FinishScope(Execution action, AvatarUniteTableScope? scope) { Action=action; _scope=scope; }
        public void Dispose() => _scope?.Dispose();
    }

    internal sealed class CommitScope : IDisposable
    {
        internal readonly AvatarRuntime Runtime;
        internal readonly long Epoch;
        internal readonly IntPtr Battle;
        private readonly AvatarUniteTableScope? _scope;
        internal CommitScope(AvatarRuntime runtime, AvatarUniteTableScope? scope)
        { Runtime=runtime; Epoch=runtime.ExecutionEpoch; Battle=GSD1.BattleBase.battle_work?.Pointer ?? IntPtr.Zero; _scope=scope; }
        public void Dispose() => _scope?.Dispose();
    }

    internal static void Configure(bool enabled)
    {
        Enabled=enabled;
        if(enabled)
        {
            // A previous Talisman-only setting must not install a second owner of these native methods.
            AvatarTalismanPreview.Configure(false);
            AvatarUniteTableScope.BeginProjectionBatch=BeginProjection;
            AvatarUniteTableScope.ProjectRequirements=ProjectRequirements;
        }
        else
        {
            if(AvatarUniteTableScope.BeginProjectionBatch == BeginProjection) AvatarUniteTableScope.BeginProjectionBatch=null;
            if(AvatarUniteTableScope.ProjectRequirements == ProjectRequirements) AvatarUniteTableScope.ProjectRequirements=null;
        }
        Reset();
    }

    internal static void Reset() { _execution=null; _batch=null; _batchFailure=null; _preparingProjection=false; Reported.Clear(); }

    private static AvatarRuntime? ActiveRuntime()
    {
        var runtime=AvatarRuntime.Current;
        return Enabled && runtime?.IsActive == true && runtime.MatchesExecution(runtime.ExecutionEpoch) ? runtime : null;
    }

    private static void Once(AvatarRuntime runtime, string text)
    { if(Reported.Count<128 && Reported.Add(text)) runtime.Trace("UNITE PREVIEW " + text); }

    private static bool NativeTurnBased()
    {
        foreach(var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            var type=assembly.GetType("SuikodenRandomizer.S1BattlePreview.BattlePreviewPlugin", false);
            if(type is null) continue;
            foreach(string name in new[]{"ActiveAtbEnabled","OneShotAttackEnabled"})
            {
                var property=type.GetProperty(name, BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic);
                if(property?.PropertyType!=typeof(bool) || property.GetValue(null) is not bool value || value) return false;
            }
        }
        return true;
    }

    private static bool TryParty(AvatarRuntime runtime, bool requireEligibility, out PartySnapshot? party, out string reason)
    {
        party=null;
        if(!NativeTurnBased()) { reason="requires native turn-based combat; ATB/one-shot scheduling is active"; return false; }
        var work=GSD1.BattleBase.battle_work;
        if(work is null || work.player_kazu is <1 or >6 || work.pm_data is not { } pm
            || work.p_battle_data is not { } players || work.b_chara is not { } characters
            || pm.Count<=work.player_kazu || players.Count<=work.player_kazu || characters.Count<=work.player_kazu)
        { reason="native battle arrays are incomplete"; return false; }
        var actors=new List<UniteActor>();
        for(int slot=1;slot<=work.player_kazu;slot++)
        {
            var actor=pm[slot]; var player=players[slot]?.player_data; var character=characters[slot];
            if(actor is null || player is null || character is null || character.chara_id!=player.chara_id)
            { reason="canonical actor IDs disagree at slot " + slot; return false; }
            bool eligible=!requireEligibility || (actor.hp>0 && actor.dead_flg==0 && (actor.status&0x61)==0
                && !GSD1.BattleBase.dead_check(slot));
            actors.Add(new UniteActor(player.chara_id, runtime.MapDisplayId(player.chara_id), slot, actor.Pointer, eligible));
        }
        if(!UniteParticipantSnapshot.TryCreate(actors, out var snapshot, out var error))
        { reason=error ?? "active party identity is ambiguous"; return false; }
        party=new PartySnapshot(runtime, work, snapshot!); reason=""; return true;
    }

    private static void BeginProjection()
    {
        _batch=null; _batchFailure=null;
        var runtime=ActiveRuntime();
        if(runtime is not null && !TryParty(runtime, !_preparingProjection, out _batch, out var reason)) _batchFailure=reason;
    }

    private static bool Resolve(PartySnapshot party, ShuffledUniteRecipe recipe, out UniteParticipantGroup? group, out string reason)
    {
        group=null;
        if(!party.IsCurrent) { reason="party snapshot belongs to another battle/ownership epoch"; return false; }
        if(!party.Actors.TryResolve(recipe.Donors, out group, out var error))
        { reason=error ?? "participants could not be resolved"; return false; }
        for(int i=0;i<group!.Members.Count;i++)
        {
            var member=group.Members[i];
            var rows=party.Work.b_chara[member.NativeSlot].action_data_table;
            foreach(int row in recipe.Rows(i))
                if(rows is null || row<0 || row>=rows.Count || rows[row] is not { Count: >0 })
                { reason="fighter " + member.DisplayedDonorId + " lacks native action row " + row; group=null; return false; }
        }
        reason=""; return true;
    }

    private static byte[]? ProjectRequirements(int index, GSD1.KYORYOKU_DATA native)
    {
        var runtime=ActiveRuntime();
        if(runtime is null) return null;
        if(!AvatarUniteRecipes.TryGet(index, out var recipe) || !recipe!.Matches(native))
            throw new InvalidOperationException("Native Unite recipe " + index + " differs from the qualified game data.");
        string reason=_batchFailure ?? "no admitted party snapshot";
        UniteParticipantGroup? group=null;
        if(_batch is null || !Resolve(_batch, recipe, out group, out reason))
        { Once(runtime, "id=" + index + " " + recipe.Name + " unavailable: " + reason); return null; }
        var result=new byte[group!.Members.Count];
        for(int i=0;i<result.Length;i++) result[i]=checked((byte)group.Members[i].CanonicalRoleId);
        Once(runtime, "id=" + index + " " + recipe.Name + " available: " + Describe(group) + "; story IDs retained.");
        return result;
    }

    private static int SelectedRecipe(int slot)
    {
        var work=GSD1.BattleBase.battle_work;
        if(work is null || slot<1 || slot>work.player_kazu || work.pm_data is not { } rows || slot>=rows.Count
            || rows[slot] is not { c_koudou:4 } actor) return 0;
        return actor.c_item;
    }

    internal static bool BeforeAdmission(int slot, out Execution? action)
    {
        action=null;
        var owner=AvatarRuntime.Current;
        if(Enabled && owner is not null && owner.MatchesExecution(owner.ExecutionEpoch) && owner.FaultReason is not null) return false;
        var runtime=ActiveRuntime();
        int selected=SelectedRecipe(slot);
        if(runtime is null || selected==0) return true;
        if(_execution?.IsCurrent==true)
        { Once(runtime, "ignored a duplicate admission while " + _execution.Label + " is executing"); return false; }
        _execution=null;
        try
        {
            if(!AvatarUniteRecipes.TryGet(selected, out var recipe))
            { Cancel(runtime, selected, "unrecognized native recipe"); return false; }
            if(!TryParty(runtime, true, out var party, out string reason)
                || !Resolve(party!, recipe!, out var group, out reason))
            { Cancel(runtime, selected, reason); return false; }
            bool leader=false;
            foreach(var member in group!.Members)
            {
                if(member.NativeSlot==slot) leader=true;
                if(SelectedRecipe(member.NativeSlot)!=selected)
                { Cancel(runtime, selected, "a partner has a different queued command"); return false; }
                if(party!.Work.pm_data[member.NativeSlot].finished_flg!=0)
                { Cancel(runtime, selected, "a participant has already spent their action"); return false; }
            }
            if(!leader) { Cancel(runtime, selected, "leader is not a resolved participant"); return false; }
            action=new Execution(runtime, party!.Work, slot, recipe!, group);
            return true;
        }
        catch(Exception error) { Cancel(runtime, selected, error.Message); return false; }
    }

    internal static void AfterAdmission(Execution? action, int result)
    {
        if(action is null) return;
        if(result>0 && action.IsCurrent)
        {
            _execution=action;
            action.Runtime.Trace("UNITE PREVIEW " + action.Label + " admitted: leader=" + action.Leader
                + "; " + Describe(action.Group) + "; target=" + GSD1.BattleBase.battle_work!.pm_data[action.Leader].c_mokuhyo + ".");
        }
        else if(result<0) Cancel(action.Runtime, action.Recipe.Id, "native eligibility rejected the group");
    }

    private static void Cancel(AvatarRuntime runtime, int recipe, string reason)
    {
        var work=GSD1.BattleBase.battle_work;
        if(work?.pm_data is { } rows)
            for(int slot=1;slot<=work.player_kazu && slot<rows.Count;slot++)
                if(rows[slot] is { c_koudou:4 } actor && actor.c_item==recipe)
                { actor.c_koudou=1; actor.c_item=0; actor.c_mokuhyo=checked((sbyte)slot); }
        _execution=null;
        runtime.Trace("UNITE PREVIEW id=" + recipe + " canceled before execution: " + reason + "; its partners released to Defend.");
    }

    private static Il2CppStructArray<int>? WorkParticipants(ShuffledUniteRecipe recipe)
    {
        if(!WorkGetters.TryGetValue(recipe.Family, out var getter))
        {
            getter=typeof(GSD1.BattleBase).GetProperty(recipe.WorkGetter, BindingFlags.Static|BindingFlags.Public)
                ?? throw new MissingMemberException("BattleBase." + recipe.WorkGetter);
            WorkGetters.Add(recipe.Family, getter);
        }
        var work=getter.GetValue(null);
        if(work is null) return null;
        var type=work.GetType();
        if(!ParticipantGetters.TryGetValue(type, out var ids))
        {
            ids=type.GetProperty("p_id", BindingFlags.Instance|BindingFlags.Public)
                ?? throw new MissingMemberException(type.FullName + ".p_id");
            ParticipantGetters.Add(type, ids);
        }
        return ids.GetValue(work) as Il2CppStructArray<int>
            ?? throw new InvalidOperationException(recipe.WorkGetter + ".p_id has an unexpected native type.");
    }

    private static bool SameMembers(UniteParticipantGroup a, UniteParticipantGroup b)
    {
        if(a.Members.Count!=b.Members.Count) return false;
        for(int i=0;i<a.Members.Count;i++)
        {
            var x=a.Members[i];var y=b.Members[i];
            if(x.NativeSlot!=y.NativeSlot || x.Identity!=y.Identity || x.CanonicalRoleId!=y.CanonicalRoleId
                || x.DisplayedDonorId!=y.DisplayedDonorId) return false;
        }
        return true;
    }

    internal static FinishScope? BeforeFinish(int leader, Il2CppStructArray<int> participants)
    {
        var runtime=ActiveRuntime();
        if(runtime is null || SelectedRecipe(leader)==0) return null;
        var action=_execution;
        if(action?.IsCurrent!=true || action.Leader!=leader || action.Recipe.Id!=SelectedRecipe(leader)
            || participants is null || participants.Count<action.Group.Members.Count
            || WorkParticipants(action.Recipe)?.Pointer!=participants.Pointer)
            throw Stop(runtime, "native preparation has no matching admitted group/work array");
        // Native effects may have changed status since admission; identity remains authoritative.
        if(!TryParty(runtime, false, out var party, out string reason)
            || !Resolve(party!, action.Recipe, out var current, out reason) || !SameMembers(action.Group, current!))
            throw Stop(runtime, "participants/resources changed before preparation: " + reason);
        bool previous=_preparingProjection;
        _preparingProjection=true;
        try { return new FinishScope(action, AvatarUniteTableScope.Begin()); }
        finally { _preparingProjection=previous; }
    }

    internal static void AfterFinish(FinishScope? state, Il2CppStructArray<int> participants)
    {
        if(state is null) return;
        try
        {
            var action=state.Action;
            if(!action.IsCurrent) throw Stop(action.Runtime, "battle ownership changed during native preparation");
            var work=GSD1.BattleBase.battle_work!;
            for(int i=0;i<action.Group.Members.Count;i++)
            {
                var member=action.Group.Members[i]; var actor=work.pm_data[member.NativeSlot];
                if(participants[i]!=member.NativeSlot || actor.finished_flg!=1
                    || actor.c_mokuhyo!=work.pm_data[action.Leader].c_mokuhyo)
                    throw Stop(action.Runtime, "native participant order/target/action flags differ from the resolved group");
            }
            action.Prepared=true; action.ParticipantArray=participants.Pointer;
            action.Runtime.Trace("UNITE PREVIEW " + action.Label + " prepared: " + Describe(action.Group)
                + "; finished=" + Finished(action) + "; target=" + work.pm_data[action.Leader].c_mokuhyo + ".");
        }
        finally { state.Dispose(); }
    }

    internal static bool BeforeSequence()
    {
        var runtime=AvatarRuntime.Current;
        if(!Enabled || runtime is null || !runtime.MatchesExecution(runtime.ExecutionEpoch)) return true;
        if(runtime.FaultReason is not null) return false;
        var action=_execution;
        if(action is null) return true;
        try
        {
            if(!action.IsCurrent || !TryParty(runtime, false, out var party, out string reason)
                || !Resolve(party!, action.Recipe, out var current, out reason) || !SameMembers(action.Group, current!))
            { runtime.Fail("Unite preview stopped before a native sequence step: participants, resources or ownership changed."); return false; }
            var participants=WorkParticipants(action.Recipe);
            if(participants is not null && participants.Count<action.Group.Members.Count)
            { runtime.Fail("Unite preview native participant array is too short."); return false; }
            if(action.Prepared)
            {
                if(participants is null || participants.Pointer!=action.ParticipantArray)
                { runtime.Fail("Unite preview native participant array changed after preparation."); return false; }
                for(int i=0;i<action.Group.Members.Count;i++)
                    if(participants[i]!=action.Group.Members[i].NativeSlot)
                    { runtime.Fail("Unite preview native participant order changed after preparation."); return false; }
            }
            return true;
        }
        catch(Exception error) { runtime.Fail("Unite sequence preflight failed: " + error.Message); return false; }
    }

    internal static void ObservePendingDamage()
    {
        var action=_execution;
        if(action?.IsCurrent!=true || !action.Prepared) return;
        var work=GSD1.BattleBase.battle_work!;
        foreach(var before in action.Before)
        {
            if(before.Slot>=work.pm_data.Count || work.pm_data[before.Slot] is not { } actor || actor.Pointer!=before.Identity) continue;
            int pending=actor.damage;
            action.LastPendingDamage.TryGetValue(before.Slot, out int previous);
            action.LastPendingDamage[before.Slot]=pending;
            if(pending!=0 && pending!=previous)
                action.Runtime.Trace("UNITE PREVIEW " + action.Label + " damage pending: target=" + before.Slot
                    + "; damage=" + pending + "; HP=" + actor.hp + "; native effects retained.");
        }
    }

    internal static void AfterSequence()
    {
        var action=_execution;
        if(action?.IsCurrent!=true) return;
        ObservePendingDamage();
        var work=GSD1.BattleBase.battle_work!;
        if(!action.Prepared || work.kyoryoku_func is not null) return;
        if(WorkParticipants(action.Recipe) is not null)
            throw Stop(action.Runtime, "native sequence delegate ended without releasing its work object");
        var changes=new StringBuilder();
        foreach(var before in action.Before)
        {
            if(before.Slot>=work.pm_data.Count || work.pm_data[before.Slot] is not { } actor || actor.Pointer!=before.Identity) continue;
            if(actor.hp==before.Hp && actor.status==before.Status && actor.unbalance_cnt==before.Unbalance && actor.dead_flg==before.Dead) continue;
            changes.Append("; slot=").Append(before.Slot).Append(" HP ").Append(before.Hp).Append(" -> ").Append(actor.hp)
                .Append(" status ").Append(before.Status).Append(" -> ").Append(actor.status)
                .Append(" unbalance ").Append(before.Unbalance).Append(" -> ").Append(actor.unbalance_cnt)
                .Append(" dead ").Append(before.Dead).Append(" -> ").Append(actor.dead_flg);
        }
        action.Runtime.Trace("UNITE PREVIEW " + action.Label + " completed: " + Describe(action.Group)
            + "; finished=" + Finished(action) + changes + "; native sequence and work released.");
        _execution=null;
    }

    private static string Describe(UniteParticipantGroup group)
    {
        var text=new StringBuilder();
        foreach(var a in group.Members)
        {
            if(text.Length!=0) text.Append(", ");
            text.Append("fighter=").Append(a.DisplayedDonorId).Append(" slot=").Append(a.NativeSlot).Append(" role=").Append(a.CanonicalRoleId);
        }
        return text.ToString();
    }
    private static string Finished(Execution action)
    {
        var text=new StringBuilder();var work=GSD1.BattleBase.battle_work!;
        foreach(var member in action.Group.Members)
        { if(text.Length!=0)text.Append('/'); text.Append(work.pm_data[member.NativeSlot].finished_flg); }
        return text.ToString();
    }
    private static Exception Stop(AvatarRuntime runtime, string reason)
    { runtime.Fail("Unite preview stopped: " + reason); return new InvalidOperationException(reason); }
    internal static void ObserveSequenceException(Exception? error)
    {
        var action=_execution;
        if(error is null || !Enabled || action is null || !ReferenceEquals(action.Runtime, AvatarRuntime.Current)
            || !action.Runtime.MatchesExecution(action.Epoch) || GSD1.BattleBase.battle_work?.Pointer!=action.Battle
            || action.Runtime.FaultReason is not null) return;
        action.Runtime.Fail("Native shuffled Unite failed; unsafe retries stopped: " + error.GetType().Name + ": " + error.Message);
    }
    internal static void ObserveCommitException(CommitScope? state, Exception? error)
    {
        if(error is null || state is null || !ReferenceEquals(state.Runtime, AvatarRuntime.Current)
            || !state.Runtime.MatchesExecution(state.Epoch) || GSD1.BattleBase.battle_work?.Pointer!=state.Battle
            || state.Runtime.FaultReason is not null) return;
        state.Runtime.Fail("Native Unite command/requirements restoration failed; unsafe retries stopped: "
            + error.GetType().Name + ": " + error.Message);
    }
}

[HarmonyPatch(typeof(GSD1.BattleBase), "command_kyoryoku99")]
internal static class AvatarShuffledUniteCommitPatch
{
    [HarmonyPrepare] private static bool Prepare()=>AvatarShuffledUnites.Enabled;
    [HarmonyPrefix] private static bool Prefix(ref int __result, out AvatarShuffledUnites.CommitScope? __state)
    {
        __state=null;var runtime=AvatarRuntime.Current;
        if(runtime is null || !runtime.MatchesExecution(runtime.ExecutionEpoch)) return true;
        if(runtime.FaultReason is not null) { __result=0;return false; }
        if(!runtime.IsActive) return true;
        try { __state=new AvatarShuffledUnites.CommitScope(runtime, AvatarUniteTableScope.Begin());return true; }
        catch(Exception error) { runtime.Fail("Unite command projection failed: " + error.Message);__result=0;return false; }
    }
    [HarmonyPostfix] private static void Postfix(AvatarShuffledUnites.CommitScope? __state)=>__state?.Dispose();
    [HarmonyFinalizer] private static Exception? Finalizer(Exception? __exception, AvatarShuffledUnites.CommitScope? __state)
    {
        try { __state?.Dispose(); }catch(Exception cleanup) { __exception=__exception is null?cleanup:new AggregateException(__exception,cleanup); }
        AvatarShuffledUnites.ObserveCommitException(__state,__exception);return __exception;
    }
}

[HarmonyPatch(typeof(GSD1.BattleBase), "player_kyoryoku_sub")]
internal static class AvatarShuffledUniteAdmissionPatch
{
    [HarmonyPrepare] private static bool Prepare()=>AvatarShuffledUnites.Enabled;
    [HarmonyPrefix] private static bool Prefix(int __0, ref int __result, out AvatarShuffledUnites.Execution? __state)
    { if(AvatarShuffledUnites.BeforeAdmission(__0,out __state))return true;__result=-1;return false; }
    [HarmonyPostfix] private static void Postfix(int __result, AvatarShuffledUnites.Execution? __state)=>AvatarShuffledUnites.AfterAdmission(__state,__result);
}

[HarmonyPatch(typeof(GSD1.BattleBase), "FinishFunc")]
internal static class AvatarShuffledUniteFinishPatch
{
    [HarmonyPrepare] private static bool Prepare()=>AvatarShuffledUnites.Enabled;
    [HarmonyPrefix] private static void Prefix(int __0, Il2CppStructArray<int> __1, out AvatarShuffledUnites.FinishScope? __state)
        =>__state=AvatarShuffledUnites.BeforeFinish(__0,__1);
    [HarmonyPostfix] private static void Postfix(Il2CppStructArray<int> __1, AvatarShuffledUnites.FinishScope? __state)=>AvatarShuffledUnites.AfterFinish(__state,__1);
    [HarmonyFinalizer] private static Exception? Finalizer(Exception? __exception, AvatarShuffledUnites.FinishScope? __state)
    {
        try { __state?.Dispose(); }catch(Exception cleanup) { __exception=__exception is null?cleanup:new AggregateException(__exception,cleanup); }
        AvatarShuffledUnites.ObserveSequenceException(__exception);return __exception;
    }
}

[HarmonyPatch(typeof(GSD1.BattleBase), "battle_turn_kyoryoku")]
internal static class AvatarShuffledUniteSequencePatch
{
    [HarmonyPrepare] private static bool Prepare()=>AvatarShuffledUnites.Enabled;
    [HarmonyPrefix] private static bool Prefix(ref int __result)
    { if(AvatarShuffledUnites.BeforeSequence())return true;__result=-1;return false; }
    [HarmonyPostfix] private static void Postfix()=>AvatarShuffledUnites.AfterSequence();
    [HarmonyFinalizer] private static Exception? Finalizer(Exception? __exception)
    { AvatarShuffledUnites.ObserveSequenceException(__exception);return __exception; }
}

[HarmonyPatch(typeof(GSD1.BattleBase), "DamageFunc")]
internal static class AvatarShuffledUniteDamagePatch
{
    [HarmonyPrepare] private static bool Prepare()=>AvatarShuffledUnites.Enabled;
    [HarmonyPostfix] private static void Postfix()=>AvatarShuffledUnites.ObservePendingDamage();
}

[HarmonyPatch]
internal static class AvatarShuffledUniteBoundaryPatch
{
    [HarmonyPrepare] private static bool Prepare()=>AvatarShuffledUnites.Enabled;
    [HarmonyTargetMethods] private static IEnumerable<MethodBase> Targets()
    {
        foreach(string name in new[]{"initialize_battle","battle_end_free"})
            yield return AccessTools.Method(typeof(GSD1.BattleBase),name)??throw new MissingMethodException("BattleBase."+name);
    }
    [HarmonyPrefix] private static void Prefix()=>AvatarShuffledUnites.Reset();
}
