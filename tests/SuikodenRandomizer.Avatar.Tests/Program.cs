using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using SuikodenRandomizer.Core.Avatars;
using SuikodenRandomizer.Serialization;

internal static class Program
{
    private static readonly RoleAvatarProfile Pilot = RoleAvatarProfile.CleoEileenPilot;
    private static readonly AvatarIdentity Cleo = new AvatarIdentity(1, 1, 1);
    private static readonly string SaveA = new string('a', 64);
    private static readonly string SaveB = new string('b', 64);
    private static readonly string Build = new string('c', 64);

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
    private static void Reject(Action action, string message)
    {
        try { action(); }
        catch (ArgumentException) { return; }
        catch (InvalidOperationException) { return; }
        throw new Exception(message);
    }
    private static void RollbackMustFail(Action action)
    {
        try { action(); }
        catch (AvatarTemplateRollbackException exception) {
            Check(exception.InnerException is AggregateException && exception.OriginalFailure is not null,
                "Rollback lost original/secondary failures");
            return;
        }
        throw new Exception("Failed rollback was silently accepted");
    }
    private static RoleAvatarCampaignState Initialized()
    {
        var state = RoleAvatarCampaignState.CreateNew(Pilot);
        Check(state.ApplyNewGame(Cleo, () => true), "Test initialization failed");
        return state;
    }
    private static RoleAvatarSnapshot Clone(RoleAvatarSnapshot snapshot) =>
        JsonSerializer.Deserialize<RoleAvatarSnapshot>(JsonSerializer.Serialize(snapshot))
            ?? throw new Exception("Null snapshot");
    private static Exception RejectBinding(Action action, string message)
    {
        try {action();}
        catch (JsonException exception) {return exception;}
        catch (InvalidDataException exception) {return exception;}
        catch (ArgumentException exception) {return exception;}
        catch (InvalidOperationException exception) {return exception;}
        throw new Exception(message);
    }
    private static string RewriteBinding(string json, string name, string? rawReplacement)
    {
        using var document = JsonDocument.Parse(json);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (JsonProperty property in document.RootElement.EnumerateObject())
            {
                if (property.Name != name) {property.WriteTo(writer);continue;}
                if (rawReplacement is null) continue;
                writer.WritePropertyName(name);writer.WriteRawValue(rawReplacement);
            }
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static int Main()
    {
        var tests = new (string Name, Action Run)[]
        {
            ("Pilot is an explicit canonical Cleo role with Eileen donor", () => {
                Check(Pilot.CanonicalRoleId == 1 && Pilot.DonorCharacterId == 0
                    && Pilot.ProfileId == "cleo-eileen-avatar-pilot-v1"
                    && Pilot.StartPolicy == "donor-live-start-once", "Wrong pilot profile");
                Check(ReferenceEquals(Pilot, RoleAvatarProfile.CreatePilot(1, 0)), "Default profile differs by construction path");
            }),
            ("Profile fingerprint covers pair and start/replay policy", () => {
                Check(Pilot.Fingerprint.Length == 64 && Pilot.Fingerprint.All(character => char.IsDigit(character)
                    || character >= 'a' && character <= 'f'), "Not a lowercase fingerprint");
                Check(Pilot.Fingerprint == RoleAvatarProfile.CreatePilot(1, 0).Fingerprint
                    && Pilot.Fingerprint != RoleAvatarProfile.CreatePilot(1, 2).Fingerprint
                    && Pilot.ProgressionPolicy == RoleAvatarProgressionPolicy.PolicyId, "Profile contract does not distinguish donors/policy");
            }),
            ("Protagonist guests supports and identity donation rejected", () => {
                foreach (int id in new[] {8, 24, 108, 109, -1, 110}) {
                    Reject(() => RoleAvatarProfile.CreatePilot(id, 0), "Unreviewed role class admitted");
                    Reject(() => RoleAvatarProfile.CreatePilot(1, id), "Unreviewed donor class admitted");
                }
                Reject(() => RoleAvatarProfile.CreatePilot(1, 1), "Identity donation admitted");
            }),
            ("Canonical code data and base are never projected to donor identity", () => {
                Cleo.RequireCanonicalRole(Pilot);
                foreach (AvatarIdentity bad in new[] {new AvatarIdentity(0, 0, 0), new AvatarIdentity(1, 0, 1),
                    new AvatarIdentity(1, 1, 0), new AvatarIdentity(0, 1, 1)})
                    Reject(() => bad.RequireCanonicalRole(Pilot), "Donor or mixed identity admitted");
            }),
            ("Empty and full parties preserve their actual canonical identities", () => {
                RoleAvatarIdentityRules.ValidateOccupiedResidents(Pilot, Array.Empty<AvatarIdentity>());
                AvatarIdentity[] party = {new AvatarIdentity(8,8,8), Cleo, new AvatarIdentity(2,2,2),
                    new AvatarIdentity(35,35,35), new AvatarIdentity(32,32,32), new AvatarIdentity(0,0,0)};
                int[] before = party.Select(item => item.CodeId).ToArray();
                RoleAvatarIdentityRules.ValidateOccupiedResidents(Pilot, party);
                Check(party.Select(item => item.CodeId).SequenceEqual(before) && party.Length == 6,
                    "Avatar validation projected party IDs or changed capacity");
            }),
            ("Resident duplicates mismatched bindings and overflow rejected", () => {
                Reject(() => RoleAvatarIdentityRules.ValidateOccupiedResidents(Pilot, new[] {Cleo, Cleo}), "Duplicate role admitted");
                Reject(() => RoleAvatarIdentityRules.ValidateOccupiedResidents(Pilot, new[] {new AvatarIdentity(1,0,1)}), "Mismatched asset admitted");
                Reject(() => RoleAvatarIdentityRules.ValidateOccupiedResidents(Pilot,
                    Enumerable.Range(0,7).Select(id => new AvatarIdentity(id,id,id)).ToArray()), "Seventh resident admitted");
            }),
            ("Donor live starting values copy once while identity and plot stay canonical", () => {
                var world = new World(); var roleBefore = world.Role.Copy(); var donorBefore = world.Donor.Copy();
                var result = world.Transaction(() => world.ApplyStart(), (before, after) =>
                    after.CanonicalAndPlotEqual(before) && after.StartAndTemplatesEqual(world.Donor));
                Check(result.Committed && world.Role.Code == 1 && world.Role.Data == 1 && world.Role.Base == 1
                    && world.Role.Hp == donorBefore.Hp && world.Role.Level == donorBefore.Level
                    && world.Role.Items.SequenceEqual(donorBefore.Items), "One-time donor start was not copied correctly");
                Check(world.Donor.AllEqual(donorBefore) && world.DonorRestoreCalls == 0
                    && world.Role.Plot.SequenceEqual(roleBefore.Plot), "Donor or canonical plot state changed");
            }),
            ("Replay copies templates and preserves saved canonical progression", () => {
                var world = new World(); world.ApplyStart();
                world.Role.Hp = 17; world.Role.Mp = 3; world.Role.Level = 41; world.Role.Exp = 900;
                world.Role.Items = new[] {999, 7}; world.Role.Equipment = new[] {800}; var saved = world.Role.Copy();
                world.Donor.Hp = 999; world.Donor.Level = 1; world.Donor.Items = new[] {44};
                var result = world.Transaction(() => world.ApplyReplay(), (before, after) =>
                    after.CanonicalAndPlotEqual(before) && after.ProgressionEqual(before)
                    && after.TemplatesEqual(world.Donor));
                Check(result.Committed && world.Role.ProgressionEqual(saved), "Replay reset saved progression/items/equipment");
            }),
            ("Copied mutable templates are independent of donor cache", () => {
                var world = new World(); var donor = world.Donor.Copy();
                Check(world.Transaction(world.ApplyReplay, (before, after) => after.ProgressionEqual(before)).Committed,
                    "Independent copy failed");
                world.Role.Growth[0] = 123; world.Role.Actions[0] = 234;
                Check(world.Donor.AllEqual(donor), "Mutating copied role template changed donor cache");
            }),
            ("Policy distinguishes New Game initialization from bound replay", () => {
                foreach (RoleAvatarFieldDomain field in new[] {RoleAvatarFieldDomain.PersistentHealthAndMagic,
                    RoleAvatarFieldDomain.PersistentLevelAndExperience, RoleAvatarFieldDomain.PersistentInventory,
                    RoleAvatarFieldDomain.PersistentEquipmentAndRunes})
                    Check(RoleAvatarProgressionPolicy.MayCopyFromDonor(field, RoleAvatarCopyPhase.NewGameInitialization)
                        && !RoleAvatarProgressionPolicy.MayCopyFromDonor(field, RoleAvatarCopyPhase.BoundReplay)
                        && !RoleAvatarProgressionPolicy.MayCopyFromDonor(field), "Mutable start/replay policy mismatch");
                foreach (RoleAvatarFieldDomain field in new[] {RoleAvatarFieldDomain.CanonicalIdentity, RoleAvatarFieldDomain.PlotUniqueOverrides})
                    Check(!RoleAvatarProgressionPolicy.MayCopyFromDonor(field, RoleAvatarCopyPhase.NewGameInitialization)
                        && !RoleAvatarProgressionPolicy.MayCopyFromDonor(field, RoleAvatarCopyPhase.BoundReplay),
                        "Protected identity/plot field admitted");
                Check(RoleAvatarProgressionPolicy.MayCopyFromDonor(RoleAvatarFieldDomain.AppearanceTemplate)
                    && RoleAvatarProgressionPolicy.MayCopyFromDonor(RoleAvatarFieldDomain.GrowthTemplate)
                    && RoleAvatarProgressionPolicy.MayCopyFromDonor(RoleAvatarFieldDomain.ActionTemplate), "Replay template policy wrong");
            }),
            ("Fresh initialization is admitted only once", () => {
                var state = RoleAvatarCampaignState.CreateNew(Pilot); int calls = 0;
                Check(state.ApplyNewGame(Cleo, () => {calls++; return true;}) && state.IsInitialized, "New Game not initialized");
                Reject(() => state.ApplyNewGame(Cleo, () => {calls++; return true;}), "Second donor start admitted");
                Check(calls == 1, "Rejected initialization called native mutation callback");
            }),
            ("Unbound existing save cannot enable avatar mutation", () => {
                var state = RoleAvatarCampaignState.CreateUnbound(); bool called = false;
                Reject(() => state.ApplyNewGame(Cleo, () => {called = true; return true;}), "Unbound save initialized pilot");
                Reject(() => state.ReplayTemplate(Pilot, Cleo, () => {called = true; return true;}), "Unbound save replayed pilot");
                Reject(() => state.Capture(SaveA, Build), "Unbound save captured avatar binding");
                Check(!called && !state.IsInitialized, "Unbound save mutated native state");
            }),
            ("Pending initialization cannot save or replay", () => {
                var state = RoleAvatarCampaignState.CreateNew(Pilot);
                Reject(() => state.Capture(SaveA, Build), "Pending initialization became save binding");
                Reject(() => state.ReplayTemplate(Pilot, Cleo, () => true), "Pending role replay bypassed donor start");
            }),
            ("Capture binds initialized profile to same native snapshot and build", () => {
                var state = Initialized(); var snapshot = Clone(state.Capture(SaveA, Build));
                Check(snapshot.TemplateInitializationCompleted && snapshot.CanonicalRoleId == 1 && snapshot.DonorCharacterId == 0
                    && snapshot.ProfileFingerprint == Pilot.Fingerprint && snapshot.StartPolicy == Pilot.StartPolicy,
                    "Incomplete profile binding");
                var loaded = RoleAvatarCampaignState.Restore(Pilot, snapshot, SaveA, Build);
                Check(loaded.Origin == RoleAvatarCampaignOrigin.BoundSave && loaded.IsInitialized, "Bound replay unavailable");
            }),
            ("Bound load never reopens one-time donor starting progression", () => {
                var state = RoleAvatarCampaignState.Restore(Pilot, Initialized().Capture(SaveA, Build), SaveA, Build);
                bool called = false;
                Reject(() => state.ApplyNewGame(Cleo, () => {called = true; return true;}), "Loaded save admitted donor restart");
                Check(!called && state.ReplayTemplate(Pilot, Cleo, () => true), "Loaded same-profile replay rejected");
            }),
            ("Loaded campaign rejects live profile swap before callback", () => {
                var state = RoleAvatarCampaignState.Restore(Pilot, Initialized().Capture(SaveA, Build), SaveA, Build);
                bool called = false;
                Reject(() => state.ReplayTemplate(RoleAvatarProfile.CreatePilot(1,2), Cleo, () => {called = true; return true;}),
                    "Live donor/profile switch admitted");
                Check(!called && state.IsInitialized && state.Profile!.Fingerprint == Pilot.Fingerprint, "Rejected profile swap changed campaign");
            }),
            ("Wrong save build profile version or incomplete initialization rejected", () => {
                var snapshot = Initialized().Capture(SaveA, Build);
                Reject(() => RoleAvatarCampaignState.Restore(Pilot, snapshot, SaveB, Build), "Wrong save accepted");
                Reject(() => RoleAvatarCampaignState.Restore(Pilot, snapshot, SaveA, SaveB), "Wrong game build accepted");
                Reject(() => RoleAvatarCampaignState.Restore(RoleAvatarProfile.CreatePilot(1,2), snapshot, SaveA, Build), "Wrong donor accepted");
                foreach (int version in new[] {0,2}) {var bad = Clone(snapshot); bad.AvatarVersion = version;
                    Reject(() => RoleAvatarCampaignState.Restore(Pilot, bad, SaveA, Build), "Unknown avatar version accepted");}
                var pending = Clone(snapshot); pending.TemplateInitializationCompleted = false;
                Reject(() => RoleAvatarCampaignState.Restore(Pilot, pending, SaveA, Build), "Uninitialized snapshot accepted");
            }),
            ("Malformed policy identity and fingerprint fields cannot be rebound", () => {
                foreach (Action<RoleAvatarSnapshot> mutate in new Action<RoleAvatarSnapshot>[] {
                    item => item.StartPolicy = "reset-on-every-load", item => item.ProgressionPolicy = "copy-everything",
                    item => item.CanonicalRoleId = 0, item => item.DonorCharacterId = 1,
                    item => item.ProfileFingerprint = new string('d',64), item => item.ProfileId = "old-shuffle-profile"}) {
                    var bad = Initialized().Capture(SaveA, Build); mutate(bad);
                    Reject(() => RoleAvatarCampaignState.Restore(Pilot,bad,SaveA,Build), "Mutated binding metadata accepted");
                }
            }),
            ("Rejected canonical identity never invokes donor copy callback", () => {
                var state = RoleAvatarCampaignState.CreateNew(Pilot); bool called = false;
                Reject(() => state.ApplyNewGame(new AvatarIdentity(1,0,1), () => {called = true; return true;}), "Hybrid identity passed preflight");
                Check(!called && !state.IsInitialized && !state.IsFailed, "Rejected identity consumed initializer");
                Check(state.ApplyNewGame(Cleo,() => true), "Pure preflight rejection prevented corrected valid initialization");
            }),
            ("Failed copy transaction blocks save and retry", () => {
                var state = RoleAvatarCampaignState.CreateNew(Pilot);
                Check(!state.ApplyNewGame(Cleo, () => false) && state.IsFailed && state.FailureReason is not null, "Failure state missing");
                Reject(() => state.Capture(SaveA,Build), "Failed native transaction became save binding");
                Reject(() => state.ApplyNewGame(Cleo,() => true), "Partial-state retry admitted");
                Reject(() => state.ReplayTemplate(Pilot,Cleo,() => true), "Failed campaign replay admitted");
            }),
            ("Exceptions during mutation mark campaign failed and propagate", () => {
                var state = RoleAvatarCampaignState.CreateNew(Pilot);
                try {state.ApplyNewGame(Cleo,() => throw new ApplicationException("native-copy")); throw new Exception("Missing failure");}
                catch (ApplicationException) {Check(state.IsFailed && state.FailureReason!.Contains("native-copy"), "Copy exception lost failure state");}
                Reject(() => state.Capture(SaveA,Build), "Exception path admitted save");
            }),
            ("Reentrant save and initialization blocked while transaction runs", () => {
                var state = RoleAvatarCampaignState.CreateNew(Pilot);
                Check(state.ApplyNewGame(Cleo,() => {
                    Check(state.IsApplying, "Applying phase missing");
                    Reject(() => state.Capture(SaveA,Build), "Staged native representation saved");
                    Reject(() => state.ApplyNewGame(Cleo,() => true), "Reentrant initializer admitted");
                    Reject(() => state.ReplayTemplate(Pilot,Cleo,() => true), "Reentrant replay admitted");
                    return true;
                }), "Outer verified transaction failed");
            }),
            ("Replay with changed identity rejected before any copy", () => {
                var state = Initialized(); bool called = false;
                Reject(() => state.ReplayTemplate(Pilot,new AvatarIdentity(1,1,0),() => {called = true; return true;}), "Mismatched restored base admitted");
                Check(!called && state.IsInitialized, "Preflight replay rejection changed live campaign");
            }),
            ("Role copy exception restores exact target snapshot and leaves donor untouched", () => {
                var world = new World(); var role = world.Role.Copy(); var donor = world.Donor.Copy();
                var result = world.Transaction(() => {world.Role.Hp = 1; world.Role.Growth[0] = 99; throw new ApplicationException("mid-copy");},
                    (before,after) => true);
                Check(!result.Committed && result.RollbackVerified && result.MutationsStarted
                    && result.FailureStage == "apply-template" && world.Role.AllEqual(role) && world.Donor.AllEqual(donor)
                    && world.DonorRestoreCalls == 0, "Failed copy did not restore exact role/donor state");
            }),
            ("Replay progression overwrite is detected and rolled back", () => {
                var world = new World(); var saved = world.Role.Copy();
                var result = world.Transaction(world.ApplyStart, (before,after) => after.ProgressionEqual(before));
                Check(!result.Committed && result.RollbackVerified && result.FailureStage == "verify-role"
                    && world.Role.AllEqual(saved), "Unintended donor restart survived replay policy verification");
            }),
            ("Donor cache mutation restores both snapshots and records failed stage", () => {
                var world = new World(); var role = world.Role.Copy(); var donor = world.Donor.Copy();
                var result = world.Transaction(() => {world.ApplyReplay(); world.Donor.Growth[0] = 88;}, (before,after) => true);
                Check(!result.Committed && result.RollbackVerified && result.FailureStage == "verify-donor"
                    && world.Role.AllEqual(role) && world.Donor.AllEqual(donor) && world.DonorRestoreCalls == 1,
                    "Donor mutation was not detected/restored");
            }),
            ("Failed rollback remains fatal after both restoration attempts", () => {
                var world = new World(); int donorRestores = 0;
                RollbackMustFail(() => AvatarTemplateTransaction.Execute(
                    () => world.Role.Copy(), () => world.Donor.Copy(),
                    () => {world.Role.Hp = 1; world.Donor.Hp = 1; throw new ApplicationException("apply failed");},
                    (before,after) => true, before => throw new ApplicationException("role restore failed"),
                    before => {donorRestores++; world.Donor = before.Copy();},
                    (before,after) => before.AllEqual(after), (before,after) => before.AllEqual(after)));
                Check(donorRestores == 1, "Role restoration failure prevented donor recovery attempt");
            }),
            ("Silent incomplete restoration is rejected by snapshot verification", () => {
                var world = new World();
                RollbackMustFail(() => AvatarTemplateTransaction.Execute(
                    () => world.Role.Copy(), () => world.Donor.Copy(), () => {world.Role.Hp = 1;},
                    (before,after) => false, before => {}, before => {world.Donor = before.Copy();},
                    (before,after) => before.AllEqual(after), (before,after) => before.AllEqual(after)));
            }),
            ("Snapshot capture failure prevents mutation and has explicit preflight record", () => {
                var world = new World(); bool called = false;
                var result = AvatarTemplateTransaction.Execute<NativeImage,NativeImage>(
                    () => world.Role.Copy(), () => throw new ApplicationException("donor unavailable"),
                    () => {called = true;}, (before,after) => true,
                    before => {world.Role = before.Copy();}, before => {world.Donor = before.Copy();},
                    (before,after) => before.AllEqual(after), (before,after) => before.AllEqual(after));
                Check(!result.Committed && !result.MutationsStarted && !result.RollbackVerified
                    && result.FailureStage == "capture-donor" && !called, "Failed preflight entered copy transaction");
            }),
            ("Snapshot DTO mutation cannot swap a live campaign profile", () => {
                var state = Initialized(); var snapshot = state.Capture(SaveA,Build);
                snapshot.CanonicalRoleId = 0; snapshot.DonorCharacterId = 1; snapshot.ProfileFingerprint = "mutated";
                Check(state.Profile!.CanonicalRoleId == 1 && state.Profile.DonorCharacterId == 0
                    && state.Capture(SaveB,Build).ProfileFingerprint == Pilot.Fingerprint, "Mutable DTO changed live profile");
            }),
            ("Malformed native save build and policy enum values rejected", () => {
                var state = Initialized();
                Reject(() => state.Capture("",Build), "Missing save identity accepted");
                Reject(() => state.Capture(SaveA,new string('C',64)), "Noncanonical build digest accepted");
                Reject(() => RoleAvatarProgressionPolicy.MayCopyFromDonor((RoleAvatarFieldDomain)999), "Unknown policy field accepted");
                Reject(() => RoleAvatarProgressionPolicy.MayCopyFromDonor(RoleAvatarFieldDomain.GrowthTemplate,(RoleAvatarCopyPhase)999),
                    "Unknown copy phase accepted");
            }),
            ("Shared avatar codec roundtrips exact initialized binding metadata", () => {
                var state = Initialized();string json = RoleAvatarBindingJson.Serialize(Pilot,state,SaveA,Build);
                var loaded = RoleAvatarBindingJson.Deserialize(Pilot,json,SaveA,Build);
                Check(loaded.Origin == RoleAvatarCampaignOrigin.BoundSave && loaded.IsInitialized
                    && loaded.Profile!.Fingerprint == Pilot.Fingerprint, "Codec lost initialized profile");
                Check(RoleAvatarBindingJson.Serialize(Pilot,loaded,SaveA,Build) == json,
                    "Equivalent snapshot/profile did not serialize deterministically");
                using var document = JsonDocument.Parse(json);
                string[] expected = {"avatarVersion","profileId","profileFingerprint","canonicalRoleId","donorCharacterId",
                    "progressionPolicy","startPolicy","saveIdentity","gameBuildSha256","templateInitializationCompleted"};
                Check(document.RootElement.EnumerateObject().Select(item => item.Name).OrderBy(name => name)
                    .SequenceEqual(expected.OrderBy(name => name)), "Writer emitted unexpected schema");
            }),
            ("Shared avatar codec rejects legacy seed and foreign document formats", () => {
                foreach (string json in new[] {"{\"bindingVersion\":1,\"manifest\":{}}",
                    "{\"bindingVersion\":2,\"acquisitionHistory\":{},\"manifest\":{}}", "[]", "null", "\"avatar\"",
                    "{\"avatar\":{\"avatarVersion\":1}}"})
                    RejectBinding(() => RoleAvatarBindingJson.Deserialize(Pilot,json,SaveA,Build),
                        "Foreign or old recruitment binding was adopted as avatar state");
            }),
            ("Shared avatar codec requires every field instead of accepting DTO defaults", () => {
                string json = RoleAvatarBindingJson.Serialize(Pilot,Initialized(),SaveA,Build);
                using var document = JsonDocument.Parse(json);
                foreach (JsonProperty property in document.RootElement.EnumerateObject()) {
                    string missing = RewriteBinding(json,property.Name,null);
                    RejectBinding(() => RoleAvatarBindingJson.Deserialize(Pilot,missing,SaveA,Build),
                        "Missing required field accepted: " + property.Name);
                }
            }),
            ("Shared avatar codec rejects duplicate escaped aliases case aliases and unknown fields", () => {
                string json = RoleAvatarBindingJson.Serialize(Pilot,Initialized(),SaveA,Build);
                int end = json.LastIndexOf('}');string prefix = json.Substring(0,end);
                foreach (string bad in new[] {prefix + ",\"profileId\":\"other\"}",
                    prefix + ",\"\\u0070rofileId\":\"other\"}",
                    json.Replace("\"profileId\"","\"ProfileId\""),
                    prefix + ",\"futureAvatarField\":1}"})
                    RejectBinding(() => RoleAvatarBindingJson.Deserialize(Pilot,bad,SaveA,Build),
                        "Duplicate, aliased or unknown field accepted");
            }),
            ("Shared avatar codec rejects null wrong types and noncanonical numeric aliases", () => {
                string json = RoleAvatarBindingJson.Serialize(Pilot,Initialized(),SaveA,Build);
                using var document = JsonDocument.Parse(json);
                foreach (JsonProperty property in document.RootElement.EnumerateObject()) {
                    string bad = RewriteBinding(json,property.Name,"null");
                    RejectBinding(() => RoleAvatarBindingJson.Deserialize(Pilot,bad,SaveA,Build),
                        "Null field accepted: " + property.Name);
                }
                foreach (string numeric in new[] {"\"1\"","1.0","1e0","true","[]","{}"}) {
                    string bad = RewriteBinding(json,"avatarVersion",numeric);
                    RejectBinding(() => RoleAvatarBindingJson.Deserialize(Pilot,bad,SaveA,Build),
                        "Noncanonical version number accepted: " + numeric);
                }
                string wrongBoolean = RewriteBinding(json,"templateInitializationCompleted","\"true\"");
                RejectBinding(() => RoleAvatarBindingJson.Deserialize(Pilot,wrongBoolean,SaveA,Build),
                    "String Boolean accepted");
            }),
            ("Shared avatar codec validates progression start profile save and build matches", () => {
                string json = RoleAvatarBindingJson.Serialize(Pilot,Initialized(),SaveA,Build);
                foreach (string field in new[] {"progressionPolicy","startPolicy","profileId"}) {
                    string bad = RewriteBinding(json,field,"\"foreign-policy\"");
                    RejectBinding(() => RoleAvatarBindingJson.Deserialize(Pilot,bad,SaveA,Build),
                        "Policy/profile mismatch accepted");
                }
                RejectBinding(() => RoleAvatarBindingJson.Deserialize(Pilot,json,SaveB,Build), "Wrong native snapshot rebound");
                RejectBinding(() => RoleAvatarBindingJson.Deserialize(Pilot,json,SaveA,SaveB), "Wrong native build rebound");
                RejectBinding(() => RoleAvatarBindingJson.Serialize(RoleAvatarProfile.CreatePilot(1,2),Initialized(),SaveA,Build),
                    "Serializer relabelled live campaign as a different donor");
            }),
            ("Shared avatar codec cannot save fresh unbound failed or staged initialization", () => {
                var fresh = RoleAvatarCampaignState.CreateNew(Pilot);
                RejectBinding(() => RoleAvatarBindingJson.Serialize(Pilot,fresh,SaveA,Build), "Fresh initializer serialized as completed");
                Check(fresh.ApplyNewGame(Cleo,() => {
                    RejectBinding(() => RoleAvatarBindingJson.Serialize(Pilot,fresh,SaveA,Build), "Staged initialization serialized");
                    return true;
                }), "Rejected serialization consumed fresh New Game admission");
                RejectBinding(() => RoleAvatarBindingJson.Serialize(Pilot,RoleAvatarCampaignState.CreateUnbound(),SaveA,Build),
                    "Unbound existing save acquired avatar binding");
                var failed = RoleAvatarCampaignState.CreateNew(Pilot);failed.ApplyNewGame(Cleo,() => false);
                RejectBinding(() => RoleAvatarBindingJson.Serialize(Pilot,failed,SaveA,Build), "Failed template copy serialized");
                var loaded = RoleAvatarBindingJson.Deserialize(Pilot,RoleAvatarBindingJson.Serialize(Pilot,fresh,SaveA,Build),SaveA,Build);
                bool called = false;
                Reject(() => loaded.ApplyNewGame(Cleo,() => {called = true;return true;}), "Deserialized save reopened donor start");
                Check(!called && fresh.IsInitialized && loaded.IsInitialized, "Codec changed campaign admission unexpectedly");
            }),
            ("Shared avatar codec stores no native HP MP experience inventory or templates", () => {
                string json = RoleAvatarBindingJson.Serialize(Pilot,Initialized(),SaveA,Build);
                using var document = JsonDocument.Parse(json);
                string[] forbidden = {"hp","mp","level","exp","items","equipment","growth","actions","playerBase","playerData"};
                Check(!document.RootElement.EnumerateObject().Any(item => forbidden.Contains(item.Name)
                    || item.Value.ValueKind == JsonValueKind.Array || item.Value.ValueKind == JsonValueKind.Object),
                    "Binding duplicated mutable native progression or cache objects");
                var world = new World();world.Role.Hp=17;world.Role.Exp=999;world.Role.Items=new[] {123};var saved=world.Role.Copy();
                var loaded=RoleAvatarBindingJson.Deserialize(Pilot,json,SaveA,Build);
                Check(loaded.ReplayTemplate(Pilot,Cleo,() => world.Transaction(world.ApplyReplay,
                    (before,after) => after.ProgressionEqual(before)).Committed)
                    && world.Role.ProgressionEqual(saved), "Metadata restore/replay imported donor progression");
            }),
            ("Shared avatar codec bounds UTF8 payload and nesting before restore", () => {
                string json = RoleAvatarBindingJson.Serialize(Pilot,Initialized(),SaveA,Build);
                string exact = json + new string(' ',RoleAvatarBindingJson.MaximumPayloadBytes - Encoding.UTF8.GetByteCount(json));
                Check(RoleAvatarBindingJson.Deserialize(Pilot,exact,SaveA,Build).IsInitialized, "Exact payload bound rejected valid JSON padding");
                Exception oversized = RejectBinding(() => RoleAvatarBindingJson.Deserialize(Pilot,exact+" ",SaveA,Build),
                    "Oversized document accepted");
                Check(oversized is JsonException && oversized.Message.Contains("payload bound"), "Oversize failed for the wrong reason");
                string multibyte = RewriteBinding(json,"profileId","\""+new string('é',20000)+"\"");
                Check(multibyte.Length < RoleAvatarBindingJson.MaximumPayloadBytes, "Test did not exercise UTF8/character distinction");
                Exception utf8 = RejectBinding(() => RoleAvatarBindingJson.Deserialize(Pilot,multibyte,SaveA,Build), "Multibyte oversized payload accepted");
                Check(utf8 is JsonException && utf8.Message.Contains("payload bound"), "UTF8 bound was not enforced before profile restore");
                string nested = RewriteBinding(json,"profileId","{\"a\":{\"b\":{\"c\":{\"d\":0}}}}");
                Check(RejectBinding(() => RoleAvatarBindingJson.Deserialize(Pilot,nested,SaveA,Build), "Excessive nesting accepted") is JsonException,
                    "Deep payload reached profile restore");
            })
        };
        int failed = 0;
        foreach (var test in tests)
        {
            try {test.Run(); Console.WriteLine("PASS " + test.Name);}
            catch (Exception exception) {failed++; Console.Error.WriteLine("FAIL " + test.Name + ": " + exception);}
        }
        Console.WriteLine($"{tests.Length-failed}/{tests.Length} role-avatar checks passed.");
        return failed == 0 ? 0 : 1;
    }

    // Detached test snapshots model actual adapter obligations: role identity,
    // progression and donor-cache equality are independently verified.
    private sealed class NativeImage
    {
        public int Code, Data, Base, Hp, Mp, Level, Exp, Appearance;
        public int[] Items = Array.Empty<int>();
        public int[] Equipment = Array.Empty<int>();
        public int[] Growth = Array.Empty<int>();
        public int[] Actions = Array.Empty<int>();
        public int[] Plot = Array.Empty<int>();
        public NativeImage Copy() => new NativeImage {Code=Code,Data=Data,Base=Base,Hp=Hp,Mp=Mp,Level=Level,Exp=Exp,Appearance=Appearance,
            Items=(int[])Items.Clone(),Equipment=(int[])Equipment.Clone(),Growth=(int[])Growth.Clone(),Actions=(int[])Actions.Clone(),Plot=(int[])Plot.Clone()};
        public bool CanonicalAndPlotEqual(NativeImage other) => Code==other.Code && Data==other.Data && Base==other.Base && Plot.SequenceEqual(other.Plot);
        public bool ProgressionEqual(NativeImage other) => Hp==other.Hp && Mp==other.Mp && Level==other.Level && Exp==other.Exp
            && Items.SequenceEqual(other.Items) && Equipment.SequenceEqual(other.Equipment);
        public bool TemplatesEqual(NativeImage other) => Appearance==other.Appearance && Growth.SequenceEqual(other.Growth) && Actions.SequenceEqual(other.Actions);
        public bool StartAndTemplatesEqual(NativeImage other) => ProgressionEqual(other) && TemplatesEqual(other);
        public bool AllEqual(NativeImage other) => CanonicalAndPlotEqual(other) && StartAndTemplatesEqual(other);
    }

    private sealed class World
    {
        public NativeImage Role = new NativeImage {Code=1,Data=1,Base=1,Hp=111,Mp=7,Level=5,Exp=30,Appearance=1,
            Items=new[]{11},Equipment=new[]{12},Growth=new[]{13,14},Actions=new[]{15},Plot=new[]{16}};
        public NativeImage Donor = new NativeImage {Code=0,Data=0,Base=0,Hp=222,Mp=9,Level=8,Exp=80,Appearance=0,
            Items=new[]{21},Equipment=new[]{22},Growth=new[]{23,24},Actions=new[]{25},Plot=new[]{26}};
        public int DonorRestoreCalls;

        public void ApplyStart()
        {
            ApplyReplay(); Role.Hp=Donor.Hp;Role.Mp=Donor.Mp;Role.Level=Donor.Level;Role.Exp=Donor.Exp;
            Role.Items=(int[])Donor.Items.Clone();Role.Equipment=(int[])Donor.Equipment.Clone();
        }
        public void ApplyReplay()
        {
            Role.Appearance=Donor.Appearance;Role.Growth=(int[])Donor.Growth.Clone();Role.Actions=(int[])Donor.Actions.Clone();
        }
        public AvatarTemplateTransactionRecord Transaction(Action apply, Func<NativeImage,NativeImage,bool> verify) =>
            AvatarTemplateTransaction.Execute(() => Role.Copy(),() => Donor.Copy(),apply,verify,
                before => {Role=before.Copy();}, before => {DonorRestoreCalls++;Donor=before.Copy();},
                (before,after) => before.AllEqual(after),(before,after) => before.AllEqual(after));
    }
}
