using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using SuikodenRandomizer.Suikoden2Plugin;

internal static class Program
{
    private static readonly JsonNode Golden = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "camp-world.json")))!;
    private static int _checks;
    private static void Check(bool condition, string message)
    { _checks++; if (!condition) throw new Exception(message); }
    private static T Reject<T>(Action run) where T : Exception
    {
        _checks++;
        try { run(); } catch (T exception) { return exception; }
        throw new Exception("Expected " + typeof(T).Name);
    }
    private static Il2CppStructArray<T> ArrayOf<T>(int count, string tag) where T : struct => new(count) { Tag = tag };
    private static Il2CppStructArray<byte> Bank(GAME_WORK game, string name) => name switch
    {
        "chara_flag" => game.chara_flag, "town_flag" => game.town_flag, "event_flag" => game.event_flag,
        "scpoi_flag" => game.scpoi_flag, "t_box_flag" => game.t_box_flag, _ => throw new Exception("Unknown test bank")
    };
    private static byte Mask(string name, int index)
    {
        if (Golden[name] is not JsonArray values) return 0;
        return (byte)values.Where(row => row![0]!.GetValue<int>() == index).Sum(row => row![1]!.GetValue<int>());
    }
    private static GAME_WORK Fresh(bool richState = false)
    {
        NativeWrites.Reset();
        var game = new GAME_WORK
        {
            game_data = new GAME_DATA
            {
                deru_pos = ArrayOf<int>(2, "game_data.deru_pos"), bozu_name = "Ríou", macd_name = "Imported McDohl", m_base_name = "Imported castle",
                difficulty = 3, language = 2, nakam_1_num = 108, untouched_settings = new { MessageSpeed = 2, Window = 7, Audio = "fresh" },
                play_time = new[] { 0, 0, 0 }, hanyou_po = 0, event_time = new[] { new[] { 0, 0 }, new[] { 0, 0 } }
            },
            party_data = new PARTY_DATA
            {
                party_cha_no = ArrayOf<byte>(8, "party_data.party_cha_no"), gold = 12345,
                party_item = new[] { new { Item = 55, Uses = 9 } }, event_item = new byte[] { 3, 4 }, graphics = new { OriginalCache = "preserve" }
            },
            chara_flag = ArrayOf<byte>(128, "chara_flag"), town_flag = ArrayOf<byte>(71, "town_flag"), event_flag = ArrayOf<byte>(256, "event_flag"),
            scpoi_flag = ArrayOf<byte>(8, "scpoi_flag"), t_box_flag = ArrayOf<byte>(32, "t_box_flag"),
            chara_data = new { VariableStarts = new[] { new { Hp = 33, Xp = 0, Armor = 91, Rune = 72 } }, OriginalKotei = new[] { 4, 8 }, Friendship = new[] { 5, 6 }, ImportedCharacter = new { Level = 52, Weapon = 14 } },
            c_teg_dat = new { Names = new[] { "new hero", "imported hero" } }, arms_data = new { WeaponInfo = new[] { 8, 9, 17 } },
            shop_data = new { event_no = 5, event_tim = 12, keve_stim = 99 }, war_data = new { FreshValue = 18 }, back_log_list = new[] { "fresh run only" }
        };
        game.party_data.party_cha_no[0] = 1;
        if (richState)
        {
            foreach (string name in new[] { "chara_flag", "town_flag", "event_flag", "scpoi_flag", "t_box_flag" })
            {
                var bank = Bank(game, name);
                for (int i = 0; i < bank.Count; i++) bank[i] = (byte)(0xAA & ~Mask(name, i));
            }
            game.event_flag[150] = 0x3F; // All reviewed optional-import bits, untouched by the checkpoint.
        }
        return game;
    }
    private static string Snapshot(GAME_WORK game) => JsonSerializer.Serialize(game);
    private static string Untouched(GAME_WORK game) => JsonSerializer.Serialize(new
    {
        game.chara_data, game.c_teg_dat, game.arms_data, game.shop_data, game.war_data, game.back_log_list,
        game.game_data.bozu_name, game.game_data.macd_name, game.game_data.m_base_name, game.game_data.difficulty, game.game_data.language,
        game.game_data.nakam_1_num, game.game_data.untouched_settings, game.game_data.play_time, game.game_data.hanyou_po, game.game_data.event_time,
        Party = game.party_data.party_cha_no.ToArray(), game.party_data.gold, game.party_data.party_item, game.party_data.event_item, game.party_data.graphics,
        Scenario = game.scpoi_flag.ToArray(), Treasure = game.t_box_flag.ToArray()
    });
    private static void Main()
    {
        var tests = new (string Name, Action Run)[]
        {
            ("applies the genuine interior fixture and effective saved position", () =>
            {
                var game = Fresh();var start = new Gsd2CampStart();start.Arm(true);
                Check(start.ApplyAtNativeBoundary(game) && start.IsApplied, "Fresh camp transaction was not applied");
                var expected = Golden["game_data"]!;
                Check(game.game_data.area_no == expected["area_no"]!.GetValue<int>() && game.game_data.town_no == expected["town_no"]!.GetValue<int>() && game.game_data.map_no == expected["map_no"]!.GetValue<int>(), "Wrong interior scene");
                Check(game.game_data.area_no2 == expected["area_no2"]!.GetValue<int>() && game.game_data.town_no2 == expected["town_no2"]!.GetValue<int>() && game.game_data.mem_type == expected["mem_type"]!.GetValue<byte>(), "Wrong interior return/memory state");
                Check(Gsd2CampStart.Point == "ViktorCamp" && Gsd2CampStart.SavePx == Golden["savedPosition"]![0]!.GetValue<int>() && Gsd2CampStart.SavePy == Golden["savedPosition"]![1]!.GetValue<int>(), "First save position policy disagrees with genuine capture");
                Check(game.game_data.deru_pos.SequenceEqual(new[] { Golden["savedPosition"]![0]!.GetValue<int>() * 8, Golden["savedPosition"]![1]!.GetValue<int>() * 8 }), "Used stale entry position instead of saved player position");
                Check(game.party_data.party_info == Golden["party_info"]!.GetValue<byte>() && game.party_data.party_cha_no.SequenceEqual(Golden["party_cha_no"]!.AsArray().Select(x => x!.GetValue<byte>())), "Wrong canonical party");
                foreach (string name in new[] { "chara_flag", "town_flag", "event_flag" })
                    for (int i = 0; i < Bank(game, name).Count; i++) Check(Bank(game, name)[i] == Mask(name, i), "Flag disagreement with original capture: " + name + "[" + i + "]");
            }),
            ("preserves fresh character/name/settings/import/economy data and native references", () =>
            {
                var game = Fresh(true);string before = Untouched(game);
                object[] references = { game.game_data, game.party_data, game.chara_data, game.c_teg_dat, game.arms_data, game.shop_data, game.game_data.deru_pos, game.party_data.party_cha_no, game.chara_flag, game.town_flag, game.event_flag };
                var original = new[] { game.chara_flag.ToArray(), game.town_flag.ToArray(), game.event_flag.ToArray() };
                var start = new Gsd2CampStart();start.Arm(true);start.ApplyAtNativeBoundary(game);
                Check(Untouched(game) == before, "Fresh data outside the allowlist changed");
                object[] after = { game.game_data, game.party_data, game.chara_data, game.c_teg_dat, game.arms_data, game.shop_data, game.game_data.deru_pos, game.party_data.party_cha_no, game.chara_flag, game.town_flag, game.event_flag };
                Check(references.Zip(after).All(pair => ReferenceEquals(pair.First, pair.Second)), "Transaction replaced a native reference");
                int b = 0;
                foreach (string name in new[] { "chara_flag", "town_flag", "event_flag" })
                {
                    var bank = Bank(game, name);
                    for (int i = 0; i < bank.Count; i++) Check(bank[i] == (byte)(original[b][i] | Mask(name, i)), "Unrelated flag bits changed");
                    b++;
                }
                Check(game.event_flag[150] == 0x3F && game.party_data.gold == 12345, "Import marker or fresh money was overwritten");
            }),
            ("unarmed, cancelled and later calls cannot mutate a world", () =>
            {
                var game = Fresh();string before = Snapshot(game);var start = new Gsd2CampStart();int writes = 0;
                NativeWrites.Before = (_, _) => writes++;
                Check(!start.ApplyAtNativeBoundary(game) && !start.IsApplied, "Unarmed call applied");
                start.Arm(true);start.Cancel();Check(!start.ApplyAtNativeBoundary(game), "Cancelled arm survived");
                Check(writes == 0 && Snapshot(game) == before, "Unarmed/cancelled call wrote data");
                start.Arm(true);Check(start.ApplyAtNativeBoundary(game), "Rearmed fresh call failed");string applied = Snapshot(game);writes = 0;
                Check(!start.ApplyAtNativeBoundary(game) && start.IsApplied && writes == 0 && Snapshot(game) == applied, "Later call reapplied checkpoint");
                start.Arm(false);Check(!start.IsApplied && !start.ApplyAtNativeBoundary(game), "Disabled arm did not clear lifecycle state");
            }),
            ("explicit new-campaign rearming applies once to the new fresh world", () =>
            {
                var start = new Gsd2CampStart();start.Arm(true);start.ApplyAtNativeBoundary(Fresh());
                var fresh = Fresh();start.Arm(true);Check(!start.IsApplied && start.ApplyAtNativeBoundary(fresh), "New campaign could not rearm independently");
                start.Cancel();Check(!start.IsApplied && !start.ApplyAtNativeBoundary(Fresh()), "Title cancellation retained an applied state");
            }),
            ("a nested native setter callback cannot consume or repeat the outer transaction", () =>
            {
                var game = Fresh();var start = new Gsd2CampStart();start.Arm(true);int attempts = 0;
                NativeWrites.Before = (_, _) => { attempts++;Check(!start.ApplyAtNativeBoundary(game), "Nested call entered transaction"); };
                Check(start.ApplyAtNativeBoundary(game) && start.IsApplied && attempts == 20, "Nested attempt disrupted the outer transaction");
            }),
            ("invalid layouts or nonfresh worlds are rejected before any write and consume the arm", () =>
            {
                var cases = new List<Action<GAME_WORK>>
                {
                    g=>g.game_data=null!,g=>g.party_data=null!,g=>g.game_data.deru_pos=null!,g=>g.game_data.deru_pos=ArrayOf<int>(1,"position"),g=>g.game_data.deru_pos=ArrayOf<int>(3,"position"),
                    g=>g.party_data.party_cha_no=null!,g=>g.party_data.party_cha_no=ArrayOf<byte>(7,"party"),g=>g.party_data.party_cha_no=ArrayOf<byte>(9,"party"),
                    g=>g.party_data.party_cha_no[0]=0,g=>g.party_data.party_cha_no[0]=2,g=>g.party_data.party_cha_no[1]=80,g=>g.party_data.party_cha_no[6]=80,
                    g=>g.party_data.party_info=1,g=>g.game_data.area_no=1,g=>g.game_data.town_no=1,g=>g.game_data.map_no=1,g=>g.game_data.area_no2=1,g=>g.game_data.town_no2=1,g=>g.game_data.mem_type=1,g=>g.game_data.deru_pos[0]=1,g=>g.game_data.deru_pos[1]=1,
                    g=>g.chara_flag=null!,g=>g.chara_flag=ArrayOf<byte>(127,"flags"),g=>g.town_flag=null!,g=>g.town_flag=ArrayOf<byte>(72,"flags"),g=>g.event_flag=null!,g=>g.event_flag=ArrayOf<byte>(255,"flags"),g=>g.scpoi_flag=null!,g=>g.scpoi_flag=ArrayOf<byte>(7,"flags"),g=>g.t_box_flag=null!,g=>g.t_box_flag=ArrayOf<byte>(31,"flags")
                };
                foreach (var invalidate in cases)
                {
                    var game = Fresh();invalidate(game);string before = Snapshot(game);int writes = 0;NativeWrites.Before = (_, _) => writes++;
                    var start = new Gsd2CampStart();start.Arm(true);Reject<InvalidOperationException>(()=>start.ApplyAtNativeBoundary(game));
                    Check(writes == 0 && Snapshot(game) == before && !start.IsApplied && !start.ApplyAtNativeBoundary(Fresh()), "Invalid input mutated data or retained its arm");
                }
                var missing = new Gsd2CampStart();missing.Arm(true);Reject<InvalidOperationException>(()=>missing.ApplyAtNativeBoundary(null!));Check(!missing.ApplyAtNativeBoundary(Fresh()), "Null-world rejection retained its arm");
            }),
            ("known opening progression cannot be applied over an already advanced flag state", () =>
            {
                foreach (string name in new[] { "chara_flag", "town_flag", "event_flag" })
                    foreach (var row in Golden[name]!.AsArray())
                    {
                        var game = Fresh();Bank(game,name)[row![0]!.GetValue<int>()] = row[1]!.GetValue<byte>();string before = Snapshot(game);int writes = 0;NativeWrites.Before = (_, _) => writes++;
                        var start = new Gsd2CampStart();start.Arm(true);Reject<InvalidOperationException>(()=>start.ApplyAtNativeBoundary(game));
                        Check(writes == 0 && Snapshot(game) == before && !start.ApplyAtNativeBoundary(Fresh()), "Existing progression was overwritten or arm survived");
                    }
            }),
            ("every before-write and after-write failure restores the exact fresh world", () =>
            {
                foreach (bool afterWrite in new[] { false, true })
                    for (int point = 1; point <= 20; point++)
                    {
                        var game = Fresh(true);string before = Snapshot(game);var start = new Gsd2CampStart();start.Arm(true);int count = 0;bool fired = false;
                        Action<string,object> fault = (_, _) => { count++;if (!fired && count == point) { fired = true;throw new InvalidOperationException("injected write failure"); } };
                        if (afterWrite) NativeWrites.After = fault;else NativeWrites.Before = fault;
                        var exception = Reject<InvalidOperationException>(()=>start.ApplyAtNativeBoundary(game));NativeWrites.Reset();
                        Check(fired && exception.Message == "injected write failure", "Fault injection did not reach transaction write " + point);
                        Check(Snapshot(game) == before && !start.IsApplied && !start.ApplyAtNativeBoundary(game), "Rollback did not restore exact values or consumed state at write " + point);
                    }
            }),
            ("rollback failure is aggregated and other fields still receive restoration attempts", () =>
            {
                var game = Fresh(true);string untouched = Untouched(game);var start = new Gsd2CampStart();start.Arm(true);bool failed = false;
                NativeWrites.After = (name, value) => { if (!failed && name == "game_data.deru_pos[1]") { failed = true;throw new InvalidOperationException("original write failed after mutation"); } };
                NativeWrites.Before = (name, value) => { if (failed && name == "game_data.deru_pos[1]" && (int)value == 0) throw new InvalidOperationException("one rollback setter failed"); };
                var aggregate = Reject<AggregateException>(()=>start.ApplyAtNativeBoundary(game));NativeWrites.Reset();
                Check(aggregate.InnerExceptions.Count == 2 && aggregate.InnerExceptions[0].Message == "original write failed after mutation" && aggregate.InnerExceptions[1].Message == "one rollback setter failed", "Rollback did not preserve both failures");
                Check(game.game_data.deru_pos[1] == 1648 && game.game_data.deru_pos[0] == 0 && game.game_data.area_no == 0 && game.game_data.town_no == 0 && game.game_data.map_no == 0 && game.game_data.area_no2 == 0 && game.game_data.town_no2 == 0 && game.game_data.mem_type == 0 && game.party_data.party_info == 0, "An undo failure prevented other location restorations");
                foreach (string name in new[] { "chara_flag", "town_flag", "event_flag" })
                    for (int i = 0; i < Bank(game,name).Count; i++)
                        Check(Bank(game,name)[i] == (byte)(name == "event_flag" && i == 150 ? 0x3F : 0xAA & ~Mask(name,i)), "An undo failure prevented flag restoration");
                Check(Untouched(game) == untouched && !start.IsApplied && !start.ApplyAtNativeBoundary(game), "Rollback failure changed unrelated data or left transaction armed");
            })
        };
        int failed = 0;
        foreach (var (name, run) in tests)
        {
            NativeWrites.Reset();
            try { run();Console.WriteLine("PASS " + name); }
            catch (Exception exception) { failed++;Console.WriteLine("FAIL " + name + ": " + exception); }
            finally { NativeWrites.Reset(); }
        }
        Console.WriteLine((tests.Length - failed) + "/" + tests.Length + " actual-linked camp-start checks passed; " + _checks + " assertions.");
        if (failed != 0) throw new Exception(failed + " camp-start checks failed.");
    }
}
