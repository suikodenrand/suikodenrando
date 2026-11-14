extern alias GSD1;
using System;
using System.Linq;
using System.Reflection;
using SuikodenRandomizer.AvatarPlugin;

internal static partial class Program
{
 private static object? InvokePatch(Type type, string method, params object?[] args) =>
  type.GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, args);
 private static void ResetNativeReads()
 { GSD1.OldSrcBase.Reads=GSD1.GlobalWork.Reads=GSD1.ChapterManager.Reads=0; }
 private static void NoNativeReads(string message) => Check(
  GSD1.OldSrcBase.Reads==0&&GSD1.GlobalWork.Reads==0&&GSD1.ChapterManager.Reads==0,message);
 private static AvatarSaveCall EnterSave()
 {
  object?[] args={false,null,null};
  Check((bool)InvokePatch(typeof(AvatarSavePreparationPatch),"Prefix",args)!,"Owned save refused");
  return (AvatarSaveCall)args[2]!;
 }
 private static void ExitSave(AvatarSaveCall? call) => InvokePatch(typeof(AvatarSavePreparationPatch),"Finalizer",null,call);
 private static bool StorageAllowed() => (bool)InvokePatch(typeof(AvatarStorageGuardPatch),"Prefix",null,false)!;
 private static void RunExecutionOwnershipChecks()
 {
  Case("Foreign owner makes suspension and all runtime native entry points inert",()=>{
   var w=new World();w.Ready();bool owns=true;long epoch=0;
   w.Runtime.SetExecutionOwnership(()=>owns,()=>epoch);
   var oldProxy=w.Runtime.ProxyFor(1)!;int copies=AvatarNativeData.CopyCalls;
   owns=false;epoch++;ResetNativeReads();w.Runtime.SuspendExecution();
   w.Runtime.BeginNewGame();w.Runtime.PrepareLoad(new GSD1.SaveDataManager.SaveData());w.Runtime.Tick();
   Check(!w.Runtime.IsActive&&!w.Runtime.IsAdmitted&&!w.Runtime.IsRole(1)&&!w.Runtime.IsFixedRoster,"Foreign campaign admitted");
   Check(w.Runtime.MapDisplayId(1)==1&&w.Runtime.ProxyFor(1) is null,"Foreign presentation projected");
   Check(w.Runtime.ProjectPlayerData(1,w.Originals[1])==w.Originals[1],"Foreign getter projected");
   Check(!w.Runtime.TryGetOriginal(1,out _)&&!w.Runtime.StartingPartyBound()&&!w.Runtime.HasMappedPartyRole(),"Foreign readiness admitted");
   Check(!w.Runtime.EnsureProxy("foreign",true,true),"Foreign required boundary admitted");
   w.Runtime.BindResidents();w.Runtime.VerifyFinalParty("foreign");w.Runtime.VerifyFixedBattleParty();w.Runtime.TracePartyLayout("foreign");
   ViktorStart.TraceState("foreign");ViktorStart.ObserveSpawn(0,0);
   NoNativeReads("Foreign or suspended helper touched native singleton");
   Check(w.Cache[1]!.Pointer==oldProxy.Pointer&&AvatarNativeData.CopyCalls==copies,"Suspend restored or mutated native data");
  });
  Case("Returning to ownership cannot revive an old admission without a boundary",()=>{
   var w=new World();w.Ready();bool owns=true;long epoch=0;w.Runtime.SetExecutionOwnership(()=>owns,()=>epoch);
   owns=false;epoch++;w.Runtime.SuspendExecution();owns=true;ResetNativeReads();
   Check(!w.Runtime.IsActive&&!w.Runtime.MatchesExecution(0)&&!w.Runtime.MatchesExecution(epoch),"Old campaign revived");
   Check(!w.Runtime.EnsureProxy("re-entry",true,true),"Re-entry republished");
   NoNativeReads("Unadmitted re-entry read campaign pointers");w.Runtime.Tick();
   Check(GSD1.OldSrcBase.Reads==0&&GSD1.GlobalWork.Reads==0&&GSD1.ChapterManager.Reads==1,
    "Owned title observer read gameplay data before a supported boundary");
  });
  Case("Owned title boundary restores the retained manager cache and preserves progress",()=>{
   var w=new World();w.Ready();bool owns=true;long epoch=0;w.Runtime.SetExecutionOwnership(()=>owns,()=>epoch);
   w.Base(1).exp=944;w.Base(1).hp=7;var old=w.Runtime.ProxyFor(1)!;
   owns=false;epoch++;w.Runtime.SuspendExecution();Check(w.Cache[1]!.Pointer==old.Pointer,"Foreign cache restored early");
   owns=true;GSD1.ChapterManager.GR1Instance!.activeChapter=new GSD1.TitleChapter();w.Runtime.Tick();
   Check(!w.Runtime.IsAdmitted&&w.Cache[1]!.Pointer==w.Originals[1].Pointer,"Title did not restore canonical cache");
   Check(w.Base(1).exp==944&&w.Base(1).hp==7,"Title cleanup reset earned progress");
  });
  Case("Retired cache ledger survives a different manager until the original manager is reused",()=>{
   var w=new World();w.Ready();bool owns=true;long epoch=0;w.Runtime.SetExecutionOwnership(()=>owns,()=>epoch);
   var manager=GSD1.GlobalWork.Instance!.player_manager!;var old=w.Runtime.ProxyFor(1)!;
   owns=false;epoch++;w.Runtime.SuspendExecution();owns=true;
   GSD1.GlobalWork.Instance!.player_manager=new GSD1.PlayerManager();w.Runtime.BeginNewGame();
   Check(w.Cache[1]!.Pointer==old.Pointer,"Different active manager caused outgoing cache dereference/write");
   GSD1.GlobalWork.Instance!.player_manager=manager;w.Runtime.BeginNewGame();
   Check(w.Cache[1]!.Pointer==w.Originals[1].Pointer,"Reused manager lost original restoration ledger");
  });
  Case("Owned bound load accepts the new epoch without copying donor starts again",()=>{
   var w=new World();w.Ready();var save=new GSD1.SaveDataManager.SaveData();w.Runtime.WriteCampaignBinding(save);
   int copies=AvatarNativeData.CopyCalls;string fingerprint=w.Runtime.CampaignFingerprint;
   w.Base(1).exp=799;w.Base(1).hp=6;bool owns=true;long epoch=0;w.Runtime.SetExecutionOwnership(()=>owns,()=>epoch);
   owns=false;epoch++;w.Runtime.SuspendExecution();owns=true;AvatarPlugin.ConfiguredSeed="other game";
   w.Runtime.PrepareLoad(save);w.Ready();
   Check(w.Runtime.MatchesExecution(epoch)&&w.Runtime.CampaignFingerprint==fingerprint,"Bound profile or epoch changed");
   Check(w.Base(1).exp==799&&w.Base(1).hp==6&&AvatarNativeData.CopyCalls==copies,"Owned load reset progression");
  });
  Case("A manager missing at admission is restored before its returning cache becomes a donor source",()=>{
   var w=new World();w.Ready();var manager=GSD1.GlobalWork.Instance!.player_manager!;
   var proxy=w.Runtime.ProxyFor(1)!;bool owns=true;long epoch=0;w.Runtime.SetExecutionOwnership(()=>owns,()=>epoch);
   owns=false;epoch++;w.Runtime.SuspendExecution();owns=true;GSD1.GlobalWork.Instance!.player_manager=null;
   w.Runtime.BeginNewGame();Check(w.Cache[1]!.Pointer==proxy.Pointer,"Missing manager was dereferenced");
   GSD1.GlobalWork.Instance!.player_manager=manager;w.Ready();
   Check(w.Runtime.TryGetOriginal(1,out var original)&&original.Pointer==w.Originals[1].Pointer,
    "Returning published proxy was adopted as a pristine original");
   w.Runtime.SuspendExecution();w.Runtime.BeginNewGame();
   Check(w.Cache[1]!.Pointer==w.Originals[1].Pointer,"Restored donor cache was lost after second admission");
  });
  Case("Stale save capture cannot bind and stale shared-storage scope cannot deny another game",()=>{
   var w=new World();w.Ready();bool owns=true;long epoch=0;w.Runtime.SetExecutionOwnership(()=>owns,()=>epoch);
   var call=EnterSave();owns=false;epoch++;w.Runtime.SuspendExecution();ResetNativeReads();
   InvokePatch(typeof(AvatarSaveCapturePatch),"Postfix",new GSD1.SaveDataManager.SaveData(),true);
   Check(AvatarBindingStore.Bindings.Count==0&&call.BindingFailed,"Stale snapshot bound a campaign");
   Check(StorageAllowed(),"Stale S1 scope denied foreign SystemSave");NoNativeReads("Stale save touched native singleton");
   ExitSave(call);Check(AvatarSaveCall.Current is null,"Stale finalizer leaked ThreadStatic scope");
  });
  Case("Owned save binding uses the captured runtime even if Current is replaced",()=>{
   var w=new World();w.Ready();var call=EnterSave();var save=new GSD1.SaveDataManager.SaveData();
   AvatarRuntime.Current=new AvatarRuntime();InvokePatch(typeof(AvatarSaveCapturePatch),"Postfix",save,true);
   Check(!call.BindingFailed&&AvatarBindingStore.Bindings.Count==1&&StorageAllowed(),"Save rebound through a replacement runtime");
   ExitSave(call);Check(AvatarSaveCall.Current is null,"Owned finalizer leaked scope");
  });
  Case("Nested owned save scopes unwind correctly after an epoch switch",()=>{
   var w=new World();w.Ready();bool owns=true;long epoch=0;w.Runtime.SetExecutionOwnership(()=>owns,()=>epoch);
   var outer=EnterSave();var inner=EnterSave();Check(inner.Previous==outer,"Nested scope lost predecessor");
   owns=false;epoch++;w.Runtime.SuspendExecution();ExitSave(inner);Check(AvatarSaveCall.Current==outer,"Inner finalizer lost outer scope");
   object?[] args={false,null,null};Check((bool)InvokePatch(typeof(AvatarSavePreparationPatch),"Prefix",args)!&&args[2] is null,"Foreign preparation entered scope");
   Check(AvatarSaveCall.Current==outer&&StorageAllowed(),"Foreign call modified or was denied by outer scope");
   ExitSave(null);Check(AvatarSaveCall.Current==outer,"Unrelated finalizer popped outer scope");ExitSave(outer);
  });
  Case("Stale load completion and exception cannot fault or republish the next game",()=>{
   var w=new World();w.Ready();var save=new GSD1.SaveDataManager.SaveData();w.Runtime.WriteCampaignBinding(save);
   bool owns=true;long epoch=0;w.Runtime.SetExecutionOwnership(()=>owns,()=>epoch);
   object?[] args={save,null};InvokePatch(typeof(AvatarSaveRestorePatch),"Prefix",args);var scope=args[1];
   owns=false;epoch++;w.Runtime.SuspendExecution();ResetNativeReads();
   InvokePatch(typeof(AvatarSaveRestorePatch),"Postfix",false,scope);
   var exception=new InvalidOperationException("late native callback");
   Check(InvokePatch(typeof(AvatarSaveRestorePatch),"Finalizer",exception,scope)==exception,"Native exception changed");
   Check(w.Runtime.FaultReason is null&&!w.Runtime.IsAdmitted,"Stale load affected next epoch");NoNativeReads("Stale load touched native pointers");
  });
  Case("Foreign load prefix is neutral before snapshot identity or cache access",()=>{
   var w=new World();w.Ready();w.Runtime.SetExecutionOwnership(()=>false,()=>1L);ResetNativeReads();
   object?[] args={new GSD1.SaveDataManager.SaveData(),null};InvokePatch(typeof(AvatarSaveRestorePatch),"Prefix",args);
   Check(args[1] is null,"Foreign load allocated execution scope");NoNativeReads("Foreign load touched native state");
  });
  Case("Retained S1 faults cannot suppress foreign battle or event dispatch",()=>{
   var w=new World();w.Ready();w.Runtime.Fail("retained outgoing fault");
   w.Runtime.SetExecutionOwnership(()=>false,()=>1L);w.Runtime.SuspendExecution();ResetNativeReads();
   object?[] battle={918273};Check((bool)InvokePatch(typeof(AvatarBattleEntryPatch),"Prefix",battle)!
    &&(int)battle[0]! == 918273,"Foreign battle changed or blocked");
   var driver=new GSD1.Event_c();Check((bool)InvokePatch(typeof(AvatarFaultDriverPatch),"Prefix",driver)!
    &&driver.ClearCalls==0,"Foreign event stream cleared or blocked");
   InvokePatch(typeof(AvatarGameInitPatch),"Prefix",0);
   var state=w.Runtime.State;
   InvokePatch(typeof(AvatarNewGamePatch),"Prefix",new GSD1.TitleChapter(),(int)GSD1.TitleChapter.State.DifficultyInit);
   Check(ReferenceEquals(state,w.Runtime.State),"Foreign native title admitted a campaign");NoNativeReads("Foreign lifecycle hook read native singleton");
  });
  Case("Title transition deduplication includes the ownership epoch",()=>{
   var w=new World(false);long epoch=7;w.Runtime.SetExecutionOwnership(()=>true,()=>epoch);
   var title=new GSD1.TitleChapter();int step=(int)GSD1.TitleChapter.State.DifficultyInit;
   InvokePatch(typeof(AvatarNewGamePatch),"Prefix",title,step);var first=w.Runtime.State;
   Check(w.Runtime.IsAdmitted&&w.Runtime.MatchesExecution(epoch),"First title boundary did not admit");
   InvokePatch(typeof(AvatarNewGamePatch),"Prefix",title,step);Check(ReferenceEquals(first,w.Runtime.State),"Same step admitted twice");
   epoch++;w.Runtime.SuspendExecution();InvokePatch(typeof(AvatarNewGamePatch),"Prefix",title,step);
   Check(w.Runtime.IsAdmitted&&w.Runtime.MatchesExecution(epoch)&&!ReferenceEquals(first,w.Runtime.State),"Reused title pointer/step suppressed new epoch admission");
  });
  Case("Owned title observation clears a retained fault before any new campaign",()=>{
   var w=new World();w.Ready();w.Runtime.Tick();w.Runtime.Fail("old campaign fault");
   GSD1.ChapterManager.GR1Instance!.activeChapter=new GSD1.TitleChapter();w.Runtime.Tick();
   Check(w.Runtime.FaultReason is null&&!w.Runtime.IsAdmitted&&w.Cache[1]!.Pointer==w.Originals[1].Pointer,"Owned title failed lifecycle cleanup");
  });
 }
}
