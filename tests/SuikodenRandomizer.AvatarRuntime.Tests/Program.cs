extern alias GSD1;
using SuikodenRandomizer.Core;
using SuikodenRandomizer.Core.Avatars;
using SuikodenRandomizer.Serialization;
using SuikodenRandomizer.AvatarPlugin;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

internal static class Program
{
 private static int _passed;
 private static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
 private static void Case(string name,Action action){action();_passed++;Console.WriteLine("PASS "+name);}
 private static void Main()
 {
  try
  {
   Case("Unbound campaign never projects or initializes",()=>{
    var w=new World(false);Check(!w.Runtime.EnsureProxy("unbound",false)&&!w.Runtime.IsAdmitted&&!w.Runtime.IsActive,"Unbound admitted");
    Check(w.Runtime.MapDisplayId(1)==1&&AvatarNativeData.CopyCalls==0,"Unbound copied/projected");
   });
   Case("Missing original asset waits without fault or mutation",()=>{
    var w=new World();w.Cache.Remove(68);Check(!w.Runtime.EnsureProxy("partial preload",false),"Incomplete cache accepted");
    Check(w.Runtime.FaultReason is null&&AvatarNativeData.CopyCalls==0&&!w.Runtime.State.IsInitialized,"Transient preload became failure/mutation");
    w.Cache[68]=w.Originals[68];w.Ready();
   });
   Case("Zero native base waits until initialized",()=>{
    var w=new World();var b=w.Base(68);b.level=0;b.max_hp=0;
    Check(!w.Runtime.EnsureProxy("partial stats",false)&&w.Runtime.FaultReason is null,"Zero base caused fault");
    b.level=1;b.max_hp=168;b.hp=168;w.Ready();
   });
   Case("Incomplete native nested record waits without permanent stop",()=>{
    var w=new World();w.Base(68).IsComplete=false;
    Check(!w.Runtime.EnsureProxy("nested incomplete",false)&&w.Runtime.FaultReason is null,"Incomplete record caused stop");
    w.Base(68).IsComplete=true;w.Ready();
   });
   Case("Every active starter must bind its actual live canonical base",()=>{
    var w=new World();w.Game.player_data[2]=null;
    Check(w.Runtime.HasMappedPartyRole()&&!w.Runtime.StartingPartyBound(),"Readiness confused with code membership");
    Check(!w.Runtime.EnsureProxy("party partial",false)&&AvatarNativeData.CopyCalls==0&&w.Runtime.FaultReason is null,"Partial party initialized");
    w.Game.player_data[2]=w.Originals[6];w.Ready();
   });
   Case("Wrong world base pointer cannot satisfy starter readiness",()=>{
    var w=new World();w.Game.player_data[0]!.player_base=AvatarNativeData.CloneBase(w.Base(1));
    Check(!w.Runtime.StartingPartyBound()&&!w.Runtime.EnsureProxy("stale base",false)&&w.Runtime.FaultReason is null,"Stale world binding admitted");
    w.Originals[1].player_base=w.Base(1);w.Ready();
   });
   Case("Required boundary stops when eligible role data is unavailable",()=>{
    var w=new World();w.Cache.Remove(68);
    Check(w.Runtime.HasMappedPartyRole()&&!w.Runtime.EnsureProxy("battle",true)&&w.Runtime.FaultReason is not null,"Required boundary passed");
    Check(AvatarNativeData.CopyCalls==0&&!w.Runtime.State.IsInitialized,"Failed admission mutated");
   });
   Case("Protected-only party is distinguished from eligible incomplete party",()=>{
    var w=new World();w.Game.party_data.chara_code=new List<short>{8,108,109,-1,-1,-1};w.Game.party_data.player_kazu=3;
    w.Game.party_data.narabi=new List<sbyte>{0,1,2,-1,-1,-1};
    w.Game.player_data=new List<GSD1.PLAYER_DATA?>{w.Originals[8],w.Originals[108],w.Originals[109],null,null,null};
    Check(!w.Runtime.HasMappedPartyRole()&&!w.Runtime.StartingPartyBound(),"Protected-only party marked mapped");
   });
   Case("Five loaded opening residents initialize while only Tir is active",()=>{
    var w=new World();w.StageOpening();
    var before=w.Runtime.CanonicalLayoutFingerprint();
    Check(!w.Runtime.HasMappedPartyRole()&&w.Runtime.StartingPartyBound(),"Staged canonical anchor confused with active membership");
    w.Ready();
    Check(w.Game.party_data.player_kazu==1&&w.Game.party_data.narabi[0]==0&&w.Runtime.CanonicalLayoutFingerprint()==before,"Initialization recruited or reordered staged residents");
    Check(w.Runtime.MapDisplayId(1)==76&&w.Runtime.MapDisplayId(2)==11&&w.Runtime.MapDisplayId(6)==56,"Default opening assignments differ from Meg Anji Lotte");
    foreach(int id in new[]{1,2,6}){
     int slot=w.Game.party_data.chara_code.IndexOf((short)id);
     Check(w.Game.player_data[slot]?.Pointer==w.Runtime.ProxyFor(id)?.Pointer,"Bound staged role was not projected");
    }
    w.Runtime.VerifyFinalParty("pre-join opening");
   });
   Case("An inactive Cleo anchor initializes without filling null or stale staged residents",()=>{
    var w=new World();w.StageOpening();w.Game.player_data[1]=null;
    w.Originals[6].player_base=AvatarNativeData.CloneBase(w.Base(6));
    var stale=w.Game.player_data[2];var staleBase=stale?.player_base?.Pointer;
    Check(w.Runtime.StartingPartyBound(),"Other inactive residents blocked a valid Cleo anchor");
    w.Ready();
    Check(w.Game.player_data[1] is null,"Null staged reservation was filled");
    Check(w.Game.player_data[2]?.Pointer==stale?.Pointer&&w.Game.player_data[2]?.player_base?.Pointer==staleBase,"Stale inactive resident was rebound prematurely");
    Check(w.Game.player_data[3]?.Pointer==w.Runtime.ProxyFor(1)?.Pointer,"Cleo staged avatar missing");
    w.Runtime.VerifyFinalParty("staged leftovers ignored");
   });
   Case("Unbound eligible staged codes cannot substitute for a real canonical anchor",()=>{
    var w=new World();w.StageOpening();w.Game.player_data[1]=null;w.Game.player_data[2]=null;w.Game.player_data[3]=null;
    Check(!w.Runtime.HasMappedPartyRole()&&!w.Runtime.StartingPartyBound(),"Unbound staged codes became a live anchor");
    Check(!w.Runtime.EnsureProxy("no live anchor",false)&&!w.Runtime.IsActive&&w.Runtime.FaultReason is null&&AvatarNativeData.CopyCalls==0,"Missing anchor mutated or failed campaign");
    w.Game.player_data[3]=w.Originals[1];w.Ready();
   });
   Case("The active order can select a nonleading resident slot",()=>{
    var w=new World();w.StageOpening();w.Game.party_data.player_kazu=1;
    w.Game.party_data.narabi=new List<sbyte>{3,0,1,2,4,-1};w.Game.player_data[1]=null;w.Game.player_data[2]=null;
    Check(w.Runtime.HasMappedPartyRole()&&w.Runtime.StartingPartyBound(),"Nonleading active Cleo slot was ignored");
    w.Ready();w.Runtime.VerifyFinalParty("permuted active slot");
    Check(w.Game.player_data[1] is null&&w.Game.player_data[2] is null&&w.Game.party_data.narabi[0]==3,"Inactive/order state changed");
   });
   Case("Inactive duplicate codes and empty reservation data do not change active membership",()=>{
    var w=new World();w.StageOpening();w.Game.party_data.chara_code=new List<short>{8,1,1,-1,-1,-1};
    w.Game.player_data=new List<GSD1.PLAYER_DATA?>{w.Originals[8],w.Originals[1],null,w.Originals[2],null,null};
    var unused=w.Game.player_data[3]?.Pointer;w.Ready();w.Runtime.VerifyFinalParty("inactive reservations");
    Check(w.Game.player_data[2] is null&&w.Game.player_data[3]?.Pointer==unused,"Unused reservation data was published as a resident");
    Check(w.Game.party_data.player_kazu==1&&!w.Runtime.HasMappedPartyRole(),"Inactive duplicate became active");
   });
   Case("An invalid active order blocks startup even with a valid staged anchor",()=>{
    var w=new World();w.StageOpening();w.Game.party_data.narabi[0]=6;
    Check(!w.Runtime.StartingPartyBound()&&!w.Runtime.EnsureProxy("invalid active index",false)&&AvatarNativeData.CopyCalls==0,"Invalid active index initialized");
    Check(w.Runtime.FaultReason is null,"Transient invalid active order caused permanent stop");
    w.Game.party_data.narabi[0]=0;w.Ready();
   });
   Case("A mismatched active base blocks a valid inactive canonical anchor",()=>{
    var w=new World();w.StageOpening();w.Originals[8].player_base=AvatarNativeData.CloneBase(w.Base(8));
    Check(!w.Runtime.StartingPartyBound()&&!w.Runtime.EnsureProxy("stale active Tir",false)&&AvatarNativeData.CopyCalls==0,"Stale active binding accepted because staged Cleo was ready");
    w.Originals[8].player_base=w.Base(8);w.Ready();
   });
   Case("Party verification rejects invalid active order after initialization",()=>{
    var w=new World();w.StageOpening();w.Ready();w.Game.party_data.narabi[0]=-1;
    bool rejected=false;try{w.Runtime.VerifyFinalParty("invalid active order");}catch(InvalidOperationException){rejected=true;}
    Check(rejected,"Invalid active order was ignored");
   });
   Case("Bound replay preserves staged opening progress and saved assignments",()=>{
    var w=new World();w.StageOpening();w.Game.player_data[1]=null;w.Ready();w.Base(1).hp=7;w.Base(1).exp=912;
    string id=new string('d',64);string map=w.Runtime.Profile.Fingerprint;
    AvatarBindingStore.Bindings[id]=AvatarShuffleBindingJson.Serialize(w.Runtime.Profile,w.Runtime.State,id,AvatarBindingStore.Build);
    string progression=AvatarNativeData.Fingerprint(w.Base(1));AvatarPlugin.ConfiguredSeed="changed after opening";
    w.Runtime.PrepareLoad(new GSD1.SaveDataManager.SaveData{Identity=id});w.Ready();
    Check(w.Runtime.Profile.Fingerprint==map&&AvatarNativeData.Fingerprint(w.Base(1))==progression&&AvatarNativeData.CopyCalls==75,"Staged save replay rerolled or reset progress");
    Check(w.Game.player_data[1] is null&&w.Game.party_data.player_kazu==1,"Replay activated an unbound staged reservation");
    w.Runtime.VerifyFinalParty("staged replay");
   });
   Case("Failed staged initialization restores every base cache and original reservation",()=>{
    var w=new World();w.StageOpening();w.Game.player_data[1]=null;
    w.Originals[6].player_base=AvatarNativeData.CloneBase(w.Base(6));
    var bases=w.Bases.ToDictionary(k=>k.Key,k=>AvatarNativeData.Fingerprint(k.Value));
    var residents=w.Game.player_data.Select(p=>p?.Pointer).ToArray();
    string layout=w.Runtime.CanonicalLayoutFingerprint();AvatarNativeData.FailCopyRoleOnce=35;
    Check(!w.Runtime.EnsureProxy("staged failure",true)&&w.Runtime.FaultReason is not null&&!w.Runtime.IsActive,"Failed staged update became active");
    foreach(var pair in bases)Check(AvatarNativeData.Fingerprint(w.Base(pair.Key))==pair.Value,"Staged rollback base differs");
    foreach(int id in CombatCatalog.NormalCombatIds)Check(w.Cache[id]?.Pointer==w.Originals[id].Pointer,"Staged rollback cache differs");
    Check(w.Runtime.CanonicalLayoutFingerprint()==layout&&w.Game.player_data.Select(p=>p?.Pointer).SequenceEqual(residents),"Staged rollback changed order or reservations");
   });
   Case("Full initialization copies all75 pristine donor snapshots and canonical identities",()=>{
    var w=new World();var before=w.Bases.ToDictionary(k=>k.Key,k=>AvatarNativeData.CloneBase(k.Value));w.Ready();
    Check(AvatarNativeData.CopyCalls==75,"Starting copy count wrong");
    foreach(int id in w.Runtime.Profile.RoleIds){int donor=w.Runtime.Profile.GetDonor(id);var b=w.Base(id);var proxy=w.Runtime.ProxyFor(id) ?? throw new Exception("Missing proxy");
     Check(b.chara_no==id&&proxy.chara_id==id&&proxy.player_base?.Pointer==b.Pointer,"Canonical identity lost");
     Check(b.max_hp==before[donor].max_hp&&b.level==before[donor].level&&b.Equipment==before[donor].Equipment&&b.Rune==before[donor].Rune&&b.Items.SequenceEqual(before[donor].Items),"Cascaded donor start copy");
     Check(proxy.chara_name==w.Originals[donor].chara_name&&proxy.Pointer!=w.Originals[id].Pointer,"Wrong/aliased avatar metadata");
    }
    foreach(int id in new[]{1,2,6})Check(w.Game.player_data[w.Game.party_data.chara_code.IndexOf((short)id)]?.Pointer==w.Runtime.ProxyFor(id)?.Pointer,"Resident proxy absent");
   });
   Case("All excluded bases and original assets remain unchanged",()=>{
    var w=new World();var protectedIds=Enumerable.Range(0,110).Where(id=>!CombatCatalog.IsNormalCombat(id)).ToArray();
    var fingerprints=protectedIds.ToDictionary(id=>id,id=>AvatarNativeData.Fingerprint(w.Base(id)));w.Ready();
    foreach(int id in protectedIds)Check(w.Cache[id]?.Pointer==w.Originals[id].Pointer&&AvatarNativeData.Fingerprint(w.Base(id))==fingerprints[id]&&w.Runtime.MapDisplayId(id)==id,"Protected identity changed");
    foreach(int id in Enumerable.Range(0,110))Check(w.Originals[id].chara_id==id&&w.Originals[id].chara_name==0x10000u+(uint)id,"Shared original changed");
   });
   Case("Repeated ensure preserves live progress and does not rerun starting copies",()=>{
    var w=new World();w.Ready();w.Base(1).hp=7;w.Base(1).exp=912;w.Base(1).Items=new[]{44,77};var before=AvatarNativeData.Fingerprint(w.Base(1));
    Check(w.Runtime.EnsureProxy("repeat",true)&&AvatarNativeData.CopyCalls==75&&before==AvatarNativeData.Fingerprint(w.Base(1)),"Repeated ensure reset progress");
   });
   Case("Bound reload restores savedseed and templates without donor starts",()=>{
    var w=new World();w.Ready();w.Base(1).hp=7;w.Base(1).exp=912;w.Base(1).Items=new[]{44,77};
    string id=new string('a',64);string map=w.Runtime.Profile.Fingerprint;string seed=w.Runtime.Profile.Seed;
    AvatarBindingStore.Bindings[id]=AvatarShuffleBindingJson.Serialize(w.Runtime.Profile,w.Runtime.State,id,AvatarBindingStore.Build);
    AvatarPlugin.ConfiguredSeed="different future campaign";var before=AvatarNativeData.Fingerprint(w.Base(1));
    w.Runtime.PrepareLoad(new GSD1.SaveDataManager.SaveData{Identity=id});
    Check(w.Runtime.Profile.Fingerprint==map&&w.Runtime.Profile.Seed==seed,"Config rerolled save");
    w.Ready();Check(AvatarNativeData.CopyCalls==75&&before==AvatarNativeData.Fingerprint(w.Base(1)),"Replay copied mutable donor state");
   });
   Case("Unbound loaded save disables old proxies and restores original cache",()=>{
    var w=new World();w.Ready();w.Runtime.PrepareLoad(new GSD1.SaveDataManager.SaveData{Identity=new string('c',64)});
    Check(!w.Runtime.IsAdmitted&&!w.Runtime.IsActive&&w.Runtime.MapDisplayId(1)==1,"Unbound load retained projections");
    foreach(int id in CombatCatalog.NormalCombatIds)Check(w.Cache[id]?.Pointer==w.Originals[id].Pointer,"Old cache proxy remains");
   });
   Case("Mid-copy failure rolls back all bases caches and resident pointers",()=>{
    var w=new World();var bases=w.Bases.ToDictionary(k=>k.Key,k=>AvatarNativeData.Fingerprint(k.Value));
    var residents=w.Game.player_data.Select(p=>p?.Pointer).ToArray();AvatarNativeData.FailCopyRoleOnce=35;
    Check(!w.Runtime.EnsureProxy("injected failure",true)&&w.Runtime.FaultReason is not null&&!w.Runtime.IsActive,"Failed batch became active");
    foreach(var pair in bases)Check(AvatarNativeData.Fingerprint(w.Base(pair.Key))==pair.Value,"Rollback base differs");
    foreach(int id in CombatCatalog.NormalCombatIds)Check(w.Cache[id]?.Pointer==w.Originals[id].Pointer,"Rollback cache differs");
    Check(w.Game.player_data.Select(p=>p?.Pointer).SequenceEqual(residents),"Rollback residents differ");
   });
   Case("Returning to title retires projection and restores original cache",()=>{
    var w=new World();w.Ready();w.Runtime.Tick();GSD1.ChapterManager.GR1Instance!.activeChapter=new GSD1.TitleChapter();w.Runtime.Tick();
    Check(!w.Runtime.IsAdmitted&&!w.Runtime.IsActive&&w.Runtime.State.Origin==RoleAvatarCampaignOrigin.UnboundSave,"Title retained campaign");
    foreach(int id in CombatCatalog.NormalCombatIds)Check(w.Cache[id]?.Pointer==w.Originals[id].Pointer,"Title cache not restored");
   });
   Case("Party identity verification rejects a donor identity in canonical slot",()=>{
    var w=new World();w.Ready();w.Game.player_data[0]!.chara_id=0;
    bool rejected=false;try{w.Runtime.VerifyFinalParty("bad triplet");}catch(InvalidOperationException){rejected=true;}
    Check(rejected,"Mixed identity accepted");
   });
   Case("Party verification rejects a same-ID active base from the wrong native world",()=>{
    var w=new World();w.Ready();w.Game.player_data[0]!.player_base=AvatarNativeData.CloneBase(w.Base(1));
    bool rejected=false;try{w.Runtime.VerifyFinalParty("stale active world pointer");}catch(InvalidOperationException){rejected=true;}
    Check(rejected,"Same-ID cloned active base passed exact live-world verification");
   });
   Case("Protagonist-only opening never scans donor assets or native bases",()=>{
    var w=new World();w.Game.party_data.chara_code=new List<short>{8,-1,-1,-1,-1,-1};w.Game.party_data.player_kazu=1;
    w.Game.party_data.narabi=new List<sbyte>{0,-1,-1,-1,-1,-1};
    w.Game.player_data=new List<GSD1.PLAYER_DATA?>{w.Originals[8],null,null,null,null,null};
    w.Game.player_base.ResetReads();w.Cache.ResetLookups();
    int waits=AvatarPlugin.PluginLog.Messages.Count(x=>x.Contains("WAIT:"));
    for(int n=0;n<1000;n++){w.Runtime.EnsureProxy("opening retry",false);w.Runtime.Tick();w.Runtime.ProjectPlayerData(68,w.Originals[68]);}
    for(int n=0;n<40;n++){w.Advance();w.Runtime.EnsureProxy("opening retry",false);}
    Check(w.Game.player_base.Reads==0&&w.Cache.Lookups==0,"Excluded-only opening scanned the75 donor roster");
    Check(w.Runtime.PerformanceStatus.Contains("donor-roster scans=0")&&w.Runtime.PerformanceStatus.Contains("native-base index passes=0"),"F6 counters disagree with untouched native model");
    Check(AvatarPlugin.PluginLog.Messages.Count(x=>x.Contains("WAIT:"))==waits+1,"Unchanged wait message repeated");
    Check(w.Runtime.FaultReason is null&&AvatarNativeData.CopyCalls==0,"Waiting scene faulted or mutated");
   });
   Case("Tick getters and native retries share one background retry budget",()=>{
    var w=new World();w.Game.player_data[2]=null;w.Game.player_base.ResetReads();w.Cache.ResetLookups();
    for(int n=0;n<1000;n++){w.Runtime.EnsureProxy("native retry",false);w.Runtime.Tick();w.Runtime.ProjectPlayerData(68,w.Originals[68]);}
    long firstPass=w.Game.player_base.Reads;
    Check(firstPass<=660&&w.Cache.Lookups==0&&w.Runtime.PerformanceStatus.Contains("background checks=1;"),"Same-frame retry sources repeated native scans");
    w.Advance();w.Runtime.EnsureProxy("next retry",false);
    Check(w.Game.player_base.Reads<=firstPass+660&&w.Runtime.FaultReason is null&&w.Runtime.PerformanceStatus.Contains("background checks=2;"),"Next scheduled readiness retry missing");
   });
   Case("Complete roster initialization indexes native bases in bounded linear passes",()=>{
    var w=new World();w.Game.player_base.ResetReads();w.Ready();
    Check(w.Game.player_base.Reads<=660,"Initialization rescanned all bases per hero");
   });
   Case("Ordinary active getter hits do no roster scans or rebinding",()=>{
    var w=new World();w.Ready();w.Base(1).exp=321;w.Base(1).hp=9;
    var proxy=w.Runtime.ProxyFor(1) ?? throw new Exception("Missing proxy");
    w.Game.player_base.ResetReads();w.Cache.ResetLookups();
    for(int n=0;n<10000;n++)Check(w.Runtime.ProjectPlayerData(1,proxy)?.Pointer==proxy.Pointer,"Getter lost its assigned proxy");
    Check(w.Game.player_base.Reads==0&&w.Cache.Lookups==0&&AvatarNativeData.CopyCalls==75,"Getter hit performed roster work");
    Check(w.Runtime.PerformanceStatus.Contains("direct cache hits=10000"),"F6 direct-hit counter omitted the exercised getters");
    Check(w.Base(1).exp==321&&w.Base(1).hp==9,"Getter hit changed progression");
   });
   Case("Background checks are limited while required battle boundaries still run immediately",()=>{
    var w=new World();w.Ready();w.Game.player_base.ResetReads();w.Cache.ResetLookups();
    for(int n=0;n<1000;n++)w.Runtime.EnsureProxy("background hit",false);
    Check(w.Game.player_base.Reads==0&&w.Cache.Lookups==0,"Active background retries bypassed interval");
    w.Cache.Remove(68);
    Check(!w.Runtime.EnsureProxy("mandatory battle",true)&&w.Runtime.FaultReason is not null,"Required boundary was throttled");
   });
   Case("Forced save and load checks bypass retry delay without failing a transient wait",()=>{
    var w=new World();w.Cache.Remove(68);
    Check(!w.Runtime.EnsureProxy("partial preload",false)&&w.Runtime.FaultReason is null,"Transient wait failed");
    w.Cache[68]=w.Originals[68];
    Check(!w.Runtime.EnsureProxy("same instant",false)&&!w.Runtime.IsActive,"Background bypassed budget");
    Check(w.Runtime.EnsureProxy("save or restore completion",false,force:true)&&w.Runtime.IsActive,"Forced check was delayed");
   });
   Case("Scheduled active rebinding uses a single index pass and preserves earned stats",()=>{
    var w=new World();w.Ready();w.Base(1).exp=912;w.Base(1).hp=7;
    w.Game.player_base.ResetReads();w.Cache.ResetLookups();w.Advance();
    Check(w.Runtime.EnsureProxy("scheduled check",false),"Scheduled check did not run");
    Check(w.Game.player_base.Reads<=330&&w.Cache.Lookups==75,"Active rebind repeated per-role base searches");
    Check(w.Base(1).exp==912&&w.Base(1).hp==7&&AvatarNativeData.CopyCalls==75,"Rebind reset progression");
   });
   Console.WriteLine(_passed+"/"+_passed+" production-runtime managed adapter checks passed.");
   Console.WriteLine("Scope: actual linked AvatarRuntime with managed native test doubles; not full-plugin compilation or native gameplay.");
  }
  finally{if(Directory.Exists(BepInEx.Paths.ConfigPath))Directory.Delete(BepInEx.Paths.ConfigPath,true);}
 }
 private sealed class World
 {
  internal GSD1.GAME_WORK Game=new GSD1.GAME_WORK();
  internal Dictionary<int,GSD1.PLAYER_DATA> Originals=new Dictionary<int,GSD1.PLAYER_DATA>();
  internal Dictionary<int,GSD1.PLAYER_BASE> Bases=new Dictionary<int,GSD1.PLAYER_BASE>();
  internal GSD1.CountingPlayerCache Cache=new GSD1.CountingPlayerCache();
  internal long Now;
  internal AvatarRuntime Runtime;
  internal World(bool admit=true)
  {
   Runtime=new AvatarRuntime(()=>Now);
   AvatarNativeData.CopyCalls=0;AvatarNativeData.FailCopyRoleOnce=null;AvatarBindingStore.Bindings.Clear();AvatarPlugin.ConfiguredSeed="agilen-080";
   for(int id=0;id<110;id++){
    var b=new GSD1.PLAYER_BASE{chara_no=id,level=(byte)(1+id%50),max_hp=(ushort)(100+id),hp=(ushort)(100+id),exp=(ushort)(id*3),Equipment=1000+id,Rune=2000+id,Items=new[]{3000+id},seicho_type=Enumerable.Repeat((byte)id,7).ToArray()};
    var p=new GSD1.PLAYER_DATA{chara_id=(byte)id,chara_name=0x10000u+(uint)id,player_base=b,seicho_type=Enumerable.Repeat((byte)id,7).ToArray()};
    Bases.Add(id,b);Originals.Add(id,p);Cache.Add(id,p);Game.player_base.Add(b);
   }
   Game.player_data=Game.party_data.chara_code.Select(id=>id<0?null:Originals[id]).ToList();
   GSD1.OldSrcBase.game_work=Game;GSD1.GlobalWork.Instance=new GSD1.GlobalWork{player_manager=new GSD1.PlayerManager{player_cache=Cache}};
   GSD1.ChapterManager.GR1Instance=new GSD1.ChapterManager{activeChapter=new GSD1.Chapter()};
   AvatarRuntime.Current=Runtime;if(admit)Runtime.BeginNewGame();
  }
  internal GSD1.PLAYER_BASE Base(int id)=>Bases[id];
  internal void StageOpening()
  {
   Game.party_data.chara_code=new List<short>{8,2,6,1,108,-1};
   Game.party_data.narabi=new List<sbyte>{0,-1,-1,-1,-1,-1};Game.party_data.player_kazu=1;
   Game.player_data=Game.party_data.chara_code.Select(id=>id<0?null:Originals[id]).ToList();
  }
  internal void Advance()=>Now+=Math.Max(1L,Stopwatch.Frequency/4);
  internal void Ready()=>Check(Runtime.EnsureProxy("model ready",true)&&Runtime.IsActive&&Runtime.FaultReason is null,"Expected ready model: "+Runtime.FaultReason);
 }
}
