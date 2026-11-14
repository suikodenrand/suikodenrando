// Explicit NativeModel helpers simulate the reviewed S2 calculation protocol;
// this fixture does not execute native formulas, action streams or animations.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using SuikodenRandomizer.Suikoden2Plugin;

internal static partial class Program
{
    private static int[] CalcRoles=Array.Empty<int>();
    private static int[] CalcDonors=Array.Empty<int>();
    private static int[] CalcMembers=Array.Empty<int>();
    private static BattlePlayerCharacter Player(int slot)=>(BattlePlayerCharacter)ScopeWork.pm_data[slot];
    private static void InstallIslands()
    {
        Harmony.Clear();
        var harmony=new Harmony("s2-unite-linked-fixture");
        var prefix=typeof(Gsd2UniteCanonicalIslandPatch).GetMethod("Prefix",BindingFlags.Static|BindingFlags.NonPublic)!;
        foreach(var target in Gsd2UniteCanonicalIslandPatch.Targets())harmony.Patch(target,new HarmonyMethod(prefix));
    }
    private static void SetupCalculation(Expected e,int rotation=0,int actionNumber=1)
    {
        SetupScope();InstallIslands();Gsd2ShuffledUnites.Configure(true);
        CalcMembers=Enumerable.Range(0,e.Scalars["player_kazu"]).Select(i=>(new[]{4,1,5,2,3}[i]+rotation)%6).ToArray();
        var required=e.Members.Select(x=>(int)(x&127)).ToHashSet();
        CalcRoles=Enumerable.Range(1,83).Reverse().Where(x=>!required.Contains(x)).Take(6).ToArray();
        CalcDonors=new int[6];for(int i=0;i<CalcMembers.Length;i++)CalcDonors[CalcMembers[i]]=e.Members[i]&127;
        var used=required.ToHashSet();
        for(int s=0;s<6;s++)if(CalcDonors[s]==0){CalcDonors[s]=Enumerable.Range(1,83).First(x=>!used.Contains(x));used.Add(CalcDonors[s]);}
        ScopeRuntime.Mapping.Clear();
        var rows=new PM_DATA[9];var graphs=new BATTLE_CHARA[9];
        for(int s=0;s<9;s++)
        {
            if(s<6)
            {
                rows[s]=new BattlePlayerCharacter{chara_id=(byte)CalcRoles[s],chara_no=s,battle_work=ScopeWork,hp=(short)(100+s),
                    c_koudou=4,c_item=(sbyte)e.Index,c_mokuhyo=7,action_num=(byte)actionNumber};
                graphs[s]=new BATTLE_CHARA{chara_id=CalcRoles[s]};ScopeRuntime.Mapping.Add(CalcRoles[s],CalcDonors[s]);
            }
            else {rows[s]=new PM_DATA{chara_id=(byte)(200+s),chara_no=s,battle_work=ScopeWork,hp=(short)(500+(s-6)*100)};graphs[s]=new BATTLE_CHARA{chara_id=200+s};}
        }
        ScopeWork.player_kazu=6;ScopeWork.pm_kazu=9;ScopeWork.pm_data=new Il2CppReferenceArray<PM_DATA>(rows);
        ScopeWork.b_chara=new Il2CppReferenceArray<BATTLE_CHARA>(graphs);
        ScopeWork.turn_step=new BattleTurnStep();ScopeWork.flg_table=new Il2CppStructArray<byte>(new byte[6]);ScopeWork.action_sp=null;
    }
    private static void AssertCanonicalIds()
    {
        for(int s=0;s<6;s++)Check(Player(s).chara_id==CalcRoles[s]&&ScopeWork.b_chara[s].chara_id==CalcRoles[s],"Canonical actor/graph IDs leaked at "+s);
    }
    private static int[] NativeModelResolve()
    {
        var row=Row(Player(CalcMembers.Last()).c_item);
        var result=new List<int>();
        for(int p=0;p<row.player_kazu;p++)
        {
            int id=row.player_id[p]&127;
            int[] matching=Enumerable.Range(0,6).Where(s=>Player(s).chara_id==id).ToArray();
            if(matching.Length!=1)return Array.Empty<int>();
            result.Add(matching[0]);
        }
        return result.ToArray();
    }
    private static int NativeModelGetIDToNo(int displayed)
    {
        int found=-1;
        for(int s=0;s<6;s++)if(Player(s).chara_id==displayed)found=s;
        return found;
    }
    private static void NativeModelCanonicalGearIsland()
    {
        bool graphAliases=Enumerable.Range(0,6).Any(s=>ScopeWork.b_chara[s].chara_id!=CalcRoles[s]);
        bool resourceView=Gsd2Graphics.PreserveRawResourceIds();
        var canonicalGear=CalcRoles.Select((role,slot)=>(role,gear:10000+slot)).ToDictionary(x=>x.role,x=>x.gear);
        object?[] outer={null};Call(typeof(Gsd2UniteCanonicalIslandPatch),"Prefix",outer);
        Check(outer[0] is not null,"Required helper did not enter canonical island.");
        try
        {
            for(int s=0;s<6;s++)Check(Player(s).chara_id==CalcRoles[s]&&ScopeWork.b_chara[s].chara_id==CalcRoles[s]&&canonicalGear[Player(s).chara_id]==10000+s,"Native gear lookup read donor story equipment.");
            Check(!Gsd2Graphics.PreserveRawResourceIds()&&!Gsd2Presentation.PreserveRawResourceIds(),"Canonical helper retained raw donor resource view.");
            int writes=Enumerable.Range(0,6).Sum(s=>Player(s).IdWrites);
            object?[] inner={null};Call(typeof(Gsd2UniteCanonicalIslandPatch),"Prefix",inner);
            Check(inner[0] is not null,"Nested helper did not retain canonical island.");
            Check(Enumerable.Range(0,6).Sum(s=>Player(s).IdWrites)==writes,"Nested helper rewrote canonical identities.");
            Check(Call(typeof(Gsd2UniteCanonicalIslandPatch),"Finalizer",null,inner[0]) is null,"Nested helper cleanup failed.");
            for(int s=0;s<6;s++)Check(Player(s).chara_id==CalcRoles[s],"Inner helper restored aliases before outer helper returned.");
        }
        finally {Check(Call(typeof(Gsd2UniteCanonicalIslandPatch),"Finalizer",null,outer[0]) is null,"Outer canonical helper cleanup failed.");}
        for(int s=0;s<6;s++)Check(Player(s).chara_id==CalcDonors[s]&&ScopeWork.b_chara[s].chara_id==(graphAliases?CalcDonors[s]:CalcRoles[s]),"Helper did not restore donor branch/graph view.");
        Check(Gsd2Graphics.PreserveRawResourceIds()==resourceView&&Gsd2Presentation.PreserveRawResourceIds()==resourceView,"Helper changed resource policy lifetime.");
    }
    private static ACTION_DATA NativeModelNormal(Expected e,int[] members)
    {
        for(int s=0;s<6;s++)Check(Player(s).chara_id==CalcDonors[s]&&ScopeWork.b_chara[s].chara_id==CalcRoles[s],"Normal branch identity namespace differs.");
        foreach(int member in members)Check(NativeModelGetIDToNo(CalcDonors[member])==member,"Native hardcoded-ID lookup selected wrong slot.");
        NativeModelCanonicalGearIsland();
        var bytes=Enumerable.Repeat((byte)99,7).ToArray();
        for(int p=1;p<members.Length;p++)bytes[p-1]=(byte)members[p];
        var action=new ACTION_DATA{chara_no=(sbyte)members[0],action_type=7,data=new Il2CppStructArray<byte>(bytes)};
        if(ScopeWork.action_sp is { } previous)previous.next_action_data=action;else ScopeWork.action_top=action;
        ScopeWork.action_sp=action;Player(members[0]).action_data=action;
        return action;
    }
    private static ACTION_DATA Calculate(Expected e,int initiator,bool upgrade=false,Action<ACTION_DATA>? simulatedNativeEffect=null,bool allowDerived=false)
    {
        var before=Enumerable.Range(0,6).Select(s=>Player(s).action_kaisu).ToArray();
        object?[] args={Player(initiator),null};Call(typeof(Gsd2ShuffledUniteCalculationPatch),"Prefix",args);
        var state=(Gsd2ShuffledUnites.Calculation?)args[1];Check(state is not null,"Calculation scope missing.");
        ACTION_DATA action;
        try
        {
            int[] members=NativeModelResolve();Check(members.SequenceEqual(CalcMembers),"Native canonical requirement matcher found wrong ordered participants.");
            foreach(int s in members)
            {
                Player(s).IncreaseActionKaisu();
                Check(Player(s).UniteCanCheck(),"Fixture participant cannot act in native calculation.");
            }
            Player(members[0]).c_mokuhyo=Player(initiator).c_mokuhyo;
            int effective=upgrade?40:e.Index;if(upgrade)Player(initiator).c_item=40;
            var nativeSlots=new Il2CppStructArray<int>(members.Concat(Enumerable.Repeat(0,6-members.Length)).ToArray());
            object?[] normal={Player(initiator),(byte)effective,nativeSlots,null};
            Call(typeof(Gsd2ShuffledUniteNormalPatch),"Prefix",normal);
            try{action=NativeModelNormal(e,members);simulatedNativeEffect?.Invoke(action);Call(typeof(Gsd2ShuffledUniteNormalPatch),"Postfix",1,normal[3]);}
            finally{Check(Call(typeof(Gsd2ShuffledUniteNormalPatch),"Finalizer",null,normal[3]) is null,"Native normal cleanup failed.");}
            AssertCanonicalIds();
            Call(typeof(Gsd2ShuffledUniteCalculationPatch),"Postfix",1,state);
            foreach(int s in Enumerable.Range(0,6))
            {
                int expected=before[s]+(members.Contains(s)?1:0);
                Check(Player(s).action_kaisu==expected,"Wrong number of native actions consumed at "+s);
                Check(Player(s).finished_flg==(expected>=Player(s).action_num?1:0),"Native finish threshold differs at "+s);
            }
        }
        finally{Check(Call(typeof(Gsd2ShuffledUniteCalculationPatch),"Finalizer",null,state) is null,"Calculation finalizer failed.");}
        AssertCanonicalIds();if(allowDerived)RestoredStable();else Restored();return action;
    }
    private static void ModelPlaybackCompletion(ACTION_DATA action)
    {
        ScopeWork.battle_chara_no=action.chara_no;ScopeWork.magic_func=new IOverFunc();
        Check(!Gsd2Graphics.PreserveRawResourceIds()&&!Gsd2Presentation.PreserveRawResourceIds(),"Resource view leaked before playback.");
        foreach(bool completed in new[]{false,true})
        {
            object?[] args={ScopeManager,null};Call(typeof(Gsd2ShuffledUnitePlaybackPatch),"Prefix",args);
            Check(args[1] is not null,"Queued action playback was not tracked.");
            try
            {
                for(int s=0;s<6;s++)Check(Player(s).chara_id==CalcDonors[s]&&ScopeWork.b_chara[s].chara_id==CalcDonors[s],"Playback native PM/graph lookup did not see donor IDs.");
                foreach(int member in CalcMembers)Check(NativeModelGetIDToNo(CalcDonors[member])==member&&Enumerable.Range(0,6).Single(s=>ScopeWork.b_chara[s].chara_id==CalcDonors[member])==member,"Native playback donor branch/graph scan chose wrong host.");
                Check(Gsd2Graphics.PreserveRawResourceIds()&&Gsd2Presentation.PreserveRawResourceIds(),"Playback remapped original donor resource IDs twice.");
                NativeModelCanonicalGearIsland();
                if(completed){ScopeWork.turn_step=new BattleTurnStep();ScopeWork.flg_table[0]=1;}
                Call(typeof(Gsd2ShuffledUnitePlaybackPatch),"Postfix",args[1]);
            }
            finally{Check(Call(typeof(Gsd2ShuffledUnitePlaybackPatch),"Finalizer",null,args[1]) is null,"Playback cleanup failed.");}
            AssertCanonicalIds();Check(!Gsd2Graphics.PreserveRawResourceIds()&&!Gsd2Presentation.PreserveRawResourceIds(),"Playback resource view leaked between frames.");
        }
        ScopeWork.battle_chara_no=-1;object?[] next={ScopeManager,null};Call(typeof(Gsd2ShuffledUnitePlaybackPatch),"Prefix",next);
        Check(next[1] is null,"Ordinary battle turn was claimed after native playback completion.");
    }
    static partial void RunCalculationChecks()
    {
        foreach(var e in Facts.Skip(1))Case("Recipe "+e.Index+" "+e.Name+" six foreign formations calculate with canonical gear and native +1 actions",()=>{
            for(int rotation=0;rotation<6;rotation++)
            {
                SetupCalculation(e,rotation,actionNumber:rotation%2+1);
                using(var menu=Gsd2UniteScope.Begin(ScopeManager))
                    Check(Enumerable.Range(0,e.Members.Length).Select(p=>(int)(Row(e.Index).player_id[p]&127)).SequenceEqual(CalcMembers.Select(s=>CalcRoles[s])),"Menu projection lost ordered hosts.");
                Restored();int leader=CalcMembers.Last();var pointers=ScopeWork.pm_data.Select(x=>x.Pointer).ToArray();
                var action=Calculate(e,leader);ModelPlaybackCompletion(action);
                Check(ScopeWork.pm_data.Select(x=>x.Pointer).SequenceEqual(pointers),"Adapter replaced battle actors.");
                Check(ScopeRuntime.Logs.Count(x=>x.Contains(" calculated:"))==1&&ScopeRuntime.Logs.Count(x=>x.Contains(" completed:"))==1,"Calculation/playback observation duplicated or missing.");
                Check(ScopeRuntime.FaultReason is null,"Valid fixture faulted: "+ScopeRuntime.FaultReason);
            }
        });
        Case("Kobold native upgrade3 to40 retains participants and uses upgraded metadata without wiping flags",()=>{
            var e=Facts[3];SetupCalculation(e);var action=Calculate(e,CalcMembers.Last(),upgrade:true);ModelPlaybackCompletion(action);
            Check(ScopeRuntime.Logs.Any(x=>x.Contains("id=40 100 Kobolds Attack calculated:")),"Native upgrade identity was not retained.");
            Check(Row(3).player_id[0]==156&&Row(40).player_id[0]==28,"Upgrade overwrote distinct original status flags.");
        });
        Case("Every required canonical helper registration is checked before native projection",()=>{
            foreach(var target in Gsd2UniteCanonicalIslandPatch.Targets().ToArray())
            {
                SetupCalculation(Facts[1]);Harmony.Remove(target);
                object?[] args={ScopeManager,null};Throws(()=>Call(typeof(Gsd2UniteSafetyPatch),"Prefix",args));
                Check(ScopeRuntime.FaultReason is not null&&Writes()==0,"Missing canonical helper admitted native read: "+target.Name);
            }
        });
        Case("Derived runtime table fields remain admitted after calculation while stable bytes still detect corruption",()=>{
            var e=Facts[4];SetupCalculation(e);Calculate(e,CalcMembers[0]);
            Row(4).hosei=200;Row(4).m_status=7;Row(4).m_status_prob=30;
            using(var scope=Gsd2UniteScope.Begin(ScopeWork))Check(scope is not null,"Native derived fields blocked later scope.");RestoredStable();
            Row(50).p_status=12;object?[] args={ScopeManager,null};Throws(()=>Call(typeof(Gsd2UniteSafetyPatch),"Prefix",args));
            Check(ScopeRuntime.FaultReason is not null,"Changed stable native status metadata admitted.");
        });
        Case("Shared loaded native table preserves derived values on first use and across reset/new battle reuse",()=>{
            var e=Facts[4];SetupCalculation(e);var shared=ScopeWork.unite_data_table;
            shared[4].hosei=237;shared[4].m_status=8;shared[4].m_status_prob=30;
            using(var first=Gsd2UniteScope.Begin(ScopeManager))Check(first is not null,"First encounter rejected reused native derived values.");
            Calculate(e,CalcMembers[0],allowDerived:true);Gsd2ShuffledUnites.Reset();
            var old=ScopeWork;ScopeWork=new BATTLE_WORK{player_kazu=6,pm_kazu=9,unite_data_table=shared,
                pm_data=old.pm_data,b_chara=old.b_chara,turn_step=new BattleTurnStep(),flg_table=new Il2CppStructArray<byte>(new byte[6])};
            for(int slot=0;slot<9;slot++)ScopeWork.pm_data[slot].battle_work=ScopeWork;
            for(int slot=0;slot<6;slot++){Player(slot).action_kaisu=0;Player(slot).finished_flg=0;Player(slot).action_data=null;}
            ScopeManager.battle_work=ScopeWork;
            using(var next=Gsd2UniteScope.Begin(ScopeManager))Check(next is not null,"New battle rejected retained native derived values.");
            var action=Calculate(e,CalcMembers.Last(),allowDerived:true);ModelPlaybackCompletion(action);
            Check(ScopeWork.unite_data_table.Pointer==shared.Pointer&&shared[4].hosei==237&&shared[4].m_status==8&&shared[4].m_status_prob==30,"Projection reset native derived fields or replaced the reused table.");
            RestoredStable();AssertCanonicalIds();Check(ScopeRuntime.FaultReason is null,"Reused table faulted runtime.");
        });
        Case("Native normal array order/size/recipe/initiator disagreements restore requirements and stop owned retries",()=>{
            foreach(int change in Enumerable.Range(0,4))
            {
                var e=Facts[6];SetupCalculation(e);var actor=Player(CalcMembers[0]);var calc=Gsd2ShuffledUnites.BeforeCalculate(actor)!;
                var slots=new Il2CppStructArray<int>(CalcMembers.Concat(new[]{0,0}).ToArray());byte id=6;
                if(change==0){int swap=slots[0];slots[0]=slots[1];slots[1]=swap;}
                else if(change==1)slots=new Il2CppStructArray<int>(CalcMembers.ToArray());else if(change==2)id=5;else actor=Player(Enumerable.Range(0,6).First(s=>!CalcMembers.Contains(s)));
                var error=Throws(()=>Gsd2ShuffledUnites.BeforeNormal(actor,id,slots));
                Check(Gsd2ShuffledUnites.Finish(calc,error) is not null,"Native normal failure swallowed.");
                AssertCanonicalIds();Restored();Check(ScopeRuntime.FaultReason is not null,"Native normal mismatch did not stop owned execution.");
            }
        });
        Case("Native prepared action owner/type/follower list disagreements cannot be queued",()=>{
            foreach(int change in Enumerable.Range(0,3))
            {
                var e=Facts[44];SetupCalculation(e);var actor=Player(CalcMembers[0]);var calc=Gsd2ShuffledUnites.BeforeCalculate(actor)!;
                var slots=new Il2CppStructArray<int>(CalcMembers.Concat(new[]{0,0}).ToArray());var view=Gsd2ShuffledUnites.BeforeNormal(actor,44,slots)!;
                NativeModelNormal(e,CalcMembers);var action=ScopeWork.action_sp!;
                if(change==0)action.chara_no=(sbyte)((action.chara_no+1)%6);else if(change==1)action.action_type=1;else action.data[1]=99;
                var error=Throws(()=>Gsd2ShuffledUnites.AfterNormal(view,1));Gsd2ShuffledUnites.Finish(view,error);Gsd2ShuffledUnites.Finish(calc,error);
                AssertCanonicalIds();Restored();Check(ScopeRuntime.FaultReason is not null,"Malformed action queued.");
            }
        });
        Case("Partial donor identity setter failure rolls back every PM ID and requirements",()=>{
            var e=Facts[1];SetupCalculation(e);var actor=Player(CalcMembers[0]);var calc=Gsd2ShuffledUnites.BeforeCalculate(actor)!;
            Player(2).FailOnIdWrite=Player(2).IdWrites+1;
            var slots=new Il2CppStructArray<int>(CalcMembers.Concat(new int[4]).ToArray());
            var error=Throws(()=>Gsd2ShuffledUnites.BeforeNormal(actor,1,slots));Gsd2ShuffledUnites.Finish(calc,error);
            AssertCanonicalIds();Restored();Check(ScopeRuntime.FaultReason is not null,"Failed temporary ID setter did not stop retries.");
        });
        Case("Nested calculation and teardown are rejected while a synchronous native scope is active",()=>{
            var e=Facts[1];SetupCalculation(e);var actor=Player(CalcMembers[0]);var calc=Gsd2ShuffledUnites.BeforeCalculate(actor)!;
            Throws(()=>Gsd2ShuffledUnites.BeforeCalculate(actor));Throws(Gsd2ShuffledUnites.Reset);
            Check(Gsd2ShuffledUnites.Finish(calc,null) is null,"Original calculation could not unwind.");AssertCanonicalIds();Restored();Gsd2ShuffledUnites.Reset();
        });
        Case("Complete KO/status groups retain native failed-calculation action costs without entering donor view",()=>{
            foreach(int condition in Enumerable.Range(0,8))
            {
                var e=Facts[44];SetupCalculation(e,actionNumber:2);int affected=CalcMembers[1];
                if(condition==0)Player(affected).hp=0;
                else Player(affected).status=new uint[]{0x4000,0x2000,0x10000,0x80,0x8000,0x1000,0x200}[condition-1];
                var actor=Player(CalcMembers.Last());var calc=Gsd2ShuffledUnites.BeforeCalculate(actor)!;
                foreach(int s in CalcMembers)Player(s).IncreaseActionKaisu();
                Check(!Player(affected).UniteCanCheck(),"Native ineligibility model did not reject condition.");
                Gsd2ShuffledUnites.AfterCalculate(calc,0);Check(Gsd2ShuffledUnites.Finish(calc,null) is null,"Stock native failure cleanup faulted.");
                Check(CalcMembers.All(s=>Player(s).action_kaisu==1&&Player(s).finished_flg==0),"Failed native group lost multi-action threshold semantics.");
                Check(ScopeRuntime.FaultReason is null&&ScopeRuntime.Logs.Any(x=>x.Contains("not calculated:")),"Stock ineligibility incorrectly became adapter fault.");AssertCanonicalIds();Restored();
            }
        });
        Case("Missing required displayed member stops before native partial action spending",()=>{
            SetupCalculation(Facts[1]);int absent=CalcMembers[1];ScopeRuntime.Mapping[CalcRoles[absent]]=83;
            object?[] args={Player(CalcMembers[0]),null};Throws(()=>Call(typeof(Gsd2ShuffledUniteCalculationPatch),"Prefix",args));
            Check(args[1] is null&&Enumerable.Range(0,6).All(s=>Player(s).action_kaisu==0),"Missing group partially spent native actions.");
            Check(ScopeRuntime.FaultReason is not null,"Missing queued group did not stop unsafe native execution.");AssertCanonicalIds();Restored();
        });
        Case("Native action-count under/over-spend and wrong finished threshold fail even for result0",()=>{
            foreach(int change in Enumerable.Range(0,3))
            {
                var e=Facts[6];SetupCalculation(e,actionNumber:2);var actor=Player(CalcMembers[0]);var calc=Gsd2ShuffledUnites.BeforeCalculate(actor)!;
                foreach(int s in CalcMembers)Player(s).IncreaseActionKaisu();
                if(change==0)Player(CalcMembers[1]).action_kaisu=0;else if(change==1)Player(CalcMembers[1]).action_kaisu=2;else Player(CalcMembers[1]).finished_flg=1;
                var error=Throws(()=>Gsd2ShuffledUnites.AfterCalculate(calc,0));Gsd2ShuffledUnites.Finish(calc,error);
                Check(ScopeRuntime.FaultReason is not null,"Native action consumption disagreement accepted.");AssertCanonicalIds();Restored();
            }
        });
        Case("Forward ID setter plus rollback failure still restores dirty PM identities during outer cleanup",()=>{
            var e=Facts[1];SetupCalculation(e);var actor=Player(CalcMembers[0]);var calc=Gsd2ShuffledUnites.BeforeCalculate(actor)!;
            Player(0).FailOnIdWrite=Player(0).IdWrites+2;Player(1).FailOnIdWrite=Player(1).IdWrites+1;
            var slots=new Il2CppStructArray<int>(CalcMembers.Concat(new int[4]).ToArray());
            var original=Throws(()=>Gsd2ShuffledUnites.BeforeNormal(actor,1,slots));
            Check(original is AggregateException,"Forward/rollback errors were not aggregated.");
            Check(Gsd2ShuffledUnites.Finish(calc,original) is not null,"Native identity failure was swallowed.");
            AssertCanonicalIds();Restored();Check(ScopeRuntime.FaultReason is not null,"Dirty identity rollback did not latch owned fault.");
        });
        Case("Captured calculation owner/epoch changes restore aliases without faulting a new owner",()=>{
            foreach(int change in Enumerable.Range(0,3))
            {
                var e=Facts[1];SetupCalculation(e);var actor=Player(CalcMembers[0]);var calc=Gsd2ShuffledUnites.BeforeCalculate(actor)!;
                var slots=new Il2CppStructArray<int>(CalcMembers.Concat(new int[4]).ToArray());var view=Gsd2ShuffledUnites.BeforeNormal(actor,1,slots)!;
                var originalOwner=ScopeRuntime;
                if(change==0)Gsd2Runtime.Current=new Gsd2Runtime();else if(change==1)ScopeRuntime.ExecutionEpoch++;else ScopeRuntime.Owns=false;
                var error=new InvalidOperationException("Stale native calculation error");Gsd2ShuffledUnites.Finish(view,error);Gsd2ShuffledUnites.Finish(calc,error);
                AssertCanonicalIds();Restored();Check(originalOwner.FaultReason is null&&Gsd2Runtime.Current!.FaultReason is null,"Stale calculation error claimed new owner.");
            }
        });
        Case("Every native recipe member can initiate while action owner remains first ordered member",()=>{
            foreach(var e in Facts.Skip(1))for(int p=0;p<e.Members.Length;p++)
            {
                SetupCalculation(e,actionNumber:2);var action=Calculate(e,CalcMembers[p]);
                Check(action.chara_no==CalcMembers[0],"Initiator replaced native first-member action owner.");
            }
        });
        Case("Native action tail reuse/unlinked append/missing queue head cannot pass calculation checkpoint",()=>{
            foreach(int change in Enumerable.Range(0,3))
            {
                var e=Facts[1];SetupCalculation(e);var actor=Player(CalcMembers[0]);
                ACTION_DATA? previous=null;
                if(change<2){previous=new ACTION_DATA{chara_no=(sbyte)CalcMembers[0],action_type=7,data=new Il2CppStructArray<byte>((byte)CalcMembers[1],99,99,99,99,99,99)};ScopeWork.action_top=ScopeWork.action_sp=previous;}
                var calc=Gsd2ShuffledUnites.BeforeCalculate(actor)!;var slots=new Il2CppStructArray<int>(CalcMembers.Concat(new int[4]).ToArray());var view=Gsd2ShuffledUnites.BeforeNormal(actor,1,slots)!;
                NativeModelNormal(e,CalcMembers);
                if(change==0)ScopeWork.action_sp=previous;else if(change==1)previous!.next_action_data=null;else ScopeWork.action_top=null;
                var error=Throws(()=>Gsd2ShuffledUnites.AfterNormal(view,1));Gsd2ShuffledUnites.Finish(view,error);Gsd2ShuffledUnites.Finish(calc,error);
                AssertCanonicalIds();Restored();Check(ScopeRuntime.FaultReason is not null,"Invalid native action chain was queued.");
            }
        });
        Case("Actor battle-work replacement during calculation is detected before native helper execution",()=>{
            var e=Facts[1];SetupCalculation(e);var actor=Player(CalcMembers[0]);var calc=Gsd2ShuffledUnites.BeforeCalculate(actor)!;
            Player(0).battle_work=new BATTLE_WORK();var slots=new Il2CppStructArray<int>(CalcMembers.Concat(new int[4]).ToArray());
            var error=Throws(()=>Gsd2ShuffledUnites.BeforeNormal(actor,1,slots));Gsd2ShuffledUnites.Finish(calc,error);
            AssertCanonicalIds();Restored();Check(ScopeRuntime.FaultReason is not null,"Changed actor battle owner reached native helper.");
        });
        Case("Three disjoint queued unites append distinct actions and complete independently in one native round",()=>{
            SetupCalculation(Facts[1]);CalcDonors=new[]{3,2,33,32,1,80};ScopeRuntime.Mapping.Clear();
            for(int slot=0;slot<6;slot++)ScopeRuntime.Mapping[CalcRoles[slot]]=CalcDonors[slot];
            var actions=new List<ACTION_DATA>();
            foreach(var group in new[]{(Id:1,Members:new[]{0,1}),(Id:2,Members:new[]{2,3}),(Id:20,Members:new[]{4,5})})
            {
                CalcMembers=group.Members;foreach(int slot in CalcMembers)Player(slot).c_item=(sbyte)group.Id;
                actions.Add(Calculate(Facts[group.Id],CalcMembers[1]));
            }
            Check(actions.Select(a=>a.Pointer).Distinct().Count()==3&&ScopeWork.action_top!.Pointer==actions[0].Pointer&&actions[0].next_action_data!.Pointer==actions[1].Pointer&&actions[1].next_action_data!.Pointer==actions[2].Pointer,"Native action chain reused or skipped a group.");
            foreach(var action in actions)ModelPlaybackCompletion(action);
            Check(ScopeRuntime.Logs.Count(x=>x.Contains(" completed:"))==3&&Enumerable.Range(0,6).All(slot=>Player(slot).action_kaisu==1),"Queued groups completed or consumed actions incorrectly.");
        });
        Case("Modeled native AoE/self/status results and reaction records are observed without rewriting live data",()=>{
            var e=Facts[44];SetupCalculation(e);var action=Calculate(e,CalcMembers[0],simulatedNativeEffect:a=>{
                foreach(int enemy in new[]{6,7,8})ScopeWork.pm_data[enemy].hp-=25;
                Player(CalcMembers[1]).status=0x80;Player(CalcMembers[2]).unbalance_cnt=2;
                a.reaction_data.Add(new REACTION_DATA{mokuhyo=7,reaction_type=1,damage=25,reaction_status=0});});
            var log=ScopeRuntime.Logs.Single(x=>x.Contains(" calculated:"));
            Check(log.Contains("native reactions=target=7")&&log.Contains("slot=6 HP")&&log.Contains("slot=7 HP")&&log.Contains("slot=8 HP")&&log.Contains("status 0 -> 128")&&log.Contains("unbalance 0 -> 2"),"Native result observations omitted AoE/status/reaction fields.");
            Check(ScopeWork.pm_data[6].hp==475&&ScopeWork.pm_data[7].hp==575&&ScopeWork.pm_data[8].hp==675&&action.reaction_data[0].damage==25,"Observer changed explicit model results.");AssertCanonicalIds();
        });
        Case("Playback exceptions retain original error and fault only the captured execution owner",()=>{
            foreach(int change in Enumerable.Range(0,3))
            {
                var e=Facts[1];SetupCalculation(e);var action=Calculate(e,CalcMembers[0]);ScopeWork.battle_chara_no=action.chara_no;ScopeWork.magic_func=new IOverFunc();
                object?[] args={ScopeManager,null};Call(typeof(Gsd2ShuffledUnitePlaybackPatch),"Prefix",args);Check(args[1] is not null,"Playback observation absent.");var originalOwner=ScopeRuntime;
                if(change==1)Gsd2Runtime.Current=new Gsd2Runtime();else if(change==2)ScopeRuntime.ExecutionEpoch++;
                var original=new InvalidOperationException("Modeled playback native failure");Check(ReferenceEquals(Call(typeof(Gsd2ShuffledUnitePlaybackPatch),"Finalizer",original,args[1]),original),"Playback finalizer lost native error.");
                Check((originalOwner.FaultReason is not null)==(change==0),"Playback fault attributed to wrong owner.");
                if(change>0)Check(Gsd2Runtime.Current!.FaultReason is null,"Stale playback error faulted current runtime.");AssertCanonicalIds();Restored();
            }
        });
        Case("Playback preflight catches replaced native actor and latches the owned runtime fault",()=>{
            var e=Facts[1];SetupCalculation(e);var action=Calculate(e,CalcMembers[0]);ScopeWork.battle_chara_no=action.chara_no;ScopeWork.magic_func=new IOverFunc();
            int replaced=Enumerable.Range(0,6).First(slot=>slot!=action.chara_no);ScopeWork.pm_data[replaced]=new BattlePlayerCharacter{chara_id=(byte)CalcRoles[replaced],chara_no=replaced,battle_work=ScopeWork};
            object?[] args={ScopeManager,null};Throws(()=>Call(typeof(Gsd2ShuffledUnitePlaybackPatch),"Prefix",args));Check(ScopeRuntime.FaultReason is not null&&args[1] is null,"Changed playback actor reached native stream.");AssertCanonicalIds();
        });
        Case("Protected Riou and unchanged Jowy/McDohl/Millie special IDs retain their native unite variants",()=>{
            foreach(int id in new[]{20,25,26,36,39,46})
            {
                var e=Facts[id];SetupCalculation(e,actionNumber:2);
                for(int p=0;p<e.Members.Length;p++)
                {
                    int donor=e.Members[p]&127;if(donor!=1&&donor<80)continue;
                    int slot=CalcMembers[p],oldRole=CalcRoles[slot];ScopeRuntime.Mapping.Remove(oldRole);
                    CalcRoles[slot]=donor;Player(slot).chara_id=(byte)donor;ScopeWork.b_chara[slot].chara_id=donor;ScopeRuntime.Mapping[donor]=donor;
                }
                var action=Calculate(e,CalcMembers.Last());ModelPlaybackCompletion(action);AssertCanonicalIds();
                Check(ScopeRuntime.FaultReason is null,"Protected/special native identity faulted for "+id);
            }
        });
        RawResourceAndPlaybackChecks();
        Case("Foreign ownership bypasses calculation without registering or touching temporary identities",()=>{
            SetupCalculation(Facts[1]);ScopeRuntime.Owns=false;ScopeWork.ThrowOnRead=true;
            Check(Gsd2ShuffledUnites.BeforeCalculate(Player(CalcMembers[0])) is null,"Foreign calculation controlled.");
            Check(Gsd2ShuffledUnites.BeginCanonicalIsland() is null,"Unbound canonical helper controlled.");
        });
    }
    static partial void RawResourceAndPlaybackChecks();
    private static void RestoredStable()
    {foreach(var e in Facts)Check(Definition(e).MatchesStable(Row(e.Index)),"Stable native requirements not restored at "+e.Index);}
}

