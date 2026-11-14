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
    private static IEnumerable<(string Name, Action Run)> EquipmentChecks()
    {
        yield return ("S2 compatible gear ranking prefers the role tier and has a deterministic legal fallback", () =>
        {
            Gsd2EquipmentBalance.Armor Piece(int id, int category, int defense, int price, bool ordinary = true) =>
                new() { Id = id, Category = category, Defense = defense, Price = price, Bonuses = new int[7], Ordinary = ordinary };
            var original = Piece(8, 1, 19, 2400);
            var candidates = new[] { Piece(10, 1, 24, 3500), Piece(7, 1, 14, 1200), Piece(12, 1, 19, 2400, false), Piece(40, 4, 19, 650) };
            var result = Gsd2EquipmentBalance.Comparable(original, candidates, _ => true, out bool fallback);
            Check(result?.Id == 7 && !fallback, "Equipment ranking chose special, wrong-slot, or above-role-tier gear");
            result = Gsd2EquipmentBalance.Comparable(original, candidates, item => item.Id != 7, out fallback);
            Check(result?.Id == 10 && fallback, "A legal closest substitute was left unequipped when no lower-tier gear existed");
            result = Gsd2EquipmentBalance.Comparable(original, candidates, _ => false, out fallback);
            Check(result is null && !fallback, "Equipment ranking invented a legal item for a character unable to wear that category");
            candidates = new[] { Piece(20, 1, 14, 1200), Piece(15, 1, 14, 1200) };
            result = Gsd2EquipmentBalance.Comparable(original, candidates, _ => true, out _);
            var reversed = Gsd2EquipmentBalance.Comparable(original, candidates.Reverse(), _ => true, out _);
            Check(result?.Id == 15 && reversed?.Id == 15, "Equal substitutes depended on catalog traversal order");
        });

        yield return ("S2 shuffle first resident gets original role inventory and armor without donor aliases", () =>
        {
            Gsd2Plugin.JoiningEquipment = "MatchRole";
            Gsd2Plugin.Mode = "Shuffle"; Gsd2Plugin.Seed = "gear-shuffle-first"; Gsd2Plugin.IncludeProtagonist = false;
            var game = Fixture(1); OldSrcBase.game_work = game;
            var runtime = new Gsd2Runtime(); runtime.BeginNewGame();
            var pair = runtime.Profile.Assignments.First(p => p.Key != p.Value);
            SetParty(game, 1, (byte)pair.Key);
            var original = game.chara_data;
            var gear = Gsd2EquipmentSnapshot.Capture(original.c_varia_dat[pair.Key]);
            var donor = Gsd2FighterRecord.Capture(original.c_varia_dat[pair.Value]);
            runtime.ObserveNativeDataset(); runtime.CompleteNativeNewGame();
            var actual = game.chara_data.c_varia_dat[pair.Key];
            Check(gear.Same(Gsd2EquipmentSnapshot.Capture(actual)), "Shuffle kept donor gear or omitted original item quantities");
            Check(actual.level == donor.Level && actual.exp == donor.Experience && actual.buki_mon == donor.WeaponRune
                && actual.mon_eqp.SequenceEqual(donor.Runes), "Equipment normalization changed donor progression/runes");
            Check(gear.Same(Gsd2EquipmentSnapshot.Capture(original.c_varia_dat[pair.Key])), "Original role gear was mutated");
            actual.bogu_eqp[0] = 202; actual.item_eqp[0].use_cnt = 2;
            Check(original.c_varia_dat[pair.Key].bogu_eqp[0] != 202 && original.c_varia_dat[pair.Value].bogu_eqp[0] != 202,
                "New gear shares native arrays with original or donor");
            runtime.Tick(); runtime.CompleteNativeNewGame();
            Check(actual.bogu_eqp[0] == 202 && actual.item_eqp[0].use_cnt == 2, "Repeated observation reset earned equipment/items");
        });

        yield return ("S2 shuffle persists unjoined original loadouts and initializes only at first resident join", () =>
        {
            Gsd2Plugin.JoiningEquipment = "MatchRole";
            Gsd2Plugin.Mode = "Shuffle"; Gsd2Plugin.Seed = "gear-future-save"; Gsd2Plugin.IncludeProtagonist = false;
            var game = Fixture(1); OldSrcBase.game_work = game;
            var runtime = new Gsd2Runtime(); runtime.BeginNewGame();
            var pair = runtime.Profile.Assignments.First(p => p.Key != p.Value);
            var wanted = Gsd2EquipmentSnapshot.Capture(game.chara_data.c_varia_dat[pair.Key]);
            var donorStart = Gsd2EquipmentSnapshot.Capture(game.chara_data.c_varia_dat[pair.Value]);
            runtime.ObserveNativeDataset(); runtime.CompleteNativeNewGame();
            Check(donorStart.Same(Gsd2EquipmentSnapshot.Capture(game.chara_data.c_varia_dat[pair.Key])),
                "Unjoined fighter equipment was applied before recruitment");
            runtime.BeginSave(); runtime.BindSnapshot("gear-before-recruit"); var saved = Gsd2NativeData.Characters(game.chara_data); runtime.EndSave();
            var node = JsonNode.Parse(File.ReadAllText(BindingFile("gear-before-recruit")))!;
            Check(node["JoiningEquipment"]!.GetValue<string>() == "MatchRole"
                && node["JoiningEquipmentInitializedDonors"]!.AsArray().Count == 0,
                "Saved pending gear was confused with initialized gear");
            Gsd2Plugin.JoiningEquipment = "Donor";
            game = Fixture(1); game.chara_data = saved; OldSrcBase.game_work = game;
            var loaded = new Gsd2Runtime(); loaded.PrepareLoad("gear-before-recruit"); loaded.CompleteLoad(true);
            SetParty(game, 1, (byte)pair.Key); loaded.Tick();
            Check(loaded.ActiveJoiningEquipment == "MatchRole" && wanted.Same(Gsd2EquipmentSnapshot.Capture(game.chara_data.c_varia_dat[pair.Key])),
                "Loaded pending role used donor start or current config instead of saved original loadout");
            game.chara_data.c_varia_dat[pair.Key].bogu_eqp[1] = 203;
            SetParty(game, 1); loaded.Tick(); SetParty(game, 1, (byte)pair.Key); loaded.Tick();
            Check(game.chara_data.c_varia_dat[pair.Key].bogu_eqp[1] == 203, "Rejoining shuffle fighter lost acquired armor");
        });

        foreach (string mode in new[] { "FixedChosen", "FixedRandom" })
        {
            string selected = mode;
            yield return ("S2 " + selected + " uses live first-role inventory once and carries it across roles/load", () =>
            {
                Gsd2Plugin.JoiningEquipment = "MatchRole";
                var runtime = Start(selected, 1); var game = OldSrcBase.game_work;
                game.chara_data.c_varia_dat[7].bogu_eqp[0] = 104;
                game.chara_data.c_varia_dat[7].item_eqp[1].item_no = 77;
                game.chara_data.c_varia_dat[7].item_eqp[1].use_cnt = 5;
                var wanted = Gsd2EquipmentSnapshot.Capture(game.chara_data.c_varia_dat[7]);
                SetParty(game, 1, 7); runtime.Tick();
                int donor = runtime.MapDisplayId(7);
                Check(wanted.Same(Gsd2EquipmentSnapshot.Capture(game.chara_data.c_varia_dat[7])), "Fixed first join ignored role event loadout");
                game.chara_data.c_varia_dat[7].bogu_eqp[0] = 204; game.chara_data.c_varia_dat[7].item_eqp[1].use_cnt = 1;
                SetParty(game, 1, 8); runtime.Tick();
                Check(runtime.MapDisplayId(8) == donor && game.chara_data.c_varia_dat[8].bogu_eqp[0] == 204
                    && game.chara_data.c_varia_dat[8].item_eqp[1].use_cnt == 1, "Moving a fixed fighter reset equipment or item uses");
                runtime.BeginSave(); runtime.BindSnapshot("gear-fixed-" + selected); var saved = Gsd2NativeData.Characters(game.chara_data); runtime.EndSave();
                Gsd2Plugin.JoiningEquipment = "Donor"; game = Fixture(1, 8); game.chara_data = saved; OldSrcBase.game_work = game;
                var loaded = new Gsd2Runtime(); loaded.PrepareLoad("gear-fixed-" + selected); loaded.CompleteLoad(true);
                Check(game.chara_data.c_varia_dat[8].bogu_eqp[0] == 204 && loaded.ActiveJoiningEquipment == "MatchRole", "Fixed reload reset equipped state or saved policy");
                var future = Gsd2EquipmentSnapshot.Capture(game.chara_data.c_varia_dat[9]);
                SetParty(game, 1, 8, 9); loaded.Tick();
                Check(future.Same(Gsd2EquipmentSnapshot.Capture(game.chara_data.c_varia_dat[9])), "Unused fixed donor failed saved MatchRole policy on later first assignment");
            });
        }

        yield return ("S2 legacy equipment saves remain donor-based including unused fixed fighters", () =>
        {
            var runtime = Start("FixedChosen", 1, 7); var game = OldSrcBase.game_work;
            game.chara_data.c_varia_dat[7].bogu_eqp[0] = 205;
            runtime.BeginSave(); runtime.BindSnapshot("legacy-equipment"); var saved = Gsd2NativeData.Characters(game.chara_data); runtime.EndSave();
            string path = BindingFile("legacy-equipment"); var node = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            node.Remove("JoiningEquipment"); node.Remove("JoiningEquipmentInitializedDonors"); node.Remove("OriginalRoleEquipment");
            File.WriteAllText(path, node.ToJsonString());
            Gsd2Plugin.JoiningEquipment = "MatchRole"; game = Fixture(1, 7); game.chara_data = saved; OldSrcBase.game_work = game;
            var loaded = new Gsd2Runtime(); loaded.PrepareLoad("legacy-equipment"); loaded.CompleteLoad(true);
            SetParty(game, 1, 7, 8); loaded.Tick();
            Check(loaded.ActiveJoiningEquipment == "Donor" && game.chara_data.c_varia_dat[7].bogu_eqp[0] == 205
                && game.chara_data.c_varia_dat[8].bogu_eqp[0] == 6, "Legacy save was retroactively re-equipped");
        });

        yield return ("S2 recruitment equipment rolls back a partial native write without committing history", () =>
        {
            Gsd2Plugin.JoiningEquipment = "MatchRole"; var runtime = Start("Shuffle", 1); var game = OldSrcBase.game_work;
            var pair = runtime.Profile.Assignments.First(p => p.Key != p.Value);
            var row = game.chara_data.c_varia_dat[pair.Key]; string before = JsonSerializer.Serialize(Gsd2FighterRecord.Capture(row));
            int priorLogs = Gsd2Plugin.PluginLog.Lines.Count;
            var armor = row.bogu_eqp; var item = row.item_eqp[0]; bool injected = false;
            Il2CppStructArray<byte>.BeforeWrite = (array, index, _) =>
            {
                if (ReferenceEquals(array, armor) && index == 1 && !injected) { injected = true; throw new InvalidOperationException("Injected equipment setter failure"); }
            };
            SetParty(game, 1, (byte)pair.Key); runtime.Tick();
            Check(injected && runtime.FaultReason is not null, "Equipment failure was not surfaced");
            Check(JsonSerializer.Serialize(Gsd2FighterRecord.Capture(row)) == before && ReferenceEquals(row.bogu_eqp, armor) && ReferenceEquals(row.item_eqp[0], item),
                "Equipment rollback changed fields or native references");
            Check(!Gsd2Plugin.PluginLog.Lines.Skip(priorLogs).Any(line => line.StartsWith("JOIN EQUIPMENT S2 role=" + pair.Key + "; donor=" + pair.Value, StringComparison.Ordinal)),
                "Failed recruitment equipment write committed its initialization log");
            Reject<InvalidOperationException>(() => runtime.BeginSave());
        });

        yield return ("S2 saving immediately after a join flushes equipment and binds exactly one initialization", () =>
        {
            Gsd2Plugin.JoiningEquipment = "MatchRole"; var runtime = Start("Shuffle", 1); var game = OldSrcBase.game_work;
            var pair = runtime.Profile.Assignments.First(p => p.Key != p.Value);
            SetParty(game, 1, (byte)pair.Key); runtime.BeginSave(); runtime.BindSnapshot("gear-save-boundary"); runtime.EndSave();
            var node = JsonNode.Parse(File.ReadAllText(BindingFile("gear-save-boundary")))!;
            Check(node["JoiningEquipmentInitializedDonors"]!.AsArray().Select(x => x!.GetValue<int>()).SequenceEqual(new[] { pair.Value }),
                "Save boundary omitted or duplicated recruitment initialization");
            string path = BindingFile("gear-save-boundary"); var bad = JsonNode.Parse(File.ReadAllText(path))!;
            bad["OriginalRoleEquipment"]!.AsObject().Remove(pair.Key.ToString(System.Globalization.CultureInfo.InvariantCulture));
            File.WriteAllText(path, bad.ToJsonString());
            Reject<InvalidDataException>(() => new Gsd2Runtime().PrepareLoad("gear-save-boundary"));
        });

        yield return ("S2 protected protagonist-related loadouts keep native equipment", () =>
        {
            Gsd2Plugin.JoiningEquipment = "MatchRole"; Gsd2Plugin.Mode = "Shuffle"; Gsd2Plugin.IncludeProtagonist = true; Gsd2Plugin.Seed = "protected-equipment";
            var game = Fixture(1, 80); OldSrcBase.game_work = game; var runtime = new Gsd2Runtime(); runtime.BeginNewGame();
            int donorRole = runtime.Profile.Assignments.Single(pair => pair.Value == 1).Key;
            var first = Gsd2EquipmentSnapshot.Capture(game.chara_data.c_varia_dat[1]);
            var other = Gsd2EquipmentSnapshot.Capture(game.chara_data.c_varia_dat[donorRole]);
            SetParty(game, 1, (byte)donorRole, 80); runtime.ObserveNativeDataset(); runtime.CompleteNativeNewGame(); runtime.Tick();
            Check(first.Same(Gsd2EquipmentSnapshot.Capture(game.chara_data.c_varia_dat[1])) && other.Same(Gsd2EquipmentSnapshot.Capture(game.chara_data.c_varia_dat[donorRole])),
                "Equipment balancing altered a protected protagonist-related loadout");
        });

        yield return ("S2 malformed saved equipment policy, history and loadouts are rejected", () =>
        {
            Gsd2Plugin.JoiningEquipment = "MatchRole"; var runtime = Start("Shuffle", 1); var game = OldSrcBase.game_work;
            var pair = runtime.Profile.Assignments.First(p => p.Key != p.Value);
            SetParty(game, 1, (byte)pair.Key); runtime.Tick();
            runtime.BeginSave(); runtime.BindSnapshot("gear-invalid-history"); runtime.EndSave();
            string path = BindingFile("gear-invalid-history"), valid = File.ReadAllText(path);
            foreach (Action<JsonObject> corrupt in new Action<JsonObject>[]
            {
                node => node["JoiningEquipment"] = "Unsupported",
                node => node["JoiningEquipmentInitializedDonors"] = new JsonArray(pair.Value, pair.Value),
                node => node["JoiningEquipmentInitializedDonors"] = new JsonArray(999),
                node => node["JoiningEquipmentInitializedDonors"] = null,
                node => node["OriginalRoleEquipment"] = null,
                node => node["OriginalRoleEquipment"]![pair.Key.ToString()]!["ItemUses"] = Convert.ToBase64String(new byte[] { 1, 2 }),
                node => node["OriginalRoleEquipment"]![pair.Key.ToString()] = null,
                node => node["JoiningEquipment"] = "Donor"
            })
            {
                var invalid = JsonNode.Parse(valid)!.AsObject(); corrupt(invalid); File.WriteAllText(path, invalid.ToJsonString());
                Reject<InvalidDataException>(() => new Gsd2Runtime().PrepareLoad("gear-invalid-history"));
            }
            File.WriteAllText(path, valid);
        });

        yield return ("S2 resident saved fighters require equipment initialization history before loading", () =>
        {
            Gsd2Plugin.JoiningEquipment = "MatchRole"; var runtime = Start("Shuffle", 1); var game = OldSrcBase.game_work;
            var pair = runtime.Profile.Assignments.First(p => p.Key != p.Value);
            SetParty(game, 1, (byte)pair.Key); runtime.Tick();
            runtime.BeginSave(); runtime.BindSnapshot("gear-missing-resident"); var saved = Gsd2NativeData.Characters(game.chara_data); runtime.EndSave();
            string path = BindingFile("gear-missing-resident"); var node = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            node["JoiningEquipmentInitializedDonors"] = new JsonArray(); File.WriteAllText(path, node.ToJsonString());
            game = Fixture(1, (byte)pair.Key); game.chara_data = saved; OldSrcBase.game_work = game;
            var loaded = new Gsd2Runtime(); loaded.PrepareLoad("gear-missing-resident");
            Reject<InvalidDataException>(() => loaded.CompleteLoad(true));
        });
    }
}
