using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using SuikodenRandomizer.Suikoden2Plugin;
using SuikodenRandomizer.Shared;

internal static partial class Program
{
    private static void Check(bool valid, string message) { if (!valid) throw new Exception(message); }
    private static void Reject<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
    private static Il2CppStructArray<byte> Bytes(int count, int seed)
    { var a = new Il2CppStructArray<byte>(count); for (int i=0;i<count;i++) a[i]=(byte)((seed+i)%250); return a; }
    private static GAME_WORK Fixture(params byte[] roles)
    {
        const int count=128;
        var game=new GAME_WORK { chara_data=new CHARA_DATA {
            c_kotei_dat=new Il2CppReferenceArray<C_KOTEI_DAT>(count), c_varia_dat=new Il2CppReferenceArray<C_VARIA_DAT>(count),
            ckd_info=new Il2CppSystem.Collections.Generic.List<Il2CppSystem.Collections.Generic.List<byte>>(),
            _serialized_ckd_info=new Il2CppSystem.Collections.Generic.List<IMPORT_GAME_DATA.NestDataClass<byte>>(),
            suki_value=new Il2CppStructArray<sbyte>(count) },
            party_data=new PARTY_DATA {party_cha_no=Bytes(8,0)}, c_teg_dat=new C_TEG_DAT {name=new Il2CppStructArray<int>(count)},
            arms_data=new Il2CppReferenceArray<ARMS_DATA>(count),chara_flag=Enumerable.Range(0,count).Select(i=>(byte)(i%7)).ToArray(),story_flag=new byte[] {3,17,91,128} };
        for(int i=0;i<count;i++) {
            game.chara_data.c_kotei_dat[i]=new C_KOTEI_DAT {seicyo_type=Bytes(8,i),monsyo_seigen=Bytes(8,i+1),mon_zoku_aisyo=Bytes(8,i+2),mon_kosu_type=Bytes(8,i+3),soubi_henkou=(byte)i,soubi_kanou=(byte)(i+1),okori_panic=(byte)(i+2)};
            var items=new Il2CppReferenceArray<PARTY_ITEM>(3);for(int n=0;n<3;n++)items[n]=new PARTY_ITEM {item_no=(byte)(i+n),use_cnt=(byte)(n+1)};
            game.chara_data.c_varia_dat[i]=new C_VARIA_DAT {level=(byte)(1+i%70),exp=(ushort)(i*101),now_hp=(ushort)(100+i),max_hp=(ushort)(200+i),mp=Bytes(4,i),para=Bytes(7,i+1),buki_lv=(byte)(1+i%16),buki_mon=(byte)(i%8),mon_eqp=Bytes(3,i+2),bogu_eqp=Bytes(3,i+3),item_eqp=items,todome=(ushort)(i+4)};
            var row=new Il2CppSystem.Collections.Generic.List<byte>();row.Add((byte)i);row.Add((byte)(new[] {49,50,72,74}.Contains(i)?0x60:i==26?6:0));row.Add((byte)(i+2));
            if(i==26)game.chara_data.c_varia_dat[i].buki_lv=0; // Original natural-weapon class6 legitimately uses level0.
            game.chara_data.ckd_info.Add(row);var mirror=new Il2CppSystem.Collections.Generic.List<byte>();mirror.AddRange(row);game.chara_data._serialized_ckd_info.Add(new IMPORT_GAME_DATA.NestDataClass<byte>{data=mirror});
            game.c_teg_dat.name[i]=10000+i;game.chara_data.suki_value[i]=(sbyte)(i%100);
            game.arms_data[i]=new ARMS_DATA {name_1=20000+i,name_2=21000+i,name_3=22000+i,info=Bytes(17,i),mon_no=(byte)(i%10)};
        }
        SetParty(game,roles.Length==0?new byte[]{1,80}:roles);
        return game;
    }
    private static void SetParty(GAME_WORK game, params byte[] roles)
    { for(int i=0;i<8;i++)game.party_data.party_cha_no[i]=i<roles.Length?roles[i]:(byte)0; }
    private static string Variables(CHARA_DATA d) => JsonSerializer.Serialize(d.c_varia_dat.Select(v=>new {v.level,v.exp,v.now_hp,v.max_hp,mp=v.mp.ToArray(),para=v.para.ToArray(),v.buki_lv,v.buki_mon,runes=v.mon_eqp.ToArray(),armor=v.bogu_eqp.ToArray(),items=v.item_eqp.Select(x=>new{x.item_no,x.use_cnt}).ToArray(),v.todome}));
    private static string Statics(CHARA_DATA d) => JsonSerializer.Serialize(new {info=d.ckd_info.Select(x=>x.ToArray()).ToArray(),fixedRows=d.c_kotei_dat.Select(x=>new {growth=x.seicyo_type.ToArray(),limit=x.monsyo_seigen.ToArray(),affinity=x.mon_zoku_aisyo.ToArray(),count=x.mon_kosu_type.ToArray(),x.soubi_henkou,x.soubi_kanou,x.okori_panic}).ToArray(),friendship=d.suki_value.ToArray()});
    private static string Arms(GAME_WORK g)=>JsonSerializer.Serialize(g.arms_data.Select(x=>new{x.name_1,x.name_2,x.name_3,info=x.info.ToArray(),x.mon_no}));
    private static Gsd2Runtime Start(string mode="Shuffle",params byte[] roles)
    {
        Gsd2Plugin.Seed="actual-linked-runtime";Gsd2Plugin.Mode=mode;Gsd2Plugin.Companions="2,3,4,5,6";Gsd2Plugin.IncludeProtagonist=false;Gsd2Plugin.StartPoint="Opening";
        OldSrcBase.game_work=Fixture(roles);Gsd2Graphics.IsReady=false;
        var runtime=new Gsd2Runtime();Gsd2Runtime.Current=runtime;runtime.BeginNewGame();runtime.ObserveNativeDataset();runtime.CompleteNativeNewGame();return runtime;
    }
    private static string BindingFile(string json)=>Path.Combine(BepInEx.Paths.ConfigPath,"SuikodenRandomizer","gsd2-campaigns",Gsd2Runtime.Hash(json)+".json");
    private static Gsd2Runtime PendingCamp()
    {
        Gsd2Plugin.Seed="actual-linked-camp";Gsd2Plugin.Mode="Shuffle";Gsd2Plugin.IncludeProtagonist=false;Gsd2Plugin.StartPoint=Gsd2CampStart.Point;
        OldSrcBase.game_work=Fixture(1);Gsd2Graphics.IsReady=false;
        var runtime=new Gsd2Runtime();Gsd2Runtime.Current=runtime;runtime.BeginNewGame();runtime.ObserveNativeDataset();return runtime;
    }
    private static void ResetCampCollaborators()
    {
        Gsd2Plugin.JoiningWeaponLevel="MatchRole";
        Gsd2CampStart.BeforeApply=null;Gsd2CampStart.Applications=0;
        Gsd2CampNativeGuard.PlayerReady=true;Gsd2CampNativeGuard.RejectFresh=false;Gsd2CampNativeGuard.RejectMap=false;Gsd2CampNativeGuard.RejectPlayer=false;
        Gsd2CampNativeGuard.FreshChecks=0;Gsd2CampNativeGuard.MapChecks=0;Gsd2CampNativeGuard.PlayerChecks=0;
        Gsd2RejectedSaveMirrorGuard.EnsureCalls=0;Gsd2RejectedSaveMirrorGuard.RejectEnsure=false;
    }
    private static void Main()
    {
        Directory.CreateDirectory(BepInEx.Paths.ConfigPath);
        var tests=new(string Name,Action Run)[] {
            ("shuffled weapon starts follow the native role while donor identity and earned upgrades survive reload",()=> {
                Gsd2Plugin.Mode="Shuffle";Gsd2Plugin.Seed="role-weapon-regression";Gsd2Plugin.IncludeProtagonist=false;Gsd2Plugin.StartPoint="Opening";
                var g=Fixture(1,80);OldSrcBase.game_work=g;var original=g.chara_data;var arms=g.arms_data;
                var r=new Gsd2Runtime();r.BeginNewGame();var pair=r.Profile.Assignments.First(p=>p.Key!=p.Value&&p.Key!=26&&p.Value!=26);
                int role=pair.Key,donor=pair.Value;original.c_varia_dat[role].buki_lv=2;original.c_varia_dat[donor].buki_lv=13;
                var expected=Gsd2FighterRecord.Capture(original.c_varia_dat[donor]);expected.WeaponLevel=2;
                r.ObserveNativeDataset();r.CompleteNativeNewGame();
                Check(JsonSerializer.Serialize(Gsd2FighterRecord.Capture(g.chara_data.c_varia_dat[role]))==JsonSerializer.Serialize(expected),"Weapon normalization changed donor stats/runes/equipment or retained late weapon13");
                Check(g.arms_data[role].name_1==arms[donor].name_1&&g.arms_data[role].info[0]==arms[donor].info[0],"Weapon normalization replaced donor weapon identity");
                Check(original.c_varia_dat[donor].buki_lv==13&&original.c_varia_dat[role].buki_lv==2,"Original native weapon starts were mutated");
                Check(Gsd2Plugin.PluginLog.Lines.Any(line=>line.Contains("role="+role+"; donor="+donor+"; nativeRole=2; donorBefore=13; after=2; policy=MatchRole")),"Joining weapon log omitted native/donor/policy values");
                g.chara_data.c_varia_dat[role].buki_lv=12;r.CompleteNativeNewGame();r.Tick();Check(g.chara_data.c_varia_dat[role].buki_lv==12,"Repeated initialization erased sharpening");
                r.BeginSave();r.BindSnapshot("role-weapon-save");var saved=Gsd2NativeData.Characters(g.chara_data);r.EndSave();
                var binding=JsonNode.Parse(File.ReadAllText(BindingFile("role-weapon-save")))!;
                Check(binding["JoiningWeaponLevel"]!.GetValue<string>()=="MatchRole","Weapon policy was not bound to the save");
                Gsd2Plugin.JoiningWeaponLevel="Level1";OldSrcBase.game_work=Fixture(1,80);OldSrcBase.game_work.chara_data=saved;
                var loaded=new Gsd2Runtime();loaded.PrepareLoad("role-weapon-save");loaded.CompleteLoad(true);loaded.Tick();
                Check(loaded.ActiveJoiningWeaponLevel=="MatchRole"&&OldSrcBase.game_work.chara_data.c_varia_dat[role].buki_lv==12,"Reload applied current config or reset saved sharpening");
            }),
            ("Level1 preserves protected protagonist rows and native natural weapons",()=> {
                Gsd2Plugin.Mode="Shuffle";Gsd2Plugin.Seed="level-one-protection";Gsd2Plugin.IncludeProtagonist=true;Gsd2Plugin.StartPoint="Opening";Gsd2Plugin.JoiningWeaponLevel="Level1";
                var g=Fixture(1,80);OldSrcBase.game_work=g;var original=g.chara_data;var r=new Gsd2Runtime();r.BeginNewGame();r.ObserveNativeDataset();r.CompleteNativeNewGame();
                foreach(var pair in r.Profile.Assignments) {
                    byte expected=pair.Key==1||pair.Value==1||pair.Key>=80 ? original.c_varia_dat[pair.Key].buki_lv : pair.Value==26 ? (byte)0 : (byte)1;
                    Check(g.chara_data.c_varia_dat[pair.Key].buki_lv==expected,"Level1 damaged protected/natural weapon semantics at role "+pair.Key);
                }
                Check(r.Profile.Assignments.Any(pair=>pair.Value==26&&pair.Key!=1),"Fixture never exercised natural donor");
                Gsd2Plugin.JoiningWeaponLevel="MatchRole";g=Fixture(1,80);OldSrcBase.game_work=g;r=new Gsd2Runtime();r.BeginNewGame();
                int armedRole=r.Profile.Assignments.First(pair=>pair.Key!=1&&pair.Value!=1&&pair.Value!=26).Key;
                g.chara_data.c_varia_dat[armedRole].buki_lv=0;r.ObserveNativeDataset();r.CompleteNativeNewGame();
                Check(g.chara_data.c_varia_dat[armedRole].buki_lv==1,"Armed donor inherited invalid native-role weapon0");
            }),
            ("fixed weapons initialize on first role assignment then retain sharpening across moves and saves",()=> {
                Gsd2Plugin.Mode="FixedChosen";Gsd2Plugin.Seed="fixed-weapon-assignment";Gsd2Plugin.Companions="2,3,4,5,6";Gsd2Plugin.StartPoint="Opening";
                var g=Fixture(1);g.chara_data.c_varia_dat[2].buki_lv=13;g.chara_data.c_varia_dat[3].buki_lv=14;OldSrcBase.game_work=g;
                var r=new Gsd2Runtime();r.BeginNewGame();r.ObserveNativeDataset();r.CompleteNativeNewGame();
                g.chara_data.c_varia_dat[7].buki_lv=4;SetParty(g,1,7);r.Tick();
                Check(r.MapDisplayId(7)==2&&g.chara_data.c_varia_dat[7].buki_lv==4,"Fixed donor retained late weapon or ignored native event role start");
                g.chara_data.c_varia_dat[7].buki_lv=9;SetParty(g,1,8);r.Tick();
                Check(r.MapDisplayId(8)==2&&g.chara_data.c_varia_dat[8].buki_lv==9,"Fixed donor weapon reset when assigned another role");
                SetParty(g,1,7);r.Tick();Check(g.chara_data.c_varia_dat[7].buki_lv==9,"Fixed donor sharpening reset on return");
                r.BeginSave();r.BindSnapshot("fixed-weapon-save");var saved=Gsd2NativeData.Characters(g.chara_data);r.EndSave();
                var node=JsonNode.Parse(File.ReadAllText(BindingFile("fixed-weapon-save")))!;
                Check(node["JoiningWeaponInitializedDonors"]!.AsArray().Select(x=>x!.GetValue<int>()).SequenceEqual(new[]{2}),"Fixed save did not distinguish first assignment from unused donors");
                Gsd2Plugin.JoiningWeaponLevel="Level1";OldSrcBase.game_work=Fixture(1,7);g=OldSrcBase.game_work;g.chara_data=saved;
                var loaded=new Gsd2Runtime();loaded.PrepareLoad("fixed-weapon-save");loaded.CompleteLoad(true);
                Check(g.chara_data.c_varia_dat[7].buki_lv==9,"Fixed reload reset upgraded weapon");
                g.chara_data.c_varia_dat[9].buki_lv=5;SetParty(g,1,7,9);loaded.Tick();
                Check(loaded.MapDisplayId(9)==3&&g.chara_data.c_varia_dat[9].buki_lv==5,"Unused fixed donor did not use saved MatchRole policy at its first assignment");
            }),
            ("legacy bindings keep prior and unassigned donor weapons despite new configuration",()=> {
                var r=Start("FixedChosen",1,7);var g=OldSrcBase.game_work;g.chara_data.c_varia_dat[7].buki_lv=12;
                r.BeginSave();r.BindSnapshot("legacy-weapon-save");var saved=Gsd2NativeData.Characters(g.chara_data);r.EndSave();
                string path=BindingFile("legacy-weapon-save");var node=JsonNode.Parse(File.ReadAllText(path))!.AsObject();
                node.Remove("JoiningWeaponLevel");node.Remove("JoiningWeaponInitializedDonors");node["Fighters"]!["3"]!["WeaponLevel"]=14;File.WriteAllText(path,node.ToJsonString());
                Gsd2Plugin.JoiningWeaponLevel="Level1";OldSrcBase.game_work=Fixture(1,7);g=OldSrcBase.game_work;g.chara_data=saved;
                var loaded=new Gsd2Runtime();loaded.PrepareLoad("legacy-weapon-save");loaded.CompleteLoad(true);g.chara_data.c_varia_dat[8].buki_lv=2;SetParty(g,1,7,8);loaded.Tick();
                Check(loaded.ActiveJoiningWeaponLevel=="Donor"&&g.chara_data.c_varia_dat[7].buki_lv==12&&g.chara_data.c_varia_dat[8].buki_lv==14,"Legacy binding was retroactively normalized");
                loaded.BeginSave();loaded.BindSnapshot("legacy-weapon-resave");loaded.EndSave();
                Check(JsonNode.Parse(File.ReadAllText(BindingFile("legacy-weapon-resave")))!["JoiningWeaponLevel"]!.GetValue<string>()=="Donor","Legacy compatibility policy was lost on resave");
            }),
            ("invalid saved weapon policies and initialized donor records are rejected",()=> {
                var r=Start("FixedChosen",1,7);r.BeginSave();r.BindSnapshot("bad-weapon-binding");r.EndSave();string path=BindingFile("bad-weapon-binding"),original=File.ReadAllText(path);
                foreach(string policy in new[]{"", "Level13", "matchrole"}) {
                    var node=JsonNode.Parse(original)!;node["JoiningWeaponLevel"]=policy;File.WriteAllText(path,node.ToJsonString());Reject<InvalidDataException>(()=>new Gsd2Runtime().PrepareLoad("bad-weapon-binding"));
                }
                foreach(int[] ids in new[]{new[]{2,2},new[]{127},Array.Empty<int>()}) {
                    var node=JsonNode.Parse(original)!;node["JoiningWeaponInitializedDonors"]=JsonSerializer.SerializeToNode(ids);File.WriteAllText(path,node.ToJsonString());Reject<InvalidDataException>(()=>new Gsd2Runtime().PrepareLoad("bad-weapon-binding"));
                }
            }),
            ("new game projects detached donor records once without touching canonical party or flags",()=> {
                Gsd2Plugin.Mode="Shuffle";Gsd2Plugin.Seed="actual-linked-runtime";Gsd2Plugin.IncludeProtagonist=false;Gsd2Plugin.StartPoint="Opening";
                var game=Fixture(1,80,2);OldSrcBase.game_work=game;var original=game.chara_data;var originalNames=game.c_teg_dat.name;var originalArms=game.arms_data;
                string fixedBefore=Statics(original),varsBefore=Variables(original),armsBefore=Arms(game);byte[] flags=(byte[])game.chara_flag.Clone(),story=(byte[])game.story_flag.Clone(),party=game.party_data.party_cha_no.ToArray();
                var runtime=new Gsd2Runtime();runtime.BeginNewGame();Check(!runtime.HoldNativeUpdate,"Uninitialized opening was blocked");runtime.ObserveNativeDataset();runtime.CompleteNativeNewGame();
                var pair=runtime.Profile.Assignments.First(p=>p.Key!=p.Value&&p.Key>1&&p.Value>1);int role=pair.Key,donor=pair.Value;
                Check(game.chara_data.Pointer!=original.Pointer,"Original data was published directly");
                Check(game.chara_data.c_varia_dat[role].level==original.c_varia_dat[donor].level,"Donor start missing");
                Check(game.chara_data.c_kotei_dat[role].seicyo_type[0]==original.c_kotei_dat[donor].seicyo_type[0],"Growth template not projected");
                Check(game.arms_data[role].name_1==originalArms[donor].name_1&&game.arms_data[role].info[0]==originalArms[donor].info[0],"Weapon template missing");
                Check(game.c_teg_dat.name[role]==originalNames[donor],"Donor name missing");
                Check(Statics(original)==fixedBefore&&Variables(original)==varsBefore,"Imported original template mutated");
                Check(flags.SequenceEqual(game.chara_flag)&&story.SequenceEqual(game.story_flag)&&party.SequenceEqual(game.party_data.party_cha_no),"Canonical membership/story/party identity changed");
                game.chara_data.c_varia_dat[role].level=77;game.chara_data.c_varia_dat[role].now_hp=123;game.chara_data.c_varia_dat[role].bogu_eqp[0]=201;game.arms_data[role].info[0]=219;
                runtime.CompleteNativeNewGame();runtime.ObserveNativeDataset();runtime.Tick();
                Check(game.chara_data.c_varia_dat[role].level==77&&game.chara_data.c_varia_dat[role].bogu_eqp[0]==201,"Repeated boundary reset earned state");
                Check(originalArms[donor].info[0]!=(byte)219,"Projected weapon aliases imported weapon bytes");
                Check(armsBefore==JsonSerializer.Serialize(originalArms.Select(x=>new{x.name_1,x.name_2,x.name_3,info=x.info.ToArray(),x.mon_no})),"Original arms changed");
            }),
            ("final native boundary refreshes late static changes without mutating original templates",()=> {
                Gsd2Plugin.Mode="Shuffle";Gsd2Plugin.Seed="late-native-initialization";Gsd2Plugin.IncludeProtagonist=false;Gsd2Plugin.StartPoint="Opening";
                var g=Fixture(1,80,2);OldSrcBase.game_work=g;Gsd2Graphics.IsReady=false;var original=g.chara_data;var originalNames=g.c_teg_dat.name;var originalArms=g.arms_data;
                var r=new Gsd2Runtime();r.BeginNewGame();r.ObserveNativeDataset();var pair=r.Profile.Assignments.First(p=>p.Key!=p.Value);int role=pair.Key,donor=pair.Value;
                original.c_kotei_dat[donor].seicyo_type[0]=222;original.ckd_info[donor][0]=221;originalNames[donor]=34567;originalArms[donor].name_1=45678;originalArms[donor].info[0]=219;
                original.c_kotei_dat[role].seicyo_type[0]=223;original.ckd_info[role][0]=220;originalNames[role]=34568;originalArms[role].name_1=45679;originalArms[role].info[0]=218;
                original.c_varia_dat[donor].now_hp=977;
                string statics=Statics(original),vars=Variables(original),names=JsonSerializer.Serialize(originalNames.ToArray());
                Func<string> originalWeaponState=()=>JsonSerializer.Serialize(originalArms.Select(x=>new{x.name_1,x.name_2,x.name_3,info=x.info.ToArray(),x.mon_no}));string weapons=originalWeaponState();
                Check(ReferenceEquals(g.chara_data,original),"Native initialization fixture replaced the dataset instead of mutating its rows");
                r.CompleteNativeNewGame();
                Check(g.chara_data.c_kotei_dat[role].seicyo_type[0]==222&&g.chara_data.ckd_info[role][0]==221&&g.c_teg_dat.name[role]==34567&&g.arms_data[role].name_1==45678&&g.arms_data[role].info[0]==219,"Projection used an early cached static template");
                Check(g.chara_data.c_varia_dat[role].now_hp==977,"Projection omitted the late native donor start");
                g.chara_data.c_varia_dat[role].now_hp=33;r.CompleteNativeNewGame();Check(g.chara_data.c_varia_dat[role].now_hp==33,"Duplicate final boundary reapplied native starts");
                r.BeginSave();
                Check(g.chara_data.c_kotei_dat[role].seicyo_type[0]==223&&g.chara_data.ckd_info[role][0]==220&&g.c_teg_dat.name[role]==34568&&g.arms_data[role].name_1==45679&&g.arms_data[role].info[0]==218,"Canonical save lost late original role statics");
                Check(g.chara_data.c_kotei_dat[donor].seicyo_type[0]==222&&g.c_teg_dat.name[donor]==34567&&g.arms_data[donor].name_1==45678,"Canonical save lost late original donor statics");
                Check(g.chara_data.c_varia_dat[role].now_hp==33,"Canonical save reset progression after the native boundary");r.EndSave();
                Check(Statics(original)==statics&&Variables(original)==vars&&JsonSerializer.Serialize(originalNames.ToArray())==names&&originalWeaponState()==weapons,"Projection or save mutated the final original templates");
            }),
            ("first autosave after native initialization binds donor starts before graphics are ready",()=> {
                Gsd2Plugin.Mode="Shuffle";Gsd2Plugin.Seed="first-native-autosave";Gsd2Plugin.IncludeProtagonist=false;Gsd2Plugin.StartPoint="Opening";
                var g=Fixture(1,80,2);OldSrcBase.game_work=g;Gsd2Graphics.IsReady=false;var original=g.chara_data;
                var r=new Gsd2Runtime();r.BeginNewGame();r.ObserveNativeDataset();
                Reject<InvalidOperationException>(()=>r.BeginSave());Check(ReferenceEquals(g.chara_data,original),"Pending save changed native tables");
                r.CompleteNativeNewGame();var pair=r.Profile.Assignments.First(p=>p.Key!=p.Value);int role=pair.Key,donor=pair.Value;
                Check(r.TablesReady&&!r.IsActive&&r.HoldNativeUpdate,"Autosave fixture did not precede graphics readiness");
                r.BeginSave();
                Check(g.chara_data.c_varia_dat[role].now_hp==100+donor&&g.chara_data.c_varia_dat[role].bogu_eqp[0]==donor+3,"Initial autosave omitted initialized donor starts");
                Check(g.chara_data.ckd_info[role][0]==role&&g.chara_data.c_kotei_dat[role].seicyo_type[0]==role&&g.c_teg_dat.name[role]==10000+role&&g.arms_data[role].name_1==20000+role,"Initial autosave leaked projected static tables");
                string json=JsonSerializer.Serialize(new {variables=JsonNode.Parse(Variables(g.chara_data)),statics=JsonNode.Parse(Statics(g.chara_data)),names=g.c_teg_dat.name.ToArray(),arms=JsonNode.Parse(Arms(g))});
                r.BindSnapshot(json);var binding=JsonNode.Parse(File.ReadAllText(BindingFile(json)))!;
                Check(binding["Initialized"]!.GetValue<bool>()&&binding["SaveIdentity"]!.GetValue<string>()==Gsd2Runtime.Hash(json),"Initial autosave binding was missing or not bound to its native JSON");
                var saved=Gsd2NativeData.Characters(g.chara_data);var names=Gsd2NativeData.Names(g.c_teg_dat.name);var arms=Gsd2NativeData.Arms(g.arms_data);r.EndSave();
                OldSrcBase.game_work=Fixture(1,80,2);g=OldSrcBase.game_work;g.chara_data=saved;g.c_teg_dat.name=names;g.arms_data=arms;
                Gsd2Plugin.Seed="ignored-after-autosave";var loaded=new Gsd2Runtime();loaded.PrepareLoad(json);loaded.CompleteLoad(true);
                Check(loaded.Profile.Seed=="first-native-autosave"&&g.chara_data.c_varia_dat[role].now_hp==100+donor&&g.chara_data.c_varia_dat[role].bogu_eqp[0]==donor+3,"Reload did not retain the first autosave donor starts");
                g.chara_data.c_varia_dat[role].now_hp=29;g.chara_data.c_varia_dat[role].bogu_eqp[0]=237;loaded.CompleteNativeNewGame();loaded.Tick();loaded.CompleteNativeNewGame();
                Check(g.chara_data.c_varia_dat[role].now_hp==29&&g.chara_data.c_varia_dat[role].bogu_eqp[0]==237,"Reload reapplied donor starts over earned progress");
            }),
            ("chapter readiness gate waits for graphics and remains fault-closed",()=> {
                var r=Start();Check(r.IsAdmitted&&r.TablesReady&&!r.IsActive&&r.HoldNativeUpdate,"Prepared state advanced original chapter before resources");
                Gsd2Graphics.IsReady=true;Check(r.IsActive&&!r.HoldNativeUpdate,"Ready graphics did not open gate");
                r.BeginSave();Check(r.TablesReady,"Save discarded admitted projection");r.EndSave();Check(r.IsActive&&!r.HoldNativeUpdate,"Save did not restore readiness");
                r.Fail("test fault");Check(r.HoldNativeUpdate&&!r.IsActive,"Fault did not stop native chapter");
                var native=new Gsd2Runtime();native.PrepareLoad("unbound-gate");native.CompleteLoad(true);Check(!native.HoldNativeUpdate&&!native.IsAdmitted,"Unbound original save was paused");
            }),
            ("save temporarily publishes canonical static names/arms and restores exact projected objects",()=> {
                var r=Start();var g=OldSrcBase.game_work;var projected=g.chara_data;var names=g.c_teg_dat.name;var arms=g.arms_data;
                int role=r.Profile.Assignments.First(p=>p.Key!=p.Value).Key;g.chara_data.c_varia_dat[role].now_hp=57;g.chara_data.c_varia_dat[role].bogu_eqp[0]=223;
                r.BeginSave();Check(g.chara_data.Pointer!=projected.Pointer,"Save used projected static table");
                Check(g.c_teg_dat.name[role]==10000+role&&g.arms_data[role].name_1==20000+role&&g.chara_data.ckd_info[role][0]==role,"Save leaked donor static data");
                Check(g.chara_data.c_varia_dat[role].now_hp==57&&g.chara_data.c_varia_dat[role].bogu_eqp[0]==223,"Save lost earned progression");
                Reject<InvalidOperationException>(()=>r.BeginSave());r.BindSnapshot("save-canonical");r.EndSave();r.EndSave();
                Check(ReferenceEquals(g.chara_data,projected)&&ReferenceEquals(g.c_teg_dat.name,names)&&ReferenceEquals(g.arms_data,arms),"Save cleanup did not restore exact published ownership");
                Check(File.Exists(BindingFile("save-canonical")),"Save binding not written");Reject<InvalidOperationException>(()=>r.BindSnapshot("outside-scope"));
            }),
            ("load restores saved profile while ignoring changed config and never reapplies donor starts",()=> {
                var first=Start();var g=OldSrcBase.game_work;int role=first.Profile.Assignments.First(p=>p.Key!=p.Value).Key;string fingerprint=first.Profile.Fingerprint;
                g.chara_data.c_varia_dat[role].now_hp=31;g.chara_data.c_varia_dat[role].level=88;g.chara_data.c_varia_dat[role].bogu_eqp[0]=230;
                first.BeginSave();first.BindSnapshot("earned-save");var loaded=Gsd2NativeData.Characters(g.chara_data);var loadedNames=Gsd2NativeData.Names(g.c_teg_dat.name);var loadedArms=Gsd2NativeData.Arms(g.arms_data);first.EndSave();
                Gsd2Plugin.Mode="FixedChosen";Gsd2Plugin.Companions="invalid";Gsd2Plugin.Seed="different-config";Gsd2Plugin.StartPoint="different-config";
                OldSrcBase.game_work=Fixture(1,80);OldSrcBase.game_work.chara_data=loaded;OldSrcBase.game_work.c_teg_dat.name=loadedNames;OldSrcBase.game_work.arms_data=loadedArms;
                var next=new Gsd2Runtime();next.PrepareLoad("earned-save");next.CompleteLoad(true);
                Check(next.Profile.Fingerprint==fingerprint&&next.ActiveStartPoint=="Opening","Current config overrode saved identity");
                Check(OldSrcBase.game_work.chara_data.c_varia_dat[role].now_hp==31&&OldSrcBase.game_work.chara_data.c_varia_dat[role].level==88&&OldSrcBase.game_work.chara_data.c_varia_dat[role].bogu_eqp[0]==230,"Load reset HP/equipment/level");
                Check(next.HoldNativeUpdate,"Load did not wait for donor resources");Gsd2Graphics.IsReady=true;Check(next.IsActive&&!next.HoldNativeUpdate,"Loaded resources never admitted");
            }),
            ("fixed departure and return transfer earned fighter state with canonical role IDs intact",()=> {
                var r=Start("FixedChosen",1,7,8);var g=OldSrcBase.game_work;Check(r.MapDisplayId(7)==2&&r.MapDisplayId(8)==3,"Initial fixed order wrong");
                g.chara_data.c_varia_dat[7].now_hp=19;g.chara_data.c_varia_dat[7].bogu_eqp[0]=202;
                SetParty(g,1,9,8);r.Tick();Check(r.MapDisplayId(9)==2&&g.chara_data.c_varia_dat[9].now_hp==19&&g.chara_data.c_varia_dat[9].bogu_eqp[0]==202,"Vacated fighter record reset");
                SetParty(g,1,7,8);r.Tick();Check(r.MapDisplayId(7)==2&&g.chara_data.c_varia_dat[7].now_hp==19,"Returning role lost fighter state");
                Check(g.party_data.party_cha_no[1]==7&&g.party_data.party_cha_no[2]==8,"Fixed roster changed story IDs");
            }),
            ("six nonhero roles borrow display hero without copying hero combat state and reclaim safely",()=> {
                var r=Start("FixedChosen",7,8,9,10,11,12);var g=OldSrcBase.game_work;
                Check(r.MapDisplayId(12)==1&&!Enumerable.Range(0,6).Any(i=>g.party_data.party_cha_no[i]==1),"Display borrowing inserted canonical protagonist");
                Check(g.chara_data.c_varia_dat[12].level==13&&g.chara_data.c_varia_dat[12].now_hp==112,"Borrowed role received hero combat data");
                g.chara_data.c_varia_dat[12].now_hp=39;g.chara_data.c_varia_dat[12].bogu_eqp[0]=207;
                SetParty(g,1,7,8,9,10,11);r.Tick();Check(r.MapDisplayId(1)==1&&g.chara_data.c_varia_dat[12].now_hp==39,"Hero return clobbered borrowed role progression");
                SetParty(g,7,8,9,10,11,12);r.Tick();Check(r.MapDisplayId(12)==1&&g.chara_data.c_varia_dat[12].now_hp==39&&g.chara_data.c_varia_dat[12].bogu_eqp[0]==207,"Reborrowing reset canonical borrowed-role state");
            }),
            ("departed fixed roles restore canonical growth and character metadata",()=> {
                var r=Start("FixedChosen",1,7);var g=OldSrcBase.game_work;
                Check(g.chara_data.ckd_info[7][0]==2,"Fixture did not project role7");
                SetParty(g,1,8);r.Tick();
                Check(g.chara_data.ckd_info[7][0]==7&&g.chara_data.c_kotei_dat[7].seicyo_type[0]==7,"Departed role retained stale donor metadata/growth");
                Check(g.c_teg_dat.name[7]==10007&&g.arms_data[7].name_1==20007,"Departed role retained donor names/weapon templates");
            }),
            ("canonical large-role formation class survives fixed donor presentation",()=> {
                var r=Start("FixedChosen",1,49);var g=OldSrcBase.game_work;
                Check((g.chara_data.ckd_info[49][1]&0x60)==0x60,"Fixed donor projection removed native canonical two-cell reservation");
            }),
            ("save preserves live friendship changes alongside earned combat state",()=> {
                var r=Start();var g=OldSrcBase.game_work;g.chara_data.suki_value[7]=91;
                r.BeginSave();Check(g.chara_data.suki_value[7]==91,"Canonical save reset live friendship values to new-game baseline");r.EndSave();
            }),
            ("borrowed protagonist preserves natural-weapon metadata with legal weapon level zero",()=> {
                var r=Start("FixedChosen",7,8,9,10,11,26);var g=OldSrcBase.game_work;
                Check(r.MapDisplayId(26)==1,"Fixture did not borrow protagonist for original animal role");
                Check(g.chara_data.c_varia_dat[26].buki_lv==0,"Native natural-weapon level0 was rejected or reset");
                Check((g.chara_data.ckd_info[26][1]&15)==6,"KeepCanonical mixed hero combat metadata with natural-weapon level0, risking native arms table underflow");
            }),
            ("normal fighter and borrowed canonical role preserve separate progress across repeated handoffs",()=> {
                var r=Start("FixedChosen",1,7,8,9,10,11);var g=OldSrcBase.game_work;
                Check(r.MapDisplayId(11)==6&&g.chara_data.c_varia_dat[11].now_hp==106,"Fixture lacked distinct donor6 initial state");
                g.chara_data.c_varia_dat[11].now_hp=17;g.chara_data.c_varia_dat[11].bogu_eqp[0]=211;
                SetParty(g,1,7,8,9,10,12);r.Tick();
                Check(r.MapDisplayId(12)==6&&g.chara_data.c_varia_dat[12].now_hp==17,"Normal fighter progression did not transfer");
                Check(g.chara_data.c_varia_dat[11].now_hp==111&&g.chara_data.c_varia_dat[11].bogu_eqp[0]==14,"Departing role did not restore its distinct canonical state");
                SetParty(g,7,8,9,10,12,11);r.Tick();
                Check(r.MapDisplayId(11)==1&&g.chara_data.c_varia_dat[11].now_hp==111&&g.chara_data.c_varia_dat[11].bogu_eqp[0]==14,"Borrowed hero reused prior donor6 stats/equipment");
                g.chara_data.c_varia_dat[11].now_hp=29;g.chara_data.c_varia_dat[11].bogu_eqp[0]=217;
                SetParty(g,1,7,8,9,10,11);r.Tick();
                Check(r.MapDisplayId(11)==6&&g.chara_data.c_varia_dat[11].now_hp==17&&g.chara_data.c_varia_dat[11].bogu_eqp[0]==211,"Returning normal fighter lost its separate record");
                SetParty(g,1,7,8,9,10,12);r.Tick();SetParty(g,7,8,9,10,12,11);r.Tick();
                Check(r.MapDisplayId(11)==1&&g.chara_data.c_varia_dat[11].now_hp==29&&g.chara_data.c_varia_dat[11].bogu_eqp[0]==217,"Borrowed canonical progress was reset on second interval");
                Check(g.chara_data.c_varia_dat[12].now_hp==17&&g.chara_data.c_varia_dat[1].now_hp==101,"Borrowing contaminated donor6 or canonical hero state");
            }),
            ("fixed borrowed progress persists across an actual binding save/load",()=> {
                var r=Start("FixedChosen",7,8,9,10,11,12);var g=OldSrcBase.game_work;Check(r.MapDisplayId(12)==1,"Fixture lacked borrowed hero");
                g.chara_data.c_varia_dat[12].now_hp=37;g.chara_data.c_varia_dat[12].bogu_eqp[0]=231;
                r.BeginSave();r.BindSnapshot("borrowed-save");var loaded=Gsd2NativeData.Characters(g.chara_data);var names=Gsd2NativeData.Names(g.c_teg_dat.name);var arms=Gsd2NativeData.Arms(g.arms_data);r.EndSave();
                OldSrcBase.game_work=Fixture(7,8,9,10,11,12);g=OldSrcBase.game_work;g.chara_data=loaded;g.c_teg_dat.name=names;g.arms_data=arms;
                Gsd2Plugin.Mode="Shuffle";Gsd2Plugin.Seed="new-config";var restored=new Gsd2Runtime();restored.PrepareLoad("borrowed-save");restored.CompleteLoad(true);
                Check(restored.MapDisplayId(12)==1&&g.chara_data.c_varia_dat[12].now_hp==37&&g.chara_data.c_varia_dat[12].bogu_eqp[0]==231,"Borrowed canonical state did not survive load");
                SetParty(g,1,7,8,9,10,11);restored.Tick();SetParty(g,7,8,9,10,11,12);restored.Tick();
                Check(g.chara_data.c_varia_dat[12].now_hp==37&&g.chara_data.c_varia_dat[12].bogu_eqp[0]==231,"Loaded borrowed state failed later handoff");
            }),
            ("return to title retires an active projection, preserves progress, and admits one fresh new game",()=> {
                var r=Start();var g=OldSrcBase.game_work;int role=r.Profile.Assignments.First(p=>p.Key!=p.Value).Key;
                g.chara_data.c_varia_dat[role].now_hp=23;g.chara_data.c_varia_dat[role].level=61;g.chara_data.c_varia_dat[role].bogu_eqp[0]=233;g.chara_data.suki_value[7]=88;
                Gsd2Graphics.IsReady=true;Check(r.IsActive&&r.MapDisplayId(role)!=role,"Fixture was not an active mapped campaign");
                r.CancelPendingNewGame();
                Check(!r.IsAdmitted&&!r.TablesReady&&!r.IsActive&&!r.HoldNativeUpdate&&r.MapDisplayId(role)==role,"Return to title retained campaign admission, mappings or chapter gate");
                Check(g.chara_data.ckd_info[role][0]==role&&g.chara_data.c_kotei_dat[role].seicyo_type[0]==role,"Return to title retained donor character templates");
                Check(g.c_teg_dat.name[role]==10000+role&&g.arms_data[role].name_1==20000+role&&g.arms_data[role].info[0]==role,"Return to title retained donor name/weapon tables");
                Check(g.chara_data.c_varia_dat[role].now_hp==23&&g.chara_data.c_varia_dat[role].level==61&&g.chara_data.c_varia_dat[role].bogu_eqp[0]==233&&g.chara_data.suki_value[7]==88,"Return to title reset earned combat or friendship state");
                r.CancelPendingNewGame();Check(!r.IsAdmitted&&!r.HoldNativeUpdate,"Repeated title retirement was not harmless");
                Gsd2Plugin.Seed="fresh-after-title";r.BeginNewGame();OldSrcBase.game_work=Fixture(1,80);r.ObserveNativeDataset();r.CompleteNativeNewGame();g=OldSrcBase.game_work;
                var pair=r.Profile.Assignments.First(p=>p.Key!=p.Value);Check(r.Profile.Seed=="fresh-after-title"&&g.chara_data.c_varia_dat[pair.Key].now_hp==100+pair.Value,"Subsequent new game retained old campaign progression or mapping");
                g.chara_data.c_varia_dat[pair.Key].now_hp=41;r.CompleteNativeNewGame();Check(g.chara_data.c_varia_dat[pair.Key].now_hp==41,"Fresh new game initialized donor starts more than once");
            }),
            ("unbound saves leave native data untouched and wrong-build/profile bindings are rejected",()=> {
                OldSrcBase.game_work=Fixture(1,80);var g=OldSrcBase.game_work;string before=Statics(g.chara_data)+Variables(g.chara_data)+Arms(g);var ptr=g.chara_data.Pointer;
                var native=new Gsd2Runtime();native.PrepareLoad("ordinary-save");native.CompleteLoad(true);Check(!native.IsAdmitted&&g.chara_data.Pointer==ptr&&before==Statics(g.chara_data)+Variables(g.chara_data)+Arms(g),"Unbound save changed");
                foreach(string field in new[]{"Build","ProfileFingerprint","Game","SaveIdentity","StartPoint"}) {
                    var r=Start();r.BeginSave();string json="invalid-"+field;r.BindSnapshot(json);r.EndSave();string path=BindingFile(json);var node=JsonNode.Parse(File.ReadAllText(path))!;node[field]="invalid";File.WriteAllText(path,node.ToJsonString());
                    var load=new Gsd2Runtime();Reject<InvalidDataException>(()=>load.PrepareLoad(json));
                }
            }),
            ("camp legacy completion defers until strict final boundary before donor publication",()=> {
                var r=PendingCamp();var g=OldSrcBase.game_work;var original=g.chara_data;var names=g.c_teg_dat.name;var arms=g.arms_data;string vars=Variables(original);bool beforeProjection=false;
                Gsd2CampStart.BeforeApply=world=> {
                    beforeProjection=ReferenceEquals(world.chara_data,original)&&ReferenceEquals(world.c_teg_dat.name,names)&&ReferenceEquals(world.arms_data,arms)&&Variables(world.chara_data)==vars&&Gsd2CampNativeGuard.FreshChecks==1&&Gsd2RejectedSaveMirrorGuard.EnsureCalls==1;
                };
                r.CompleteNativeNewGame();r.Tick();Check(!r.TablesReady&&Gsd2CampStart.Applications==0&&!r.NeedsCampInitialSavePosition&&ReferenceEquals(g.chara_data,original),"Legacy helper admitted the camp before options/import completed");
                Reject<InvalidOperationException>(()=>r.BeginSave());
                r.CompleteNativeNewGame(true);Check(beforeProjection&&Gsd2CampStart.Applications==1&&r.TablesReady&&r.NeedsCampInitialSavePosition,"Strict final boundary did not apply camp before projection");
                var pair=r.Profile.Assignments.First(p=>p.Key!=p.Value);Check(g.chara_data.c_varia_dat[pair.Key].now_hp==original.c_varia_dat[pair.Value].now_hp&&Variables(original)==vars,"Camp initialization copied a played capture or mutated original starts");
            }),
            ("strict camp completion and initial binding are one shot and preserve earned progress",()=> {
                var r=PendingCamp();Reject<InvalidOperationException>(()=>r.MarkCampInitialSnapshotBound());r.CompleteNativeNewGame(true);var g=OldSrcBase.game_work;int role=r.Profile.Assignments.First(p=>p.Key!=p.Value).Key;
                Check(r.HoldNativeUpdate&&!r.IsActive&&r.NeedsCampInitialSavePosition,"Camp resource/initial binding state was not armed");
                g.chara_data.c_varia_dat[role].now_hp=24;g.chara_data.c_varia_dat[role].bogu_eqp[0]=238;r.MarkCampInitialSnapshotBound();
                Check(!r.NeedsCampInitialSavePosition,"Camp initial binding was not consumed");Reject<InvalidOperationException>(()=>r.MarkCampInitialSnapshotBound());
                r.CompleteNativeNewGame(true);r.CompleteNativeNewGame();r.Tick();
                Check(Gsd2CampStart.Applications==1&&Gsd2CampNativeGuard.FreshChecks==1&&g.chara_data.c_varia_dat[role].now_hp==24&&g.chara_data.c_varia_dat[role].bogu_eqp[0]==238,"Camp completion repeated or reset progression");
                Gsd2Graphics.IsReady=true;Check(r.IsActive&&!r.HoldNativeUpdate,"Camp resources did not release normal chapter gate");
            }),
            ("bound camp load keeps stored policy and progression without reapplying current config",()=> {
                var r=PendingCamp();r.CompleteNativeNewGame(true);var g=OldSrcBase.game_work;int role=r.Profile.Assignments.First(p=>p.Key!=p.Value).Key;string fingerprint=r.Profile.Fingerprint;
                g.chara_data.c_varia_dat[role].now_hp=21;g.chara_data.c_varia_dat[role].bogu_eqp[0]=239;r.BeginSave();r.BindSnapshot("actual-camp-binding");
                var loaded=Gsd2NativeData.Characters(g.chara_data);var names=Gsd2NativeData.Names(g.c_teg_dat.name);var arms=Gsd2NativeData.Arms(g.arms_data);r.EndSave();r.MarkCampInitialSnapshotBound();
                Gsd2Plugin.StartPoint="Opening";Gsd2Plugin.Mode="FixedChosen";Gsd2Plugin.Companions="invalid";Gsd2Plugin.Seed="ignored-camp-config";
                OldSrcBase.game_work=Fixture(1);g=OldSrcBase.game_work;g.chara_data=loaded;g.c_teg_dat.name=names;g.arms_data=arms;
                var restored=new Gsd2Runtime();restored.PrepareLoad("actual-camp-binding");restored.CompleteLoad(true);restored.CompleteNativeNewGame(true);restored.ObserveCampMap(new MACHICON());restored.VerifyCampPlayer(new MACHICON());
                Check(restored.ActiveStartPoint==Gsd2CampStart.Point&&restored.Profile.Fingerprint==fingerprint&&Gsd2CampStart.Applications==1&&!restored.NeedsCampInitialSavePosition,"Camp save was reinterpreted using current config or reapplied");
                Check(restored.FaultReason is null&&g.chara_data.c_varia_dat[role].now_hp==21&&g.chara_data.c_varia_dat[role].bogu_eqp[0]==239&&Gsd2CampNativeGuard.MapChecks==0,"Camp load retained a new-game spawn guard or reset progression");
            }),
            ("title cancellation disarms pending/applied camp state and permits a fresh campaign",()=> {
                var r=PendingCamp();r.CancelPendingNewGame();r.CompleteNativeNewGame(true);Check(!r.IsAdmitted&&!r.NeedsCampInitialSavePosition&&Gsd2CampStart.Applications==0&&!r.HoldNativeUpdate,"Cancelled pending camp was still admitted");
                OldSrcBase.game_work=Fixture(1);r.BeginNewGame();r.CompleteNativeNewGame(true);var g=OldSrcBase.game_work;int role=r.Profile.Assignments.First(p=>p.Key!=p.Value).Key;g.chara_data.c_varia_dat[role].now_hp=25;
                r.CancelPendingNewGame();Check(!r.IsAdmitted&&!r.NeedsCampInitialSavePosition&&!r.HoldNativeUpdate&&g.chara_data.c_varia_dat[role].now_hp==25&&g.c_teg_dat.name[role]==10000+role&&g.arms_data[role].name_1==20000+role,"Applied camp was not retired canonically");
                r.ObserveCampMap(new MACHICON());r.VerifyCampPlayer(new MACHICON());Check(r.FaultReason is null,"Cancelled camp retained a spawn guard");
                OldSrcBase.game_work=Fixture(1);r.BeginNewGame();r.CompleteNativeNewGame(true);Check(Gsd2CampStart.Applications==2&&r.NeedsCampInitialSavePosition,"Fresh camp campaign could not initialize after title");
            }),
            ("fresh-world guard refusal faults before camp writes or donor publication",()=> {
                var r=PendingCamp();var g=OldSrcBase.game_work;var original=g.chara_data;var names=g.c_teg_dat.name;var arms=g.arms_data;string before=Variables(original)+Statics(original);Gsd2CampNativeGuard.RejectFresh=true;
                Reject<InvalidOperationException>(()=>r.CompleteNativeNewGame(true));Check(r.FaultReason is not null&&r.HoldNativeUpdate&&!r.IsAdmitted&&!r.TablesReady&&Gsd2CampStart.Applications==0&&!r.NeedsCampInitialSavePosition,"Unsafe pre-field context was not rejected");
                Check(ReferenceEquals(g.chara_data,original)&&ReferenceEquals(g.c_teg_dat.name,names)&&ReferenceEquals(g.arms_data,arms)&&before==Variables(original)+Statics(original),"Fresh guard rejection changed native data");
            }),
            ("camp transaction failure cannot publish a partial donor campaign",()=> {
                var r=PendingCamp();var g=OldSrcBase.game_work;var original=g.chara_data;Gsd2CampStart.BeforeApply=_=>throw new InvalidOperationException("injected camp write failure");
                Reject<InvalidOperationException>(()=>r.CompleteNativeNewGame(true));Check(r.FaultReason is not null&&r.HoldNativeUpdate&&!r.TablesReady&&ReferenceEquals(g.chara_data,original)&&!r.NeedsCampInitialSavePosition&&Gsd2CampStart.Applications==0,"Failed camp transaction was published");
            }),
            ("missing mirror protection refuses camp before any world or template publication",()=> {
                var r=PendingCamp();var original=OldSrcBase.game_work.chara_data;Gsd2RejectedSaveMirrorGuard.RejectEnsure=true;
                Reject<InvalidOperationException>(()=>r.CompleteNativeNewGame(true));Check(r.FaultReason is not null&&r.HoldNativeUpdate&&Gsd2CampNativeGuard.FreshChecks==0&&Gsd2CampStart.Applications==0&&!r.TablesReady&&ReferenceEquals(OldSrcBase.game_work.chara_data,original),"Unprotected mirror reached camp transaction");
            }),
            ("camp map and player mismatches fault native dispatch while successful observation retires the guard",()=> {
                var r=PendingCamp();r.CompleteNativeNewGame(true);Gsd2Graphics.IsReady=true;r.VerifyCampPlayer(new MACHICON());Check(r.FaultReason is not null&&r.HoldNativeUpdate&&!r.IsActive,"Unobserved camp map reached field input");
                ResetCampCollaborators();r=PendingCamp();r.CompleteNativeNewGame(true);Gsd2Graphics.IsReady=true;Gsd2CampNativeGuard.RejectMap=true;r.ObserveCampMap(new MACHICON());Check(r.FaultReason is not null&&r.HoldNativeUpdate,"Map mismatch did not fault admission");
                ResetCampCollaborators();r=PendingCamp();r.CompleteNativeNewGame(true);var map=new MACHICON();r.ObserveCampMap(map);r.ObserveCampMap(new MACHICON());Check(r.FaultReason is not null&&r.HoldNativeUpdate,"Second initial map was accepted");
                ResetCampCollaborators();r=PendingCamp();r.CompleteNativeNewGame(true);map=new MACHICON();r.ObserveCampMap(map);r.VerifyCampPlayer(new MACHICON());Check(r.FaultReason is not null&&r.HoldNativeUpdate,"Player from a different controller was accepted");
                ResetCampCollaborators();r=PendingCamp();r.CompleteNativeNewGame(true);map=new MACHICON();r.ObserveCampMap(map);Gsd2CampNativeGuard.RejectPlayer=true;r.VerifyCampPlayer(map);Check(r.FaultReason is not null&&r.HoldNativeUpdate,"Player-coordinate mismatch did not fault admission");
                ResetCampCollaborators();r=PendingCamp();r.CompleteNativeNewGame(true);map=new MACHICON();r.ObserveCampMap(map);Gsd2CampNativeGuard.PlayerReady=false;r.VerifyCampPlayer(map);Check(r.FaultReason is null&&Gsd2CampNativeGuard.PlayerChecks==1,"Not-yet-created player was treated as a mismatch");
                Gsd2CampNativeGuard.PlayerReady=true;r.VerifyCampPlayer(map);Gsd2CampNativeGuard.RejectPlayer=true;r.VerifyCampPlayer(new MACHICON());r.ObserveCampMap(new MACHICON());Check(r.FaultReason is null&&Gsd2CampNativeGuard.PlayerChecks==2&&Gsd2CampNativeGuard.MapChecks==1,"Verified first spawn kept intercepting later maps");
            }),
            ("faulted owned save cannot silently admit an unbound snapshot",()=> {
                var r=PendingCamp();r.CompleteNativeNewGame(true);r.BeginSave();r.Fail("injected initial storage refusal");
                Reject<InvalidOperationException>(()=>r.BindSnapshot("owned-faulted-camp-save"));
                Check(!File.Exists(BindingFile("owned-faulted-camp-save")),"Faulted owned save wrote a binding");r.EndSave();
                var unbound=new Gsd2Runtime();Reject<InvalidOperationException>(()=>unbound.BindSnapshot("owned-unbound-save"));
            }),
            ("ordinary opening saves require verified mirror protection before publishing canonical save data",()=> {
                var r=Start();var g=OldSrcBase.game_work;var projected=g.chara_data;var names=g.c_teg_dat.name;var arms=g.arms_data;
                string before=Variables(projected)+Statics(projected);Gsd2RejectedSaveMirrorGuard.RejectEnsure=true;
                Reject<InvalidOperationException>(()=>r.BeginSave());
                Check(Gsd2RejectedSaveMirrorGuard.EnsureCalls==1&&ReferenceEquals(g.chara_data,projected)&&ReferenceEquals(g.c_teg_dat.name,names)&&ReferenceEquals(g.arms_data,arms)&&before==Variables(g.chara_data)+Statics(g.chara_data),"Unprotected Opening save changed the world before refusal");
                Check(!File.Exists(BindingFile("unprotected-opening")),"Unprotected Opening save wrote a binding");
                Gsd2RejectedSaveMirrorGuard.RejectEnsure=false;r.BeginSave();Check(r.BindSnapshot("guarded-opening-save"),"Verified mirror guard did not admit a later Opening save");r.EndSave();
                Check(Gsd2RejectedSaveMirrorGuard.EnsureCalls==2&&ReferenceEquals(g.chara_data,projected),"Ordinary save did not verify protection or restore exact published data");
            }),
            ("foreign ownership never touches native state or admits a campaign",()=> {
                bool allowed=false;long epoch=7;Gsd2Runtime.SetExecutionOwnership(()=>allowed,()=>epoch);
                var r=new Gsd2Runtime();Gsd2Runtime.Current=r;OldSrcBase.RejectGameAccess=true;
                r.BeginNewGame();r.ObserveNativeDataset();r.CompleteNativeNewGame(true);r.CancelPendingNewGame();
                r.PrepareLoad("foreign-json");r.CompleteLoad(false);r.BeginSave();r.EndSave();r.BindSnapshot("foreign-json");
                r.ObserveCampMap(new MACHICON());r.VerifyCampPlayer(new MACHICON());r.Tick();
                Check(!r.OwnsExecution&&!r.IsAdmitted&&!r.TablesReady&&!r.IsActive&&!r.HoldNativeUpdate&&r.FaultReason is null,"Foreign engine was active, faulted, or held native update");
                Check(r.MapDisplayId(28)==28&&!r.IsRole(28),"Foreign presentation projected an ID");
            }),
            ("host suspension retires managed state without foreign native reads or writes",()=> {
                bool allowed=true;long epoch=10;Gsd2Runtime.SetExecutionOwnership(()=>allowed,()=>epoch);
                var r=Start("FixedChosen",1,7);Gsd2Graphics.IsReady=true;var g=OldSrcBase.game_work;
                var projected=g.chara_data;projected.c_varia_dat[7].now_hp=19;var names=g.c_teg_dat.name;var arms=g.arms_data;string before=Variables(projected)+Statics(projected);
                Check(r.IsActive,"Ownership fixture not active");allowed=false;epoch++;OldSrcBase.RejectGameAccess=true;
                int suspends=Gsd2Graphics.SuspendCalls;r.SuspendExecution();r.SuspendExecution();r.Tick();
                Check(Gsd2Graphics.SuspendCalls==suspends+1,"Suspension was not idempotent");
                Check(!r.IsAdmitted&&!r.TablesReady&&!r.IsActive&&!r.HoldNativeUpdate&&r.Profile is null,"Suspension retained active campaign");
                OldSrcBase.RejectGameAccess=false;
                Check(ReferenceEquals(g.chara_data,projected)&&ReferenceEquals(g.c_teg_dat.name,names)&&ReferenceEquals(g.arms_data,arms)&&before==Variables(g.chara_data)+Statics(g.chara_data),"Suspension wrote outgoing native state");
                allowed=true;r.Tick();Check(!r.IsAdmitted,"Re-entry resurrected prior campaign");
                Check(r.ObserveOwnedBoundary()&&!r.IsAdmitted,"Owned title boundary resurrected campaign");
                Check(g.chara_data.Pointer!=projected.Pointer&&g.chara_data.ckd_info[7][0]==7&&g.c_teg_dat.name[7]==10007&&g.arms_data[7].name_1==20007&&g.chara_data.c_varia_dat[7].now_hp==19,"Owned re-entry did not remove matching old static projection while preserving earned state");
                OldSrcBase.game_work=Fixture(1,7);r.BeginNewGame();r.CompleteNativeNewGame();Check(r.IsAdmitted&&r.ExecutionEpoch==epoch,"Owned new game could not initialize a fresh epoch");
            }),
            ("epoch change invalidates admission before the next frame",()=> {
                bool allowed=true;long epoch=20;Gsd2Runtime.SetExecutionOwnership(()=>allowed,()=>epoch);
                var r=Start();Gsd2Graphics.IsReady=true;long oldEpoch=r.ExecutionEpoch;Check(r.MatchesExecution(oldEpoch),"Owned epoch not accepted");
                epoch++;OldSrcBase.RejectGameAccess=true;
                Check(!r.MatchesExecution(oldEpoch)&&!r.IsAdmitted&&!r.IsActive&&!r.HoldNativeUpdate,"Stale epoch remained admitted before Tick");
                r.ObserveNativeDataset();r.CompleteNativeNewGame();r.CompleteLoad(false);r.BeginSave();r.EndSave();r.Tick();
                Check(r.Profile is null&&r.FaultReason is null,"Stale frame did not retire logically");
                OldSrcBase.RejectGameAccess=false;OldSrcBase.game_work=Fixture(1);var fresh=OldSrcBase.game_work;var data=fresh.chara_data;var names=fresh.c_teg_dat.name;var arms=fresh.arms_data;r.PrepareLoad("unknown-new-epoch");r.CompleteLoad(true);
                Check(r.OwnsExecution&&!r.IsAdmitted&&r.ExecutionEpoch==epoch,"Owned native unbound load inherited prior campaign");
                Check(ReferenceEquals(fresh.chara_data,data)&&ReferenceEquals(fresh.c_teg_dat.name,names)&&ReferenceEquals(fresh.arms_data,arms),"Owned re-entry wrote a different new native world");
            }),
            ("ownership loss during canonical save never restores foreign pointers",()=> {
                bool allowed=true;long epoch=30;Gsd2Runtime.SetExecutionOwnership(()=>allowed,()=>epoch);
                var r=Start();r.BeginSave();var g=OldSrcBase.game_work;var canonical=g.chara_data;var names=g.c_teg_dat.name;var arms=g.arms_data;
                allowed=false;epoch++;OldSrcBase.RejectGameAccess=true;r.SuspendExecution();r.EndSave();
                OldSrcBase.RejectGameAccess=false;
                Check(ReferenceEquals(g.chara_data,canonical)&&ReferenceEquals(g.c_teg_dat.name,names)&&ReferenceEquals(g.arms_data,arms),"Foreign save finalization restored projected pointers");
                Check(!r.IsAdmitted&&!r.HoldNativeUpdate,"Suspended save retained campaign admission");
            }),
            ("ownership callbacks may synchronously suspend without recursive native access",()=> {
                bool suspendFromPredicate=false;long epoch=40;Gsd2Runtime? active=null;
                Gsd2Runtime.SetExecutionOwnership(()=>{if(suspendFromPredicate)active?.SuspendExecution();return true;},()=>epoch);
                active=Start();Gsd2Graphics.IsReady=true;suspendFromPredicate=true;OldSrcBase.RejectGameAccess=true;
                Check(!active.OwnsExecution&&!active.IsActive&&!active.HoldNativeUpdate,"Predicate-triggered suspension was ignored or recursive");
            }),
            ("retired campaign save protection survives re-entry until an explicit native campaign boundary",()=> {
                bool allowed=true;long epoch=50;Gsd2Runtime.SetExecutionOwnership(()=>allowed,()=>epoch);
                var r=Start();allowed=false;epoch++;OldSrcBase.RejectGameAccess=true;r.SuspendExecution();r.SuspendExecution();
                Check(r.Profile is null&&r.HasAdmittedCampaignBeforeSuspend&&r.HasCampaignSaveProtection,"Suspension discarded the managed save-protection marker");
                allowed=true;OldSrcBase.RejectGameAccess=false;
                Check(r.ObserveOwnedBoundary()&&!r.IsAdmitted&&r.HasCampaignSaveProtection,"Re-entry silently readmitted or forgot the retired campaign");
                r.CancelPendingNewGame();Check(!r.HasCampaignSaveProtection&&!r.HasAdmittedCampaignBeforeSuspend,"Native title cancellation retained a stale campaign marker");
                r.BeginNewGame();r.CompleteNativeNewGame();r.SuspendExecution();
                OldSrcBase.game_work=Fixture(1);r.PrepareLoad("explicit-native-unbound-save");r.CompleteLoad(true);
                Check(!r.IsAdmitted&&!r.HasCampaignSaveProtection,"An explicit native unbound load retained prior campaign protection");
                r.BeginNewGame();r.CompleteNativeNewGame();r.SuspendExecution();OldSrcBase.game_work=Fixture(1);r.BeginNewGame();r.CompleteNativeNewGame();
                Check(r.IsAdmitted&&!r.HasAdmittedCampaignBeforeSuspend,"Fresh New Game retained the old suspension marker");
                var native=new Gsd2Runtime();native.SuspendExecution();Check(!native.HasCampaignSaveProtection,"Vanilla native suspension invented a campaign marker");
            }),
            ("loaded-root router preserves one campaign across battle active-scene hops and revokes real game changes",()=> {
                var router=new ActiveGameRouter();var token=router.ObserveGameContext(ActiveGame.Gsd2,false,true);
                Gsd2Runtime.SetExecutionOwnership(()=>router.CurrentGame==ActiveGame.Gsd2,()=>router.Generation);
                int newGames=Gsd2Plugin.PluginLog.Lines.Count(line=>line.StartsWith("NEW GSD2 campaign ",StringComparison.Ordinal));
                var r=Start();Gsd2Graphics.IsReady=true;var g=OldSrcBase.game_work;var profile=r.Profile;string fingerprint=profile.Fingerprint;
                int role=profile.Assignments.First(pair=>pair.Key!=pair.Value).Key;
                var projected=g.chara_data;var names=g.c_teg_dat.name;var arms=g.arms_data;
                projected.c_varia_dat[role].now_hp=31;projected.c_varia_dat[role].exp=12345;projected.c_varia_dat[role].bogu_eqp[0]=234;
                string vars=Variables(projected);var fixedRows=Statics(projected);string weaponRows=Arms(g);string startPoint=r.ActiveStartPoint;
                foreach(string activeScene in new[]{"GSD2","gsd2-hd-battle-bb00","gsd2-hd-battle-bb01","GSD2","gsd2-hd-battle-bb00","GSD2"}) {
                    // Host admission deliberately depends on selected mode and loaded roots;
                    // these active-scene labels are observations, never classifier inputs.
                    var next=router.ObserveGameContext(ActiveGame.Gsd2,false,true);
                    Check(next==token&&router.IsCurrent(token),"Battle active scene changed the ownership generation: "+activeScene);
                    r.Tick();r.CompleteNativeNewGame();
                    Check(r.IsActive&&ReferenceEquals(r.Profile,profile)&&r.Profile.Fingerprint==fingerprint&&r.ActiveStartPoint==startPoint,"Battle hop lost its admitted profile or starting policy");
                    Check(ReferenceEquals(g.chara_data,projected)&&ReferenceEquals(g.c_teg_dat.name,names)&&ReferenceEquals(g.arms_data,arms)&&Variables(g.chara_data)==vars&&Statics(g.chara_data)==fixedRows&&Arms(g)==weaponRows,"Battle hop reset starts, equipment, or earned progression");
                }
                Check(Gsd2Plugin.PluginLog.Lines.Count(line=>line.StartsWith("NEW GSD2 campaign ",StringComparison.Ordinal))==newGames+1,"Battle hop created another New Game");
                r.BeginSave();Check(r.BindSnapshot("battle-context-exact-native-save"),"Stable battle context refused an owned binding");r.EndSave();
                Check(File.Exists(BindingFile("battle-context-exact-native-save"))&&Variables(g.chara_data)==vars,"Stable context lost binding or earned state during save");
                r.BeginSave();long oldEpoch=r.ExecutionEpoch;router.ObserveGameContext(ActiveGame.Gsd1,true,true);
                OldSrcBase.RejectGameAccess=true;
                Check(!router.IsCurrent(token)&&!r.MatchesExecution(oldEpoch)&&!r.BindSnapshot("foreign-old-scope-save"),"Real mode change retained an old callback or save lease");
                r.SuspendExecution();r.EndSave();Check(r.Profile is null&&r.HasAdmittedCampaignBeforeSuspend&&!r.IsActive,"Real handoff did not retire the old campaign");
                Check(!File.Exists(BindingFile("foreign-old-scope-save")),"Foreign old scope wrote a binding");
                router.ObserveGameContext(ActiveGame.Gsd2,true,true);r.Tick();
                Check(!r.IsAdmitted&&r.HasCampaignSaveProtection,"Returning mode automatically revived a stale campaign");
            }),
            ("record validation rejects malformed native dimensions before restore",()=> {
                var r=Start("FixedChosen",1,7);r.BeginSave();r.BindSnapshot("bad-fighter");r.EndSave();var path=BindingFile("bad-fighter");var node=JsonNode.Parse(File.ReadAllText(path))!;node["Fighters"]!["2"]!["MagicPoints"]="AA==";File.WriteAllText(path,node.ToJsonString());
                Reject<InvalidOperationException>(()=>new Gsd2Runtime().PrepareLoad("bad-fighter"));
                var record=Gsd2FighterRecord.Capture(Fixture().chara_data.c_varia_dat[2]);record.Level=0;Reject<InvalidOperationException>(()=>record.CreateNative());
            })
        };
        tests=tests.Concat(RiouWeaponChecks()).ToArray();
        int failed=0;
        foreach(var test in tests) { try { ResetCampCollaborators();test.Run();Console.WriteLine("PASS "+test.Name); } catch(Exception exception) { failed++;Console.WriteLine("FAIL "+test.Name+": "+exception.Message); } finally { C_VARIA_DAT.BeforeWeaponWrite=null;OldSrcBase.RejectGameAccess=false;Gsd2Runtime.SetExecutionOwnership(()=>true,()=>0); } }
        Console.WriteLine($"{tests.Length-failed}/{tests.Length} actual-linked Suikoden II runtime checks passed.");
        if(failed!=0)throw new Exception(failed+" linked runtime regressions failed.");
    }
}
