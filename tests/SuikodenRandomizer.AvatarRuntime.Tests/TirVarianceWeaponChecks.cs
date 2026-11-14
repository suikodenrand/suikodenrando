extern alias GSD1;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json.Nodes;
using SuikodenRandomizer.AvatarPlugin;
using SuikodenRandomizer.Core;

internal static partial class Program
{
    private static void RunTirVarianceWeaponChecks()
    {
        Case("TirVariance defers reserve and recruited-only weapons until the first actual active join", () =>
        {
            var w = VarianceWorld(); w.Ready();
            Check(JoinedWeaponRoles(w).Count == 0 && w.Base(2).buki_data.level == 13,
                "An inactive staged resident was normalized during eager template setup");
            w.Game.member_flag[2] = 1; w.Ready();
            Check(JoinedWeaponRoles(w).Count == 0 && w.Base(2).buki_data.level == 13,
                "A recruited reserve consumed its first active join");
            SetVarianceParty(w, 8, 2); w.Ready();
            Check(w.Base(2).buki_data.level == ExpectedVariance(w, 2, 7) && JoinedWeaponRoles(w).SetEquals(new[] { 2 }),
                "First actual Gremio-role join did not use current Tir plus the seeded offset");
            Check(w.Base(2).buki_data.buki_id == w.Runtime.Profile.GetDonor(2), "Deferred balancing changed donor weapon identity");
        });

        Case("Separate joins use Tir's live weapon at each join, including when Tir is absent from the active party", () =>
        {
            var w = VarianceWorld(); w.Ready();
            w.Base(8).buki_data.level = 4; SetVarianceParty(w, 8, 2); w.Ready();
            Check(w.Base(2).buki_data.level == ExpectedVariance(w, 2, 4), "First join used a stale starting Tir level");
            w.Base(8).buki_data.level = 12;
            w.Originals[8].player_base = AvatarNativeData.CloneBase(w.Base(8));
            w.Originals[8].player_base!.buki_data.level = 1;
            SetVarianceParty(w, 6); w.Ready();
            Check(w.Base(6).buki_data.level == ExpectedVariance(w, 6, 12), "Tir reference came from the stale native asset or required Tir to be active");
        });

        Case("A simultaneous join batch uses one current Tir snapshot and leaves both Tir-protected loadouts unchanged", () =>
        {
            var w = VarianceWorld(includeTir: true); w.Ready();
            int borrowedTirRole = w.Runtime.Profile.Assignments.Single(pair => pair.Value == CombatCatalog.TirId).Key;
            string protectedLoadout = AvatarNativeData.Fingerprint(w.Base(borrowedTirRole));
            w.Base(8).buki_data.level = 9;
            var roles = w.Runtime.Profile.RoleIds.Where(id => !w.Runtime.Profile.KeepsCanonicalLoadout(id)).Take(3).ToArray();
            SetVarianceParty(w, new[] { 8, borrowedTirRole }.Concat(roles).ToArray()); w.Ready();
            Check(w.Base(8).buki_data.level == 9 && AvatarNativeData.Fingerprint(w.Base(borrowedTirRole)) == protectedLoadout,
                "Tir normalized his own reference or a protected borrowed-Tir loadout changed");
            foreach (int role in roles) Check(w.Base(role).buki_data.level == ExpectedVariance(w, role, 9), "Simultaneous joins used different Tir snapshots");
            Check(JoinedWeaponRoles(w).SetEquals(roles), "Protected role consumed joining history");
        });

        Case("Seeded offsets include every integer from minus three through zero to plus three and ignore party order", () =>
        {
            var first = VarianceWorld(); first.Ready();
            var offsets = first.Runtime.Profile.RoleIds.Select(id => first.Runtime.JoiningWeaponDelta(id, first.Runtime.Profile.GetDonor(id))).ToHashSet();
            Check(offsets.SetEquals(Enumerable.Range(-3, 7)), "Seeded offsets omitted zero or escaped the inclusive minus-three through plus-three range");
            SetVarianceParty(first, 8, 2, 6, 1); first.Ready();
            var levels = new[] { 2, 6, 1 }.ToDictionary(id => id, id => first.Base(id).buki_data.level);
            var second = VarianceWorld(); second.Ready();
            SetVarianceParty(second, 1, 6, 2, 8); second.Ready();
            Check(levels.All(pair => second.Base(pair.Key).buki_data.level == pair.Value), "Join enumeration order advanced a global RNG stream");
        });

        Case("TirVariance clamps only the final weapon result at native levels one and sixteen", () =>
        {
            var w = VarianceWorld(); w.Ready();
            int negative = w.Runtime.Profile.RoleIds.First(id => w.Runtime.JoiningWeaponDelta(id, w.Runtime.Profile.GetDonor(id)) < 0);
            int positive = w.Runtime.Profile.RoleIds.First(id => w.Runtime.JoiningWeaponDelta(id, w.Runtime.Profile.GetDonor(id)) > 0);
            w.Base(8).buki_data.level = 1; SetVarianceParty(w, 8, negative); w.Ready();
            Check(w.Base(negative).buki_data.level == 1, "Negative offset escaped the minimum weapon level");
            w.Base(8).buki_data.level = 16; SetVarianceParty(w, 8, positive); w.Ready();
            Check(w.Base(positive).buki_data.level == 16, "Positive offset escaped the maximum weapon level");
        });

        Case("Sharpening and per-role first-join history survive rejoin, reorder and v2 save reload", () =>
        {
            var w = VarianceWorld(); w.Ready(); SetVarianceParty(w, 8, 2); w.Ready(); w.Base(2).buki_data.level = 15;
            SetVarianceParty(w, 8); w.Ready(); w.Base(8).buki_data.level = 2;
            SetVarianceParty(w, 2, 8); w.Ready();
            Check(w.Base(2).buki_data.level == 15, "Rejoin or reorder lowered a sharpened weapon");
            var save = VarianceSave(w, '1'); w.Runtime.WriteCampaignBinding(save); w.Runtime.WriteJoiningWeaponBinding(save);
            var json = JsonNode.Parse(File.ReadAllText(WeaponBindingPath(save)))!.AsObject();
            Check(json["joiningWeaponVersion"]!.GetValue<int>() == 2 && json["initializedRoles"]!.AsArray().Count == 1,
                "The new binding did not persist role history in schema v2");
            AvatarPlugin.ConfiguredJoiningWeaponLevel = "Level1";
            w.Runtime.PrepareLoad(save); w.Ready();
            Check(w.Runtime.JoiningWeaponPolicy == JoiningWeaponPolicy.TirVariance && w.Base(2).buki_data.level == 15,
                "Reload forgot saved policy/history or re-applied first-join normalization");
            w.Base(8).buki_data.level = 11; SetVarianceParty(w, 8, 2, 6); w.Ready();
            Check(w.Base(2).buki_data.level == 15 && w.Base(6).buki_data.level == ExpectedVariance(w, 6, 11),
                "Reload either reset an old fighter or failed to initialize a new joining role");
        });

        Case("Fixed TirVariance applies once per donor and its offset does not depend on the first story slot", () =>
        {
            var w = FixedWeaponWorld("TirVariance"); w.Base(8).buki_data.level = 6; w.Ready();
            int delta = w.Runtime.JoiningWeaponDelta(2, 16);
            Check(delta == w.Runtime.JoiningWeaponDelta(6, 16), "A fixed fighter's offset depended on its first story role");
            ActivateWeaponRole(w, 1); w.Ready();
            Check(w.Base(2).buki_data.level == Math.Clamp(6 + delta, 1, 16), "Fixed donor did not use current Tir at its first active assignment");
            w.Base(2).buki_data.level = 15; w.Base(8).buki_data.level = 2;
            ActivateWeaponRole(w, 2); w.Ready();
            Check(w.Base(6).buki_data.level == 15, "A fixed donor's new story role re-applied joining variance");
            w.Game.party_data.narabi[2] = 3; w.Game.party_data.player_kazu = 3; w.Ready();
            Check(w.Runtime.MapDisplayId(1) == 11 && w.Base(1).buki_data.level == Math.Clamp(2 + w.Runtime.JoiningWeaponDelta(1, 11), 1, 16),
                "Unused fixed donor failed to adopt the later current Tir level");
            var save = VarianceSave(w, '2'); w.Runtime.WriteCampaignBinding(save); w.Runtime.WriteJoiningWeaponBinding(save);
            w.Runtime.PrepareLoad(save); w.Game.player_data[2] = w.Originals[6]; w.Game.player_data[3] = w.Originals[1];
            w.Runtime.CompleteFixedLoad(); w.Ready();
            Check(w.Base(6).buki_data.level == 15, "Fixed save reload discarded initialized-donor history");
        });

        Case("Regular active Tick notices a join and forced save preparation resolves pending first joins", () =>
        {
            var w = VarianceWorld(); w.Ready(); SetVarianceParty(w, 8, 2); w.Runtime.Tick();
            Check(JoinedWeaponRoles(w).Contains(2), "Regular active Tick skipped first-join observation");
            SetVarianceParty(w, 8, 2, 6);
            var save = VarianceSave(w, '3'); bool refused = false;
            try { w.Runtime.WriteJoiningWeaponBinding(save); } catch (InvalidOperationException) { refused = true; }
            Check(refused, "Post-snapshot binding silently accepted an uninitialized active role");
            Check(w.Runtime.EnsureProxy("save preparation", false, force: true), "Forced save preparation did not process pending joins");
            w.Runtime.WriteJoiningWeaponBinding(save);
            Check(JoinedWeaponRoles(w).SetEquals(new[] { 2, 6 }), "Save preparation did not commit all active first joins");
        });

        Case("A stale proxy cannot consume first-join history before binding to the new canonical live base", () =>
        {
            var w = VarianceWorld(); w.Ready(); SetVarianceParty(w, 8, 2);
            var old = w.Base(2); var fresh = AvatarNativeData.CloneBase(old);
            w.Game.player_base[w.Game.player_base.IndexOf(old)] = fresh; w.Bases[2] = fresh;
            w.Runtime.Tick();
            Check(!JoinedWeaponRoles(w).Contains(2) && fresh.buki_data.level == 13 && w.Runtime.FaultReason is null,
                "Outgoing proxy consumed history or normalized an unbound replacement base");
            w.Ready();
            Check(JoinedWeaponRoles(w).Contains(2) && w.Runtime.ProxyFor(2)!.player_base!.Pointer == fresh.Pointer
                && fresh.buki_data.level == ExpectedVariance(w, 2, 7), "Current canonical binding did not complete deferred first join");
        });

        Case("A failed deferred join batch restores the same weapon objects and commits neither history nor success logs", () =>
        {
            var w = VarianceWorld(); w.Ready(); SetVarianceParty(w, 8, 2, 6);
            var firstWeapon = w.Base(2).buki_data; var secondWeapon = w.Base(6).buki_data;
            var before = new[] { 2, 6 }.ToDictionary(id => id, id => AvatarNativeData.Fingerprint(w.Base(id)));
            int logs = AvatarPlugin.PluginLog.Messages.Count; secondWeapon.FailLevelWriteOnce = true;
            Check(!w.Runtime.EnsureProxy("deferred failure", true) && w.Runtime.FaultReason is not null, "Injected deferred setter failure did not stop publication");
            Check(ReferenceEquals(firstWeapon, w.Base(2).buki_data) && ReferenceEquals(secondWeapon, w.Base(6).buki_data)
                && before.All(pair => AvatarNativeData.Fingerprint(w.Base(pair.Key)) == pair.Value)
                && firstWeapon.level == 13 && JoinedWeaponRoles(w).Count == 0,
                "Failed deferred batch left stale weapon references, changed levels or consumed first joins");
            Check(!AvatarPlugin.PluginLog.Messages.Skip(logs).Any(line => line.Contains("JOINING WEAPON INITIALIZED")), "Failed join batch logged success");
        });

        Case("Saved active-role history is checked against the incoming snapshot before native restoration", () =>
        {
            var w = VarianceWorld(); w.Ready(); SetVarianceParty(w, 8, 2); w.Ready(); w.Base(2).buki_data.level = 15;
            var save = VarianceSave(w, '4'); w.Runtime.WriteCampaignBinding(save); w.Runtime.WriteJoiningWeaponBinding(save);
            var json = JsonNode.Parse(File.ReadAllText(WeaponBindingPath(save)))!.AsObject(); json["initializedRoles"] = new JsonArray();
            File.WriteAllText(WeaponBindingPath(save), json.ToJsonString());
            SetVarianceParty(w, 8); // Outgoing party omits the saved active role.
            w.Runtime.PrepareLoad(save);
            Check(w.Runtime.FaultReason is not null && !w.Runtime.IsAdmitted && w.Base(2).buki_data.level == 15,
                "Incomplete history was checked against the outgoing world or re-normalized saved upgrades");
        });

        Case("Version-one saved MatchRole and fixed Level1 policies retain their existing behavior", () =>
        {
            var w = new World(); w.Ready(); w.Base(2).buki_data.level = 14;
            var save = WeaponSave('5'); w.Runtime.WriteCampaignBinding(save); w.Runtime.WriteJoiningWeaponBinding(save); DowngradeWeaponBinding(save);
            AvatarPlugin.ConfiguredJoiningWeaponLevel = "TirVariance"; w.Runtime.PrepareLoad(save); w.Ready();
            Check(w.Runtime.JoiningWeaponPolicy == JoiningWeaponPolicy.MatchRole && w.Base(2).buki_data.level == 14, "Legacy v1 MatchRole adopted the new default");
            w.Runtime.WriteJoiningWeaponBinding(save); // Existing v1 file remains immutable and equivalent.
            w = FixedWeaponWorld("Level1"); ActivateWeaponRole(w, 1); w.Ready(); w.Base(2).buki_data.level = 14;
            save = WeaponSave('6'); w.Runtime.WriteCampaignBinding(save); w.Runtime.WriteJoiningWeaponBinding(save); DowngradeWeaponBinding(save);
            AvatarPlugin.ConfiguredJoiningWeaponLevel = "TirVariance";
            w.Runtime.PrepareLoad(save); w.Game.player_data[1] = w.Originals[2]; w.Runtime.CompleteFixedLoad(); w.Ready();
            ActivateWeaponRole(w, 2); w.Ready();
            Check(w.Runtime.JoiningWeaponPolicy == JoiningWeaponPolicy.Level1 && w.Base(6).buki_data.level == 14,
                "Legacy v1 fixed donor history was lost or its sharpened weapon was lowered");
        });

        Case("Incomplete current Tir weapon state waits without consuming first joins and required boundaries refuse it", () =>
        {
            var w = VarianceWorld(); w.Ready(); SetVarianceParty(w, 8, 2); w.Base(8).buki_data.level = 0;
            w.Runtime.Tick();
            Check(w.Runtime.FaultReason is null && JoinedWeaponRoles(w).Count == 0 && w.Base(2).buki_data.level == 13,
                "Transient incomplete Tir state became an invalid reference or consumed a join");
            Check(!w.Runtime.EnsureProxy("battle admission", true) && w.Runtime.FaultReason is not null && JoinedWeaponRoles(w).Count == 0,
                "Required boundary admitted an uninitialized Tir reference");
        });
    }

