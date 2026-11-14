using System;
using System.Linq;
using System.Reflection;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using SuikodenRandomizer.Suikoden2Plugin;

internal static partial class Program
{
    private static bool RawPolicy()=>Gsd2Graphics.PreserveRawResourceIds()&&Gsd2Presentation.PreserveRawResourceIds();
    private static void PreparePending(out ACTION_DATA action)
    {
        SetupCalculation(Facts[41]);action=Calculate(Facts[41],CalcMembers.Last());ScopeWork.battle_chara_no=action.chara_no;
    }
    static partial void IteratorResolverChecks();
    static partial void RawResourceAndPlaybackChecks()
    {
        IteratorResolverChecks();
        Case("Fancy Lad raw-resource hook targets exact verified nested iterator and typed owner contract",()=>{
            var target=Gsd2UniteRawResourcesPatch.Target();
            Check(target.DeclaringType==typeof(Unite.Unite41._Load_d__19)&&target.Name=="MoveNext"
                &&((MethodInfo)target).ReturnType==typeof(bool)&&target.GetParameters().Length==0,"Wrong native iterator hook.");
            var owner=target.DeclaringType!.GetProperty("__4__this")!;
            Check(owner.PropertyType==typeof(Unite.Unite41)&&owner.GetIndexParameters().Length==0,"Wrong native iterator owner field.");
        });
        Case("Owner-bound async raw iterator scopes nest across MoveNext calls without changing canonical actors",()=>{
            PreparePending(out _);var parent=new Unite.Unite41();ScopeWork.magic_func=parent;
            var iterator=new Unite.Unite41._Load_d__19{__4__this=parent};
            Check(!RawPolicy(),"Raw resource policy leaked before iterator.");
            object?[] outer={iterator,null};Call(typeof(Gsd2UniteRawResourcesPatch),"Prefix",outer);
            Check(outer[1] is not null&&RawPolicy(),"Owner-bound native iterator resources were remapped twice.");AssertCanonicalIds();
            Throws(Gsd2ShuffledUnites.Reset);
            object?[] inner={iterator,null};Call(typeof(Gsd2UniteRawResourcesPatch),"Prefix",inner);
            Check(inner[1] is not null&&RawPolicy(),"Nested original image iterator lost raw resource policy.");
            Check(Call(typeof(Gsd2UniteRawResourcesPatch),"Finalizer",null,inner[1]) is null&&RawPolicy(),"Inner resource cleanup restored parent too soon.");
            Check(Call(typeof(Gsd2UniteRawResourcesPatch),"Finalizer",null,outer[1]) is null&&!RawPolicy(),"Async resource scope leaked outside MoveNext.");
            AssertCanonicalIds();Restored();Gsd2ShuffledUnites.Reset();
        });
        Case("Unbound/foreign raw iterator owners do not claim native errors or enable resource bypass",()=>{
            foreach(bool foreign in new[]{false,true})
            {
                PreparePending(out _);ScopeWork.magic_func=new IOverFunc();if(foreign)ScopeRuntime.Owns=false;
                var iterator=new Unite.Unite41._Load_d__19{__4__this=new Unite.Unite41()};
                object?[] args={iterator,null};Call(typeof(Gsd2UniteRawResourcesPatch),"Prefix",args);
                Check(args[1] is null&&!RawPolicy(),"Unbound raw iterator was claimed.");
                var error=new InvalidOperationException("Unrelated async resource failure");
                Check(ReferenceEquals(Call(typeof(Gsd2UniteRawResourcesPatch),"Finalizer",error,args[1]),error),"Unrelated resource error lost.");
                Check(ScopeRuntime.FaultReason is null,"Unbound resource error claimed runtime.");AssertCanonicalIds();Restored();
            }
        });
        Case("Raw-resource native errors fault only their captured live owner and always unwind policy",()=>{
            foreach(int change in Enumerable.Range(0,3))
            {
                PreparePending(out _);var parent=new Unite.Unite41();ScopeWork.magic_func=parent;
                var iterator=new Unite.Unite41._Load_d__19{__4__this=parent};object?[] args={iterator,null};
                Call(typeof(Gsd2UniteRawResourcesPatch),"Prefix",args);var originalOwner=ScopeRuntime;
                if(change==1)Gsd2Runtime.Current=new Gsd2Runtime();else if(change==2)ScopeRuntime.ExecutionEpoch++;
                var error=new InvalidOperationException("Modeled native face iterator failure");
                Check(ReferenceEquals(Call(typeof(Gsd2UniteRawResourcesPatch),"Finalizer",error,args[1]),error),"Native resource error lost.");
                Check(!RawPolicy()&&(originalOwner.FaultReason is not null)==(change==0),"Raw resource policy/fault escaped owner scope.");AssertCanonicalIds();Restored();
            }
        });
        Case("Missing/zero native iterator owner fails exact hook contract without leaked policy",()=>{
            foreach(bool zero in new[]{false,true})
            {
                PreparePending(out _);var iterator=new Unite.Unite41._Load_d__19{__4__this=zero?new Unite.Unite41{Pointer=IntPtr.Zero}:null!};
                object?[] args={iterator,null};Throws(()=>Call(typeof(Gsd2UniteRawResourcesPatch),"Prefix",args));
                Check(args[1] is null&&!RawPolicy(),"Invalid owner created resource bypass.");
            }
        });
        Case("Misnested async raw scopes fail closed and leave no resource-policy lifetime behind",()=>{
            PreparePending(out _);var parent=new Unite.Unite41();ScopeWork.magic_func=parent;
            var outer=Gsd2ShuffledUnites.BeginRawResourceScope(parent.Pointer)!;var inner=Gsd2ShuffledUnites.BeginRawResourceScope(parent.Pointer)!;
            Check(Gsd2ShuffledUnites.Finish(outer,null) is not null,"Misnested raw scope accepted.");
            Check(Gsd2ShuffledUnites.Finish(inner,null) is not null&&!RawPolicy(),"Misnested resource scope leaked raw IDs.");
            Check(ScopeRuntime.FaultReason is not null,"Misnested native resource scope did not fault.");AssertCanonicalIds();Gsd2ShuffledUnites.Reset();
        });
        Case("Playback requires a validated action and native animation controller before aliasing IDs",()=>{
            foreach(bool missingCalculation in new[]{false,true})
            {
                ACTION_DATA action;
                if(missingCalculation)
                {
                    SetupCalculation(Facts[1]);action=new ACTION_DATA{chara_no=0,action_type=7,data=new Il2CppStructArray<byte>(1,99,99,99,99,99,99)};
                    Player(0).action_data=action;ScopeWork.battle_chara_no=0;ScopeWork.magic_func=new IOverFunc();
                }
                else {PreparePending(out action);ScopeWork.magic_func=null;}
                object?[] args={ScopeManager,null};Throws(()=>Call(typeof(Gsd2ShuffledUnitePlaybackPatch),"Prefix",args));
                Check(args[1] is null&&ScopeRuntime.FaultReason is not null&&!RawPolicy(),"Unvalidated native playback entered alias/resource view.");AssertCanonicalIds();
            }
        });
        Case("Playback PM/graph setter and rollback failures restore every captured identity before faulting",()=>{
            foreach(bool graph in new[]{false,true})
            {
                PreparePending(out _);ScopeWork.magic_func=new IOverFunc();
                if(graph){ScopeWork.b_chara[0].FailOnIdWrite=ScopeWork.b_chara[0].IdWrites+2;ScopeWork.b_chara[1].FailOnIdWrite=ScopeWork.b_chara[1].IdWrites+1;}
                else {Player(0).FailOnIdWrite=Player(0).IdWrites+2;Player(1).FailOnIdWrite=Player(1).IdWrites+1;}
                object?[] args={ScopeManager,null};Throws(()=>Call(typeof(Gsd2ShuffledUnitePlaybackPatch),"Prefix",args));
                AssertCanonicalIds();Check(!RawPolicy()&&ScopeRuntime.FaultReason is not null,"Failed playback alias or policy leaked.");
            }
        });
        Case("Playback helper exception restores both identity namespaces and raw policy through native finalizers",()=>{
            PreparePending(out _);ScopeWork.magic_func=new IOverFunc();object?[] playback={ScopeManager,null};
            Call(typeof(Gsd2ShuffledUnitePlaybackPatch),"Prefix",playback);Check(RawPolicy(),"Playback did not establish raw donor resource view.");
            object?[] helper={null};Call(typeof(Gsd2UniteCanonicalIslandPatch),"Prefix",helper);
            AssertCanonicalIds();Check(!RawPolicy(),"Canonical helper retained raw resource IDs.");
            var error=new InvalidOperationException("Modeled canonical native helper error");
            Check(ReferenceEquals(Call(typeof(Gsd2UniteCanonicalIslandPatch),"Finalizer",error,helper[0]),error),"Native helper error lost.");
            Check(ReferenceEquals(Call(typeof(Gsd2ShuffledUnitePlaybackPatch),"Finalizer",error,playback[1]),error),"Native playback error lost.");
            AssertCanonicalIds();Check(!RawPolicy()&&ScopeRuntime.FaultReason is not null,"Faulted playback leaked identities/resource policy.");
        });
    }
}

