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
    private static void RunJoiningEquipmentChecks()
    {
        Case("Equipment-only first joins also retry incomplete formations", () =>
        {
            var w = EquipmentWorld(); var roleKit = JoiningEquipmentNative.Capture(w.Base(2));
            w.Ready(); SetVarianceParty(w, 8, 2); w.Game.party_data.narabi[1] = -1;
            w.Runtime.Tick();
            Check(w.Runtime.IsActive && w.Runtime.FaultReason is null, "Equipment observation faulted the campaign");
            w.Game.party_data.narabi[1] = 1; w.Runtime.Tick();
            Check(JoiningEquipmentNative.Capture(w.Base(2)).Same(roleKit), "Pending role inventory did not apply");
        });
        Case("A fixed fighter follows a replacement live base without reapplying first-join gear", () =>
        {
            var w = EquipmentWorld("FixedChosen"); w.Ready(); SetVarianceParty(w, 8, 2); w.Ready();
            var replacement = AvatarNativeData.CloneBase(w.Base(2));
            replacement.exp = 905; replacement.hp = 4; replacement.buki_data.level = 14;
            replacement.item[0].data = 2;
            string progress = AvatarNativeData.Fingerprint(replacement);
            int copies = AvatarNativeData.CopyCalls;
            w.Game.player_base[w.Game.player_base.IndexOf(w.Base(2))] = replacement;
            w.Ready(); w.Runtime.VerifyFinalParty("replacement fixed fighter");
            Check(w.Game.player_data[1]?.player_base?.Pointer == replacement.Pointer
                && w.Cache[2]?.Pointer == w.Game.player_data[1]?.Pointer
                && AvatarNativeData.Fingerprint(replacement) == progress && AvatarNativeData.CopyCalls == copies,
                "Rebinding the retained fighter reset progress or left a stale record");
            w.Game.party_data.narabi[1] = -1; w.Game.party_data.player_kazu = 1; w.Ready();
            Check(w.Game.player_data[1]?.Pointer == w.Originals[2].Pointer
                && w.Game.player_data[1]?.player_base?.Pointer == replacement.Pointer,
                "Releasing the fighter restored a native resident with its outgoing base");
            w.Game.party_data.narabi[1] = 1; w.Game.party_data.player_kazu = 2; w.Ready();
            w.Runtime.VerifyFinalParty("fixed fighter rejoin after base replacement");
            Check(AvatarNativeData.Fingerprint(replacement) == progress, "Rejoining lost the fighter's progress");
        });
        Case("Role inventory planner copies every item, count, charge and legal equipment flag independently", () =>
        {
            var source = EquipmentInventory((1, 0, 6), (2, 129, 0), (1, 0, 4));
            var entries = EquipmentCatalog(EquipmentEntry(1), EquipmentEntry(2, mask: 4, defense: 8, price: 100));
            var result = JoiningEquipmentPlan.Create(source, entries, 4, 0);
            Check(result.Inventory.Same(source) && result.Changes.Length == 0, "Compatible role inventory changed");
            result.Inventory.Items[0].Data = 1;
            Check(source.Items[0].Data == 6 && !ReferenceEquals(source.Items, result.Inventory.Items), "Inventory or item records alias the role template");
        });
        Case("Incompatible armor picks closest compatible basic item without exceeding role defense and price", () =>
        {
            var source = EquipmentInventory((2, 1, 0), (1, 0, 6));
            var entries = EquipmentCatalog(EquipmentEntry(1), EquipmentEntry(2, mask: 4, defense: 10, price: 100),
                EquipmentEntry(3, mask: 8, defense: 9, price: 90), EquipmentEntry(4, mask: 8, defense: 10, price: 120),
                EquipmentEntry(5, mask: 8, defense: 8, price: 80), EquipmentEntry(6, mask: 8, defense: 9, price: 90),
                EquipmentEntry(7, mask: 8, defense: 10, price: 100, function: 1));
            var plan = JoiningEquipmentPlan.Create(source, entries, 8, 0);
            Check(plan.Inventory.Items[0].Id == 3 && plan.Inventory.Items[0].Equipped == 1
                && plan.Inventory.Items[1].Data == 6, "Comparable replacement, stable ID tie or medicine charges differ");
        });
        Case("Gender restrictions are checked and closest legal basic item is used when role budget has no candidate", () =>
        {
            var entries = EquipmentCatalog(EquipmentEntry(2, mask: 4, defense: 2, price: 10),
                EquipmentEntry(3, mask: 8, defense: 3, price: 20, type: 0x100),
                EquipmentEntry(4, mask: 8, defense: 4, price: 30, type: 0x200));
            var plan = JoiningEquipmentPlan.Create(EquipmentInventory((2, 129, 0)), entries, 8, 1);
            Check(plan.Inventory.Items[0].Id == 4 && plan.Changes.Single().Contains("above role budget"),
                "Incompatible gender item or unnecessary unequip fallback selected");
        });
        Case("A fighter with no compatible shield keeps the role shield owned and clears equipped and locked flags", () =>
        {
            var source = EquipmentInventory((2, 129, 0), (1, 0, 6));
            var entries = EquipmentCatalog(EquipmentEntry(1), EquipmentEntry(2, mask: 64, defense: 10), EquipmentEntry(3, mask: 8, defense: 10));
            var result = JoiningEquipmentPlan.Create(source, entries, 8, 0);
            Check(result.Inventory.Items[0].Id == 2 && result.Inventory.Items[0].Equipped == 0
                && result.Inventory.Count == source.Count && result.Changes.Single().Contains("retained unequipped"), "Impossible shield forced into another slot or removed");
        });
        Case("A no-shop-price role item uses closest bounded native stats before price and records its exceptional tier", () =>
        {
            var entries = EquipmentCatalog(EquipmentEntry(2, mask: 4, defense: 10),
                EquipmentEntry(3, mask: 8, defense: 1, price: 50), EquipmentEntry(4, mask: 8, defense: 10, price: 10000));
            var plan = JoiningEquipmentPlan.Create(EquipmentInventory((2, 1, 0)), entries, 8, 0);
            Check(plan.Inventory.Items[0].Id == 4 && plan.Changes.Single().Contains("source has no shop price"),
                "Missing price silently overrode native stat proximity or went untraced");
        });
        Case("Unknown item and malformed inventory reject before modifying a reference", () =>
        {
            var source = EquipmentInventory((90, 1, 4)); var before = source.Clone();
            Check(EquipmentRejects(() => JoiningEquipmentPlan.Create(source, EquipmentCatalog(), 255, 0)) && source.Same(before), "Unknown item mutated or accepted");
            Check(EquipmentRejects(() => new JoiningEquipmentInventory { Count = 10, Items = new JoiningEquipmentItem[9] }.Validate()), "Invalid capacity accepted");
        });
        Case("Shuffle applies original role complete inventory only at first active join and preserves other character fields", () =>
        {
            var w = EquipmentWorld(); int donorId = w.Runtime.Profile.GetDonor(2);
            w.Base(donorId).item[0].data = 1; w.Base(donorId).item[1].item_id = 3;
            var originals = w.Bases.ToDictionary(pair => pair.Key, pair => JoiningEquipmentNative.Capture(pair.Value));
            var donor = AvatarNativeData.CloneBase(w.Base(donorId));
            w.Ready(); Check(!EquipmentRoles(w).Contains(2), "Inactive staged role consumed its first join");
            SetVarianceParty(w, 8, 2); w.Ready();
            Check(JoiningEquipmentNative.Capture(w.Base(2)).Same(originals[2]) && w.Base(2).item_kazu == 2,
                "New fighter retained late donor inventory instead of full original role inventory");
            Check(w.Base(2).level == donor.level && w.Base(2).Rune == donor.Rune
                && w.Base(2).buki_data.buki_id == donor.buki_data.buki_id && w.Base(2).buki_data.level == 13,
                "Equipment policy modified character progression, rune or weapon policy");
            Check(!ReferenceEquals(w.Base(2).item, w.Base(donorId).item)
                && !ReferenceEquals(w.Base(2).item[0], w.Base(donorId).item[0]), "Replacement inventory aliases another live role");
            w.Base(2).item[0].data = 1; w.Base(2).item[1].item_id = 3;
            SetVarianceParty(w, 8); w.Ready(); SetVarianceParty(w, 8, 2); w.Ready();
            Check(w.Base(2).item[0].data == 1 && w.Base(2).item[1].item_id == 3, "Rejoin reset consumed medicine or earned equipment");
        });
        Case("Shuffle legal armor conversion uses replacement character class and preserves role medicine", () =>
        {
            var w = EquipmentWorld(); int donor = w.Runtime.Profile.GetDonor(2); w.Originals[donor].soubi_type = 8;
            w.Base(2).item[1].item_id = 2; w.Base(2).item[1].soubi = 1;
            w.Ready(); SetVarianceParty(w, 8, 2); w.Ready();
            Check(w.Base(2).item[1].item_id == 3 && w.Base(2).item[1].soubi == 1 && w.Base(2).item[0].data == 6,
                "Live planner did not use donor native armor eligibility or role medicine");
        });
        Case("Native active party hook initializes equipment with either MatchRole or Level1 weapons before subsequent item grants", () =>
        {
            foreach (string weapons in new[] { "MatchRole", "Level1" })
            {
                var w = EquipmentWorld(); AvatarPlugin.ConfiguredJoiningWeaponLevel = weapons; w.Runtime.BeginNewGame(); w.Ready();
                SetVarianceParty(w, 8, 2); InvokePatch(typeof(FixedRosterPartyChangePatch), "Postfix");
                Check(EquipmentRoles(w).Contains(2) && w.Base(2).item[0].data == 6, "Native active join missed equipment under weapon " + weapons);
                w.Base(2).item[w.Base(2).item_kazu++] = new GSD1.PLAYER_ITEM { item_id = 1, data = 3 };
                w.Ready(); Check(w.Base(2).item_kazu == 3 && w.Base(2).item[2].data == 3, "Post-join event reward was overwritten");
            }
        });
        Case("Simultaneous Shuffle first joins roll back every inventory and do not commit initialization history", () =>
        {
            var w = EquipmentWorld(); w.Ready();
            var before = new[] { 2, 6 }.ToDictionary(id => id, id => AvatarNativeData.Fingerprint(w.Base(id)));
            var arrays = new[] { 2, 6 }.ToDictionary(id => id, id => w.Base(id).item);
            w.Base(6).FailInventoryWriteOnce = true; SetVarianceParty(w, 8, 2, 6);
            Check(!w.Runtime.EnsureProxy("inventory rollback", true) && w.Runtime.FaultReason is not null, "Injected inventory setter failure admitted");
            foreach (int role in new[] { 2, 6 }) Check(AvatarNativeData.Fingerprint(w.Base(role)) == before[role]
                && ReferenceEquals(w.Base(role).item, arrays[role]) && !EquipmentRoles(w).Contains(role), "Inventory transaction or history partially committed");
        });
        Case("Shuffle saves bind policy and canonical inventories; reload and future joins retain correct inventory", () =>
        {
            var w = EquipmentWorld(); var role6 = JoiningEquipmentNative.Capture(w.Base(6)); w.Ready(); SetVarianceParty(w, 8, 2); w.Ready();
            w.Base(2).item[0].data = 2; w.Base(2).item[1].item_id = 3;
            var save = VarianceSave(w, '1'); WriteEquipmentCampaign(w, save);
            AvatarPlugin.ConfiguredJoiningEquipment = "Donor"; w.Runtime.PrepareLoad(save); w.Ready();
            Check(w.Runtime.JoiningEquipmentPolicy == JoiningEquipmentPolicy.MatchRole && w.Base(2).item[0].data == 2
                && w.Base(2).item[1].item_id == 3, "Saved policy or earned inventory reset on reload");
            SetVarianceParty(w, 8, 2, 6); w.Ready();
            Check(JoiningEquipmentNative.Capture(w.Base(6)).Same(role6), "Future role used a donor or reconstructed shuffled template after load");
        });
        Case("Existing Shuffle saves without equipment binding preserve legacy donor inventory despite new default", () =>
        {
            var w = new World(); EquipmentDefinitions(); w.Ready();
            w.Base(2).item_kazu = 1; w.Base(2).item[0] = new GSD1.PLAYER_ITEM { item_id = 3, soubi = 1 };
            var before = JoiningEquipmentNative.Capture(w.Base(2)); var save = VarianceSave(w, '2');
            w.Runtime.WriteCampaignBinding(save, save.Identity); w.Runtime.WriteJoiningWeaponBinding(save.Identity);
            AvatarPlugin.ConfiguredJoiningEquipment = "MatchRole"; w.Runtime.PrepareLoad(save); w.Ready();
            Check(w.Runtime.JoiningEquipmentPolicy == JoiningEquipmentPolicy.Donor && JoiningEquipmentNative.Capture(w.Base(2)).Same(before), "Legacy save was retroactively re-equipped");
        });
        Case("Protected Tir loadouts and excluded story characters keep their native inventory", () =>
        {
            var w = EquipmentWorld(includeTir: true);
            var protectedIds = w.Runtime.Profile.RoleIds.Where(w.Runtime.Profile.KeepsCanonicalLoadout).Concat(new[] { CombatCatalog.TedId, CombatCatalog.OdessaId }).Distinct().ToArray();
            var before = protectedIds.ToDictionary(id => id, id => JoiningEquipmentNative.Capture(w.Base(id)));
            w.Ready(); foreach (int role in protectedIds.Where(role => role != 8 && w.Runtime.Profile.IsRole(role))) { SetVarianceParty(w, 8, role); w.Ready(); }
            Check(protectedIds.All(id => JoiningEquipmentNative.Capture(w.Base(id)).Same(before[id])), "Protected protagonist/story inventory changed");
        });
        foreach (string mode in new[] { "FixedChosen", "FixedRandom" })
        {
            Case(mode + " gives each fighter first role inventory and retains earned inventory through another story role and reload", () =>
            {
                var w = EquipmentWorld(mode); var role2 = JoiningEquipmentNative.Capture(w.Base(2)); w.Ready();
                SetVarianceParty(w, 8, 2); w.Ready(); int fighter = w.Runtime.MapDisplayId(2);
                Check(JoiningEquipmentNative.Capture(w.Base(2)).Same(role2) && EquipmentFighters(w).Contains(fighter), "Fixed first fighter inventory differs from role");
                w.Base(2).item[0].data = 1; w.Base(2).item[1].item_id = 3;
                SetVarianceParty(w, 8); w.Ready(); SetVarianceParty(w, 8, 6); w.Ready();
                Check(w.Runtime.MapDisplayId(6) == fighter && w.Base(6).item[0].data == 1 && w.Base(6).item[1].item_id == 3,
                    "Fixed fighter lost earned inventory on role transition");
                var save = VarianceSave(w, '3'); WriteEquipmentCampaign(w, save);
                AvatarPlugin.ConfiguredJoiningEquipment = "Donor"; w.Runtime.PrepareLoad(save); w.Runtime.CompleteFixedLoad(); w.Ready();
                Check(w.Runtime.JoiningEquipmentPolicy == JoiningEquipmentPolicy.MatchRole && w.Base(6).item[0].data == 1,
                    "Fixed saved first join history was not restored");
            });
        }
        Case("Fixed first-join transaction rollback retains role inventory and allows no partial equipment history", () =>
        {
            var w = EquipmentWorld("FixedChosen"); w.Ready(); var before = AvatarNativeData.Fingerprint(w.Base(2));
            AvatarNativeData.FailCopyRoleOnce = 2; SetVarianceParty(w, 8, 2);
            Check(!w.Runtime.EnsureProxy("fixed inventory rollback", true) && w.Runtime.FaultReason is not null
                && AvatarNativeData.Fingerprint(w.Base(2)) == before && EquipmentFighters(w).Count == 0, "Fixed inventory commit escaped rollback");
        });
        Case("Existing Fixed saves without equipment binding keep earned inventory instead of adopting MatchRole", () =>
        {
            var w = FixedWeaponWorld("MatchRole"); EquipmentDefinitions(); w.Ready(); ActivateWeaponRole(w, 1); w.Ready();
            w.Base(2).item_kazu = 1; w.Base(2).item[0] = new GSD1.PLAYER_ITEM { item_id = 3, soubi = 1 };
            var save = VarianceSave(w, '4'); w.Runtime.WriteCampaignBinding(save, save.Identity); w.Runtime.WriteJoiningWeaponBinding(save.Identity);
            AvatarPlugin.ConfiguredJoiningEquipment = "MatchRole"; w.Runtime.PrepareLoad(save); w.Runtime.CompleteFixedLoad(); w.Ready();
            Check(w.Runtime.JoiningEquipmentPolicy == JoiningEquipmentPolicy.Donor && w.Base(2).item[0].item_id == 3, "Legacy fixed inventory reset");
        });
        Case("Equipment binding round-trip is immutable and rejects unknown, duplicate and missing properties", () =>
        {
            var w = EquipmentWorld(); w.Ready(); SetVarianceParty(w, 8, 2); w.Ready(); var save = VarianceSave(w, '5'); WriteEquipmentCampaign(w, save);
            string path = EquipmentBindingPath(save); string original = File.ReadAllText(path);
            var binding = JoiningEquipmentBindingStore.Read(save.Identity, w.Runtime.CampaignFingerprint, w.Runtime.Profile.Seed)!;
            w.Runtime.WriteJoiningEquipmentBinding(save.Identity);
            Check(binding.Policy == JoiningEquipmentPolicy.MatchRole && binding.InitializedRoles.Contains(2), "Equipment metadata did not round-trip");
            Check(EquipmentRejects(() => JoiningEquipmentBindingStore.Write(save.Identity, w.Runtime.CampaignFingerprint, w.Runtime.Profile.Seed,
                new JoiningEquipmentBinding(JoiningEquipmentPolicy.Donor, Array.Empty<int>(), Array.Empty<int>(), new Dictionary<int, JoiningEquipmentInventory>()))), "Bound snapshot changed equipment policy");
            foreach (string bad in new[] { original.Replace("\"policy\":", "\"unknown\":0,\"policy\":"),
                original.Replace("\"policy\":", "\"policy\":\"MatchRole\",\"policy\":"), original.Replace("\"policy\":\"MatchRole\",", "") })
            {
                File.WriteAllText(path, bad);
                Check(EquipmentRejects(() => JoiningEquipmentBindingStore.Read(save.Identity, w.Runtime.CampaignFingerprint, w.Runtime.Profile.Seed)), "Malformed equipment metadata accepted");
            }
            File.WriteAllText(path, original);
            Check(EquipmentRejects(() => JoiningEquipmentBindingStore.Read(save.Identity, new string('0', 64), w.Runtime.Profile.Seed)), "Equipment crossed campaign boundary");
        });
        Case("Missing initialized active role or canonical role reference stops loading without changing inventory", () =>
        {
            foreach (string field in new[] { "initializedRoles", "canonicalRoleInventories" })
            {
                var w = EquipmentWorld(); w.Ready(); SetVarianceParty(w, 8, 2); w.Ready(); var save = VarianceSave(w, '6'); WriteEquipmentCampaign(w, save);
                var before = JoiningEquipmentNative.Capture(w.Base(2)); var json = JsonNode.Parse(File.ReadAllText(EquipmentBindingPath(save)))!.AsObject();
                json[field] = new JsonArray(); File.WriteAllText(EquipmentBindingPath(save), json.ToJsonString()); w.Runtime.PrepareLoad(save);
                Check(w.Runtime.FaultReason is not null && !w.Runtime.IsAdmitted && JoiningEquipmentNative.Capture(w.Base(2)).Same(before), "Incomplete equipment history adopted or mutated inventory");
            }
        });
    }

    private static bool EquipmentRejects(Action action) { try { action(); return false; } catch (Exception) { return true; } }
    private static JoiningEquipmentInventory EquipmentInventory(params (ushort Id, byte Equipped, byte Data)[] values) => new()
    {
        Count = (byte)values.Length,
        Items = values.Select(value => new JoiningEquipmentItem { Id = value.Id, Equipped = value.Equipped, Data = value.Data })
            .Concat(Enumerable.Range(values.Length, 9 - values.Length).Select(_ => new JoiningEquipmentItem())).ToArray()
    };
    private static JoiningEquipmentCatalogEntry EquipmentEntry(ushort id, byte mask = 0, byte defense = 0, int price = 0, ushort type = 0, byte function = 0)
    {
        byte[]? bonuses = mask == 0 ? null : new byte[8]; if (bonuses is not null) bonuses[7] = defense;
        return new(id, price, type, mask, function, bonuses);
    }
    private static IReadOnlyDictionary<ushort, JoiningEquipmentCatalogEntry> EquipmentCatalog(params JoiningEquipmentCatalogEntry[] entries) => entries.ToDictionary(value => value.Id);
    private static void EquipmentDefinitions()
    {
        var bonuses = new List<byte>(new byte[8]); bonuses[7] = 10;
        GSD1.G_item_c_data.item_data_table = new List<GSD1.ITEM_DATA?> { null, new GSD1.ITEM_DATA(),
            new GSD1.BOUGU_DATA { soubi_type = 4, nedan = 100, equip_data = bonuses.ToList() },
            new GSD1.BOUGU_DATA { soubi_type = 8, nedan = 90, equip_data = bonuses.ToList() } };
    }
    private static World EquipmentWorld(string mode = "Shuffle", bool includeTir = false)
    {
        var w = new World(false); w.StageOpening(); EquipmentDefinitions();
        AvatarPlugin.ConfiguredJoiningEquipment = "MatchRole";
        AvatarPlugin.ConfiguredJoiningWeaponLevel = "MatchRole"; AvatarPlugin.ConfiguredIncludeTir = includeTir;
        AvatarPlugin.ConfiguredMode = mode; AvatarPlugin.ConfiguredCompanions = "16,11,12,13,14";
        foreach (var pair in w.Bases)
        {
            pair.Value.buki_data.buki_id = (byte)pair.Key; pair.Value.buki_data.level = 13;
            pair.Value.item_kazu = 2; pair.Value.item[0] = new GSD1.PLAYER_ITEM { item_id = 1, data = (byte)(1 + pair.Key % 6) };
            pair.Value.item[1] = new GSD1.PLAYER_ITEM { item_id = 2, soubi = 1 };
        }
        w.Base(2).item[0].data = 6; w.Runtime.BeginNewGame(); return w;
    }
    private static HashSet<int> EquipmentRoles(World w) => (HashSet<int>)typeof(AvatarRuntime).GetField("_joiningEquipmentRoles", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(w.Runtime)!;
    private static HashSet<int> EquipmentFighters(World w) => (HashSet<int>)typeof(AvatarRuntime).GetField("_joiningEquipmentFighters", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(w.Runtime)!;
    private static string EquipmentBindingPath(GSD1.SaveDataManager.SaveData save) => Path.Combine(AvatarRuntime.Folder, "joining-equipment-save-bindings", save.Identity + ".json");
    private static void WriteEquipmentCampaign(World w, GSD1.SaveDataManager.SaveData save)
    { w.Runtime.WriteCampaignBinding(save, save.Identity); w.Runtime.WriteJoiningWeaponBinding(save.Identity); w.Runtime.WriteJoiningEquipmentBinding(save.Identity); }
}
