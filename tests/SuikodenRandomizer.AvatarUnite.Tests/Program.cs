extern alias GSD1;

using SuikodenRandomizer.AvatarPlugin;
using SuikodenRandomizer.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

internal static class Program
{
    private static readonly (string Name, int[] Participants)[] Groups =
    {
        ("Talisman", new[] { 2, 6 }), ("Fisherman", new[] { 32, 38 }),
        ("Couple", new[] { 30, 0 }), ("Trick", new[] { 39, 76 }),
        ("Master Pupil", new[] { 8, 91 }), ("Wild Arrow Sylvina", new[] { 4, 9 }),
        ("Wild Arrow Stallion", new[] { 4, 75 }), ("Wild Arrow Rubi", new[] { 4, 51 }),
        ("Kobold", new[] { 14, 80 }), ("Bandit", new[] { 12, 31 }),
        ("Fatal", new[] { 20, 67 }), ("Carpenter", new[] { 20, 95 }),
        ("Bumpy", new[] { 15, 21 }), ("Warrior", new[] { 63, 33 }),
        ("Dragon Knight", new[] { 19, 29 }), ("Family", new[] { 30, 0, 62 }),
        ("Elf", new[] { 4, 9, 75 }), ("Kobold +1", new[] { 14, 80, 65 }),
        ("Pirate", new[] { 11, 43, 41 }), ("Flash", new[] { 7, 18, 91 }),
        ("Ninja", new[] { 23, 22, 55 }), ("Pretty Boy", new[] { 17, 48, 49 }),
        ("Pretty Girl", new[] { 3, 33, 23 }), ("Martial Arts", new[] { 6, 66, 52 }),
        ("Beauty Valeria", new[] { 1, 0, 36 }), ("Beauty Sonya", new[] { 1, 0, 10 }),
        ("Blacksmith", new[] { 69, 70, 72, 5, 71 })
    };
    private static int _passed;
    private static readonly Type Patch = typeof(AvatarUniteCandidatePatch);
    private static readonly MethodBase NativeMethod = typeof(GSD1.BattleBase).GetMethod("check_kyoryoku")!;

    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
    private static void Test(string name, Action test)
    {
        Setup(new[] { 2, 6 });
        test();
        _passed++;
        Console.WriteLine("PASS " + name);
    }
    private static GSD1.KYORYOKU_DATA Group(params int[] participants) => new GSD1.KYORYOKU_DATA
    {
        player_kazu = checked((byte)participants.Length),
        player_id = new GSD1.NativeArray<byte>(participants.Select(x => checked((byte)x)).ToArray()),
        func_no = 42, type = 123, name = 456, help = 789
    };
    private static AvatarRuntime Setup(int[] players, params GSD1.KYORYOKU_DATA?[] groups)
    {
        var runtime = new AvatarRuntime();
        AvatarRuntime.Current = runtime;
        GSD1.BattleBase.battle_work = new GSD1.BATTLE_WORK
        {
            p_battle_data = new GSD1.NativeArray<GSD1.P_BATTLE_DATA?>(players.Select(id =>
                new GSD1.P_BATTLE_DATA { player_data = new GSD1.PLAYER_DATA { chara_id = id } }).ToArray())
        };
        GSD1.BattleBase.kyoryoku_data_table = new GSD1.NativeArray<GSD1.KYORYOKU_DATA?>(groups);
        return runtime;
    }
    private static void Swap(AvatarRuntime runtime, int changed, int[] present)
    {
        int other = CombatCatalog.NormalCombatIds.First(x => !present.Contains(x));
        runtime.Profile.Assignments[changed] = other;
        runtime.Profile.Assignments[other] = changed;
    }
    // This independent model checks participant membership. It is not the native
    // function body, which is unavailable in the supplied evidence.
    private static bool Matches(GSD1.KYORYOKU_DATA group, int[] players)
    {
        for (int n = 0; n < group.player_kazu; n++)
            if (!players.Contains(group.player_id![n])) return false;
        return group.player_kazu > 0;
    }
    private static (bool Run, int Result, AvatarUniteTableScope? State) Prefix(MethodBase? method = null)
    {
        object?[] args = { method ?? NativeMethod, 918273, null };
        bool run = (bool)Patch.GetMethod("Prefix", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, args)!;
        return (run, (int)args[1]!, (AvatarUniteTableScope?)args[2]);
    }
    private static void Postfix(AvatarUniteTableScope? state)
    {
        try { Patch.GetMethod("Postfix", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, new object?[] { state }); }
        catch (TargetInvocationException error) { throw error.InnerException!; }
    }
    private static Exception? Finalizer(Exception? exception, AvatarUniteTableScope? state) =>
        (Exception?)Patch.GetMethod("Finalizer", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, new object?[] { exception, state });

