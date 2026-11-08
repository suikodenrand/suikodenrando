extern alias GSD1;
using SuikodenRandomizer.AvatarPlugin;
using SuikodenRandomizer.Core.Avatars;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

internal static class Program
{
    private static int _passed;
    private static string Identity(char c) => new string(c,64);
    private static string PathFor(string id) => Path.Combine(AvatarRuntime.Folder,"nuzlocke-save-bindings",id+".json");
    private static void Check(bool value,string message) { if(!value) throw new Exception(message); }
    private static void Case(string name,Action body) { body(); _passed++; Console.WriteLine("PASS "+name); }
    private static Exception Reject(Action body)
    {
        try { body(); } catch(InvalidDataException error) { return error; } catch(ArgumentException error) { return error; }
        throw new Exception("Expected malformed state to be rejected.");
    }
    private static NuzlockeBinding Binding()
    {
        var campaign=NuzlockeCampaign.Create("nuzlocke-persistence-test").ReconcileActive(new[]{8,1,2});
        return new NuzlockeBinding {
            Campaign=campaign.Capture(),
            FighterStates=campaign.ActiveRoleIds.Where(role=>role!=8).ToDictionary(role=>campaign.GetDonor(role),
                role=>new FixedRosterBaseSnapshot{CharacterId=campaign.GetDonor(role)}),
            RoleBases=new Dictionary<int,FixedRosterBaseSnapshot>{{1,new FixedRosterBaseSnapshot{CharacterId=1}}},
            RoleRules=new Dictionary<int,int>{{1,campaign.GetDonor(1)}}
        };
    }
    private static JsonObject ReadObject(string id) => JsonNode.Parse(File.ReadAllText(PathFor(id)))!.AsObject();
    private static void SaveObject(string id,JsonObject root,bool rehash=false)
    {
        if(rehash) root["StateHash"]=AvatarSaveIdentity.Hash(Encoding.UTF8.GetBytes(root["State"]!.ToJsonString()));
        File.WriteAllText(PathFor(id),root.ToJsonString());
    }
    private static void Main()
    {
        try
        {
            Case("Missing binding has no implied campaign and the strong native identity is delegated",()=>{
                Check(NuzlockeBindingStore.Read(Identity('a')) is null,"Missing binding appeared");
                Check(NuzlockeBindingStore.ComputeIdentity(new GSD1.SaveDataManager.SaveData{Identity=Identity('c')})==Identity('c'),"Wrong native identity");
                NuzlockeBindingStore.CheckWritable();
            });
            Case("A campaign save retains mapping, retirement, reserve order and fighter state",()=>{
                var binding=Binding(); var c=NuzlockeCampaign.Restore(binding.Campaign);
                int dead=c.GetDonor(1); c=c.CommitBattle(1,new[]{dead});
                binding.Campaign=c.Capture();
                foreach(int role in c.ActiveRoleIds.Where(role=>role!=8)) {
                    int fighter=c.GetDonor(role);
                    binding.FighterStates.TryAdd(fighter,new FixedRosterBaseSnapshot{CharacterId=fighter});
                }
                binding.FighterStates[c.GetDonor(1)].Hp=17;
                NuzlockeBindingStore.Write(Identity('a'),binding);
                var saved=NuzlockeBindingStore.Read(Identity('a'))!;
                var restored=NuzlockeCampaign.Restore(saved.Campaign);
                Check(restored.RetiredHeroIds.SequenceEqual(new[]{dead}),"Death lost");
                Check(restored.ReserveOrder.SequenceEqual(c.ReserveOrder)&&restored.ReservePosition==c.ReservePosition,"Reserve position lost");
                Check(restored.Assignments.OrderBy(p=>p.Key).SequenceEqual(c.Assignments.OrderBy(p=>p.Key)),"Mapping lost");
                Check(saved.FighterStates[c.GetDonor(1)].Hp==17,"Mutable fighter state lost");
                Check(restored.GetDonor(8)==8&&!restored.IncludesTir&&!saved.FighterStates.ContainsKey(8),"Binding transformed protected Tir into a fighter");
            });
            Case("Equivalent dictionary insertion orders are idempotent",()=>{
                var b=Binding(); NuzlockeBindingStore.Write(Identity('c'),b);
                byte[] original=File.ReadAllBytes(PathFor(Identity('c')));
                b.Campaign.Assignments=b.Campaign.Assignments.Reverse().ToDictionary(p=>p.Key,p=>p.Value);
                b.FighterStates=b.FighterStates.Reverse().ToDictionary(p=>p.Key,p=>p.Value);
                NuzlockeBindingStore.Write(Identity('c'),b);
                Check(original.SequenceEqual(File.ReadAllBytes(PathFor(Identity('c')))),"Idempotent write changed published bytes");
            });
            Case("Balanced starting gear is persisted while older bindings retain original gear",()=>{
                var b=Binding();b.BalancedStartingGear=true;
                NuzlockeBindingStore.Write(Identity('9'),b);
                Check(NuzlockeBindingStore.Read(Identity('9'))!.BalancedStartingGear,"Balanced gear preference was lost during publication");
                var root=ReadObject(Identity('9'));
                root["State"]!.AsObject().Remove("BalancedStartingGear");
                SaveObject(Identity('9'),root,true);
                var legacy=NuzlockeBindingStore.Read(Identity('9'))!;
                Check(!legacy.BalancedStartingGear,"A pre-option campaign adopted new balanced starting gear rules");
                NuzlockeBindingStore.Write(Identity('9'),legacy);
                Check(!NuzlockeBindingStore.Read(Identity('9'))!.BalancedStartingGear,"Rewriting an older binding enabled balancing");
            });
            Case("An exhausted companion roster restores with native Tir still available",()=>{
                var campaign=NuzlockeCampaign.Create("exhausted-save").ReconcileActive(new[]{8,1});
                for(int battle=1;!campaign.IsExhausted;battle++) {
                    Check(battle<=75,"Run did not exhaust its finite companion roster");
                    campaign=campaign.CommitBattle(battle,new[]{campaign.GetDonor(1)});
                }
                var b=new NuzlockeBinding{Campaign=campaign.Capture()};
                NuzlockeBindingStore.Write(Identity('b'),b);
                var restored=NuzlockeCampaign.Restore(NuzlockeBindingStore.Read(Identity('b'))!.Campaign);
                Check(restored.IsExhausted&&restored.GetDonor(8)==8&&restored.GetDonor(1)==-1&&restored.RetiredHeroIds.Count==75,"Companion exhaustion or protected Tir was lost");
            });
            Case("A conflicting payload cannot replace an existing native snapshot binding",()=>{
                var b=Binding(); NuzlockeBindingStore.Write(Identity('d'),b);
                byte[] original=File.ReadAllBytes(PathFor(Identity('d')));
                b.FighterStates.Values.First().Hp=4;
                Reject(()=>NuzlockeBindingStore.Write(Identity('d'),b));
                Check(original.SequenceEqual(File.ReadAllBytes(PathFor(Identity('d')))),"Conflicting write altered existing state");
            });
            Case("Concurrent identical publishers commit one complete binding",()=>{
                Parallel.For(0,8,_=>NuzlockeBindingStore.Write(Identity('e'),Binding()));
                Check(NuzlockeBindingStore.Read(Identity('e')) is not null,"Concurrent publish missing");
                Check(!Directory.EnumerateFiles(Path.GetDirectoryName(PathFor(Identity('e')))! ,"*.tmp").Any(),"Temporary files leaked");
            });
            Case("Save identity, build, version and state checksum are verified",()=>{
                foreach(string field in new[]{"SaveIdentity","GameBuildSha256","Version","StateHash"}) {
                    NuzlockeBindingStore.Write(Identity('f'),Binding());
                    var root=ReadObject(Identity('f'));
                    root[field]=field=="Version"?JsonValue.Create(99):JsonValue.Create("wrong");
                    SaveObject(Identity('f'),root);
                    Reject(()=>NuzlockeBindingStore.Read(Identity('f')));
                    File.Delete(PathFor(Identity('f')));
                }
            });
            Case("Invalid campaign or incomplete fighter snapshots are rejected before publication",()=>{
                var b=Binding(); b.Campaign.ReservePosition=77; Reject(()=>NuzlockeBindingStore.Write(Identity('f'),b));
                b=Binding(); b.FighterStates.Clear(); Reject(()=>NuzlockeBindingStore.Write(Identity('f'),b));
                b=Binding(); b.FighterStates.Values.First().CharacterId=108; Reject(()=>NuzlockeBindingStore.Write(Identity('f'),b));
                b=Binding(); b.RoleBases[1].CharacterId=2; Reject(()=>NuzlockeBindingStore.Write(Identity('f'),b));
                b=Binding(); b.RoleRules.Clear(); Reject(()=>NuzlockeBindingStore.Write(Identity('f'),b));
                b=Binding(); b.RoleRules[1]=24; Reject(()=>NuzlockeBindingStore.Write(Identity('f'),b));
                b=Binding(); b.FighterStates[8]=new FixedRosterBaseSnapshot{CharacterId=8}; Reject(()=>NuzlockeBindingStore.Write(Identity('f'),b));
                b=Binding(); b.RoleBases[8]=new FixedRosterBaseSnapshot{CharacterId=8}; b.RoleRules[8]=8;
                Reject(()=>NuzlockeBindingStore.Write(Identity('f'),b));
                b=Binding(); b.FighterStates.Values.First().Level=0; Reject(()=>NuzlockeBindingStore.Write(Identity('f'),b));
                Check(!File.Exists(PathFor(Identity('f'))),"Invalid state published");
            });
            Case("Unsafe 0.8.8 binding and campaign versions are rejected",()=>{
                NuzlockeBindingStore.Write(Identity('f'),Binding());
                var root=ReadObject(Identity('f'));
                root["Version"]=1;
                SaveObject(Identity('f'),root);
                var error=Reject(()=>NuzlockeBindingStore.Read(Identity('f')));
                Check(error.Message.Contains("0.8.8")&&error.Message.Contains("new Nuzlocke",StringComparison.OrdinalIgnoreCase),
                    "Legacy binding rejection did not explain the need for a new Nuzlocke game");
                File.Delete(PathFor(Identity('f')));
                NuzlockeBindingStore.Write(Identity('f'),Binding());
                root=ReadObject(Identity('f'));
                root["State"]!["Campaign"]!["Version"]=1;
                SaveObject(Identity('f'),root,true);
                error=Reject(()=>NuzlockeBindingStore.Read(Identity('f')));
                Check(error.Message.Contains("0.8.8")&&error.Message.Contains("new Nuzlocke",StringComparison.OrdinalIgnoreCase),
                    "Legacy campaign rejection did not explain the need for a new Nuzlocke game");
                File.Delete(PathFor(Identity('f')));
                var b=Binding(); b.Campaign.IncludesTir=true;
                Reject(()=>NuzlockeBindingStore.Write(Identity('f'),b));
            });
            Case("Invalid identities, oversized files and duplicate JSON properties are rejected",()=>{
                Reject(()=>NuzlockeBindingStore.Read("../evil"));
                Reject(()=>NuzlockeBindingStore.Write("BAD",Binding()));
                File.WriteAllText(PathFor(Identity('f')),new string(' ',524289));
                Reject(()=>NuzlockeBindingStore.Read(Identity('f'))); File.Delete(PathFor(Identity('f')));
                NuzlockeBindingStore.Write(Identity('f'),Binding());
                string version=ReadObject(Identity('f'))["Version"]!.ToString();
                string raw=File.ReadAllText(PathFor(Identity('f'))).Replace("\"Version\":"+version+",\"SaveIdentity\"",
                    "\"Version\":"+version+",\"Version\":"+version+",\"SaveIdentity\"");
                File.WriteAllText(PathFor(Identity('f')),raw);
                Reject(()=>NuzlockeBindingStore.Read(Identity('f')));
            });
            Console.WriteLine(_passed+"/"+_passed+" Nuzlocke binding persistence checks passed.");
            Console.WriteLine("Scope: linked production store with real Core campaign state and model native identity/fighter snapshot contracts.");
        }
        finally { if(Directory.Exists(AvatarRuntime.Folder)) Directory.Delete(AvatarRuntime.Folder,true); }
    }
}
