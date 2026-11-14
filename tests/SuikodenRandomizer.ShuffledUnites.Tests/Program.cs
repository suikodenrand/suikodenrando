// Actual production classes are linked by the project. NativeModel* helpers are
// independent managed models of reviewed native matching/FinishFunc effects.
// They never execute foreign game code, Harmony detours, damage formulas or animation.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using SuikodenRandomizer.AvatarPlugin;
using Scheduler = SuikodenRandomizer.S1BattlePreview.BattlePreviewPlugin;

internal static class Program
{
    private sealed record Expected(int Id, string Name, int Function, int Family, int Type, int NameId, int HelpId, int[] Donors, int[]? Opds, int[][] Rows);
    private static readonly Expected[] Facts = ReadFacts();
    private static AvatarRuntime _runtime = null!;
    private static int _passed;
    private static BATTLE_WORK Work => BattleBase.battle_work!;
    private static PM_DATA Actor(int slot) => Work.pm_data[slot];
    private static KYORYOKU_DATA Recipe(int id) => BattleBase.kyoryoku_data_table[id];
    private static Expected Fact(int id) => Facts.Single(x => x.Id == id);
    private static readonly int[] EnemySlots = { 7, 8, 9 };

    private static Expected[] ReadFacts()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("SuikodenRandomizer.ShuffledUnites.Tests.expected-native.json")!;
        using var data = JsonDocument.Parse(stream);
        return data.RootElement.EnumerateArray().Select(r => new Expected(
            r.GetProperty("index").GetInt32(), r.GetProperty("englishName").GetString()!,
            r.GetProperty("func_no").GetInt32(), r.GetProperty("animationFamily").GetInt32(),
            r.GetProperty("type").GetInt32(), r.GetProperty("name").GetInt32(), r.GetProperty("help").GetInt32(),
            r.GetProperty("player_id").EnumerateArray().Select(x => x.GetInt32()).ToArray(),
            r.GetProperty("opd_nums").ValueKind == JsonValueKind.Null ? null : r.GetProperty("opd_nums").EnumerateArray().Select(x => x.GetInt32()).ToArray(),
            r.GetProperty("rows").EnumerateArray().Select(a => a.EnumerateArray().Select(x => x.GetInt32()).ToArray()).ToArray())).ToArray();
    }
    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
    private static void Case(string name, Action action)
    { Setup(); action(); _passed++; Console.WriteLine("PASS " + name); }
    private static Exception Throws(Action action)
    {
        try { action(); } catch (Exception error) { return error; }
        throw new InvalidOperationException("Expected failure was not raised.");
    }
    private static object? Call(Type type, string method, params object?[] args)
    {
        try { return (type.GetMethod(method, BindingFlags.Static|BindingFlags.NonPublic) ?? throw new MissingMethodException(type.Name, method)).Invoke(null,args); }
        catch (TargetInvocationException error) { throw error.InnerException!; }
    }
    private static Type Patch(string suffix) => Assembly.GetExecutingAssembly().GetTypes().Single(t => t.Name == "AvatarShuffledUnite" + suffix + "Patch");
    private static PropertyInfo WorkGetter(Expected fact) => typeof(BattleBase).GetProperty("kmagic" + fact.Family.ToString("00") + "_work")!;
    private static void ClearNativeWork()
    {
        foreach (var family in Facts.Select(x => x.Family).Distinct()) WorkGetter(Facts.First(x => x.Family == family)).SetValue(null,null);
        if (BattleBase.battle_work is { } work) work.kyoryoku_func = null;
    }
    private static Il2CppStructArray<int> MakeNativeWork(Expected fact)
    {
        var getter=WorkGetter(fact);
        object state = Activator.CreateInstance(getter.PropertyType)!;
        var ids = new Il2CppStructArray<int>(new int[fact.Donors.Length]);
        getter.PropertyType.GetProperty("p_id")!.SetValue(state,ids);
        getter.PropertyType.GetProperty("p_flg")!.SetValue(state,new Il2CppStructArray<int>(new int[fact.Donors.Length]));
        getter.SetValue(null,state); Work.kyoryoku_func = new object();
        return ids;
    }
    private static void Setup()
    {
        Scheduler.ActiveAtbEnabled=false; Scheduler.OneShotAttackEnabled=false;
        _runtime = new AvatarRuntime(); AvatarRuntime.Current=_runtime;
        var native = new KYORYOKU_DATA[34];
        foreach (var f in Facts) native[f.Id]=new KYORYOKU_DATA {
            player_kazu=(byte)f.Donors.Length, player_id=new NativeArray<byte>(f.Donors.Select(x=>(byte)x).ToArray()),
            func_no=(byte)f.Function, type=(ushort)f.Type, name=f.NameId, help=f.HelpId,
            opd_nums=f.Opds is null ? null : new NativeArray<int>(f.Opds.ToArray()) };
        BattleBase.kyoryoku_data_table=new NativeArray<KYORYOKU_DATA>(native);
        var actors = Enumerable.Range(0,10).Select(i=>new PM_DATA { hp=i>6 ? 500+(i-7)*100 : 100+i, c_mokuhyo=7 }).ToArray();
        var players = new P_BATTLE_DATA[7]; var chars = new B_CHARA[7];
        for (int slot=1;slot<=6;slot++) {
            players[slot]=new P_BATTLE_DATA { player_data=new PLAYER_DATA { chara_id=100+slot } };
            chars[slot]=new B_CHARA { chara_id=100+slot, action_data_table=FullRows() };
        }
        BattleBase.battle_work=new BATTLE_WORK { player_kazu=6, pm_kazu=9,
            p_battle_data=new NativeArray<P_BATTLE_DATA>(players), b_chara=new NativeArray<B_CHARA>(chars), pm_data=new NativeArray<PM_DATA>(actors) };
        BattleBase.DeadCheck=_=>false; ClearNativeWork(); AvatarShuffledUnites.Configure(true);
    }
    private static NativeArray<Il2CppStructArray<short>> FullRows()
        => new NativeArray<Il2CppStructArray<short>>(Enumerable.Range(0,20).Select(_=>new Il2CppStructArray<short>((short)1)).ToArray());

    // Builds a bijective active six-member fixture, independently of production
    // recipe/resolver code. Every tested required donor has an unrelated role.
    private static int[] SyntheticSnapshot(Expected fact, int rotation=0, bool stockTir=false)
    {
        int[] slots = Enumerable.Range(0,fact.Donors.Length).Select(i=>(new[]{5,2,6,3}[i]+rotation-1)%6+1).ToArray();
        var required = fact.Donors.ToHashSet();
        var roles = Enumerable.Range(0,110).Reverse().Where(x=>!required.Contains(x) && x!=8).Take(6).ToArray();
        var donors = Enumerable.Range(0,110).Where(x=>!required.Contains(x) && x!=8).Take(6).ToArray();
        for (int i=0;i<fact.Donors.Length;i++) donors[slots[i]-1]=fact.Donors[i];
        // Replace unused filler duplicates introduced by required placement.
        var used = required.ToHashSet();
        for(int slot=1;slot<=6;slot++) if(!slots.Contains(slot)) {
            donors[slot-1]=Enumerable.Range(0,110).First(x=>!used.Contains(x) && x!=8); used.Add(donors[slot-1]); }
        if(stockTir) {
            int tir=Array.IndexOf(fact.Donors,8);
            Check(tir>=0,"Stock Tir fixture needs a Tir recipe.");
            int swap=Array.IndexOf(slots,1);
            if(swap>=0) slots[swap]=slots[tir];
            slots[tir]=1;
            // Recompute to preserve every ordered donor on its resulting slot.
            var free=Enumerable.Range(0,110).Where(x=>!required.Contains(x)&&x!=8).GetEnumerator();
            for(int s=1;s<=6;s++) { free.MoveNext(); donors[s-1]=free.Current; }
            for(int i=0;i<slots.Length;i++) donors[slots[i]-1]=fact.Donors[i];
            roles[0]=8;
        }
        _runtime.Mapping.Clear();
        for (int slot=1;slot<=6;slot++) {
            int role=roles[slot-1]; Work.p_battle_data[slot].player_data.chara_id=role; Work.b_chara[slot].chara_id=role;
            _runtime.Mapping.Add(role,donors[slot-1]);
        }
        return slots;
    }
    private static void Party(params int[] displayed)
    {
        Check(displayed.Length==6,"Party fixture needs six members."); _runtime.Mapping.Clear();
        int[] roles=Enumerable.Range(0,110).Reverse().Where(x=>!displayed.Contains(x)).Take(6).ToArray();
        for(int s=1;s<=6;s++) { Work.p_battle_data[s].player_data.chara_id=roles[s-1]; Work.b_chara[s].chara_id=roles[s-1]; _runtime.Mapping[roles[s-1]]=displayed[s-1]; }
    }
    private static int[] NativeModelMembers(KYORYOKU_DATA native, bool requireEligibility=true)
    {
        var result=new List<int>();
        for(int i=0;i<native.player_kazu;i++) {
            var matches=Enumerable.Range(1,6).Where(s=>Work.p_battle_data[s].player_data.chara_id==native.player_id[i]
                && (!requireEligibility || (Actor(s).hp>0 && Actor(s).dead_flg==0 && (Actor(s).status&0x61)==0 && !BattleBase.dead_check(s)))).ToArray();
            if(matches.Length!=1) return Array.Empty<int>(); result.Add(matches[0]);
        }
        return result.ToArray();
    }
    private static void NativeModelCommit(Expected fact,int target)
    {
        var slots=NativeModelMembers(Recipe(fact.Id)); Check(slots.Length==fact.Donors.Length,"Native commit model could not match projected roles.");
        foreach(int s in slots) { Actor(s).c_koudou=4; Actor(s).c_item=(sbyte)fact.Id; Actor(s).c_mokuhyo=(sbyte)target; }
    }
    private static void AssertRestored()
    {
        Check(BattleBase.kyoryoku_data_table.Count==34 && BattleBase.kyoryoku_data_table[0] is null && BattleBase.kyoryoku_data_table[33] is null,"Native table sentinels changed.");
        foreach(var f in Facts) {
            var r=Recipe(f.Id);
            Check(r.player_kazu==f.Donors.Length && r.func_no==f.Function && r.type==f.Type && r.name==f.NameId && r.help==f.HelpId,"Native metadata changed at "+f.Id);
            Check(Enumerable.Range(0,r.player_id.Count).Select(i=>(int)r.player_id[i]).SequenceEqual(f.Donors),"Native ordered IDs leaked at "+f.Id);
            Check((r.opd_nums is null)==(f.Opds is null),"Native opcode null state changed at "+f.Id);
            if(f.Opds is not null) Check(Enumerable.Range(0,r.opd_nums!.Count).Select(i=>r.opd_nums[i]).SequenceEqual(f.Opds),"Native opcode data changed at "+f.Id);
        }
    }
    private static void Commit(Expected fact,int target=7)
    {
        object?[] args={999,null}; Check((bool)Call(Patch("Commit"),"Prefix",args)!,"Actual command99 prefix rejected valid fixture.");
        try { NativeModelCommit(fact,target); } finally { Call(Patch("Commit"),"Postfix",args[1]); }
        AssertRestored();
    }
    private static (AvatarShuffledUnites.Execution Action,Il2CppStructArray<int> Ids) Admit(Expected fact,int leader)
    {
        object?[] args={leader,999,null};
        Check((bool)Call(Patch("Admission"),"Prefix",args)! && args[2] is AvatarShuffledUnites.Execution,"Recipe admission failed for "+fact.Id);
        var action=(AvatarShuffledUnites.Execution)args[2]!;
        var ids=MakeNativeWork(fact); Call(Patch("Admission"),"Postfix",1,action);
        return (action,ids);
    }
    private static void NativeModelFinish(Expected fact,int leader,Il2CppStructArray<int> ids)
    {
        int[] members=NativeModelMembers(Recipe(fact.Id),requireEligibility:false); Check(members.Length==fact.Donors.Length,"Native Finish model lacks ordered participants.");
        for(int i=0;i<members.Length;i++) { ids[i]=members[i]; Actor(members[i]).finished_flg=1; Actor(members[i]).c_mokuhyo=Actor(leader).c_mokuhyo; }
    }
    private static void Prepare(Expected fact,int leader,Il2CppStructArray<int> ids)
    {
        object?[] args={leader,ids,null}; Call(Patch("Finish"),"Prefix",args);
        var scope=(AvatarShuffledUnites.FinishScope?)args[2]; Check(scope is not null,"Qualified Finish scope absent.");
        try { NativeModelFinish(fact,leader,ids); Call(Patch("Finish"),"Postfix",ids,scope); }
        finally { Call(Patch("Finish"),"Finalizer",null,scope); }
        AssertRestored();
    }
    private static void Complete(Expected fact)
    { Work.kyoryoku_func=null; WorkGetter(fact).SetValue(null,null); AvatarShuffledUnites.AfterSequence(); }
    private static int CompletedLogs() => _runtime.Logs.Count(x=>x.Contains("completed:",StringComparison.Ordinal));
    private static bool Candidate(out AvatarUniteTableScope? state)
    {
        object?[] args={typeof(BattleBase).GetMethod("check_kyoryoku")!,999,null};
        bool result=(bool)Call(typeof(AvatarUniteCandidatePatch),"Prefix",args)!; state=(AvatarUniteTableScope?)args[2]; return result;
    }

    private static void Main()
    {
        Case("Exact immutable metadata/opcodes/row contracts for all 32 native recipes",()=> {
            Check(AvatarUniteRecipes.All.Count==32,"Catalog recipe count changed.");
            foreach(var f in Facts) {
                Check(AvatarUniteRecipes.TryGet(f.Id,out var r) && r is not null,"Missing catalog ID.");
                Check(r!.Id==f.Id && r.Name==f.Name && r.Function==f.Function && r.Family==f.Family && r.Type==f.Type && r.NameId==f.NameId && r.HelpId==f.HelpId,"Catalog metadata differs from independent extracted facts at "+f.Id);
                Check(r.Donors.SequenceEqual(f.Donors) && ((r.OpdNumbers is null)==(f.Opds is null)) && (f.Opds is null || r.OpdNumbers!.SequenceEqual(f.Opds)),"Catalog ordered data differs.");
                for(int i=0;i<f.Rows.Length;i++) Check(r.Rows(i).SequenceEqual(f.Rows[i]),"Wrong row contract.");
                Throws(()=>((IList<int>)r.Donors)[0]=109);
                if(r.OpdNumbers is not null) Throws(()=>((IList<int>)r.OpdNumbers)[0]=99);
                foreach(int i in Enumerable.Range(0,f.Rows.Length)) if(r.Rows(i).Count>0) Throws(()=>((IList<int>)r.Rows(i))[0]=99);
            }
            Check(!AvatarUniteRecipes.TryGet(0,out _)&&!AvatarUniteRecipes.TryGet(33,out _),"Sentinels became catalog recipes."); AssertRestored();
        });

        foreach(var f in Facts) Case("Recipe "+f.Id+" "+f.Name+": six foreign-role formations preserve native matching/order/target/actions",()=> {
            for(int rotation=0;rotation<6;rotation++) {
                Setup(); int[] expected=SyntheticSnapshot(f,rotation); int leader=expected.Last();
                var pointers=Enumerable.Range(1,9).Select(s=>Actor(s).Pointer).ToArray();
                Check(Candidate(out var menu),"Actual native candidate prefix rejected valid fixture.");
                try { Check(NativeModelMembers(Recipe(f.Id)).SequenceEqual(expected),"Native menu matched wrong host/order for recipe "+f.Id); }
                finally { Call(typeof(AvatarUniteCandidatePatch),"Postfix",menu); }
                AssertRestored(); Commit(f,target:8);
                var (_,ids)=Admit(f,leader); Check(AvatarShuffledUnites.BeforeSequence(),"Native sequence guard rejected admitted group.");
                Prepare(f,leader,ids);
                Check(Enumerable.Range(0,ids.Count).Select(i=>ids[i]).SequenceEqual(expected),"Finish p_id differs from native ordered membership.");
                foreach(int s in Enumerable.Range(1,6)) Check(Actor(s).finished_flg==(expected.Contains(s)?1:0),"Wrong native turn consumed at slot "+s);
                Check(expected.All(s=>Actor(s).c_mokuhyo==8),"Target not propagated to every participant.");
                Check(AvatarShuffledUnites.BeforeSequence(),"Prepared consumed flags wrongly blocked sequence.");
                Complete(f); Check(CompletedLogs()==1,"Native release did not produce one completion.");
                Check(Enumerable.Range(1,9).Select(s=>Actor(s).Pointer).SequenceEqual(pointers),"Projection replaced actors.");
                foreach(int s in Enumerable.Range(1,6)) Check(Work.b_chara[s].chara_id==Work.p_battle_data[s].player_data.chara_id,"Canonical identities changed.");
                Check(_runtime.FaultReason is null,"Valid recipe faulted: "+_runtime.FaultReason); AssertRestored();
            }
        });

        Case("Stock Tir in primary role/slot and IncludeTir foreign avatar both resolve Master and Pupil",()=> {
            var f=Fact(32); var expected=SyntheticSnapshot(f,stockTir:true);
            Check(Work.p_battle_data[1].player_data.chara_id==8&&_runtime.MapDisplayId(8)==8,"Stock Tir primary fixture broken.");
            using(var scope=AvatarUniteTableScope.Begin()) Check(NativeModelMembers(Recipe(32)).SequenceEqual(expected),"Stock Tir pair unavailable.");
            Commit(f); var (_,ids)=Admit(f,1); Prepare(f,1,ids); Complete(f);
            Setup(); expected=SyntheticSnapshot(f); Check(Work.p_battle_data[expected[0]].player_data.chara_id!=8,"IncludeTir fixture is not foreign.");
            Commit(f); (_,ids)=Admit(f,expected[0]); Prepare(f,expected[0],ids); Check(ids[0]==expected[0],"Tir donor forced to story role eight.");
        });

        Case("Native aliases 11/12/14 select shared work01 without rewriting function identity",()=> {
            foreach(int id in new[]{12,13,19}) { Setup(); var f=Fact(id); var slots=SyntheticSnapshot(f); Commit(f); var (_,ids)=Admit(f,slots[0]);
                Check(BattleBase.kmagic01_work is not null && Recipe(id).func_no==f.Function,"Alias used wrong native work family."); Prepare(f,slots[0],ids); Complete(f); Check(CompletedLogs()==1,"Alias sequence did not finish."); }
        });

        Case("Missing each exact required native row blocks that recipe; unrelated rows do not replace it",()=> {
            foreach(var f in Facts) for(int p=0;p<f.Rows.Length;p++) foreach(int row in f.Rows[p]) {
                Setup(); var slots=SyntheticSnapshot(f); Work.b_chara[slots[p]].action_data_table[row]=new Il2CppStructArray<short>();
                using var scope=AvatarUniteTableScope.Begin(); Check(!NativeModelMembers(Recipe(f.Id)).Any(),"Missing row "+row+" accepted at recipe "+f.Id+" participant "+p);
            }
        });
        Case("Function22 participant0 uses custom static arrays and requires no generic row9",()=> {
            var f=Fact(9); var slots=SyntheticSnapshot(f); Work.b_chara[slots[0]].action_data_table=new NativeArray<Il2CppStructArray<short>>();
            using(var scope=AvatarUniteTableScope.Begin()) Check(NativeModelMembers(Recipe(9)).SequenceEqual(slots),"Function22 first actor incorrectly needs generic row9.");
            Commit(f); var (_,ids)=Admit(f,slots[1]); Prepare(f,slots[1],ids); Complete(f); Check(CompletedLogs()==1,"Function22 special resources did not finish.");
        });

        Case("Unrelated native metadata/opcodes cannot be projected and fault stops later readers",()=> {
            foreach(int mutation in Enumerable.Range(0,7)) {
                Setup(); SyntheticSnapshot(Fact(10)); var r=Recipe(10);
                if(mutation==0) r.func_no++; else if(mutation==1)r.name++; else if(mutation==2)r.help++; else if(mutation==3)r.type++;
                else if(mutation==4)r.player_id[0]=109; else if(mutation==5)r.opd_nums![1]=99; else r.player_kazu--;
                Check(!Candidate(out var scope)&&scope is null&&_runtime.FaultReason is not null,"Malformed recipe accepted for mutation "+mutation);
                int writes=Facts.Sum(f=>Recipe(f.Id).player_id.Writes); Check(!Candidate(out _)&&Facts.Sum(f=>Recipe(f.Id).player_id.Writes)==writes,"Fault allowed later shared writes.");
            }
        });
        Case("Preflight covers all records before writing shared recipe arrays",()=> {
            SyntheticSnapshot(Fact(1)); Recipe(32).func_no=1;
            Check(!Candidate(out _)&&Facts.All(f=>Recipe(f.Id).player_id.Writes==0),"Late malformed record allowed partial projection.");
        });
        Case("Nested scopes share one synchronous projection and native IDs restore once",()=> {
            var slots=SyntheticSnapshot(Fact(1)); _runtime.DisplayReads=0;
            using(var outer=AvatarUniteTableScope.Begin()) {
                int writes=Recipe(1).player_id.Writes; int reads=_runtime.DisplayReads;
                using(var inner=AvatarUniteTableScope.Begin()) Check(NativeModelMembers(Recipe(1)).SequenceEqual(slots),"Nested scope lost native projection.");
                Check(Recipe(1).player_id.Writes==writes&&_runtime.DisplayReads==reads,"Nested scope rebuilt batch or restored outer IDs early.");
            }
            AssertRestored();
        });
        Case("Partial native setter failure rolls back every changed requirement without poisoning scope depth",()=> {
            SyntheticSnapshot(Fact(6)); Recipe(6).player_id.FailOnWrite=2; Throws(()=>AvatarUniteTableScope.Begin()); AssertRestored();
            using(var scope=AvatarUniteTableScope.Begin()) Check(NativeModelMembers(Recipe(6)).Length==4,"Rollback poisoned next scope."); AssertRestored();
        });
        Case("Native commit exception restores scopes and repeated cleanup is harmless",()=> {
            SyntheticSnapshot(Fact(6)); object?[] args={999,null}; Check((bool)Call(Patch("Commit"),"Prefix",args)!,"Commit failed.");
            var error=new InvalidOperationException("Modeled native command reader exception"); Check(ReferenceEquals(Call(Patch("Commit"),"Finalizer",error,args[1]),error),"Original native error was lost.");
            AssertRestored(); int writes=Recipe(6).player_id.Writes; Call(Patch("Commit"),"Postfix",args[1]); Check(Recipe(6).player_id.Writes==writes,"Cleanup repeated writes.");
        });

        Case("KO cancellation clears only the selected recipe and preserves another queued unite",()=> {
            Party(6,2,32,38,8,91); Commit(Fact(1)); Commit(Fact(2),8); Actor(1).hp=0;
            Check(!AvatarShuffledUnites.BeforeAdmission(2,out var rejected)&&rejected is null,"KO partner admitted.");
            Check(Actor(1).c_koudou==1&&Actor(2).c_koudou==1&&Actor(1).c_item==0&&Actor(2).c_item==0,"Invalid recipe remained queued.");
            Check(Actor(3).c_koudou==4&&Actor(4).c_koudou==4&&Actor(3).c_item==2&&Actor(4).c_item==2,"Cancellation stole another recipe.");
            var (_,ids)=Admit(Fact(2),3); Prepare(Fact(2),3,ids); Complete(Fact(2)); Check(CompletedLogs()==1,"Unaffected queued group could not complete.");
        });
        Case("Multiple disjoint native unites execute in the same round without reset or lost turns",()=> {
            Party(6,2,32,38,8,91); Commit(Fact(1)); Commit(Fact(2),8); Commit(Fact(32),9);
            foreach(var pair in new[]{(1,1),(2,3),(32,5)}) {
                var f=Fact(pair.Item1); var (_,ids)=Admit(f,pair.Item2); Prepare(f,pair.Item2,ids); Complete(f);
            }
            Check(CompletedLogs()==3&&Enumerable.Range(1,6).All(s=>Actor(s).finished_flg==1),"Round lost or duplicated a native group.");
        });
        Case("A changed partner command is preserved while invalid group members Defend",()=> {
            var slots=SyntheticSnapshot(Fact(1)); Commit(Fact(1)); Actor(slots[1]).c_koudou=2;
            Check(!AvatarShuffledUnites.BeforeAdmission(slots[0],out _),"Changed partner action admitted.");
            Check(Actor(slots[0]).c_koudou==1&&Actor(slots[1]).c_koudou==2,"Cancel overwrote another selected action.");
        });
        Case("Already-spent participants cannot replay a native group",()=> {
            var f=Fact(6); var slots=SyntheticSnapshot(f); Commit(f); var (_,ids)=Admit(f,slots[0]); Prepare(f,slots[0],ids); Complete(f);
            Check(!AvatarShuffledUnites.BeforeAdmission(slots[1],out var second)&&second is null,"Consumed native actions replayed.");
        });
        Case("Repeated in-flight admission preserves first group context and commands",()=> {
            var f=Fact(4); var slots=SyntheticSnapshot(f); Commit(f); var (_,ids)=Admit(f,slots[0]);
            Check(!AvatarShuffledUnites.BeforeAdmission(slots[1],out var other)&&other is null,"Second in-flight sequence admitted.");
            Check(slots.All(s=>Actor(s).c_koudou==4),"In-flight rejection canceled live commands."); Prepare(f,slots[0],ids); Complete(f); Check(CompletedLogs()==1,"Original sequence context was lost.");
        });

        Case("All actor HP and status deltas are observed after modeled native AoE/self/status playback",()=> {
            var f=Fact(30); var slots=SyntheticSnapshot(f); Commit(f); var (_,ids)=Admit(f,slots[0]); Prepare(f,slots[0],ids);
            foreach(int e in EnemySlots) Actor(e).hp-=50; Actor(slots[0]).hp-=17; Actor(slots[1]).status=0x20; Actor(slots[2]).unbalance_cnt=2;
            Check(AvatarShuffledUnites.BeforeSequence(),"Native self status incorrectly reapplied initial eligibility checks.");
            int before=_runtime.Logs.Count; Complete(f); var logs=string.Join("\n",_runtime.Logs.Skip(before));
            Check(CompletedLogs()==1,"AoE sequence not observed at native terminal release.");
            foreach(int e in EnemySlots) Check(logs.Contains(e.ToString()),"AoE completion omitted actor slot "+e);
            Check(logs.Contains("450")&&logs.Contains("550")&&logs.Contains("650"),"AoE final HP changes not captured.");
            Check(logs.Contains("status 0 -> 32")&&logs.Contains("unbalance 0 -> 2"),"Native status/unbalance deltas not captured.");
            Check(Actor(slots[0]).hp==100+slots[0]-17&&Actor(slots[1]).status==0x20,"Observer changed native live results.");
        });
        Case("Terminal completion waits for actual native work release and is logged exactly once",()=> {
            var f=Fact(5); var slots=SyntheticSnapshot(f); Commit(f); var (_,ids)=Admit(f,slots[0]); Prepare(f,slots[0],ids);
            AvatarShuffledUnites.AfterSequence(); Check(CompletedLogs()==0,"Live native work completed early.");
            Complete(f); AvatarShuffledUnites.AfterSequence(); Check(CompletedLogs()==1,"Completion repeated or disappeared.");
        });

        Case("Mismatched p_id array identity and wrong native finish order permanently stop unsafe execution",()=> {
            foreach(int change in Enumerable.Range(0,3)) {
                Setup(); var f=Fact(6); var slots=SyntheticSnapshot(f); Commit(f); var (_,ids)=Admit(f,slots[0]);
                if(change==0) Throws(()=>AvatarShuffledUnites.BeforeFinish(slots[0],new Il2CppStructArray<int>(0,0,0,0)));
                else { var scope=AvatarShuffledUnites.BeforeFinish(slots[0],ids); NativeModelFinish(f,slots[0],ids);
                    if(change==1) { int x=ids[0]; ids[0]=ids[1];ids[1]=x; } else Actor(slots[1]).finished_flg=0;
                    Throws(()=>AvatarShuffledUnites.AfterFinish(scope,ids)); scope?.Dispose(); }
                Check(_runtime.FaultReason is not null&&!AvatarShuffledUnites.BeforeSequence(),"Unsafe native preparation did not latch stop."); AssertRestored();
            }
        });
        Case("Changed actor identities/resources fail sequence guards before unsafe native frame",()=> {
            foreach(int change in Enumerable.Range(0,3)) {
                Setup(); var f=Fact(1); var slots=SyntheticSnapshot(f); Commit(f); Admit(f,slots[0]);
                if(change==0) Actor(slots[1]).Pointer=TestIdentity.Next(); else if(change==1) Work.b_chara[slots[1]].action_data_table[15]=new Il2CppStructArray<short>();
                else WorkGetter(f).SetValue(null,Activator.CreateInstance(WorkGetter(f).PropertyType));
                Check(!AvatarShuffledUnites.BeforeSequence()&&_runtime.FaultReason is not null,"Unsafe frame accepted for change "+change);
            }
        });
        Case("Prepared participant arrays reject replacement/truncation/reordering/released work before native step",()=> {
            foreach(int change in Enumerable.Range(0,4)) {
                Setup(); var f=Fact(6); var slots=SyntheticSnapshot(f); Commit(f); var (_,ids)=Admit(f,slots[0]);Prepare(f,slots[0],ids);
                var getter=WorkGetter(f);var native=getter.GetValue(null)!;
                if(change==0)getter.PropertyType.GetProperty("p_id")!.SetValue(native,new Il2CppStructArray<int>(slots.ToArray()));
                else if(change==1)getter.PropertyType.GetProperty("p_id")!.SetValue(native,new Il2CppStructArray<int>(slots.Take(3).ToArray()));
                else if(change==2){int first=ids[0];ids[0]=ids[1];ids[1]=first;}
                else getter.SetValue(null,null);
                Check(!AvatarShuffledUnites.BeforeSequence()&&_runtime.FaultReason is not null,"Unsafe prepared array accepted for change "+change);
            }
        });
        Case("Native exception during Finish restores every recipe and fault stops later native retries",()=> {
            var f=Fact(6); var slots=SyntheticSnapshot(f); Commit(f); var (_,ids)=Admit(f,slots[0]);
            var scope=AvatarShuffledUnites.BeforeFinish(slots[0],ids); Check(scope is not null,"Finish scope absent.");
            var error=new IndexOutOfRangeException("Modeled unexpected native row error"); scope!.Dispose(); AvatarShuffledUnites.ObserveSequenceException(error);
            AssertRestored(); Check(_runtime.FaultReason is not null&&!AvatarShuffledUnites.BeforeSequence(),"Native failure allowed unsafe retry.");
            int count=_runtime.Logs.Count; AvatarShuffledUnites.ObserveSequenceException(error); Check(_runtime.Logs.Count==count,"Repeated fault overwrote diagnostic.");
        });
        Case("Back-to-back battle reset and owner/epoch replacement make stale contexts inert",()=> {
            foreach(int change in Enumerable.Range(0,4)) {
                Setup(); var f=Fact(1); var slots=SyntheticSnapshot(f); Commit(f); var (_,ids)=Admit(f,slots[0]); Prepare(f,slots[0],ids); int count=_runtime.Logs.Count;
                if(change==0) AvatarShuffledUnites.Reset(); else if(change==1) AvatarRuntime.Current=new AvatarRuntime(); else if(change==2) _runtime.ExecutionEpoch++; else Work.Pointer=TestIdentity.Next();
                ClearNativeWork(); AvatarShuffledUnites.AfterSequence(); Check(_runtime.Logs.Count==count,"Stale context logged into new owner/encounter.");
                Setup(); slots=SyntheticSnapshot(f); Commit(f); (_,ids)=Admit(f,slots[0]); Prepare(f,slots[0],ids); Complete(f); Check(CompletedLogs()==1,"Next battle could not establish new context.");
            }
        });
        Case("KO/dead/incapacitated/native-dead/ambiguous identities independently block all group availability",()=> {
            foreach(int change in Enumerable.Range(0,5)) {
                Setup(); var f=Fact(6); var slots=SyntheticSnapshot(f); int s=slots[2];
                if(change==0)Actor(s).hp=0; else if(change==1)Actor(s).dead_flg=1; else if(change==2)Actor(s).status=0x20; else if(change==3)BattleBase.DeadCheck=i=>i==s;
                else { int other=Enumerable.Range(1,6).First(x=>!slots.Contains(x)); _runtime.Mapping[Work.p_battle_data[other].player_data.chara_id]=f.Donors[2]; }
                using var scope=AvatarUniteTableScope.Begin(); Check(!NativeModelMembers(Recipe(6)).Any(),"Unavailable group admitted for change "+change);
            }
        });
        Case("ATB and one-shot schedulers retain safety masking for unsupported multi-hero scheduling",()=> {
            foreach(bool atb in new[]{true,false}) { Setup(); SyntheticSnapshot(Fact(1)); Scheduler.ActiveAtbEnabled=atb;Scheduler.OneShotAttackEnabled=!atb;
                using var scope=AvatarUniteTableScope.Begin(); Check(!NativeModelMembers(Recipe(1)).Any(),"Foreign scheduler admitted native group."); }
        });
        Case("Actual admission prefix returns stock failure -1 for canceled native group",()=> {
            var f=Fact(6); var slots=SyntheticSnapshot(f); Commit(f); Actor(slots[2]).hp=0;
            object?[] args={slots[0],999,null}; Check(!(bool)Call(Patch("Admission"),"Prefix",args)!&&(int)args[1]! == -1&&args[2] is null,"Canceled admission returned wait/zero or native continuation.");
        });
        Case("Native self cost/status between admission and Finish is preserved without rechecking menu eligibility",()=> {
            var f=Fact(30); var slots=SyntheticSnapshot(f); Commit(f); var (_,ids)=Admit(f,slots[0]);
            Actor(slots[0]).hp=0; Actor(slots[1]).status=0x20; Actor(slots[2]).unbalance_cnt=2;
            Prepare(f,slots[0],ids); Check(AvatarShuffledUnites.BeforeSequence(),"Prepared native side effects were rejected."); Complete(f);
            Check(CompletedLogs()==1&&Actor(slots[0]).hp==0&&Actor(slots[1]).status==0x20,"Native side effects altered by adapter.");
        });
        Case("Sequence native exception finalizer retains original error and stops actual prefix retries",()=> {
            var f=Fact(1); var slots=SyntheticSnapshot(f); Commit(f); Admit(f,slots[0]);
            var error=new InvalidOperationException("Modeled native outer sequence exception"); Check(ReferenceEquals(Call(Patch("Sequence"),"Finalizer",error),error),"Native exception lost.");
            object?[] args={999}; Check(!(bool)Call(Patch("Sequence"),"Prefix",args)!&&(int)args[0]! == -1,"Faulted native sequence retried.");
        });
        Case("Boundary patch resets encounter contexts and targets both real battle lifecycle methods",()=> {
            var f=Fact(1); var slots=SyntheticSnapshot(f); Commit(f); var (_,ids)=Admit(f,slots[0]); Prepare(f,slots[0],ids);
            var targets=(IEnumerable<MethodBase>)Call(Patch("Boundary"),"Targets")!;
            Check(targets.Select(x=>x.Name).SequenceEqual(new[]{"initialize_battle","battle_end_free"}),"Incorrect boundary hooks.");
            Call(Patch("Boundary"),"Prefix"); Complete(f); Check(CompletedLogs()==0&&slots.All(s=>Actor(s).c_koudou==4),"Boundary consumed or rewrote previous commands.");
        });
        Case("Every recipe member can initiate without changing native ordered p_id",()=> {
            foreach(var f in Facts) for(int p=0;p<f.Donors.Length;p++) {
                Setup(); var slots=SyntheticSnapshot(f); Commit(f); var (_,ids)=Admit(f,slots[p]); Prepare(f,slots[p],ids);
                Check(Enumerable.Range(0,ids.Count).Select(i=>ids[i]).SequenceEqual(slots),"Initiator changed native order at "+f.Id); Complete(f);
            }
        });
        Case("Canonical role zero remains a valid foreign host at native slot six",()=> {
            var f=Fact(1); var slots=SyntheticSnapshot(f,rotation:1); int host=slots[0];
            int previous=Work.p_battle_data[host].player_data.chara_id; _runtime.Mapping.Remove(previous); _runtime.Mapping[0]=f.Donors[0];
            Work.p_battle_data[host].player_data.chara_id=0;Work.b_chara[host].chara_id=0;
            Check(host==6,"Role-zero fixture did not exercise native slot six."); Commit(f); var (_,ids)=Admit(f,host); Prepare(f,host,ids);
            Check(ids[0]==6,"Role zero interpreted as missing."); Complete(f);
        });
        Case("Owned command native exception restores projection and permanently stops command/admission retries",()=> {
            var f=Fact(6); var slots=SyntheticSnapshot(f); object?[] args={999,null};Check((bool)Call(Patch("Commit"),"Prefix",args)!,"Commit prefix rejected fixture.");
            var error=new InvalidOperationException("Modeled command native read failure");
            Check(ReferenceEquals(Call(Patch("Commit"),"Finalizer",error,args[1]),error),"Commit finalizer lost original error."); AssertRestored();
            Check(_runtime.FaultReason is not null,"Owned command reader exception did not latch fault.");
            args=new object?[]{999,null};Check(!(bool)Call(Patch("Commit"),"Prefix",args)!&&(int)args[0]! == 0,"Faulted command prefix retried.");
            Actor(slots[0]).c_koudou=4;Actor(slots[0]).c_item=6;
            object?[] admission={slots[0],999,null};Check(!(bool)Call(Patch("Admission"),"Prefix",admission)!&&(int)admission[1]! == -1,"Faulted native admission continued.");
        });
        Case("Commit restoration failure preserves original error, restores other entries, and faults later native readers",()=> {
            var f=Fact(6);SyntheticSnapshot(f);object?[] args={999,null};Check((bool)Call(Patch("Commit"),"Prefix",args)!,"Commit failed.");
            var ids=Recipe(6).player_id;ids.FailOnWrite=ids.Writes+1;var original=new InvalidOperationException("Modeled native command failure");
            var error=(Exception?)Call(Patch("Commit"),"Finalizer",original,args[1]);
            Check(error is AggregateException aggregate&&aggregate.Flatten().InnerExceptions.Contains(original),"Cleanup failure replaced original native error.");
            Check(Enumerable.Range(0,3).All(i=>ids[i]==f.Donors[i]),"Cleanup failure prevented remaining restores.");
            Check(_runtime.FaultReason is not null,"Failed native restoration did not latch fault.");
            Check(!Candidate(out _),"Unrestored shared table reached native reader after fault.");
        });
        Case("Command exceptions from changed owners/epochs/battles do not fault new execution owner",()=> {
            foreach(int change in Enumerable.Range(0,3)) {
                Setup(); SyntheticSnapshot(Fact(1));object?[] args={999,null};Check((bool)Call(Patch("Commit"),"Prefix",args)!,"Commit failed.");
                var originalOwner=_runtime;
                if(change==0)AvatarRuntime.Current=new AvatarRuntime();else if(change==1)_runtime.ExecutionEpoch++;else Work.Pointer=TestIdentity.Next();
                var error=new InvalidOperationException("Unrelated command completion failure");
                Check(ReferenceEquals(Call(Patch("Commit"),"Finalizer",error,args[1]),error),"Unrelated error lost.");
                Check(originalOwner.FaultReason is null&&AvatarRuntime.Current!.FaultReason is null,"Stale command fault claimed a new execution owner."); AssertRestored();
            }
        });
        Case("Pending native damage logs each changed nonzero amount without applying HP",()=> {
            var f=Fact(1); var slots=SyntheticSnapshot(f); Commit(f); var (_,ids)=Admit(f,slots[0]); Prepare(f,slots[0],ids);
            Actor(7).damage=40; Call(Patch("Damage"),"Postfix"); Call(Patch("Damage"),"Postfix");
            Check(_runtime.Logs.Count(x=>x.Contains("damage pending:"))==1&&Actor(7).hp==500,"Pending damage duplicated or changed HP.");
            Actor(7).damage=0; Call(Patch("Damage"),"Postfix"); Actor(7).damage=40; Call(Patch("Damage"),"Postfix");
            Check(_runtime.Logs.Count(x=>x.Contains("damage pending:"))==2,"New later damage amount was suppressed.");
        });
        Case("Disabled adapter registers no projection and foreign runtime exceptions are not claimed",()=> {
            SyntheticSnapshot(Fact(1)); AvatarShuffledUnites.Configure(false); Check(AvatarUniteTableScope.ProjectRequirements is null,"Disabled projection remained registered.");
            Check(AvatarShuffledUnites.BeforeSequence(),"Disabled adapter controlled native sequence.");
            AvatarShuffledUnites.ObserveSequenceException(new Exception("unrelated")); Check(_runtime.FaultReason is null,"Unbound exception was claimed.");
            AvatarShuffledUnites.Configure(true); _runtime.Owns=false; Check(AvatarShuffledUnites.BeforeSequence(),"Foreign runtime sequence was controlled.");
        });
        Console.WriteLine("PASS "+_passed+" linked shuffled-unite checks, including all 32 recipes across six formations. Native matching/Finish effects are independent managed models; Harmony detours, foreign native code, damage formulas and choreography were not executed.");
    }
}
