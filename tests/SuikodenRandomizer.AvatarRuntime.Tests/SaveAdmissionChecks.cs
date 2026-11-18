extern alias GSD1;
using System;
using System.IO;
using System.Linq;
using SuikodenRandomizer.AvatarPlugin;

internal static partial class Program
{
 private static object FinishUnboundLoad(bool success=true)
 {
  object?[] load={new GSD1.SaveDataManager.SaveData{Identity=new string('e',64)},null};
  InvokePatch(typeof(AvatarSaveRestorePatch),"Prefix",load);
  Check(load[1] is not null,"Owned load did not capture its scope");
  InvokePatch(typeof(AvatarSaveRestorePatch),"Postfix",success,load[1]);
  return load[1]!;
 }
 private static void RunSaveAdmissionChecks()
 {
  Case("Shuffle and fixed saves compute the base snapshot digest once per capture and load",()=>{
   foreach (string mode in new[]{"Shuffle","FixedChosen"})
   {
    var w=EquipmentWorld(mode); w.Ready();
    var save=VarianceSave(w,'b');
    var call=EnterSave(); int before=AvatarSaveIdentity.Computes;
    InvokePatch(typeof(AvatarSaveCapturePatch),"Postfix",save,true);
    ExitSave(call);
    Check(!call.BindingFailed && AvatarSaveIdentity.Computes==before+1, "Capture rehashed the game or failed to bind");
    Check(TreasureBindingStore.Bindings.ContainsKey(save.Identity) && File.Exists(JoiningWeaponBindingStore.PathFor(save.Identity))
     && File.Exists(JoiningEquipmentBindingStore.PathFor(save.Identity)), "Policy sidecars lost their original base identity");
    Check(mode=="Shuffle" ? AvatarBindingStore.Bindings.ContainsKey(save.Identity)
     : FixedRosterBindingStore.Bindings.ContainsKey("fixed-"+save.Identity), "Campaign binding used the wrong identity");
    w.Runtime.PrepareLoad(save);
    Check(w.Runtime.FaultReason is null && AvatarSaveIdentity.Computes==before+2, "Load rehashed the game or failed to bind");
   }
  });
  Case("An unbound snapshot with any surviving randomizer policy sidecar cannot save as vanilla",()=>{
   foreach (var pathFor in new Func<string,string>[] { TreasureBindingStore.PathFor, JoiningWeaponBindingStore.PathFor, JoiningEquipmentBindingStore.PathFor })
   {
    var w=new World(false);
    string path=pathFor(new string('e',64));
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllText(path,"{}");
    try
    {
     FinishUnboundLoad();
     object?[] args={true,null,null};
     Check(w.Runtime.FaultReason is not null && !w.Runtime.TryPrepareUnboundNativeSave()
      && !(bool)InvokePatch(typeof(AvatarSavePreparationPatch),"Prefix",args)!,
      "A snapshot missing its campaign binding entered native passthrough");
     object?[] otherLoad={new GSD1.SaveDataManager.SaveData{Identity=new string('d',64)},null};
     InvokePatch(typeof(AvatarSaveRestorePatch),"Prefix",otherLoad);
     InvokePatch(typeof(AvatarSaveRestorePatch),"Postfix",true,otherLoad[1]);
     Check(w.Runtime.TryPrepareUnboundNativeSave() && File.ReadAllText(path)=="{}",
      "An unrelated vanilla snapshot was blocked or the orphaned policy was modified");
    }
    finally { File.Delete(path); }
   }
  });
  Case("A completed unbound native load saves without projection, binding metadata or an invented callback",()=>{
   var w=new World(false);FinishUnboundLoad();
   var before=w.Bases.ToDictionary(p=>p.Key,p=>AvatarNativeData.Fingerprint(p.Value));
   int callbacks=0;Il2CppSystem.Action<bool,GSD1.SaveDataManager.SaveData> cb=(_,_)=>callbacks++;
   object?[] args={true,cb,null};
   Check((bool)InvokePatch(typeof(AvatarSavePreparationPatch),"Prefix",args)!,"Clean unbound native save was refused");
   var scope=(AvatarSaveCall)args[2]!;
   Check(scope.NativePassthrough&&!scope.BindingFailed&&ReferenceEquals(AvatarSaveCall.Current,scope),"Native scope inherited binding obligations");
   InvokePatch(typeof(AvatarSaveCapturePatch),"Postfix",new GSD1.SaveDataManager.SaveData(),true);
   Check((bool)args[0]!&&callbacks==0&&StorageAllowed(),"Passthrough changed native result/callback/storage behavior");
   Check(!w.Runtime.IsAdmitted&&!w.Runtime.IsActive&&AvatarNativeData.CopyCalls==0,"Vanilla save was admitted or randomized");
   Check(AvatarBindingStore.Bindings.Count==0&&FixedRosterBindingStore.Bindings.Count==0&&TreasureBindingStore.Bindings.Count==0,
    "Native save wrote randomizer campaign metadata");
   Check(!Directory.EnumerateFiles(AvatarRuntime.Folder,"*.json",SearchOption.AllDirectories).Any(),"Native save wrote a policy sidecar");
   Check(before.All(p=>AvatarNativeData.Fingerprint(w.Base(p.Key))==p.Value),"Native passthrough changed persistent state");
   ExitSave(scope);Check(AvatarSaveCall.Current is null,"Native finalizer leaked save scope");
  });
  Case("Unbound saves wait for successful native load completion and keep native failure callbacks",()=>{
   var w=new World(false);int callbacks=0;bool? result=null;
   Il2CppSystem.Action<bool,GSD1.SaveDataManager.SaveData> cb=(ok,_)=>{callbacks++;result=ok;};
   object?[] args={true,cb,null};
   Check(!(bool)InvokePatch(typeof(AvatarSavePreparationPatch),"Prefix",args)!&&!(bool)args[0]!&&callbacks==1&&result==false,
    "Unloaded runtime bypassed admission or lost failure notification");
   object?[] load={new GSD1.SaveDataManager.SaveData{Identity=new string('e',64)},null};
   InvokePatch(typeof(AvatarSaveRestorePatch),"Prefix",load);
   args=new object?[]{true,cb,null};
   Check(!(bool)InvokePatch(typeof(AvatarSavePreparationPatch),"Prefix",args)!&&args[2] is null,"Pending load saved before completion");
   InvokePatch(typeof(AvatarSaveRestorePatch),"Postfix",true,load[1]);
   args=new object?[]{false,cb,null};
   Check((bool)InvokePatch(typeof(AvatarSavePreparationPatch),"Prefix",args)!,"Successful completion did not permit native saving");
   ExitSave((AvatarSaveCall)args[2]!);
  });
  Case("Failed native restoration and rejected bindings remain save-blocked",()=>{
   var w=new World(false);FinishUnboundLoad(false);
   object?[] args={true,null,null};
   Check(!(bool)InvokePatch(typeof(AvatarSavePreparationPatch),"Prefix",args)!&&w.Runtime.FaultReason is not null,"Failed native load downgraded to passthrough");
   w=new World(false);AvatarBindingStore.Bindings[new string('e',64)]="{}";FinishUnboundLoad();
   args=new object?[]{true,null,null};
   Check(!(bool)InvokePatch(typeof(AvatarSavePreparationPatch),"Prefix",args)!&&w.Runtime.FaultReason is not null,"Rejected campaign binding downgraded to vanilla saving");
  });
  Case("Pending New Game and fixed roster admissions never become native passthrough",()=>{
   var w=new World(false);FinishUnboundLoad();w.Runtime.BeginNewGame();w.Cache.Remove(68);
   object?[] args={true,null,null};
   Check(!(bool)InvokePatch(typeof(AvatarSavePreparationPatch),"Prefix",args)!&&args[2] is null,"Incomplete shuffle saved as vanilla");
   Check(!w.Runtime.TryPrepareUnboundNativeSave(),"New Game retained native-load admission");
   w=new World(false);FinishUnboundLoad();AvatarPlugin.ConfiguredMode="FixedChosen";AvatarPlugin.ConfiguredCompanions="1,2,3,4,5";
   w.Runtime.BeginNewGame();w.Runtime.CompleteUnboundNativeLoad();
   Check(w.Runtime.IsFixedRoster&&!w.Runtime.TryPrepareUnboundNativeSave(),"Fixed roster masqueraded as an unbound native campaign");
  });
  Case("Suspension and ownership epoch changes invalidate unbound native-save permission",()=>{
   var w=new World(false);FinishUnboundLoad();w.Runtime.SuspendExecution();
   object?[] args={true,null,null};Check(!(bool)InvokePatch(typeof(AvatarSavePreparationPatch),"Prefix",args)!,"Suspended native campaign saved through old permission");
   w=new World(false);FinishUnboundLoad();w.Runtime.SetExecutionOwnership(()=>true,()=>1L);
   args=new object?[]{true,null,null};Check(!(bool)InvokePatch(typeof(AvatarSavePreparationPatch),"Prefix",args)!,"Old admission epoch permitted native saving");
   w.Runtime.SetExecutionOwnership(()=>false,()=>1L);args=new object?[]{true,null,null};
   Check((bool)InvokePatch(typeof(AvatarSavePreparationPatch),"Prefix",args)!&&args[2] is null,"Foreign owner was intercepted");
  });
  Case("An unbound native load removes old cache projections without resetting saved progression",()=>{
   var w=new World();w.Ready();w.Base(1).exp=777;w.Base(1).hp=7;int copies=AvatarNativeData.CopyCalls;
   FinishUnboundLoad();
   Check(w.Cache[1]!.Pointer==w.Originals[1].Pointer&&!w.Runtime.IsActive,"Unbound load retained old cache projection");
   object?[] args={true,null,null};Check((bool)InvokePatch(typeof(AvatarSavePreparationPatch),"Prefix",args)!,"Loaded native campaign stayed read-only");
   Check(w.Base(1).exp==777&&w.Base(1).hp==7&&AvatarNativeData.CopyCalls==copies,"Native save reset earned progression");
   ExitSave((AvatarSaveCall)args[2]!);
  });
  Case("Unbound native saving restores a returning manager while unrelated retired managers stay inert",()=>{
   var w=new World();w.Ready();var manager=GSD1.GlobalWork.Instance!.player_manager;var proxy=w.Runtime.ProxyFor(1)!;
   GSD1.GlobalWork.Instance!.player_manager=null;FinishUnboundLoad();
   object?[] args={true,null,null};Check(!(bool)InvokePatch(typeof(AvatarSavePreparationPatch),"Prefix",args)!,"Unresolved current cache ledger was ignored");
   GSD1.GlobalWork.Instance!.player_manager=manager;args=new object?[]{true,null,null};
   Check((bool)InvokePatch(typeof(AvatarSavePreparationPatch),"Prefix",args)!&&w.Cache[1]!.Pointer==w.Originals[1].Pointer,
    "Returning manager cache was not restored before native save");ExitSave((AvatarSaveCall)args[2]!);
   w=new World();w.Ready();proxy=w.Runtime.ProxyFor(1)!;GSD1.GlobalWork.Instance!.player_manager=new GSD1.PlayerManager();FinishUnboundLoad();
   args=new object?[]{true,null,null};Check((bool)InvokePatch(typeof(AvatarSavePreparationPatch),"Prefix",args)!,"Unrelated retired manager blocked clean current native save");
   Check(w.Cache[1]!.Pointer==proxy.Pointer,"Unrelated manager was dereferenced or mutated");ExitSave((AvatarSaveCall)args[2]!);
  });
  Case("Nested native passthrough masks an outer binding call and restores it even on exception",()=>{
   var w=new World();w.Ready();var outer=EnterSave();FinishUnboundLoad();
   object?[] args={true,null,null};Check((bool)InvokePatch(typeof(AvatarSavePreparationPatch),"Prefix",args)!,"Nested native save inherited outer admission");
   var inner=(AvatarSaveCall)args[2]!;
   Check(inner.NativePassthrough&&inner.Previous==outer&&outer.BindingFailed,"Native nested scope lost outer transaction");
   InvokePatch(typeof(AvatarSaveCapturePatch),"Postfix",new GSD1.SaveDataManager.SaveData(),true);
   Check(!inner.BindingFailed&&outer.BindingFailed&&StorageAllowed()&&AvatarBindingStore.Bindings.Count==0,
    "Native capture/storage inherited outer binding failure");
   var exception=new InvalidOperationException("native save failure");
   Check(InvokePatch(typeof(AvatarSavePreparationPatch),"Finalizer",exception,inner)==exception&&AvatarSaveCall.Current==outer,
    "Native exception finalizer lost outer save scope");ExitSave(outer);
  });
  Case("Bound shuffle saves still capture binding metadata and retain storage-failure protection",()=>{
   var w=new World();w.Ready();var call=EnterSave();
   Check(!call.NativePassthrough&&!StorageAllowed(),"Bound save became an unverified native save");
   InvokePatch(typeof(AvatarSaveCapturePatch),"Postfix",new GSD1.SaveDataManager.SaveData(),true);
   Check(!call.BindingFailed&&AvatarBindingStore.Bindings.Count==1&&StorageAllowed(),"Bound metadata path changed");
   ExitSave(call);
  });
 }
}
