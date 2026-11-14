using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using SuikodenRandomizer.Suikoden2Plugin;

internal static partial class Program
{
    private static Gsd2Runtime ScopeRuntime=null!;
    private static BATTLE_WORK ScopeWork=null!;
    private static BattleManager ScopeManager=null!;
    private static object? Call(Type type,string method,params object?[] args)
    {
        try{return (type.GetMethod(method,BindingFlags.Static|BindingFlags.NonPublic)??throw new MissingMethodException(type.Name,method)).Invoke(null,args);}
        catch(TargetInvocationException error){throw error.InnerException!;}
    }
    private static void SetupScope()
    {
        ScopeRuntime=new Gsd2Runtime();Gsd2Runtime.Current=ScopeRuntime;
        Gsd2UniteScope.BeginProjectionBatch=null;Gsd2UniteScope.ProjectRequirements=null;
        ScopeWork=new BATTLE_WORK{
            unite_data_table=new Il2CppReferenceArray<UNITE_DATA>(Facts.Select(Native).ToArray()),
            b_chara=new Il2CppReferenceArray<BATTLE_CHARA>(Enumerable.Range(1,6).Select(id=>new BATTLE_CHARA{chara_id=id}).ToArray())};
        ScopeManager=new BattleManager{battle_work=ScopeWork};
    }
    private static void ScopeCase(string name,Action action)=>Case(name,()=>{SetupScope();action();});
    private static UNITE_DATA Row(int index)=>ScopeWork.unite_data_table[index];
    private static void Restored()
    {foreach(var e in Facts)Check(Definition(e).MatchesOriginal(Row(e.Index)),"Native requirement table not restored at "+e.Index);}
    private static int Writes()=>Facts.Sum(f=>Row(f.Index).player_id?.Writes??0);
    private static void ProjectAll()
    {
        Gsd2UniteScope.ProjectRequirements=(index,native,absent)=>Definition(Facts[index]).RawMembers
            .Select(x=>(byte)((x&128)|(((x&127)+17)%83+1))).ToArray();
    }
    private static bool NativeModelEligible(UNITE_DATA native)
    {
        var present=ScopeWork.b_chara.Select(x=>x.chara_id).ToHashSet();
        return native.player_kazu>0&&Enumerable.Range(0,native.player_kazu).All(i=>present.Contains(native.player_id[i]&127));
    }
    static partial void RunAdapterChecks()
    {
        ScopeChecks();RunCalculationChecks();
    }
    static partial void RunCalculationChecks();
    private static void ScopeChecks()
    {
        ScopeCase("Legacy default scope masks incompatible roles and preserves every ordered status exemption",()=>{
            ScopeRuntime.Mapping[3]=2;
            Check(NativeModelEligible(Row(1)),"Fixture original Cross pair is not present.");
            using(var scope=Gsd2UniteScope.Begin(ScopeManager))
            {
                Check(scope is not null&&!NativeModelEligible(Row(1)),"Legacy scope enabled unsafe original-role unite.");
                foreach(var e in Facts.Skip(1))for(int i=0;i<e.Members.Length;i++)
                    Check((Row(e.Index).player_id[i]&128)==(e.Members[i]&128),"Legacy mask changed status exemption.");
                Check(Row(2).player_id[0]==33&&Row(2).player_id[1]==32,"Unchanged recipe was edited.");
            }
            Restored();
        });
        ScopeCase("All50 callback projections preserve native order/bitflags and restore every original field",()=>{
            int batch=0,projected=0;
            Gsd2UniteScope.BeginProjectionBatch=work=>{Check(ReferenceEquals(work,ScopeWork),"Batch received foreign work.");batch++;};
            Gsd2UniteScope.ProjectRequirements=(index,native,absent)=>{
                projected++;Check(absent>0&&absent<=127&&!ScopeWork.b_chara.Any(x=>x.chara_id==absent),"Invalid masking identity.");
                Check(Definition(Facts[index]).MatchesOriginal(native),"Projection callback received changed original metadata.");
                return Facts[index].Members.Select(x=>(byte)((x&128)|(((x&127)+17)%83+1))).ToArray();};
            using(var scope=Gsd2UniteScope.Begin(ScopeWork))
            {
                Check(batch==1&&projected==50,"Scope did not make exactly one batch and50 ordered callbacks.");
                foreach(var e in Facts.Skip(1))
                {
                    var row=Row(e.Index);
                    Check(Definition(e).MatchesOriginalMetadata(row),"Projection changed native scalar fields.");
                    for(int i=0;i<e.Members.Length;i++)Check(row.player_id[i]==(byte)((e.Members[i]&128)|(((e.Members[i]&127)+17)%83+1)),"Projection changed participant position or flag.");
                }
            }
            Restored();
        });
        ScopeCase("Nested same-battle scopes reuse one projection and restore only after outer scope",()=>{
            int batches=0;ProjectAll();Gsd2UniteScope.BeginProjectionBatch=_=>batches++;
            using(var outer=Gsd2UniteScope.Begin(ScopeWork))
            {
                int writes=Writes();
                using(var inner=Gsd2UniteScope.Begin(ScopeManager))
                    Check(!Definition(Facts[1]).MatchesOriginal(Row(1)),"Nested scope lost projected table.");
                Check(Writes()==writes&&batches==1,"Nested scope restored or rebuilt parent projection.");
            }
            Restored();
        });
        ScopeCase("Foreign owner paths do not read native manager or battle state",()=>{
            ScopeRuntime.Owns=false;ScopeManager.ThrowOnRead=true;ScopeWork.ThrowOnRead=true;
            Check(Gsd2UniteScope.Begin(ScopeManager) is null&&Gsd2UniteScope.Begin(ScopeWork) is null,"Foreign ownership created a projection.");
            Check(ScopeManager.NativeReads==0&&ScopeWork.NativeReads==0,"Foreign native state was read.");
            object?[] args={ScopeManager,null};Call(typeof(Gsd2UniteSafetyPatch),"Prefix",args);
            Check(args[1] is null&&ScopeManager.NativeReads==0,"Foreign safety prefix read native manager.");
        });
        ScopeCase("Inactive owned scope leaves shared table alone and owned fault stops readers",()=>{
            ScopeRuntime.Active=false;Check(Gsd2UniteScope.Begin(ScopeWork) is null&&ScopeWork.NativeReads==0,"Inactive scope read or edited native arrays.");
            ScopeRuntime.Active=true;ScopeRuntime.Fail("fixture owned fault");
            Throws(()=>Gsd2UniteScope.Begin(ScopeWork));Check(ScopeWork.NativeReads==0,"Faulted native reader accessed table.");
        });
        ScopeCase("Every malformed native list is rejected before any shared byte writes",()=>{
            foreach(int change in Enumerable.Range(0,5))
            {
                SetupScope();ProjectAll();var row=Row(50);
                if(change==0)row.player_id=null!;else if(change==1)row.player_kazu=7;
                else if(change==2)row.player_id=new Il2CppStructArray<byte>(179);else if(change==3)ScopeWork.b_chara=null!;
                else ScopeWork.b_chara=new Il2CppReferenceArray<BATTLE_CHARA>();
                Throws(()=>Gsd2UniteScope.Begin(ScopeWork));
                if(change<3)Check(Facts.Where(e=>e.Index!=50).All(e=>Row(e.Index).player_id.Writes==0),"Late malformed row caused partial writes.");
            }
        });
        ScopeCase("Invalid projected count/highflag/zero identity fails whole-batch preflight",()=>{
            foreach(int change in Enumerable.Range(0,3))
            {
                SetupScope();
                Gsd2UniteScope.ProjectRequirements=(index,native,absent)=>{
                    var result=Facts[index].Members.ToArray();if(index!=50)return result;
                    if(change==0)return result.Take(result.Length-1).ToArray();
                    if(change==1)result[0]^=128;else result[0]=(byte)(result[0]&128);return result;};
                Throws(()=>Gsd2UniteScope.Begin(ScopeWork));Check(Writes()==0,"Malformed projected tail row caused earlier native writes.");Restored();
            }
        });
        ScopeCase("Partial native projection failure rolls back all completed and attempted setters",()=>{
            ProjectAll();Row(1).player_id.FailOnWrite=2;
            Throws(()=>Gsd2UniteScope.Begin(ScopeWork));Restored();
            using(var next=Gsd2UniteScope.Begin(ScopeWork))Check(!Definition(Facts[1]).MatchesOriginal(Row(1)),"Failed projection poisoned scope depth.");
            Restored();
        });
        ScopeCase("Safety prefix records owned preflight failure and later frames remain stopped",()=>{
            ScopeManager.battle_work=null!;object?[] args={ScopeManager,null};
            Throws(()=>Call(typeof(Gsd2UniteSafetyPatch),"Prefix",args));Check(ScopeRuntime.FaultReason is not null&&args[1] is null,"Owned safety preflight failure was not captured.");
            ScopeManager.battle_work=ScopeWork;args=new object?[]{ScopeManager,null};
            Throws(()=>Call(typeof(Gsd2UniteSafetyPatch),"Prefix",args));Check(Writes()==0,"Faulted next frame projected native bytes.");
        });
        ScopeCase("Safety postfix/finalizer restore once and preserve native reader exceptions",()=>{
            ProjectAll();object?[] args={ScopeManager,null};Call(typeof(Gsd2UniteSafetyPatch),"Prefix",args);
            var original=new InvalidOperationException("Modeled native candidate failure");
            Check(ReferenceEquals(Call(typeof(Gsd2UniteSafetyPatch),"Finalizer",original,args[1]),original),"Original native candidate error was lost.");
            Restored();Check(ScopeRuntime.FaultReason is not null,"Owned native reader error did not latch.");
            int writes=Writes();Call(typeof(Gsd2UniteSafetyPatch),"Postfix",args[1]);Check(Writes()==writes,"Repeated cleanup rewrote table.");
        });
        ScopeCase("Restoration errors aggregate with original native exception while other rows restore",()=>{
            ProjectAll();object?[] args={ScopeManager,null};Call(typeof(Gsd2UniteSafetyPatch),"Prefix",args);
            var row=Row(50).player_id;row.FailOnWrite=row.Writes+1;
            var original=new InvalidOperationException("Modeled original native reader error");
            var result=(Exception?)Call(typeof(Gsd2UniteSafetyPatch),"Finalizer",original,args[1]);
            Check(result is AggregateException aggregate&&aggregate.Flatten().InnerExceptions.Contains(original),"Cleanup error replaced original native exception.");
            foreach(var e in Facts.Take(50))Check(Definition(e).MatchesOriginal(Row(e.Index)),"Restoration failure skipped another row.");
            Check(ScopeRuntime.FaultReason is not null,"Failed restoration did not stop later owned native readers.");
            Throws(()=>Gsd2UniteScope.Begin(ScopeWork));
        });
        ScopeCase("Captured owner/epoch changes do not attribute stale native exceptions to current runtime",()=>{
            foreach(int change in Enumerable.Range(0,3))
            {
                SetupScope();ProjectAll();object?[] args={ScopeManager,null};Call(typeof(Gsd2UniteSafetyPatch),"Prefix",args);
                var originalOwner=ScopeRuntime;
                if(change==0)Gsd2Runtime.Current=new Gsd2Runtime();else if(change==1)ScopeRuntime.ExecutionEpoch++;else ScopeRuntime.Owns=false;
                var error=new InvalidOperationException("Unrelated stale reader failure");
                Check(ReferenceEquals(Call(typeof(Gsd2UniteSafetyPatch),"Finalizer",error,args[1]),error),"Stale native error was lost.");
                Restored();Check(originalOwner.FaultReason is null&&Gsd2Runtime.Current!.FaultReason is null,"Stale error faulted execution owner.");
            }
        });
        ScopeCase("Aliased member arrays are written once for compatible projections and reject conflicting rows",()=>{
            foreach(bool conflict in new[]{false,true})
            {
                SetupScope();Row(2).player_id=Row(1).player_id;var ids=Row(1).player_id;
                Gsd2UniteScope.ProjectRequirements=(index,native,absent)=>index is 1 or 2
                    ?new byte[]{(byte)(conflict&&index==2?10:9),8}:null;
                if(conflict){Throws(()=>Gsd2UniteScope.Begin(ScopeWork));Check(ids.Writes==0,"Conflicting alias wrote shared bytes.");}
                else{using(var scope=Gsd2UniteScope.Begin(ScopeWork))Check(ids.Writes==2,"Compatible alias wrote same member twice.");
                    Check(ids.Writes==4&&ids[0]==3&&ids[1]==2,"Compatible alias did not restore exactly once.");}
            }
        });
        ScopeCase("Misnested disposal restores native table and faults without poisoning future scopes",()=>{
            ProjectAll();var outer=Gsd2UniteScope.Begin(ScopeWork)!;var inner=Gsd2UniteScope.Begin(ScopeWork)!;
            Throws(outer.Dispose);inner.Dispose();Restored();Check(ScopeRuntime.FaultReason is not null,"Misnested owned scope did not fault.");
            SetupScope();ProjectAll();using(var scope=Gsd2UniteScope.Begin(ScopeWork))Check(scope is not null,"Misnest poisoned later battle scope.");Restored();
        });
        ScopeCase("Nested scope cannot cross battle pointers or live owners",()=>{
            foreach(int change in Enumerable.Range(0,2))
            {
                SetupScope();ProjectAll();using(var outer=Gsd2UniteScope.Begin(ScopeWork))
                {
                    if(change==0)Gsd2Runtime.Current=new Gsd2Runtime();
                    var other=change==0?ScopeWork:new BATTLE_WORK();
                    Throws(()=>Gsd2UniteScope.Begin(other));
                }
                Restored();
            }
        });
        ScopeCase("Safety targets retain every native matcher/window/command reader including commit99",()=>{
            var targets=(IEnumerable<MethodBase>)Call(typeof(Gsd2UniteSafetyPatch),"Targets")!;
            Check(targets.Select(x=>x.Name).SequenceEqual(new[]{"check_unite","make_command_data","make_unite_window","command_unite","UniteSelectBeforeFunc","command_unite01","command_unite99","make_unite_player_window"}),"Native scope hook set changed.");
        });
    }
}

