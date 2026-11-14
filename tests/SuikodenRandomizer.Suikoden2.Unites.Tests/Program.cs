// Links actual production catalog. Native objects are managed metadata-shaped doubles.
// expected-native.json comes from a fresh independent read of the original asset;
// tests never derive their expected values from the production catalog.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using SuikodenRandomizer.Suikoden2Plugin;

internal static partial class Program
{
    private sealed record Expected(int Index,string Name,Dictionary<string,int> Scalars,byte[] Members);
    private static readonly Expected[] Facts=ReadFacts();
    private static int _passed;
    private static void Check(bool condition,string message)
    {if(!condition)throw new InvalidOperationException(message);}
    private static void Case(string title,Action action)
    {action();_passed++;Console.WriteLine("PASS "+title);}
    private static Exception Throws(Action action)
    {
        try{action();}catch(Exception error){return error;}
        throw new InvalidOperationException("Expected operation to fail.");
    }
    private static Expected[] ReadFacts()
    {
        using var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("SuikodenRandomizer.Suikoden2.Unites.Tests.expected-native.json")!;
        using var doc=JsonDocument.Parse(stream);
        return doc.RootElement.EnumerateArray().Select(r=>new Expected(r.GetProperty("index").GetInt32(),
            r.GetProperty("name").GetString()!,r.GetProperty("raw").EnumerateObject().Where(x=>x.Name!="player_id").ToDictionary(x=>x.Name,x=>x.Value.GetInt32()),
            r.GetProperty("raw").GetProperty("player_id").EnumerateArray().Select(x=>x.GetByte()).ToArray())).ToArray();
    }
    private static UNITE_DATA Native(Expected e)
    {
        var native=new UNITE_DATA{player_id=new Il2CppStructArray<byte>(e.Members.ToArray())};
        foreach(var (name,value) in e.Scalars)
        {
            var property=typeof(UNITE_DATA).GetProperty(name)!;
            property.SetValue(native,Convert.ChangeType(value,property.PropertyType));
        }
        return native;
    }
    private static Gsd2UniteRecipe Definition(Expected e)
    {
        Check(Gsd2UniteRecipes.TryGet(e.Index,out var recipe)&&recipe is not null,"Missing native recipe "+e.Index);
        return recipe!;
    }
    private static void CatalogChecks()
    {
        Case("Catalog preserves original51 slots and disabled sentinel five-byte member array",()=>{
            Check(Gsd2UniteRecipes.OriginalCount==51&&Gsd2UniteRecipes.All.Count==51&&Facts.Length==51,"Wrong native slot count.");
            var zero=Gsd2UniteRecipes.All[0];
            Check(zero.Index==0&&zero.IsDisabled&&zero.Count==0&&zero.RawMembers.SequenceEqual(new byte[5]),"Disabled native record changed.");
            foreach(int invalid in new[]{-1,51,100,int.MinValue,int.MaxValue})
                Check(!Gsd2UniteRecipes.TryGet(invalid,out var missing)&&missing is null,"Out-of-range recipe resolved.");
            Check(Throws(()=>((IList<Gsd2UniteRecipe>)Gsd2UniteRecipes.All)[0]=Gsd2UniteRecipes.All[1]) is NotSupportedException,"Catalog list is mutable.");
        });
        foreach(var e in Facts)Case("Exact original scalar/ordered-byte metadata for S2 recipe "+e.Index+" "+e.Name,()=>{
            var r=Definition(e);var native=Native(e);
            Check(r.Index==e.Index&&r.Name==e.Name&&r.Count==e.Scalars["player_kazu"],"Recipe label/count mismatch.");
            Check(r.NameId==e.Scalars["name"]&&r.TextId==e.Scalars["text"]&&r.Info==e.Scalars["info"]&&r.Attribute==e.Scalars["atrb"],"Name/target metadata mismatch.");
            Check(r.Function==e.Scalars["func_no"]&&r.Hosei==e.Scalars["hosei"],"Function/correction mismatch.");
            Check(r.MonsterStatus==e.Scalars["m_status"]&&r.MonsterStatusProbability==e.Scalars["m_status_prob"]
                &&r.PlayerStatus==e.Scalars["p_status"]&&r.PlayerStatusProbability==e.Scalars["p_status_prob"],"Status metadata mismatch.");
            Check(r.RawMembers.SequenceEqual(e.Members),"Native encoded participant order differs.");
            Check(r.DonorIds.SequenceEqual(e.Members.Select(x=>(int)(x&127)))&&r.HighBitFlags.SequenceEqual(e.Members.Select(x=>(byte)(x&128))),"Encoded donor/flag split differs.");
            Check(r.MatchesOriginal(native)&&r.MatchesOriginalMetadata(native)&&r.MatchesStable(native)&&r.MatchesStableMetadata(native),"Original data failed catalog validation.");
            Check(r.IsUpgradeOnly==(e.Index==40)&&r.HasNativeSpecialEligibility==(e.Index==19),"Special native recipe policy differs.");
        });
        Case("Every original scalar mutation is detected; stable validation permits only three reviewed derived fields",()=>{
            var mutable=new HashSet<string>{"hosei","m_status","m_status_prob"};
            foreach(var e in Facts)foreach(var (name,value) in e.Scalars)
            {
                var native=Native(e);var property=typeof(UNITE_DATA).GetProperty(name)!;
                property.SetValue(native,Convert.ChangeType(value+1,property.PropertyType));var r=Definition(e);
                Check(!r.MatchesOriginal(native)&&!r.MatchesOriginalMetadata(native),"Changed original field accepted: "+e.Index+"/"+name);
                Check(r.MatchesStable(native)==mutable.Contains(name)&&r.MatchesStableMetadata(native)==mutable.Contains(name),"Stable validation ignored wrong field: "+e.Index+"/"+name);
            }
        });
        Case("Native derived correction/monster-status values stay live without mutating catalog defaults",()=>{
            foreach(var e in Facts.Skip(1))
            {
                var r=Definition(e);var native=Native(e);native.hosei=731;native.m_status=8;native.m_status_prob=99;
                Check(r.MatchesStable(native)&&!r.MatchesOriginal(native),"Derived values incorrectly blocked or treated as originals.");
                Check(r.Hosei==e.Scalars["hosei"]&&r.MonsterStatus==e.Scalars["m_status"]&&r.MonsterStatusProbability==e.Scalars["m_status_prob"],"Runtime native values mutated immutable defaults.");
            }
        });
        Case("All raw-member bytes and high flags are validated independently of low7-bit donor identity",()=>{
            foreach(var e in Facts)for(int p=0;p<e.Members.Length;p++)
            {
                var r=Definition(e);var native=Native(e);native.player_id[p]^=0x80;
                Check(!r.MatchesOriginal(native)&&!r.MatchesStable(native),"Lost/added high flag accepted: "+e.Index+"/"+p);
                Check(r.MatchesOriginalMetadata(native)&&r.MatchesStableMetadata(native),"Member mutation changed scalar-only validation.");
                native=Native(e);native.player_id[p]=(byte)(native.player_id[p]^1);
                Check(!r.MatchesOriginal(native)&&!r.MatchesStable(native),"Changed participant donor accepted.");
            }
        });
        Case("Missing/truncated/extended participant arrays are rejected for every original recipe",()=>{
            foreach(var e in Facts)
            {
                var r=Definition(e);var native=Native(e);native.player_id=null!;
                Check(!r.MatchesOriginal(native)&&!r.MatchesStable(native),"Missing member array accepted.");
                native=Native(e);native.player_id=new Il2CppStructArray<byte>(e.Members.Take(e.Members.Length-1).ToArray());
                Check(!r.MatchesOriginal(native)&&!r.MatchesStable(native),"Short member array accepted.");
                native.player_id=new Il2CppStructArray<byte>(e.Members.Concat(new byte[]{0}).ToArray());
                Check(!r.MatchesOriginal(native)&&!r.MatchesStable(native),"Trailing member array bytes accepted.");
            }
        });
        Case("Projection preserves every ordered high-bit exemption while changing only canonical low7 bits",()=>{
            int flagged=0;
            foreach(var e in Facts.Skip(1))
            {
                var r=Definition(e);
                for(int p=0;p<r.Count;p++)
                {
                    if((e.Members[p]&128)!=0)flagged++;
                    foreach(int host in new[]{1,2,83,127})
                    {
                        byte encoded=r.EncodeForRole(p,host);
                        Check((encoded&127)==host&&(encoded&128)==(e.Members[p]&128),"Projected role wiped or moved native high flag.");
                    }
                }
                Throws(()=>r.EncodeForRole(0,0));Throws(()=>r.EncodeForRole(0,128));Throws(()=>r.EncodeForRole(0,-1));
            }
            Check(flagged==12,"Original high-bit operand count changed.");
        });
        Case("Every catalog member/donor/flag array is immutable and constructor detaches caller arrays",()=>{
            foreach(var e in Facts)
            {
                var r=Definition(e);
                Check(Throws(()=>((IList<byte>)r.RawMembers)[0]=99) is NotSupportedException,"Raw members are mutable.");
                Check(Throws(()=>((IList<int>)r.DonorIds)[0]=99) is NotSupportedException,"Donor array is mutable.");
                Check(Throws(()=>((IList<byte>)r.HighBitFlags)[0]=128) is NotSupportedException,"Flag array is mutable.");
            }
            byte[] members={131,2};var fresh=new Gsd2UniteRecipe(1,"fixture",1234,1235,32,0,2,members,86,150,7,30,0,0);
            members[0]=19;Check(fresh.RawMembers[0]==131&&fresh.DonorIds[0]==3&&fresh.HighBitFlags[0]==128,"Caller mutated catalog membership.");
            Check(typeof(Gsd2UniteRecipe).GetFields(BindingFlags.NonPublic|BindingFlags.Instance).All(f=>f.IsInitOnly),"Catalog owns mutable instance fields.");
        });
        Case("All50 native recipes retain two/three/four/five-member variants and shared function121 distinctions",()=>{
            var active=Gsd2UniteRecipes.All.Skip(1).ToArray();
            Check(active.Count(x=>x.Count==2)==32&&active.Count(x=>x.Count==3)==16&&active.Count(x=>x.Count==4)==1&&active.Count(x=>x.Count==5)==1,"Participant-count distribution changed.");
            Check(active.Single(x=>x.Index==36).Function==121&&active.Single(x=>x.Index==40).Function==121,"Native shared animation function changed.");
            Check(!active.Single(x=>x.Index==36).MatchesOriginal(Native(Facts[40])),"Shared function confused Family and upgraded Kobold recipes.");
            Check(active.Where(x=>x.Name=="Buddy Attack").Select(x=>x.DonorIds.Last()).SequenceEqual(new[]{80,81}),"Alternate Jowy variants collapsed.");
            Check(active.Where(x=>x.Name=="Pretty Girl Attack").Any(x=>x.DonorIds.Contains(83)),"Alternate Millie native ID collapsed.");
        });
    }
    static partial void RunAdapterChecks();
    private static void Main()
    {
        CatalogChecks();RunAdapterChecks();
        Console.WriteLine("PASS "+_passed+" linked S2 unite checks. Catalog/native objects use managed doubles; foreign native code, Harmony detours, damage formulas and animations were not executed.");
    }
}

