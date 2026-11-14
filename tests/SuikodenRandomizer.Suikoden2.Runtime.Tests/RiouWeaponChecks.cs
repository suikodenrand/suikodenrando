using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using SuikodenRandomizer.Suikoden2Plugin;

internal static partial class Program
{
    private static Gsd2Runtime RiouPending(string seed, string mode="Shuffle", bool include=false, params byte[] roles)
    {
        Gsd2Plugin.JoiningWeaponLevel="RiouVariance";Gsd2Plugin.Seed=seed;Gsd2Plugin.Mode=mode;
        Gsd2Plugin.IncludeProtagonist=include;Gsd2Plugin.Companions="2,3,4,5,6";Gsd2Plugin.StartPoint="Opening";
        OldSrcBase.game_work=Fixture(roles.Length==0?new byte[]{1}:roles);
        var runtime=new Gsd2Runtime();Gsd2Runtime.Current=runtime;runtime.BeginNewGame();runtime.ObserveNativeDataset();return runtime;
    }
    private static int[] ArmedRoles(Gsd2Runtime runtime) => runtime.Profile.Assignments
        .Where(pair=>pair.Key!=1&&pair.Value!=1&&pair.Value!=26&&pair.Key<80).Select(pair=>pair.Key).ToArray();
    private static byte ExpectedRiou(Gsd2Runtime runtime,int role,int reference) =>
        (byte)Math.Clamp(reference+runtime.JoiningWeaponDelta(role,runtime.Profile.GetDonor(role)),1,16);
    private static IEnumerable<(string Name,Action Run)> RiouWeaponChecks()
    {
        yield return ("Riou variance defers shuffle until first party join and leaves resident native rows stable",()=> {
            var r=RiouPending("riou-late-joins");var g=OldSrcBase.game_work;int[] roles=ArmedRoles(r);
            int first=roles[0],second=roles[1];var native=g.chara_data;
            byte originalFirst=native.c_varia_dat[r.Profile.GetDonor(first)].buki_lv;
            r.CompleteNativeNewGame();
            Check(g.chara_data.c_varia_dat[first].buki_lv==originalFirst,"Unused shuffle role normalized at New Game");
            var published=g.chara_data;var row=published.c_varia_dat[first];var arms=g.arms_data;var names=g.c_teg_dat.name;
            var expected=Gsd2FighterRecord.Capture(row);expected.WeaponLevel=ExpectedRiou(r,first,9);
            g.chara_data.c_varia_dat[1].buki_lv=9;SetParty(g,1,(byte)first);r.Tick();
            Check(JsonSerializer.Serialize(Gsd2FighterRecord.Capture(row))==JsonSerializer.Serialize(expected),"First join did not use current Riou9 or altered other fighter data");
            Check(ReferenceEquals(g.chara_data,published)&&ReferenceEquals(g.chara_data.c_varia_dat[first],row)
                &&ReferenceEquals(g.arms_data,arms)&&ReferenceEquals(g.c_teg_dat.name,names),"Joining replaced native tables/actor rows");
            row.buki_lv=15;g.chara_data.c_varia_dat[1].buki_lv=3;SetParty(g,1);r.Tick();SetParty(g,1,(byte)first,(byte)second);r.Tick();
            Check(row.buki_lv==15&&g.chara_data.c_varia_dat[second].buki_lv==ExpectedRiou(r,second,3),"Rejoining reset sharpening or late recruit ignored current Riou3");
            Check(native.c_varia_dat[r.Profile.GetDonor(first)].buki_lv==originalFirst,"Imported native donor data mutated");
        });
        yield return ("Riou variance starting party clamps low and high weapons and includes unbiased zero offset",()=> {
            foreach(int reference in new[]{1,16}) {
                var r=RiouPending("riou-clamps");var g=OldSrcBase.game_work;int[] roles=ArmedRoles(r);
                Check(roles.Select(role=>r.JoiningWeaponDelta(role,r.Profile.GetDonor(role))).ToHashSet().SetEquals(Enumerable.Range(-3,7)),"Variance did not cover all seven offsets including zero");
                int negative=roles.First(role=>r.JoiningWeaponDelta(role,r.Profile.GetDonor(role))<0);
                int positive=roles.First(role=>r.JoiningWeaponDelta(role,r.Profile.GetDonor(role))>0);
                int zero=roles.First(role=>r.JoiningWeaponDelta(role,r.Profile.GetDonor(role))==0);
                g.chara_data.c_varia_dat[1].buki_lv=(byte)reference;SetParty(g,1,(byte)negative,(byte)positive,(byte)zero);r.CompleteNativeNewGame();
                foreach(int role in new[]{negative,positive,zero}) Check(g.chara_data.c_varia_dat[role].buki_lv==ExpectedRiou(r,role,reference),"Starting party did not use native Riou reference/clamp");
                Check(g.chara_data.c_varia_dat[zero].buki_lv==reference,"Zero offset did not match Riou");
                Check(g.chara_data.c_varia_dat[reference==1?negative:positive].buki_lv==reference,"Variance escaped native1..16 range");
            }
        });
        yield return ("Riou variance offsets are deterministic per seed and independent of iteration order",()=> {
            var first=RiouPending("riou-offset-seed");var offsets=ArmedRoles(first).ToDictionary(role=>role,role=>first.JoiningWeaponDelta(role,first.Profile.GetDonor(role)));
            foreach(int role in offsets.Keys.Reverse()) Check(offsets[role]==first.JoiningWeaponDelta(role,first.Profile.GetDonor(role)),"Unrelated offset requests advanced the random stream");
            var again=RiouPending("riou-offset-seed");Check(offsets.All(pair=>pair.Value==again.JoiningWeaponDelta(pair.Key,again.Profile.GetDonor(pair.Key))),"Same seed rerolled offsets");
            var different=RiouPending("riou-offset-other");Check(offsets.Any(pair=>pair.Value!=different.JoiningWeaponDelta(pair.Key,different.Profile.GetDonor(pair.Key))),"Different seed did not change any offset");
        });
        yield return ("Riou variance protects protagonist combat setups and natural weapons when appearances are shuffled",()=> {
            var r=RiouPending("riou-protagonist-reference",include:true);var g=OldSrcBase.game_work;var native=g.chara_data;
            Check(r.Profile.GetDonor(1)!=1,"Fixture did not shuffle protagonist appearance");
            int normal=ArmedRoles(r)[0],natural=r.Profile.Assignments.Single(pair=>pair.Value==26).Key;
            native.c_varia_dat[1].buki_lv=5;native.c_varia_dat[r.Profile.GetDonor(1)].buki_lv=14;
            SetParty(g,1,(byte)normal,(byte)natural);r.CompleteNativeNewGame();
            Check(r.MapDisplayId(1)!=1&&g.chara_data.c_varia_dat[1].buki_lv==5,"Shuffled protagonist lost native weapon setup");
            Check(g.chara_data.c_varia_dat[normal].buki_lv==ExpectedRiou(r,normal,5),"Variance referenced shuffled donor rather than canonical Riou");
            Check(g.chara_data.c_varia_dat[natural].buki_lv==(natural==1?native.c_varia_dat[1].buki_lv:0),"Natural weapon semantics changed");
            int protectedRole=r.Profile.Assignments.Single(pair=>pair.Value==1).Key;SetParty(g,1,(byte)protectedRole);r.Tick();
            Check(g.chara_data.c_varia_dat[protectedRole].buki_lv==native.c_varia_dat[protectedRole].buki_lv,"Riou donor lost protected story-role weapon setup");
        });
        yield return ("Riou variance saves only initialized shuffle donors and preserves upgrades while normalizing pre-save late joins",()=> {
            var r=RiouPending("riou-save-seed");var g=OldSrcBase.game_work;r.CompleteNativeNewGame();int[] roles=ArmedRoles(r);int first=roles[0],second=roles[1];
            g.chara_data.c_varia_dat[1].buki_lv=6;SetParty(g,1,(byte)first);r.Tick();g.chara_data.c_varia_dat[first].buki_lv=15;
            r.BeginSave();r.BindSnapshot("riou-variance-save");var saved=Gsd2NativeData.Characters(g.chara_data);r.EndSave();
            var binding=JsonNode.Parse(File.ReadAllText(BindingFile("riou-variance-save")))!;
            Check(binding["JoiningWeaponLevel"]!.GetValue<string>()=="RiouVariance"
                &&binding["JoiningWeaponInitializedDonors"]!.AsArray().Count==1,"Partial shuffle joining state was not saved");
            Gsd2Plugin.JoiningWeaponLevel="Level1";OldSrcBase.game_work=Fixture(1,(byte)first);g=OldSrcBase.game_work;g.chara_data=saved;
            var loaded=new Gsd2Runtime();loaded.PrepareLoad("riou-variance-save");loaded.CompleteLoad(true);
            Check(loaded.ActiveJoiningWeaponLevel=="RiouVariance"&&g.chara_data.c_varia_dat[first].buki_lv==15,"Reload changed policy or erased sharpening");
            g.chara_data.c_varia_dat[1].buki_lv=10;SetParty(g,1,(byte)first,(byte)second);loaded.BeginSave();loaded.BindSnapshot("riou-variance-late-save");loaded.EndSave();
            Check(g.chara_data.c_varia_dat[second].buki_lv==ExpectedRiou(loaded,second,10),"Immediate save before Tick omitted late recruit normalization");
            Check(JsonNode.Parse(File.ReadAllText(BindingFile("riou-variance-late-save")))!["JoiningWeaponInitializedDonors"]!.AsArray().Count==2,"Immediate save omitted newly initialized donor");
        });
        yield return ("Riou variance fixed chosen and random rosters initialize unused fighters at their actual first assignment",()=> {
            foreach(string mode in new[]{"FixedChosen","FixedRandom"}) {
                var r=RiouPending("riou-fixed-late",mode);var g=OldSrcBase.game_work;r.CompleteNativeNewGame();
                int first=r.Profile.CompanionIds[0],second=r.Profile.CompanionIds[1];
                Check(r.JoiningWeaponDelta(7,first)==r.JoiningWeaponDelta(9,first),"Fixed variance depended on story role");
                g.chara_data.c_varia_dat[1].buki_lv=4;SetParty(g,1,7);r.Tick();
                byte expected=first==26?(byte)0:(byte)Math.Clamp(4+r.JoiningWeaponDelta(7,first),1,16);
                Check(r.MapDisplayId(7)==first&&g.chara_data.c_varia_dat[7].buki_lv==expected,"Fixed first assignment ignored current Riou4");
                g.chara_data.c_varia_dat[7].buki_lv=first==26?(byte)0:(byte)15;g.chara_data.c_varia_dat[1].buki_lv=12;SetParty(g,1,8);r.Tick();
                Check(g.chara_data.c_varia_dat[8].buki_lv==(first==26?(byte)0:(byte)15),"Fixed move rerolled initialized weapon");
                SetParty(g,1,8,9);r.Tick();expected=second==26?(byte)0:(byte)Math.Clamp(12+r.JoiningWeaponDelta(9,second),1,16);
                Check(r.MapDisplayId(9)==second&&g.chara_data.c_varia_dat[9].buki_lv==expected,"Unused fixed fighter ignored late Riou12");
            }
        });
        yield return ("Riou default does not override old MatchRole or Level1 saved campaigns",()=> {
            foreach(string policy in new[]{"MatchRole","Level1"}) {
                var r=RiouPending("old-policy-"+policy,"FixedChosen");Gsd2Plugin.JoiningWeaponLevel=policy;r.BeginNewGame();r.ObserveNativeDataset();r.CompleteNativeNewGame();
                var g=OldSrcBase.game_work;g.chara_data.c_varia_dat[1].buki_lv=11;SetParty(g,1,7);r.Tick();g.chara_data.c_varia_dat[7].buki_lv=14;
                r.BeginSave();r.BindSnapshot("old-policy-"+policy);var saved=Gsd2NativeData.Characters(g.chara_data);r.EndSave();
                Gsd2Plugin.JoiningWeaponLevel="RiouVariance";OldSrcBase.game_work=Fixture(1,7);g=OldSrcBase.game_work;g.chara_data=saved;
                var loaded=new Gsd2Runtime();loaded.PrepareLoad("old-policy-"+policy);loaded.CompleteLoad(true);
                Check(loaded.ActiveJoiningWeaponLevel==policy&&g.chara_data.c_varia_dat[7].buki_lv==14,"New default replaced old campaign policy/sharpening");
                g.chara_data.c_varia_dat[8].buki_lv=5;SetParty(g,1,7,8);loaded.Tick();
                Check(g.chara_data.c_varia_dat[8].buki_lv==(policy=="Level1"?(byte)1:(byte)5),"Old campaign late join adopted new Riou default");
            }
        });
        yield return ("Riou variance refuses unavailable reference and rolls back a failed group weapon write",()=> {
            var r=RiouPending("riou-invalid-reference");var g=OldSrcBase.game_work;r.CompleteNativeNewGame();int[] roles=ArmedRoles(r);
            int first=roles[0],second=roles[1];g.chara_data.c_varia_dat[1].buki_lv=0;SetParty(g,1,(byte)first);
            byte old=g.chara_data.c_varia_dat[first].buki_lv;r.Tick();Check(r.FaultReason is not null&&g.chara_data.c_varia_dat[first].buki_lv==old,"Invalid Riou reference changed weapon state");
            r=RiouPending("riou-write-rollback");g=OldSrcBase.game_work;r.CompleteNativeNewGame();roles=ArmedRoles(r);first=roles[0];second=roles[1];
            g.chara_data.c_varia_dat[1].buki_lv=10;var a=g.chara_data.c_varia_dat[first];var b=g.chara_data.c_varia_dat[second];a.buki_lv=16;b.buki_lv=16;
            string before=Variables(g.chara_data);bool injected=false;
            C_VARIA_DAT.BeforeWeaponWrite=(row,value)=> {if(ReferenceEquals(row,b)&&value!=16&&!injected){injected=true;throw new InvalidOperationException("injected native write failure");}};
            SetParty(g,1,(byte)first,(byte)second);r.Tick();C_VARIA_DAT.BeforeWeaponWrite=null;
            Check(injected&&r.FaultReason is not null&&Variables(g.chara_data)==before,"Failed native write left a partial party weapon update");
        });
        yield return ("Riou save validation rejects missing initialization for an already resident fighter",()=> {
            var r=RiouPending("riou-binding-completeness");var g=OldSrcBase.game_work;r.CompleteNativeNewGame();int role=ArmedRoles(r)[0];SetParty(g,1,(byte)role);r.Tick();
            r.BeginSave();r.BindSnapshot("riou-missing-resident");var saved=Gsd2NativeData.Characters(g.chara_data);r.EndSave();
            string path=BindingFile("riou-missing-resident");var binding=JsonNode.Parse(File.ReadAllText(path))!;binding["JoiningWeaponInitializedDonors"]=new JsonArray();File.WriteAllText(path,binding.ToJsonString());
            OldSrcBase.game_work=Fixture(1,(byte)role);OldSrcBase.game_work.chara_data=saved;var loaded=new Gsd2Runtime();loaded.PrepareLoad("riou-missing-resident");
            Reject<InvalidDataException>(()=>loaded.CompleteLoad(true));
        });
    }
}
