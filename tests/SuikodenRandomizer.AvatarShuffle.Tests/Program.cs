using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SuikodenRandomizer.Core;
using SuikodenRandomizer.Core.Avatars;
using SuikodenRandomizer.Serialization;

internal static class Program
{
    private static readonly string Save = new string('a', 64), Build = new string('b', 64);
    private static AvatarShuffleProfile Profile => AvatarShuffleProfile.Create("Agilen-080");
    private static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (ArgumentException) { return; }
        catch (InvalidOperationException) { return; }
        catch (InvalidDataException) { return; }
        catch (JsonException) { return; }
        catch (NotSupportedException) { return; }
        throw new Exception("Expected operation to be rejected.");
    }
    private static AvatarShuffleCampaignState Initialized()
    {
        var state = AvatarShuffleCampaignState.CreateNew(Profile);
        Check(state.ApplyNewGame(() => true), "Fixture initialization failed."); return state;
    }
    private static string Json() => AvatarShuffleBindingJson.Serialize(Profile, Initialized(), Save, Build);
    private static string Change(Action<JsonObject> edit)
    {
        var root = JsonNode.Parse(Json())!.AsObject(); edit(root); return root.ToJsonString();
    }
    private static AvatarShuffleSnapshot Snapshot() => Initialized().Capture(Save, Build);
    private static void RejectJson(string json) => Reject(() => AvatarShuffleBindingJson.Deserialize(json, Save, Build));

    private static int Main()
    {
        var tests = new (string, Action)[]
        {
            ("Pool is exactly all 75 reviewed combat roles and donors", () => {
                var p = Profile; Check(p.RoleIds.Count == 75 && p.Assignments.Count == 75, "Wrong pool size.");
                Check(p.RoleIds.SequenceEqual(CombatCatalog.NormalCombatIds.OrderBy(x => x))
                    && p.Assignments.Values.OrderBy(x => x).SequenceEqual(p.RoleIds), "Permutation is incomplete.");
            }),
            ("Protected protagonist guests and all support identities stay unchanged", () => {
                var p = Profile;
                for (int id = 0; id < 110; id++) if (!CombatCatalog.IsNormalCombat(id))
                    Check(!p.IsRole(id) && p.GetDonor(id) == id && !p.Assignments.Values.Contains(id), "Protected identity entered pool.");
            }),
            ("Opt-in Tir joins both sides of the distinct permutation", () => {
                var p = AvatarShuffleProfile.Create("Agilen-080", includeTir: true);
                Check(p.IncludesTir && p.ProfileId == AvatarShuffleProfile.IncludeTirProfileId
                    && p.RoleIds.Count == 76 && p.Fingerprint != Profile.Fingerprint, "Wrong opt-in profile.");
                Check(p.RoleIds.SequenceEqual(CombatCatalog.NormalCombatIds.Append(CombatCatalog.TirId).OrderBy(x => x))
                    && p.Assignments.Values.OrderBy(x => x).SequenceEqual(p.RoleIds), "Invalid opt-in permutation.");
                int tirRole = p.Assignments.Single(pair => pair.Value == CombatCatalog.TirId).Key;
                Check(p.KeepsCanonicalLoadout(CombatCatalog.TirId) && p.KeepsCanonicalLoadout(tirRole)
                    && p.RoleIds.Where(id => id != CombatCatalog.TirId && id != tirRole).All(id => !p.KeepsCanonicalLoadout(id)),
                    "Tir loadout isolation affected the wrong role.");
                Check(!Profile.IncludesTir && !Profile.KeepsCanonicalLoadout(CombatCatalog.TirId), "Opt-in changed legacy policy.");
            }),
            ("Invalid display identity inputs are rejected", () => { Reject(() => Profile.GetDonor(-1)); Reject(() => Profile.GetDonor(110)); }),
            ("Stable shuffle matches independent Python golden fixture", () => {
                var p = Profile;
                Check(p.Fingerprint == "27d283e70e6bfb8ec2143e68d79e56fe19f5f7b8178c3ff1ee1356ca744acac4", "Fingerprint/algorithm changed.");
                Check(p.RoleIds.Take(12).Select(p.GetDonor).SequenceEqual(new[] {65,26,37,102,40,55,92,32,56,107,16,54}), "Shuffle differs from cross-language fixture.");
            }),
            ("Identical seeds reproduce identical complete mappings", () => {
                var a = Profile; var b = Profile;
                Check(a.Fingerprint == b.Fingerprint && a.RoleIds.All(id => a.GetDonor(id) == b.GetDonor(id)), "Seed was nondeterministic.");
            }),
            ("Distinct seeds change assignment and fingerprint", () => {
                var a = Profile; var b = AvatarShuffleProfile.Create("Agilen-081");
                Check(a.Fingerprint != b.Fingerprint && a.RoleIds.Any(id => a.GetDonor(id) != b.GetDonor(id)), "Distinct seeds collapsed.");
            }),
            ("Seed trimming and Unicode normalization are canonical", () => {
                Check(AvatarShuffleProfile.Create("  Agilen-080  ").Fingerprint == Profile.Fingerprint, "Trim was not canonical.");
                Check(AvatarShuffleProfile.Create("e\u0301").Fingerprint == AvatarShuffleProfile.Create("é").Fingerprint, "Unicode normalization differs.");
            }),
            ("Empty overlong and control-character seeds are rejected", () => {
                foreach (string seed in new[] { "", "  ", new string('a',129), "bad\nseed", "bad\0seed" }) Reject(() => AvatarShuffleProfile.Create(seed));
                Reject(() => AvatarShuffleProfile.Create(null!));
            }),
            ("Profile collections cannot be externally changed", () => {
                var p = Profile;
                Reject(() => ((IDictionary<int,int>)p.Assignments)[p.RoleIds[0]] = 108);
                Reject(() => ((IList<int>)p.RoleIds)[0] = 108);
            }),
            ("A thousand seeds preserve complete unique protected-free permutations", () => {
                for (int n = 0; n < 1000; n++) {
                    var p = AvatarShuffleProfile.Create("seed-" + n);
                    Check(p.Assignments.Values.Distinct().Count() == 75 && p.Assignments.Values.All(CombatCatalog.IsNormalCombat), "Invalid generated permutation.");
                }
            }),
            ("Unbound state rejects mutation capture and replay", () => {
                var state = AvatarShuffleCampaignState.CreateUnbound(); bool called = false;
                Reject(() => state.ApplyNewGame(() => called = true));
                Reject(() => state.ReplayTemplates(Profile, () => called = true));
                Reject(() => state.Capture(Save, Build)); Check(!called, "Unbound callback ran.");
            }),
            ("Pending campaign cannot save or replay before transaction", () => {
                var state = AvatarShuffleCampaignState.CreateNew(Profile);
                Reject(() => state.Capture(Save, Build)); Reject(() => state.ReplayTemplates(Profile, () => true));
            }),
            ("New Game donor start can execute exactly once", () => {
                var state = AvatarShuffleCampaignState.CreateNew(Profile); int calls = 0;
                Check(state.ApplyNewGame(() => { calls++; return true; }), "First start rejected.");
                Reject(() => state.ApplyNewGame(() => { calls++; return true; })); Check(calls == 1, "Donor start ran twice.");
            }),
            ("Native false result permanently fails campaign", () => {
                var state = AvatarShuffleCampaignState.CreateNew(Profile);
                Check(!state.ApplyNewGame(() => false) && state.IsFailed && !state.IsInitialized, "False native commit accepted.");
                Reject(() => state.ApplyNewGame(() => true)); Reject(() => state.Capture(Save, Build));
            }),
            ("Native exception preserves failure and forbids retry", () => {
                var state = AvatarShuffleCampaignState.CreateNew(Profile);
                Reject(() => state.ApplyNewGame(() => throw new InvalidOperationException("native failure")));
                Check(state.IsFailed && state.FailureReason!.Contains("native failure"), "Failure detail lost.");
                Reject(() => state.ApplyNewGame(() => true));
            }),
            ("Reentrant mutation and capture are refused during apply", () => {
                var state = AvatarShuffleCampaignState.CreateNew(Profile);
                Check(state.ApplyNewGame(() => { Check(state.IsApplying, "Applying flag absent."); Reject(() => state.ApplyNewGame(() => true)); Reject(() => state.Capture(Save, Build)); return true; }), "Transaction failed.");
            }),
            ("Bound campaign replays without reopening donor start", () => {
                var loaded = AvatarShuffleBindingJson.Deserialize(Json(),Save,Build); int replay = 0;
                Check(loaded.Origin == RoleAvatarCampaignOrigin.BoundSave && loaded.IsInitialized, "Bound state unavailable.");
                Reject(() => loaded.ApplyNewGame(() => true));
                Check(loaded.ReplayTemplates(Profile, () => { replay++; return true; }) && replay == 1, "Same profile replay failed.");
            }),
            ("Loaded campaign rejects a changed seed before mutation", () => {
                var state = Initialized(); bool called = false;
                Reject(() => state.ReplayTemplates(AvatarShuffleProfile.Create("other"), () => called = true));
                Check(!called && state.IsInitialized, "Changed profile mutated state.");
            }),
            ("Complete 75-pair metadata survives codec round trip", () => {
                string json = Json(); var loaded = AvatarShuffleBindingJson.Deserialize(json,Save,Build);
                Check(loaded.Profile!.Fingerprint == Profile.Fingerprint && loaded.Profile.RoleIds.Count == 75, "Binding lost mapping.");
                Check(AvatarShuffleBindingJson.Serialize(loaded.Profile,loaded,Save,Build) == json, "Codec was not canonical.");
            }),
            ("Tir opt-in binding restores exact profile and rejects relabeling", () => {
                var profile = AvatarShuffleProfile.Create("Agilen-080", includeTir: true);
                var state = AvatarShuffleCampaignState.CreateNew(profile);
                Check(state.ApplyNewGame(() => true), "Opt-in fixture initialization failed.");
                string json = AvatarShuffleBindingJson.Serialize(profile, state, Save, Build);
                var loaded = AvatarShuffleBindingJson.Deserialize(json, Save, Build);
                Check(loaded.Profile?.IncludesTir == true && loaded.Profile.Fingerprint == profile.Fingerprint
                    && loaded.Profile.RoleIds.SequenceEqual(profile.RoleIds), "Opt-in profile was lost on load.");
                RejectJson(json.Replace(AvatarShuffleProfile.IncludeTirProfileId, AvatarShuffleProfile.CurrentProfileId));
                RejectJson(Json().Replace(AvatarShuffleProfile.CurrentProfileId, AvatarShuffleProfile.IncludeTirProfileId));
            }),
            ("Binding contains metadata only and no native progression", () => {
                string json = Json();
                foreach (string key in new[] { "hp", "exp", "level", "items", "runes", "player_base", "pointer" })
                    Check(!json.Contains("\""+key+"\"",StringComparison.OrdinalIgnoreCase), "Native progression duplicated.");
                Check(Encoding.UTF8.GetByteCount(json) < AvatarShuffleBindingJson.MaximumPayloadBytes, "Normal binding exceeds bound.");
            }),
            ("Wrong snapshot build and uppercase digest rejected", () => {
                Reject(() => AvatarShuffleBindingJson.Deserialize(Json(),new string('c',64),Build));
                Reject(() => AvatarShuffleBindingJson.Deserialize(Json(),Save,new string('c',64)));
                RejectJson(Change(x => x["gameBuildSha256"] = new string('B',64)));
            }),
            ("Wrong profile version algorithm and policies rejected", () => {
                foreach (string field in new[] { "profileId", "shuffleAlgorithm", "startPolicy", "progressionPolicy" }) RejectJson(Change(x => x[field] = "changed"));
                RejectJson(Change(x => x["avatarShuffleVersion"] = 2));
            }),
            ("Incomplete initialization cannot be restored", () => RejectJson(Change(x => x["templateInitializationCompleted"] = false))),
            ("Changed seed or fingerprint cannot relabel mapping", () => {
                RejectJson(Change(x => x["seed"] = "other")); RejectJson(Change(x => x["profileFingerprint"] = new string('c',64)));
                RejectJson(Change(x => x["seed"] = " Agilen-080 "));
            }),
            ("Missing extra and duplicate root properties rejected", () => {
                RejectJson(Change(x => x.Remove("seed"))); RejectJson(Change(x => x["extra"] = 1));
                RejectJson(Json().Replace("\"seed\":", "\"seed\":\"duplicate\",\"seed\":"));
            }),
            ("Legacy pilot recruitment and primitive bindings rejected", () => {
                foreach (string json in new[] {"{}","[]","null","42","{\"avatarVersion\":1,\"canonicalRoleId\":1}","{\"seedVersion\":1}"}) RejectJson(json);
            }),
            ("Missing extra duplicate or protected mapping identities rejected", () => {
                RejectJson(Change(x => x["assignments"]!.AsArray().RemoveAt(0)));
                RejectJson(Change(x => x["assignments"]!.AsArray().Add(new JsonObject { ["roleId"]=108,["donorId"]=108 })));
                RejectJson(Change(x => x["assignments"]![0]!["roleId"] = 108));
                RejectJson(Change(x => x["assignments"]![0]!["donorId"] = 8));
                RejectJson(Change(x => x["assignments"]![1]!["roleId"] = 0));
                RejectJson(Change(x => x["assignments"]![1]!["donorId"] = x["assignments"]![0]!["donorId"]!.DeepClone()));
            }),
            ("Assignment order is canonical and cannot be reordered", () => {
                var s = Snapshot(); var pair = s.Assignments[0]; s.Assignments[0] = s.Assignments[1]; s.Assignments[1] = pair;
                Reject(() => AvatarShuffleCampaignState.Restore(s,Save,Build));
            }),
            ("Unknown missing and duplicate assignment properties rejected", () => {
                RejectJson(Change(x => x["assignments"]![0]!.AsObject().Remove("roleId")));
                RejectJson(Change(x => x["assignments"]![0]!["extra"] = 1));
                RejectJson(Json().Replace("\"roleId\": 0", "\"roleId\":0,\"roleId\":0"));
            }),
            ("Noncanonical integer and nonboolean values rejected", () => {
                foreach (string token in new[] {"1.0","1e0","\"1\"","null","true"}) RejectJson(Json().Replace("\"avatarShuffleVersion\": 1", "\"avatarShuffleVersion\": "+token));
                RejectJson(Change(x => x["templateInitializationCompleted"] = "true"));
                RejectJson(Json().Replace("\"roleId\": 0", "\"roleId\": -0"));
            }),
            ("Comments trailing comma and excess nesting rejected", () => {
                RejectJson("/*comment*/"+Json()); RejectJson(Json().TrimEnd().TrimEnd('}')+",}");
                RejectJson("{\"nested\":"+new string('[',8)+"0"+new string(']',8)+"}");
            }),
            ("UTF8 byte size and character bounds are enforced", () => {
                RejectJson(Json()+new string(' ',65536)); RejectJson(new string('é',33000));
            }),
            ("Serializing with another configured profile is rejected", () => Reject(() => AvatarShuffleBindingJson.Serialize(AvatarShuffleProfile.Create("other"),Initialized(),Save,Build))),
            ("Captured mapping is detached from immutable campaign", () => {
                var state = Initialized(); var a = state.Capture(Save,Build); a.Assignments[0].DonorId = 108;
                var b = state.Capture(Save,Build); Check(b.Assignments[0].DonorId == Profile.GetDonor(0), "Snapshot mutation changed live mapping.");
            }),
            ("Batch captures every pristine donor before any apply and handles cycles", () => {
                int[] roles = {0,1,2}; var live = new Dictionary<int,int>{{0,10},{1,20},{2,30},{8,999}}; int captures=0;
                var r = AvatarShuffleTransaction.Execute(roles,id=>{captures++;return live[id];},
                    (id,snap)=>{Check(captures==3,"Applied before capturing all donors.");live[id]=snap[(id+1)%3];},
                    (id,before,after)=>after==new[]{20,30,10}[id],(id,before)=>live[id]=before,(a,b)=>a==b,()=>live[8]==999);
                Check(r.Committed && live[0]==20 && live[1]==30 && live[2]==10 && live[8]==999,"Swap cycle cascaded or protected value changed.");
            }),
            ("Batch replay keeps earned progression while replacing templates", () => {
                var live = new Dictionary<int,Record>{{0,new Record(0,20,900,10)},{1,new Record(1,35,500,20)}};
                var r=AvatarShuffleTransaction.Execute(new[]{0,1},id=>live[id].Copy(),(id,b)=>live[id].Template=b[1-id].Template,
                    (id,b,a)=>a.Id==b.Id&&a.Level==b.Level&&a.Exp==b.Exp&&a.Template!=b.Template,
                    (id,b)=>live[id]=b.Copy(),Record.Equal,()=>true);
                Check(r.Committed&&live[0].Level==20&&live[0].Exp==900&&live[0].Template==20,"Replay reset progression.");
            }),
            ("Capture failure performs zero mutations", () => {
                int writes=0;
                var r=AvatarShuffleTransaction.Execute(new[]{0,1},id=>id==1?throw new InvalidOperationException("capture"):5,
                    (id,b)=>writes++,(id,b,a)=>true,(id,b)=>writes++,(a,b)=>a==b,()=>true);
                Check(!r.Committed&&!r.MutationsStarted&&writes==0,"Preflight capture wrote state.");
            }),
            ("Failure midway restores every original role including touched donor", () => {
                var live=new Dictionary<int,int>{{0,10},{1,20},{2,30}};var restored=new List<int>();
                var r=AvatarShuffleTransaction.Execute(new[]{0,1,2},id=>live[id],(id,b)=>{live[id]=99;if(id==1){live[2]=88;throw new InvalidOperationException("midway");}},
                    (id,b,a)=>true,(id,b)=>{restored.Add(id);live[id]=b;},(a,b)=>a==b,()=>true);
                Check(!r.Committed&&r.RollbackVerified&&live.Values.SequenceEqual(new[]{10,20,30})&&restored.SequenceEqual(new[]{2,1,0}),"Rollback missed a role or donor.");
            }),
            ("Postcondition failure restores all applied roles", () => {
                var live=new[]{10,20};
                var r=AvatarShuffleTransaction.Execute(new[]{0,1},id=>live[id],(id,b)=>live[id]=99,(id,b,a)=>id!=1,
                    (id,b)=>live[id]=b,(a,b)=>a==b,()=>true);
                Check(!r.Committed&&r.RollbackVerified&&live.SequenceEqual(new[]{10,20}),"Failed postcondition escaped rollback.");
            }),
            ("Shared canonical state failure causes rollback", () => {
                var live=new[]{10,20};bool shared=true;
                var r=AvatarShuffleTransaction.Execute(new[]{0,1},id=>live[id],(id,b)=>{live[id]=99;shared=false;},(id,b,a)=>true,
                    (id,b)=>{live[id]=b;shared=true;},(a,b)=>a==b,()=>shared);
                Check(!r.Committed&&r.RollbackVerified&&shared&&live[0]==10,"Shared state change accepted.");
            }),
            ("Rollback failures propagate and retain original exception", () => {
                var live=new[]{10,20};int restores=0;
                try {
                    AvatarShuffleTransaction.Execute(new[]{0,1},id=>live[id],(id,b)=>{live[id]=99;throw new InvalidOperationException("original");},(id,b,a)=>true,
                        (id,b)=>{restores++;if(id==1)throw new InvalidOperationException("restore");live[id]=b;},(a,b)=>a==b,()=>true);
                    throw new Exception("Rollback failure swallowed.");
                } catch(AvatarShuffleRollbackException e) {
                    Check(restores==2&&e.OriginalFailure.Message=="original"&&e.InnerException is AggregateException,"Rollback aborted remaining roles or lost original failure.");
                }
            }),
            ("Duplicate and empty batch role sets are rejected before capture", () => {
                int captures=0;
                foreach(int[] ids in new[]{Array.Empty<int>(),new[]{1,1}})
                    Reject(()=>AvatarShuffleTransaction.Execute(ids,id=>{captures++;return 0;},(id,b)=>{},(id,b,a)=>true,(id,b)=>{},(a,b)=>true,()=>true));
                Check(captures==0,"Invalid role set reached native capture.");
            })
        };
        int passed=0;
        foreach(var test in tests)
        {
            try {test.Item2();passed++;Console.WriteLine("PASS "+test.Item1);}
            catch(Exception e) {Console.Error.WriteLine("FAIL "+test.Item1+": "+e);return 1;}
        }
        Console.WriteLine(passed+"/"+tests.Length+" avatar-shuffle checks passed.");return 0;
    }
    private sealed class Record
    {
        internal Record(int id,int level,int exp,int template){Id=id;Level=level;Exp=exp;Template=template;}
        internal int Id,Level,Exp,Template;
        internal Record Copy()=>new Record(Id,Level,Exp,Template);
        internal static bool Equal(Record a,Record b)=>a.Id==b.Id&&a.Level==b.Level&&a.Exp==b.Exp&&a.Template==b.Template;
    }
}
