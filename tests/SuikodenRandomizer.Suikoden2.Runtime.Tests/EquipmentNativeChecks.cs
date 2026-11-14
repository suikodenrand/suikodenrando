using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using SuikodenRandomizer.Suikoden2Plugin;

internal static partial class Program
{
    private static BOGU_DATA ArmorData(int classes, int category, int defense, int price) => new() {
        info = new Il2CppStructArray<byte>(2) { [0] = (byte)classes, [1] = (byte)category },
        def_val = (sbyte)defense, botch = (ushort)price, bonus = new Il2CppStructArray<sbyte>(7),
        atrb = new Il2CppStructArray<byte>(4), set_status = new Il2CppStructArray<byte>(3), blk_status = new Il2CppStructArray<byte>(3) };

    private static GAME_WORK NativeEquipmentFixture()
    {
        var game = Fixture(1);
        for (int index = 0; index < game.ovit_data.bogu_data.Count; index++) game.ovit_data.bogu_data[index] = ArmorData(0, 0, 0, 0);
        // Observed native master-table facts, without shipping the game's table.
        game.ovit_data.bogu_data[7] = ArmorData(2, 1, 14, 1200);
        game.ovit_data.bogu_data[9] = ArmorData(1, 1, 20, 1820);
        game.ovit_data.bogu_data[14] = ArmorData(6, 2, 2, 20);
        game.ovit_data.bogu_data[19] = ArmorData(3, 2, 14, 590);
        game.ovit_data.bogu_data[22] = ArmorData(1, 2, 19, 1200);
        game.ovit_data.bogu_data[38] = ArmorData(1, 4, 2, 30);
        for (int index = 45; index <= 47; index++) game.ovit_data.bogu_data[index] = ArmorData(126, 40, index - 44, (index - 44) * 10);
        for (int index = 50; index <= 52; index++) game.ovit_data.bogu_data[index] = ArmorData(125, 24, index - 48, (index - 49) * 30);
        return game;
    }

    private static (Gsd2Runtime Runtime, GAME_WORK Game, int Role, int Donor) UnwearableJoin(string mode, bool fullBag = false)
    {
        Gsd2Plugin.JoiningEquipment = "MatchRole"; Gsd2Plugin.JoiningWeaponLevel = "MatchRole";
        Gsd2Plugin.Mode = mode; Gsd2Plugin.Seed = "native-equipment-" + mode; Gsd2Plugin.IncludeProtagonist = false;
        Gsd2Plugin.Companions = "2,3,4,5,6";
        var game = Fixture(1); OldSrcBase.game_work = game; var runtime = new Gsd2Runtime(); runtime.BeginNewGame();
        var assignment = mode == "Shuffle" ? runtime.Profile.Assignments.First(pair => pair.Key != pair.Value && pair.Key != 26 && pair.Value != 26)
            : new KeyValuePair<int, int>(7, runtime.Profile.CompanionIds.First());
        game.chara_data.c_kotei_dat[assignment.Value].soubi_kanou = 0;
        var original = game.chara_data.c_varia_dat[assignment.Key]; original.bogu_eqp[0] = 10; original.bogu_eqp[1] = 11; original.bogu_eqp[2] = 12;
        for (int index = 0; index < 3; index++) { original.item_eqp[index].item_no = (byte)(index == 0 ? 1 : 0); original.item_eqp[index].use_cnt = (byte)(index == 0 ? 6 : 0); }
        if (fullBag) foreach (var item in game.party_data.party_item) { item.item_no = 7; item.use_cnt = 5; }
        runtime.ObserveNativeDataset(); runtime.CompleteNativeNewGame();
        return (runtime, game, assignment.Key, assignment.Value);
    }