    private static void Main()
    {
        foreach (var fixture in Groups)
        {
            Test(fixture.Name + ": changed participant blocked and exact requirements restored", () =>
            {
                var group = Group(fixture.Participants);
                var runtime = Setup(fixture.Participants, group);
                Swap(runtime, fixture.Participants[^1], fixture.Participants);
                var array = group.player_id;
                var table = GSD1.BattleBase.kyoryoku_data_table;
                Check(Matches(group, fixture.Participants), "Baseline combination must match.");
                var call = Prefix();
                Check(call.Run && call.Result == 918273, "Native result/state machine must remain authoritative.");
                Check(call.State?.MaskedGroupCount == 1, "One incompatible group should be masked.");
                Check(!Matches(group, fixture.Participants), "Changed choreography remained eligible.");
                Check(CombatCatalog.IsNormalCombat(group.player_id![fixture.Participants.Length - 1]), "Mask must use a valid combat ID.");
                Check(ReferenceEquals(array, group.player_id) && ReferenceEquals(table, GSD1.BattleBase.kyoryoku_data_table), "Native arrays must retain identity.");
                Check(group.player_kazu == fixture.Participants.Length && group.func_no == 42 && group.type == 123 && group.name == 456 && group.help == 789, "Native combination shape/data changed.");
                Postfix(call.State);
                Check(Finalizer(null, call.State) is null, "Normal finalizer failed.");
                Check(fixture.Participants.SequenceEqual(Enumerable.Range(0, group.player_id.Count).Select(i => (int)group.player_id[i])), "Requirement restoration was not exact.");
                Check(Matches(group, fixture.Participants), "Native combination not restored after scope.");
            });
        }
        Test("Every listed unchanged combination remains native eligible", () =>
        {
            foreach (var fixture in Groups)
            {
                var group = Group(fixture.Participants);
                Setup(fixture.Participants, group);
                using var scope = AvatarUniteTableScope.Begin();
                Check(scope?.MaskedGroupCount == 0 && Matches(group, fixture.Participants), fixture.Name + " unnecessarily disabled.");
                Check(group.player_id!.Writes == 0, "Compatible requirements must never be rewritten.");
            }
        });
        Test("Compatible pair retained beside incompatible triple sharing a native array", () =>
        {
            var pair = Group(30, 0); var triple = Group(30, 0, 62);
            pair.player_id = triple.player_id;
            var runtime = Setup(new[] { 30, 0, 62 }, pair, triple);
            Swap(runtime, 62, new[] { 30, 0, 62 });
            using (var scope = AvatarUniteTableScope.Begin())
            {
                Check(Matches(pair, new[] { 30, 0, 62 }), "Compatible prefix pair disabled by aliased triple.");
                Check(!Matches(triple, new[] { 30, 0, 62 }), "Incompatible triple retained.");
            }
            Check(triple.player_id![2] == 62, "Shared buffer restoration failed.");
        });
        Test("Shared native requirement arrays are snapshotted before mutation and restored once", () =>
        {
            var a = Group(2, 6); var b = Group(2, 6); b.player_id = a.player_id;
            var runtime = Setup(new[] { 2, 6 }, a, b); Swap(runtime, 6, new[] { 2, 6 });
            using (var scope = AvatarUniteTableScope.Begin())
            {
                Check(scope?.MaskedGroupCount == 2 && a.player_id!.Writes == 1, "Shared native write was duplicated.");
            }
            Check(a.player_id![1] == 6 && a.player_id.Writes == 2, "Aliased restore captured an already masked value.");
        });
        Test("Nested hook scopes do not remap or restore the outer scope early", () =>
        {
            var group = Group(2, 6); var runtime = Setup(new[] { 2, 6 }, group); Swap(runtime, 6, new[] { 2, 6 });
            var outer = Prefix(); var inner = Prefix(typeof(GSD1.BattleBase).GetMethod("make_command_data"));
            Check(outer.State?.MaskedGroupCount == 1 && inner.State?.MaskedGroupCount == 0, "Nested scope repeated masking.");
            Postfix(inner.State); Finalizer(null, inner.State);
            Check(!Matches(group, new[] { 2, 6 }), "Inner completion restored outer data.");
            Postfix(outer.State); Finalizer(null, outer.State);
            Check(Matches(group, new[] { 2, 6 }) && group.player_id!.Writes == 2, "Outer restoration failed.");
        });
        Test("Original native exception is retained while finalizer restores requirements", () =>
        {
            var group = Group(2, 6); var runtime = Setup(new[] { 2, 6 }, group); Swap(runtime, 6, new[] { 2, 6 });
            var call = Prefix(); var original = new InvalidOperationException("Native simulated failure");
            Check(ReferenceEquals(Finalizer(original, call.State), original), "Native exception replaced.");
            Check(Matches(group, new[] { 2, 6 }), "Native exception leaked masked requirements.");
            Finalizer(original, call.State);
            Check(group.player_id!.Writes == 2, "Finalizer restoration is not idempotent.");
        });
        Test("Preflight validates every record before any native mutation", () =>
        {
            var first = Group(2, 6); var invalid = Group(2, 6); invalid.player_kazu = 3;
            var runtime = Setup(new[] { 2, 6 }, first, invalid); Swap(runtime, 6, new[] { 2, 6 });
            var call = Prefix();
            Check(!call.Run && runtime.FaultReason is not null && call.State is null, "Malformed list must stop native candidate building.");
            Check(first.player_id!.Writes == 0, "Validation changed an earlier native group.");
            Check(!Prefix().Run, "Faulted runtime fell through to unsafe native candidate building.");
        });
        Test("Partial masking failure rolls back all attempted native writes", () =>
        {
            var a = Group(2, 6); var b = Group(2, 6); b.player_id!.FailOnWrite = 1;
            var runtime = Setup(new[] { 2, 6 }, a, b); Swap(runtime, 6, new[] { 2, 6 });
            var call = Prefix();
            Check(!call.Run && runtime.FaultReason is not null, "Native write failure should fail closed.");
            Check(a.player_id![1] == 6 && b.player_id[1] == 6, "Partial write rollback leaked a mask.");
            Setup(new[] { 2, 6 }, Group(2, 6));
            using var scope = AvatarUniteTableScope.Begin();
            Check(scope?.MaskedGroupCount == 0, "Write failure leaked nesting depth.");
        });
        Test("Restore failure marks the runtime faulted and prevents future native fallback", () =>
        {
            var group = Group(2, 6); var runtime = Setup(new[] { 2, 6 }, group); Swap(runtime, 6, new[] { 2, 6 });
            var call = Prefix(); group.player_id!.FailOnWrite = 2;
            Exception? thrown = null;
            try { Postfix(call.State); } catch (Exception error) { thrown = error; }
            Check(thrown is not null && runtime.FaultReason is not null, "Failed native restoration must be surfaced and faulted.");
            Check(ReferenceEquals(Finalizer(thrown, call.State), thrown), "Restore error was swallowed.");
            Check(!Prefix().Run, "Restoration failure allowed another unsafe native call.");
        });
        Test("Null and native disabled sentinel records remain intact", () =>
        {
            var disabled = new GSD1.KYORYOKU_DATA(); var group = Group(2, 6);
            var runtime = Setup(new[] { 2, 6 }, null, disabled, group); Swap(runtime, 6, new[] { 2, 6 });
            using var scope = AvatarUniteTableScope.Begin();
            Check(scope?.MaskedGroupCount == 1 && GSD1.BattleBase.kyoryoku_data_table![0] is null && disabled.player_id is null, "Unused native table slots changed.");
        });
        Test("All populated battle-player slots are excluded from the substitute requirement", () =>
        {
            var group = Group(2, 6); var runtime = Setup(new[] { 0, 1, 2, 6, 8, 108 }, group); Swap(runtime, 6, new[] { 0, 1, 2, 6, 8, 108 });
            using var scope = AvatarUniteTableScope.Begin();
            Check(!new[] { 0, 1, 2, 6, 8, 108 }.Contains(group.player_id![1]), "Mask chose a present battle-player identity.");
            Check(CombatCatalog.IsNormalCombat(group.player_id[1]), "Mask chose an invalid or excluded identity.");
        });
        Test("Null battle-player holes do not contribute an identity", () =>
        {
            var group = Group(2, 6); var runtime = Setup(new[] { 2, 6 }, group); Swap(runtime, 6, new[] { 2, 6 });
            GSD1.BattleBase.battle_work!.p_battle_data = new GSD1.NativeArray<GSD1.P_BATTLE_DATA?>(null, new GSD1.P_BATTLE_DATA(), new GSD1.P_BATTLE_DATA { player_data = new GSD1.PLAYER_DATA { chara_id = 2 } }, new GSD1.P_BATTLE_DATA { player_data = new GSD1.PLAYER_DATA { chara_id = 6 } });
            using var scope = AvatarUniteTableScope.Begin();
            Check(!Matches(group, new[] { 2, 6 }), "Null player slots broke valid masking.");
        });
        Test("Inactive and missing runtimes leave native calls and result untouched", () =>
        {
            AvatarRuntime.Current!.Active = false; GSD1.BattleBase.battle_work = null;
            var inactive = Prefix(); Check(inactive.Run && inactive.Result == 918273 && inactive.State is null, "Inactive game changed.");
            AvatarRuntime.Current = null;
            var missing = Prefix(); Check(missing.Run && missing.Result == 918273 && missing.State is null, "Missing runtime changed native calls.");
        });
        Test("Missing battle data and invalid native IDs fail before eligibility executes", () =>
        {
            GSD1.BattleBase.battle_work = null;
            Check(!Prefix().Run && AvatarRuntime.Current!.FaultReason is not null, "Missing data passed native call.");
            Setup(new[] { 255 }, Group(2, 6)); Check(!Prefix().Run, "Invalid battle identity passed native call.");
            Setup(new[] { 2, 6 }, Group(2, 255)); Check(!Prefix().Run, "Invalid required identity passed native call.");
            Setup(Array.Empty<int>(), Group(2, 6)); Check(!Prefix().Run, "Empty battle data passed native call.");
        });
        Test("Safety hooks target all seven candidate builders and leave native cancellation unpatched", () =>
        {
            var targets = (IEnumerable<MethodBase>)Patch.GetMethod("Targets", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, null)!;
            string[] names = targets.Select(x => x.Name).ToArray();
            Check(names.Length == 7 && names.Distinct().Count() == 7 && names.Contains("check_kyoryoku") && names.Contains("command_kyoryoku10") && !names.Contains("command_kyoryoku99"), "Native candidate boundary coverage changed.");
        });
        Test("Repeated candidate checks do not spam safety logs", () =>
        {
            var group = Group(2, 6); var runtime = Setup(new[] { 2, 6 }, group); runtime.Profile.Fingerprint = "bounded-log-test"; Swap(runtime, 6, new[] { 2, 6 });
            for (int n = 0; n < 25; n++) { var call = Prefix(); Postfix(call.State); Finalizer(null, call.State); }
            Check(runtime.Logs.Count == 1 && runtime.Logs[0].Contains("incompatible native groups=1"), "Repeated command checks spammed logs.");
        });
        Console.WriteLine(_passed + "/" + _passed + " avatar Unite safety managed checks passed.");
        Console.WriteLine("Scope: actual production table scope and Harmony callbacks with explicit native models; 27 supplied participant combinations. Native matching, detours and full-plugin compilation are not executed.");
    }
}