    private static World VarianceWorld(bool includeTir = false)
    {
        var w = new World(false); w.StageOpening();
        foreach (var pair in w.Bases) { pair.Value.buki_data.buki_id = (byte)pair.Key; pair.Value.buki_data.level = 13; }
        w.Base(8).buki_data.level = 7; AvatarPlugin.ConfiguredJoiningWeaponLevel = "TirVariance"; AvatarPlugin.ConfiguredIncludeTir = includeTir;
        w.Runtime.BeginNewGame(); return w;
    }
    private static void SetVarianceParty(World w, params int[] roles)
    {
        w.Game.party_data.chara_code = roles.Select(id => (short)id).Concat(Enumerable.Repeat((short)-1, 6 - roles.Length)).ToList();
        w.Game.party_data.narabi = Enumerable.Range(0, roles.Length).Select(slot => (sbyte)slot).Concat(Enumerable.Repeat((sbyte)-1, 6 - roles.Length)).ToList();
        w.Game.party_data.player_kazu = (byte)roles.Length;
        w.Game.player_data = w.Game.party_data.chara_code.Select(id => id < 0 ? null : w.Runtime.ProxyFor(id) ?? w.Originals[id]).ToList();
    }
    private static byte ExpectedVariance(World w, int role, int tir) => (byte)Math.Clamp(tir + w.Runtime.JoiningWeaponDelta(role, w.Runtime.Profile.GetDonor(role)), 1, 16);
    private static HashSet<int> JoinedWeaponRoles(World w) => (HashSet<int>)typeof(AvatarRuntime)
        .GetField("_joiningWeaponRoles", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(w.Runtime)!;
    private static GSD1.SaveDataManager.SaveData VarianceSave(World w, char identity) => new()
    {
        Identity = new string(identity, 64), party_data = new GSD1.PARTY_DATA
        {
            chara_code = w.Game.party_data.chara_code.ToList(), narabi = w.Game.party_data.narabi.ToList(), player_kazu = w.Game.party_data.player_kazu
        }
    };
    private static string WeaponBindingPath(GSD1.SaveDataManager.SaveData save) => Path.Combine(AvatarRuntime.Folder, "joining-weapon-save-bindings", save.Identity + ".json");
    private static void DowngradeWeaponBinding(GSD1.SaveDataManager.SaveData save)
    {
        string path = WeaponBindingPath(save); var json = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        json["joiningWeaponVersion"] = 1; json.Remove("initializedRoles"); File.WriteAllText(path, json.ToJsonString());
    }
}