    private static IEnumerable<(string Name, Action Run)> EquipmentNativeChecks()
    {
        yield return ("S2 native mask planner substitutes legal head/body gear and preserves raw item flags", () =>
        {
            var game = NativeEquipmentFixture(); game.chara_data.c_kotei_dat[2].soubi_kanou = 10; game.chara_data.ckd_info[2][1] = 0;
            var original = new Gsd2EquipmentSnapshot { Armor = new byte[] { 9, 22, 38 }, Items = new byte[] { 1, 50, 45 }, ItemUses = new byte[] { 6, 0x90, 0x10 } };
            var plan = Gsd2EquipmentBalance.Plan(game, game.chara_data, 7, 2, original);
            Check(plan.Armor.SequenceEqual(new byte[] { 7, 19, 0 }) && plan.TransferToBag.SequenceEqual(new byte[] { 38 }),
                "Native class masks did not select appropriate head/body substitutes or preserve an unusable shield");
            Check(plan.Items.SequenceEqual(original.Items) && plan.ItemUses.SequenceEqual(original.ItemUses), "Medicine, accessory kind or high flag bits changed");
            Check(original.Armor[2] == 38 && original.ItemUses[1] == 0x90, "Planning mutated the original role template");
            game.chara_data.c_kotei_dat[2].soubi_kanou = 37;
            game.ovit_data.bogu_data[9].set_status[0] = 16;
            plan = Gsd2EquipmentBalance.Plan(game, game.chara_data, 7, 2, original);
            Check(plan.Armor[0] == 9, "A legal original special item lost its effect through unnecessary replacement");
        });

        foreach (byte first in new byte[] { 45, 50 })
        {
            byte category = first;
            yield return ("S2 native accessory limits retain at most two " + (first == 45 ? "gloves" : "boots"), () =>
            {
                var game = NativeEquipmentFixture(); game.chara_data.ckd_info[2][1] = 0;
                var original = new Gsd2EquipmentSnapshot { Armor = new byte[3], Items = new byte[] { category, (byte)(category + 1), (byte)(category + 2) }, ItemUses = new byte[] { 0x90, 16, 16 } };
                var plan = Gsd2EquipmentBalance.Plan(game, game.chara_data, 7, 2, original);
                Check(plan.Items.SequenceEqual(new byte[] { category, (byte)(category + 1), 0 }) && plan.ItemUses.SequenceEqual(new byte[] { 0x90, 16, 0 })
                    && plan.TransferToBag.SequenceEqual(new byte[] { (byte)(category + 2) }), "Accessory cap, locked armor kind or lossless transfer differs from native rules");
            });
        }

        foreach (string mode in new[] { "Shuffle", "FixedChosen", "FixedRandom" })
        {
            string selected = mode;
            yield return ("S2 " + selected + " first join moves wholly unusable armor into the shared bag exactly once", () =>
            {
                var setup = UnwearableJoin(selected); var game = setup.Game; var runtime = setup.Runtime;
                game.party_data.party_item[0].item_no = 33; game.party_data.party_item[0].use_cnt = 2;
                SetParty(game, 1, (byte)setup.Role); runtime.Tick(); var fighter = game.chara_data.c_varia_dat[setup.Role];
                Check(fighter.bogu_eqp.All(item => item == 0) && fighter.item_eqp[0].item_no == 1 && fighter.item_eqp[0].use_cnt == 6,
                    "Unwearable gear remained equipped or original consumables were omitted");
                Check(game.party_data.party_item.Skip(1).Take(3).Select(item => item.item_no).SequenceEqual(new byte[] { 10, 11, 12 })
                    && game.party_data.party_item.Skip(1).Take(3).All(item => item.use_cnt == 0x10), "Unwearable armor was lost or encoded as a consumable");
                Check(game.party_data.party_item[0].item_no == 33 && game.party_data.party_item[0].use_cnt == 2, "Existing shared bag items changed");
                runtime.Tick(); runtime.BeginSave(); runtime.BindSnapshot("unwearable-" + selected); runtime.EndSave();
                Check(game.party_data.party_item.Count(item => item.item_no == 10 && item.use_cnt == 16) == 1, "A repeated observation/save duplicated unwearable armor");
            });
        }

        yield return ("S2 full shared bag defers first-join gear without loss and retries once space exists", () =>
        {
            var setup = UnwearableJoin("Shuffle", true); var game = setup.Game; var runtime = setup.Runtime;
            var before = Gsd2EquipmentSnapshot.Capture(game.chara_data.c_varia_dat[setup.Role]);
            int logs = Gsd2Plugin.PluginLog.Lines.Count;
            SetParty(game, 1, (byte)setup.Role); runtime.Tick(); runtime.Tick(); runtime.Tick();
            Check(runtime.FaultReason is null && before.Same(Gsd2EquipmentSnapshot.Capture(game.chara_data.c_varia_dat[setup.Role])), "Full bag caused partial gear writes or a fatal error");
            Check(Gsd2Plugin.PluginLog.Lines.Skip(logs).Count(line => line.Contains("pending role=" + setup.Role + "; donor=" + setup.Donor)) == 1, "Full-bag deferral spammed or omitted its explanation");
            runtime.BeginSave(); runtime.BindSnapshot("gear-full-bag"); runtime.EndSave();
            var binding = JsonNode.Parse(File.ReadAllText(BindingFile("gear-full-bag")))!;
            Check(binding["JoiningEquipmentInitializedDonors"]!.AsArray().Count == 0 && binding["PendingJoiningEquipment"]![setup.Donor.ToString()] is not null, "Deferred gear was falsely recorded as initialized");
            foreach (var item in game.party_data.party_item.Take(3)) { item.item_no = 0; item.use_cnt = 0; }
            runtime.Tick(); runtime.Tick();
            Check(game.chara_data.c_varia_dat[setup.Role].bogu_eqp.All(item => item == 0)
                && game.party_data.party_item.Take(3).Select(item => item.item_no).SequenceEqual(new byte[] { 10, 11, 12 }), "Deferred normalization did not resume safely when shared inventory space became available");
        });

        yield return ("S2 deferred first-join equipment never erases items earned while the shared bag is full", () =>
        {
            var setup = UnwearableJoin("Shuffle", true); var game = setup.Game; var runtime = setup.Runtime;
            SetParty(game, 1, (byte)setup.Role); runtime.Tick();
            var fighter = game.chara_data.c_varia_dat[setup.Role]; fighter.item_eqp[0].item_no = 44; fighter.item_eqp[0].use_cnt = 2;
            foreach (var item in game.party_data.party_item.Take(3)) { item.item_no = 0; item.use_cnt = 0; }
            var changed = Gsd2EquipmentSnapshot.Capture(fighter); runtime.Tick(); runtime.Tick();
            Check(changed.Same(Gsd2EquipmentSnapshot.Capture(fighter)) && game.party_data.party_item.Take(3).All(item => item.item_no == 0), "A delayed normalization erased an earned item or copied armor after cancellation");
            Check(Gsd2Plugin.PluginLog.Lines.Any(line => line.Contains("skipped role=" + setup.Role + "; donor=" + setup.Donor) && line.Contains("inventory changed")), "Deferred inventory change had no preservation trace");
        });

        yield return ("S2 fixed pending gear survives load and donor movement using its first original role", () =>
        {
            var setup = UnwearableJoin("FixedChosen", true); var game = setup.Game; var runtime = setup.Runtime;
            SetParty(game, 1, (byte)setup.Role); runtime.Tick(); runtime.BeginSave(); runtime.BindSnapshot("fixed-pending-gear");
            var saved = Gsd2NativeData.Characters(game.chara_data); runtime.EndSave();
            game = Fixture(1, (byte)setup.Role); game.chara_data = saved;
            foreach (var item in game.party_data.party_item) { item.item_no = 7; item.use_cnt = 5; }
            OldSrcBase.game_work = game; var loaded = new Gsd2Runtime(); loaded.PrepareLoad("fixed-pending-gear"); loaded.CompleteLoad(true);
            SetParty(game, 1, 8); loaded.Tick();
            foreach (var item in game.party_data.party_item.Take(3)) { item.item_no = 0; item.use_cnt = 0; }
            loaded.Tick();
            Check(loaded.FaultReason is null && loaded.MapDisplayId(8) == setup.Donor
                && game.party_data.party_item.Take(3).Select(item => item.item_no).SequenceEqual(new byte[] { 10, 11, 12 }), "Pending fixed donor forgot its first role, failed to load, or used the later role's inventory");
            var node = JsonNode.Parse(File.ReadAllText(BindingFile("fixed-pending-gear")))!;
            node["PendingJoiningEquipment"]![setup.Donor.ToString()]!["ObservedLoadout"] = null;
            File.WriteAllText(BindingFile("fixed-pending-gear"), node.ToJsonString());
            Reject<InvalidDataException>(() => new Gsd2Runtime().PrepareLoad("fixed-pending-gear"));
        });

        yield return ("S2 partial shared-bag setter failure rolls back both armor and bag before history commit", () =>
        {
            var setup = UnwearableJoin("Shuffle"); var game = setup.Game; var runtime = setup.Runtime;
            var fighter = game.chara_data.c_varia_dat[setup.Role]; var armor = fighter.bogu_eqp; var item = fighter.item_eqp[0];
            string before = JsonSerializer.Serialize(Gsd2FighterRecord.Capture(fighter)); bool injected = false;
            int logs = Gsd2Plugin.PluginLog.Lines.Count;
            PARTY_ITEM.BeforeUseWrite = (target, value) => {
                if (ReferenceEquals(target, game.party_data.party_item[1]) && value == 16 && !injected) { injected = true; throw new InvalidOperationException("Injected shared bag failure"); }
            };
            SetParty(game, 1, (byte)setup.Role); runtime.Tick();
            Check(injected && runtime.FaultReason is not null && JsonSerializer.Serialize(Gsd2FighterRecord.Capture(fighter)) == before
                && ReferenceEquals(fighter.bogu_eqp, armor) && ReferenceEquals(fighter.item_eqp[0], item), "Bag failure left character gear or native aliases changed");
            Check(game.party_data.party_item.All(entry => entry.item_no == 0 && entry.use_cnt == 0), "Bag failure left duplicated or partially granted armor");
            Check(!Gsd2Plugin.PluginLog.Lines.Skip(logs).Any(line => line.StartsWith("JOIN EQUIPMENT S2 role=" + setup.Role + "; donor=" + setup.Donor, StringComparison.Ordinal)), "Failed bag transaction committed first-join history/log");
        });
    }
}
