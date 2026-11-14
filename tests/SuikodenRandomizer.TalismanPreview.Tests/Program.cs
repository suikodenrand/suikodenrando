// Links production projection/admission/observation code. The explicitly named
// NativeModel helpers below are managed simulations of reviewed native effects;
// passing these checks does not certify native detours, damage formulas or animation.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using SuikodenRandomizer.AvatarPlugin;
using Scheduler = SuikodenRandomizer.S1BattlePreview.BattlePreviewPlugin;

internal static class Program
{
    private static int _passed;
    private static AvatarRuntime _runtime = null!;
    private const int GremioSlot = 2, PahnSlot = 4, EnemySlot = 6;
    private static BATTLE_WORK Work => BattleBase.battle_work!;
    private static KYORYOKU_DATA Talisman => BattleBase.kyoryoku_data_table[1];
    private static PM_DATA Actor(int slot) => Work.pm_data[slot];

    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
    private static void Case(string name, Action test)
    {
        Setup();
        test();
        _passed++;
        Console.WriteLine("PASS " + name);
    }
    private static Exception Throws(Action action)
    {
        try { action(); }
        catch (Exception error) { return error; }
        throw new InvalidOperationException("Expected a failure, but the operation succeeded.");
    }
    private static object? Call(Type type, string method, params object?[] args)
    {
        try
        {
            return (type.GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(type.Name, method)).Invoke(null, args);
        }
        catch (TargetInvocationException error) { throw error.InnerException!; }
    }
    private static KYORYOKU_DATA Recipe(byte function, params byte[] ids) => new KYORYOKU_DATA
    {
        player_kazu = checked((byte)ids.Length), player_id = new NativeArray<byte>(ids), func_no = function,
        name = 501, help = 502, type = 503
    };

    private static void Setup(bool enabled = true)
    {
        Scheduler.ActiveAtbEnabled = false;
        Scheduler.OneShotAttackEnabled = false;
        _runtime = new AvatarRuntime();
        AvatarRuntime.Current = _runtime;
        _runtime.Mapping.Add(39, 2);
        _runtime.Mapping.Add(91, 6);
        _runtime.Mapping.Add(2, 63);
        _runtime.Mapping.Add(6, 3);
        int[] roles = { -1, 8, 39, 2, 91, 6 };
        var players = new P_BATTLE_DATA[6];
        var characters = new B_CHARA[6];
        var actors = new PM_DATA[7];
        for (int i = 1; i <= 5; i++)
        {
            players[i] = new P_BATTLE_DATA { player_data = new PLAYER_DATA { chara_id = roles[i] } };
            characters[i] = new B_CHARA
            {
                chara_id = roles[i], action_data_table = new NativeArray<Il2CppStructArray<short>>(
                    Enumerable.Range(0, 16).Select(_ => new Il2CppStructArray<short>((short)1)).ToArray())
            };
            actors[i] = new PM_DATA { hp = 100 + i, c_koudou = 0, c_mokuhyo = EnemySlot };
        }
        actors[EnemySlot] = new PM_DATA { hp = 500 };
        BattleBase.battle_work = new BATTLE_WORK
        {
            player_kazu = 5, pm_kazu = 6,
            p_battle_data = new NativeArray<P_BATTLE_DATA>(players), b_chara = new NativeArray<B_CHARA>(characters),
            pm_data = new NativeArray<PM_DATA>(actors)
        };
        BattleBase.kyoryoku_data_table = new NativeArray<KYORYOKU_DATA>(
            null!, Recipe(1, 6, 2), Recipe(5, 39, 91), Recipe(9, 8, 20));
        Talisman.type = 3; // Qualified shipped native cctor: Pahn first, Gremio second.
        BattleBase.kmagic01_work = null;
        BattleBase.DeadCheck = _ => false;
        AvatarTalismanPreview.Configure(enabled);
    }

    // Membership model is independent of the production resolver: native
    // requirements are compared directly with canonical p_battle_data identities.
    private static int[] NativeModelMembers(KYORYOKU_DATA recipe)
    {
        var slots = new List<int>();
        for (int member = 0; member < recipe.player_kazu; member++)
        {
            int found = 0;
            for (int slot = 1; slot <= Work.player_kazu; slot++)
                if (Work.p_battle_data[slot].player_data.chara_id == recipe.player_id[member]
                    && Actor(slot).hp > 0 && Actor(slot).dead_flg == 0 && (Actor(slot).status & 0x61) == 0)
                {
                    Check(found == 0, "Native model fixture contains ambiguous canonical IDs.");
                    found = slot;
                }
            if (found == 0) return Array.Empty<int>();
            slots.Add(found);
        }
        return slots.ToArray();
    }

    private static void NativeModelCommit()
    {
        foreach (int slot in NativeModelMembers(Talisman))
        { Actor(slot).c_koudou = 4; Actor(slot).c_item = 1; Actor(slot).c_mokuhyo = EnemySlot; }
    }

    // Reviews FinishFunc's important effects without substituting this model for
    // evidence of its real native invocation: fill recipe-ordered slots, consume
    // both actors once and copy the leader's selected target to every participant.
    private static void NativeModelFinish(int leader, Il2CppStructArray<int> slots)
    {
        var members = NativeModelMembers(Talisman);
        Check(members.Length == 2, "Native fixture could not find both projected participants.");
        for (int i = 0; i < members.Length; i++)
        {
            slots[i] = members[i];
            Actor(members[i]).finished_flg = 1;
            Actor(members[i]).c_mokuhyo = Actor(leader).c_mokuhyo;
        }
    }

    private static void Commit()
    {
        object?[] args = { 12345, null };
        Check((bool)Call(typeof(AvatarTalismanCommitPatch), "Prefix", args)!, "Commit projection failed.");
        try { NativeModelCommit(); }
        finally { Call(typeof(AvatarTalismanCommitPatch), "Postfix", args[1]); }
        Check(Talisman.player_id[0] == 6 && Talisman.player_id[1] == 2, "Commit left shared requirements projected.");
    }
    private static AvatarTalismanPreview.Execution Admit(int leader = GremioSlot)
    {
        object?[] args = { leader, 12345, null };
        Check((bool)Call(typeof(AvatarTalismanAdmissionPatch), "Prefix", args)! && args[2] is AvatarTalismanPreview.Execution,
            "Qualified group was not admitted.");
        var action = (AvatarTalismanPreview.Execution)args[2]!;
        Call(typeof(AvatarTalismanAdmissionPatch), "Postfix", 1, action);
        return action!;
    }
    private static Il2CppStructArray<int> Prepare(int leader = GremioSlot)
    {
        var ids = new Il2CppStructArray<int>(0, 0);
        BattleBase.kmagic01_work = new KMAGIC01_WORK { p_id = ids };
        object?[] args = { leader, ids, null };
        Call(typeof(AvatarTalismanFinishPatch), "Prefix", args);
        var state = (AvatarTalismanPreview.FinishScope?)args[2];
        Check(state is not null, "Admitted sequence did not acquire its finish scope.");
        try
        {
            Check(Talisman.player_id[0] == 91 && Talisman.player_id[1] == 39, "FinishFunc reader did not receive projected roles.");
            NativeModelFinish(leader, ids);
            Call(typeof(AvatarTalismanFinishPatch), "Postfix", ids, state);
        }
        finally { Call(typeof(AvatarTalismanFinishPatch), "Finalizer", null, state); }
        return ids;
    }
    private static int CompletedLogs() => _runtime.Logs.Count(x => x.Contains("Talisman completed:"));
    private static bool CandidatePrefix(out AvatarUniteTableScope? state)
    {
        object?[] args = { typeof(BattleBase).GetMethod("check_kyoryoku")!, 12345, null };
        bool result = (bool)Call(typeof(AvatarUniteCandidatePatch), "Prefix", args)!;
        state = (AvatarUniteTableScope?)args[2];
        return result;
    }

    private static void Main()
    {
        Case("Startup opt-out registers no preview hooks and keeps existing Unite masking", () =>
        {
            AvatarTalismanPreview.Configure(false);
            Check(!AvatarTalismanPreview.Enabled && AvatarUniteTableScope.ProjectRequirements is null, "Opt-out left a projection registered.");
            foreach (var type in new[] { typeof(AvatarTalismanCommitPatch), typeof(AvatarTalismanAdmissionPatch),
                typeof(AvatarTalismanFinishPatch), typeof(AvatarTalismanDamagePatch), typeof(AvatarTalismanCompletionPatch),
                typeof(AvatarTalismanBoundaryPatch), typeof(AvatarTalismanSequenceGuardPatch) })
                Check(!(bool)Call(type, "Prepare")!, "Disabled startup admitted " + type.Name);
            using (var scope = AvatarUniteTableScope.Begin())
                Check(!NativeModelMembers(Talisman).Any(), "Opt-out enabled unsafe original-role choreography.");
            Check(Talisman.player_id[0] == 6 && Talisman.player_id[1] == 2, "Opt-out changed persistent requirements.");
        });

        Case("Qualified natural fighters expose Talisman on their actual foreign story slots", () =>
        {
            var originalPointers = Enumerable.Range(1, 5).Select(i => Actor(i).Pointer).ToArray();
            using (var scope = AvatarUniteTableScope.Begin())
            {
                Check(Talisman.player_id[0] == 91 && Talisman.player_id[1] == 39, "Talisman was not projected to donor hosts.");
                Check(NativeModelMembers(Talisman).SequenceEqual(new[] { 4, 2 }), "Canonical matcher selected the original story roles.");
                Check(scope!.MaskedGroupCount == 1, "Unqualified changed recipe lost its safety mask.");
                Check(!NativeModelMembers(BattleBase.kyoryoku_data_table[2]).Any(), "Another shuffled choreography was accidentally enabled.");
                Check(BattleBase.kyoryoku_data_table[3].player_id[0] == 8 && BattleBase.kyoryoku_data_table[3].player_id[1] == 20,
                    "An unchanged recipe was edited.");
            }
            Check(Talisman.player_id[0] == 6 && Talisman.player_id[1] == 2, "Recipe was not restored.");
            Check(Talisman.func_no == 1 && Talisman.name == 501 && Talisman.help == 502 && Talisman.type == 3, "Recipe behavior fields changed.");
            Check(Enumerable.Range(1, 5).Select(i => Actor(i).Pointer).SequenceEqual(originalPointers), "Projection replaced live battle actors.");
            Check(Work.p_battle_data[2].player_data.chara_id == 39 && Work.b_chara[4].chara_id == 91, "Persistent/canonical IDs were rewritten.");
        });

        Case("Nested reader scopes see one projection and restore only after the outer reader", () =>
        {
            using (var outer = AvatarUniteTableScope.Begin())
            {
                int writes = Talisman.player_id.Writes;
                using (var nested = AvatarUniteTableScope.Begin())
                    Check(NativeModelMembers(Talisman).SequenceEqual(new[] { 4, 2 }), "Nested reader lost projected requirements.");
                Check(Talisman.player_id.Writes == writes && Talisman.player_id[0] == 91, "Nested disposal restored the outer scope early.");
            }
            Check(Talisman.player_id[0] == 6, "Outer disposal failed to restore native IDs.");
        });

        Case("Altered native function identity fails closed before shared table writes", () =>
        {
            Talisman.func_no = 2;
            Check(!CandidatePrefix(out var state) && state is null && _runtime.FaultReason is not null, "Wrong native choreography was admitted.");
            Check(Talisman.player_id.Writes == 0, "Failed preflight wrote shared requirements.");
        });

        Case("Reversed or malformed recipe membership fails closed", () =>
        {
            Talisman.player_id = new NativeArray<byte>(2, 6);
            Check(!CandidatePrefix(out _), "Reversed native actor order was accepted.");
            Setup();
            Talisman.player_kazu = 3;
            Check(!CandidatePrefix(out _), "Malformed participant count was accepted.");
        });

        Case("Wrong native targeting type fails before projecting recipe requirements", () =>
        {
            Talisman.type = 0;
            Check(!CandidatePrefix(out _) && _runtime.FaultReason is not null && Talisman.player_id.Writes == 0,
                "Unqualified targeting type reached native readers.");
        });

        Case("Missing displayed Pahn blocks Talisman without enabling the story-slot pair", () =>
        {
            _runtime.Mapping[91] = 76;
            using (var scope = AvatarUniteTableScope.Begin())
                Check(!NativeModelMembers(Talisman).Any(), "Absent Pahn was supplied by an inactive campaign identity.");
            Check(_runtime.Logs.Any(x => x.Contains("unavailable")), "Unavailable reason was not recorded.");
        });

        Case("KO, dead flags, incapacitating status and native dead_check each block the group", () =>
        {
            foreach (int condition in Enumerable.Range(0, 4))
            {
                Setup();
                if (condition == 0) Actor(PahnSlot).hp = 0;
                else if (condition == 1) Actor(PahnSlot).dead_flg = 1;
                else if (condition == 2) Actor(PahnSlot).status = 0x20;
                else BattleBase.DeadCheck = slot => slot == PahnSlot;
                using var scope = AvatarUniteTableScope.Begin();
                Check(!NativeModelMembers(Talisman).Any(), "Incapable participant admitted for condition " + condition);
            }
        });

        Case("Ambiguous displayed fighters keep the recipe unavailable", () =>
        {
            _runtime.Mapping[2] = 2;
            using var scope = AvatarUniteTableScope.Begin();
            Check(!NativeModelMembers(Talisman).Any(), "A duplicate Gremio avatar was accepted.");
        });

        Case("Mismatched canonical graphics identity and incomplete battle arrays block projection", () =>
        {
            Work.b_chara[PahnSlot].chara_id = 90;
            using (var scope = AvatarUniteTableScope.Begin())
                Check(!NativeModelMembers(Talisman).Any(), "Mismatched actor graphics identity passed preflight.");
            Setup();
            Work.b_chara = new NativeArray<B_CHARA>(new B_CHARA[Work.player_kazu]);
            using (var scope = AvatarUniteTableScope.Begin())
                Check(!NativeModelMembers(Talisman).Any(), "Incomplete native battle arrays passed preflight.");
        });

        Case("Missing required Gremio/Pahn native animation rows block projection", () =>
        {
            Work.b_chara[GremioSlot].action_data_table = new NativeArray<Il2CppStructArray<short>>(new Il2CppStructArray<short>[9]);
            using (var scope = AvatarUniteTableScope.Begin())
                Check(!NativeModelMembers(Talisman).Any(), "Short Gremio action table was accepted.");
            Setup();
            Work.b_chara[GremioSlot].action_data_table[15] = new Il2CppStructArray<short>();
            using (var scope = AvatarUniteTableScope.Begin())
                Check(!NativeModelMembers(Talisman).Any(), "Empty Gremio second-participant action row was accepted.");
        });

        Case("Native first participant Pahn needs row nine while second participant Gremio supplies row fifteen", () =>
        {
            Work.b_chara[PahnSlot].action_data_table = new NativeArray<Il2CppStructArray<short>>(
                Enumerable.Range(0, 10).Select(_ => new Il2CppStructArray<short>((short)1)).ToArray());
            using var scope = AvatarUniteTableScope.Begin();
            Check(NativeModelMembers(Talisman).SequenceEqual(new[] { PahnSlot, GremioSlot }),
                "Native first/second participant action requirements were reversed.");
        });

        Case("ATB and one-shot scheduling independently disable this native-turn prototype", () =>
        {
            Scheduler.ActiveAtbEnabled = true;
            using (var scope = AvatarUniteTableScope.Begin())
                Check(!NativeModelMembers(Talisman).Any(), "ATB was allowed to issue a multi-hero native action.");
            Scheduler.ActiveAtbEnabled = false;
            Scheduler.OneShotAttackEnabled = true;
            using (var scope = AvatarUniteTableScope.Begin())
                Check(!NativeModelMembers(Talisman).Any(), "One-shot scheduler was allowed to issue a multi-hero action.");
        });

        Case("Canonical role zero can host a qualified natural fighter", () =>
        {
            Work.p_battle_data[GremioSlot].player_data.chara_id = 0;
            Work.b_chara[GremioSlot].chara_id = 0;
            _runtime.Mapping[0] = 2;
            _runtime.Mapping.Remove(39);
            using var scope = AvatarUniteTableScope.Begin();
            Check(Talisman.player_id[1] == 0 && NativeModelMembers(Talisman).SequenceEqual(new[] { 4, 2 }), "Role zero was interpreted as absent.");
        });

        Case("Actual command99 prefix/postfix commit the two real hosts and restore recipe IDs", () =>
        {
            Commit();
            Check(Actor(2).c_koudou == 4 && Actor(4).c_koudou == 4 && Actor(2).c_item == 1 && Actor(4).c_item == 1,
                "Native commit model did not assign the projected partners.");
            Check(Actor(3).c_koudou == 0 && Actor(5).c_koudou == 0, "Original story-role occupants received the Unite.");
            Check(Actor(2).hp == 102 && Actor(4).hp == 104, "Commit changed live HP/stats.");
        });

        Case("Commit finalizer restores shared requirements after a native reader exception", () =>
        {
            object?[] args = { 12345, null };
            Check((bool)Call(typeof(AvatarTalismanCommitPatch), "Prefix", args)!, "Commit unexpectedly failed.");
            var failure = new InvalidOperationException("Modeled native reader failure");
            Check(ReferenceEquals(Call(typeof(AvatarTalismanCommitPatch), "Finalizer", failure, args[1]), failure), "Finalizer swallowed the original error.");
            Check(Talisman.player_id[0] == 6 && Talisman.player_id[1] == 2, "Exceptional commit leaked shared table projection.");
            int writes = Talisman.player_id.Writes;
            Call(typeof(AvatarTalismanCommitPatch), "Postfix", args[1]);
            Check(Talisman.player_id.Writes == writes, "Postfix/finalizer double-disposal wrote the table twice.");
        });

        Case("A partial native setter failure rolls back every projected requirement", () =>
        {
            Talisman.player_id.FailOnWrite = 2;
            Throws(() => AvatarUniteTableScope.Begin());
            Check(Talisman.player_id[0] == 6 && Talisman.player_id[1] == 2, "Partial projection was not rolled back.");
            using var scope = AvatarUniteTableScope.Begin();
            Check(Talisman.player_id[0] == 91, "Rollback left nesting state poisoned.");
        });

        Case("Admission, native Finish model and production checks preserve recipe order and consume both actors", () =>
        {
            Commit();
            Admit();
            var ids = Prepare();
            Check(ids[0] == PahnSlot && ids[1] == GremioSlot, "Wrong native participant-slot order.");
            Check(Actor(2).finished_flg == 1 && Actor(4).finished_flg == 1, "Only the leader consumed its action.");
            Check(Actor(1).finished_flg == 0 && Actor(3).finished_flg == 0 && Actor(5).finished_flg == 0, "An unrelated hero lost their turn.");
            Check(Actor(2).c_mokuhyo == EnemySlot && Actor(4).c_mokuhyo == EnemySlot, "Selected target was not carried to both heroes.");
            Check(Talisman.player_id[0] == 6 && Talisman.player_id[1] == 2, "Finish scope did not restore recipe identity.");
        });

        Case("Pahn can lead the same ordered Gremio/Pahn sequence", () =>
        {
            Commit();
            Admit(PahnSlot);
            var ids = Prepare(PahnSlot);
            Check(ids[0] == PahnSlot && ids[1] == GremioSlot, "Leader choice changed choreography order.");
        });

        Case("A partner with a different selected target is aligned by native Finish", () =>
        {
            Commit(); Actor(PahnSlot).c_mokuhyo = 1;
            Admit(); Prepare();
            Check(Actor(PahnSlot).c_mokuhyo == EnemySlot, "Follower retained a different target.");
        });

        Case("Wrong native participant array identity stops before unsafe preparation", () =>
        {
            Commit(); Admit();
            BattleBase.kmagic01_work = new KMAGIC01_WORK { p_id = new Il2CppStructArray<int>(0, 0) };
            Throws(() => AvatarTalismanPreview.BeforeFinish(GremioSlot, new Il2CppStructArray<int>(0, 0)));
            Check(_runtime.FaultReason is not null, "Mismatched native work did not fault the preview.");
            Check(Talisman.player_id[0] == 6, "Failed preparation changed shared recipe data.");
        });

        Case("Actor identity replacement between admission and preparation stops execution", () =>
        {
            Commit(); Admit();
            Actor(PahnSlot).Pointer = TestIdentity.Next();
            var ids = new Il2CppStructArray<int>(0, 0);
            BattleBase.kmagic01_work = new KMAGIC01_WORK { p_id = ids };
            Throws(() => AvatarTalismanPreview.BeforeFinish(GremioSlot, ids));
            Check(_runtime.FaultReason is not null, "Stale actor pointer was admitted to choreography.");
        });

        Case("A native Finish slot/order disagreement fails closed and restores its scope", () =>
        {
            Commit(); Admit();
            var ids = new Il2CppStructArray<int>(0, 0);
            BattleBase.kmagic01_work = new KMAGIC01_WORK { p_id = ids };
            var state = AvatarTalismanPreview.BeforeFinish(GremioSlot, ids);
            ids[0] = GremioSlot; ids[1] = PahnSlot;
            Actor(2).finished_flg = Actor(4).finished_flg = 1;
            Throws(() => AvatarTalismanPreview.AfterFinish(state, ids));
            Check(_runtime.FaultReason is not null && Talisman.player_id[0] == 6, "Wrong native slots left the projection active.");
        });

        Case("Incomplete native action consumption fails the finish checkpoint", () =>
        {
            Commit(); Admit();
            var ids = new Il2CppStructArray<int>(PahnSlot, GremioSlot);
            BattleBase.kmagic01_work = new KMAGIC01_WORK { p_id = ids };
            var state = AvatarTalismanPreview.BeforeFinish(GremioSlot, ids);
            Actor(GremioSlot).finished_flg = 1;
            Throws(() => AvatarTalismanPreview.AfterFinish(state, ids));
            Check(_runtime.FaultReason is not null && Talisman.player_id[0] == 6, "Unconsumed partner passed the preparation checkpoint.");
        });

        Case("Damage observation distinguishes queued damage from playback-applied final HP", () =>
        {
            Commit(); Admit(); var ids = Prepare();
            var before = AvatarTalismanPreview.BeforeDamage(GremioSlot, 2, 20, ids);
            Check(before is not null && before.Target == EnemySlot && before.Before == 500, "Wrong damage target snapshot.");
            Actor(EnemySlot).damage = 130; // Simulated native queue; this does not implement the damage formula.
            AvatarTalismanPreview.AfterDamage(before);
            Check(_runtime.Logs.Any(x => x.Contains("damage queued: target=6; damage=130; immediate HP 500 -> 500; native multiplier=20")),
                "Queued damage was misrepresented as already-applied HP loss.");
            Actor(EnemySlot).hp = 370; // Explicit simulated playback effect.
            BattleBase.kmagic01_work = null;
            AvatarTalismanPreview.AfterSequence(0);
            Check(_runtime.Logs.Any(x => x.Contains("target=6; final HP 500 -> 370")), "Playback-applied HP change was not observed at completion.");
            Check(Actor(GremioSlot).hp == 102 && Actor(PahnSlot).hp == 104, "Damage observer overwrote participant stats.");
        });

        Case("Invalid prepared damage members or target are rejected", () =>
        {
            Commit(); Admit(); var ids = Prepare();
            Throws(() => AvatarTalismanPreview.BeforeDamage(GremioSlot, 1, 20, ids));
            Check(_runtime.FaultReason is not null, "Incomplete damage participant list was admitted.");
            Setup(); Commit(); Admit(); ids = Prepare();
            Actor(GremioSlot).c_mokuhyo = 0;
            Throws(() => AvatarTalismanPreview.BeforeDamage(GremioSlot, 2, 20, ids));
            Check(_runtime.FaultReason is not null, "Invalid target slot was admitted.");
        });

        Case("Completion waits for native work release and logs exactly once", () =>
        {
            Commit(); Admit(); Prepare();
            AvatarTalismanPreview.AfterSequence(-1);
            AvatarTalismanPreview.AfterSequence(0);
            Check(CompletedLogs() == 0, "Sequence completed while native work was still active.");
            BattleBase.kmagic01_work = null;
            AvatarTalismanPreview.AfterSequence(-1);
            Check(CompletedLogs() == 0, "Native wait result completed a sequence.");
            AvatarTalismanPreview.AfterSequence(0);
            AvatarTalismanPreview.AfterSequence(0);
            Check(CompletedLogs() == 1, "Completion was lost or repeated.");
        });

        Case("Invalid pre-execution group uses stock -1 failure and releases surviving partners to Defend", () =>
        {
            Commit(); Actor(PahnSlot).hp = 0;
            object?[] args = { GremioSlot, 0, null };
            Check(!(bool)Call(typeof(AvatarTalismanAdmissionPatch), "Prefix", args)! && (int)args[1]! == -1 && args[2] is null,
                "Invalid group returned native wait/zero instead of stock failure/-1.");
            Check(Actor(2).c_koudou == 1 && Actor(4).c_koudou == 1 && Actor(2).c_item == 0 && Actor(4).c_item == 0,
                "Cancelled group was left queued for a replay.");
            Check(Actor(2).c_mokuhyo == 2 && Actor(4).c_mokuhyo == 4, "Defend targets were not restored to each actor.");
            Actor(GremioSlot).finished_flg = 1; // Stock outer battle_turn02 handles the -1 leader result.
            Check(Actor(GremioSlot).finished_flg == 1 && Actor(PahnSlot).finished_flg == 0,
                "Adapter consumed actions before the modeled stock failure path.");
        });

        Case("A partner's changed queued command cancels the group before admission", () =>
        {
            Commit(); Actor(PahnSlot).c_koudou = 2;
            Check(!AvatarTalismanPreview.BeforeAdmission(GremioSlot, out var action) && action is null, "Partner's new command was silently overwritten.");
            Check(Actor(GremioSlot).c_koudou == 1 && Actor(PahnSlot).c_koudou == 2, "Cancel stole a partner's different action.");
        });

        Case("An unrelated hero cannot lead a manually corrupted Talisman selection", () =>
        {
            Commit(); Actor(1).c_koudou = 4; Actor(1).c_item = 1;
            Check(!AvatarTalismanPreview.BeforeAdmission(1, out _), "Nonparticipant was admitted as leader.");
            Check(Actor(1).c_koudou == 1 && Actor(2).c_koudou == 1 && Actor(4).c_koudou == 1, "Corrupted command group was not cancelled.");
        });

        Case("Already-finished participants cannot replay a previously completed group", () =>
        {
            Commit(); Admit(); Prepare();
            BattleBase.kmagic01_work = null;
            AvatarTalismanPreview.AfterSequence(0);
            Check(!AvatarTalismanPreview.BeforeAdmission(GremioSlot, out var action) && action is null,
                "Finished actors were admitted for a second action.");
            Check(Actor(2).c_koudou == 1 && Actor(4).c_koudou == 1, "Replay rejection left another Unite queued.");
        });

        Case("Repeated admission during an in-flight sequence does not clobber its context", () =>
        {
            Commit(); Admit();
            Check(!AvatarTalismanPreview.BeforeAdmission(PahnSlot, out var repeated) && repeated is null,
                "A second leader was admitted while a native group was in flight.");
            Check(Actor(2).c_koudou == 4 && Actor(4).c_koudou == 4, "In-flight rejection cancelled the live group.");
            Prepare();
            BattleBase.kmagic01_work = null;
            AvatarTalismanPreview.AfterSequence(0);
            Check(CompletedLogs() == 1, "Repeated admission destroyed the first sequence's completion context.");
        });

        Case("Owner, epoch and native battle changes make old execution observations inert", () =>
        {
            foreach (int change in Enumerable.Range(0, 3))
            {
                Setup(); Commit(); Admit(); var ids = Prepare();
                var observation = AvatarTalismanPreview.BeforeDamage(GremioSlot, 2, 20, ids);
                int logs = _runtime.Logs.Count;
                if (change == 0) AvatarRuntime.Current = new AvatarRuntime();
                else if (change == 1) _runtime.ExecutionEpoch++;
                else Work.Pointer = TestIdentity.Next();
                Actor(EnemySlot).hp = 333;
                AvatarTalismanPreview.AfterDamage(observation);
                BattleBase.kmagic01_work = null;
                AvatarTalismanPreview.AfterSequence(0);
                Check(_runtime.Logs.Count == logs, "Stale sequence modified/logged a new owner/battle for change " + change);
            }
        });

        Case("Reset/battle boundary clears pending execution without changing queued battle commands", () =>
        {
            Commit(); Admit(); Prepare();
            Call(typeof(AvatarTalismanBoundaryPatch), "Prefix");
            BattleBase.kmagic01_work = null;
            AvatarTalismanPreview.AfterSequence(0);
            Check(CompletedLogs() == 0 && Actor(2).c_koudou == 4, "Boundary reset completed or rewrote an old encounter.");
            var methods = (IEnumerable<MethodBase>)Call(typeof(AvatarTalismanBoundaryPatch), "Targets")!;
            Check(methods.Select(x => x.Name).SequenceEqual(new[] { "initialize_battle", "battle_end_free" }), "Boundary hooks changed unexpectedly.");
        });

        Case("Animation preflight accepts the admitted group with native work and qualified resources", () =>
        {
            Commit(); Admit();
            BattleBase.kmagic01_work = new KMAGIC01_WORK { p_id = new Il2CppStructArray<int>(0, 0) };
            Check(AvatarTalismanPreview.CanStepSequence(true, GremioSlot), "Valid pre-animation work was rejected.");
            Prepare();
            Check(AvatarTalismanPreview.CanStepSequence(true, GremioSlot), "Spent action flags incorrectly blocked the prepared sequence.");
        });

        Case("Animation preflight faults changed ownership epoch, native actor or required resource", () =>
        {
            foreach (int change in Enumerable.Range(0, 4))
            {
                Setup(); Commit(); Admit();
                BattleBase.kmagic01_work = new KMAGIC01_WORK { p_id = new Il2CppStructArray<int>(0, 0) };
                if (change == 0) _runtime.ExecutionEpoch++;
                else if (change == 1) Actor(PahnSlot).Pointer = TestIdentity.Next();
                else if (change == 2) Work.b_chara[GremioSlot].action_data_table[15] = new Il2CppStructArray<short>();
                else BattleBase.kmagic01_work.p_id = new Il2CppStructArray<int>(0);
                Check(!AvatarTalismanPreview.CanStepSequence(true, GremioSlot) && _runtime.FaultReason is not null,
                    "Unsafe animation preflight passed for change " + change);
                object?[] args = { typeof(BattleBase).GetMethod("battle_turn_kyoryoku")!, GremioSlot, 0 };
                Check(!(bool)Call(typeof(AvatarTalismanSequenceGuardPatch), "Prefix", args)! && (int)args[2]! == -1,
                    "A faulted later frame could repeat unsafe native array reads.");
            }
        });

        Case("A selected Talisman without an admitted group cannot enter native choreography", () =>
        {
            Commit();
            BattleBase.kmagic01_work = new KMAGIC01_WORK { p_id = new Il2CppStructArray<int>(0, 0) };
            object?[] args = { typeof(BattleBase).GetMethod("kmagic01_func02")!, GremioSlot, 0 };
            Check(!(bool)Call(typeof(AvatarTalismanSequenceGuardPatch), "Prefix", args)! && (int)args[2]! == -1
                && _runtime.FaultReason is not null, "Unadmitted choreography entered an unsafe native reader.");
        });

        Case("Disabled and foreign-owned animation hooks leave unrelated native behavior alone", () =>
        {
            Commit();
            _runtime.Owns = false;
            Check(AvatarTalismanPreview.CanStepSequence(true, GremioSlot), "Foreign-owned runtime was controlled by this preview.");
            AvatarTalismanPreview.Configure(false);
            _runtime.Owns = true;
            Check(AvatarTalismanPreview.CanStepSequence(true, GremioSlot), "Disabled preview controlled native execution.");
        });

        Case("A faulted or invalid command projection cannot commit native Talisman commands", () =>
        {
            Talisman.func_no = 2;
            object?[] args = { 999, null };
            Check(!(bool)Call(typeof(AvatarTalismanCommitPatch), "Prefix", args)! && (int)args[0]! == 0
                && args[1] is null && _runtime.FaultReason is not null, "Malformed recipe reached the command commit reader.");
            Talisman.func_no = 1;
            args = new object?[] { 999, null };
            Check(!(bool)Call(typeof(AvatarTalismanCommitPatch), "Prefix", args)! && args[1] is null,
                "A later frame re-entered command commit after the explicit fault.");
            Check(Actor(GremioSlot).c_koudou == 0 && Actor(PahnSlot).c_koudou == 0 && Talisman.player_id.Writes == 0,
                "Failed commit preflight changed native state.");
        });

        Case("Unexpected native Finish exception restores table IDs and permanently stops unsafe retries", () =>
        {
            Commit(); Admit();
            var ids = new Il2CppStructArray<int>(0, 0);
            BattleBase.kmagic01_work = new KMAGIC01_WORK { p_id = ids };
            object?[] args = { GremioSlot, ids, null };
            Call(typeof(AvatarTalismanFinishPatch), "Prefix", args);
            Check(Talisman.player_id[0] == 91, "Finish scope did not project native readers.");
            var failure = new IndexOutOfRangeException("Modeled unexpected native row failure");
            Check(ReferenceEquals(Call(typeof(AvatarTalismanFinishPatch), "Finalizer", failure, args[2]), failure),
                "Finish finalizer lost the native exception.");
            Check(Talisman.player_id[0] == 6 && Talisman.player_id[1] == 2 && _runtime.FaultReason is not null,
                "Native exception left shared tables projected or failed to latch a fault.");
            Check(!AvatarTalismanPreview.CanStepSequence(true, GremioSlot)
                && !AvatarTalismanPreview.CanStepSequence(false, GremioSlot), "A later frame could retry the failed native reader.");
            int count = _runtime.Logs.Count;
            AvatarTalismanPreview.ObserveSequenceException(failure);
            Check(_runtime.Logs.Count == count, "Repeated native fault overwrote or duplicated the first diagnostic.");
        });

        Case("Sequence and completion finalizers preserve owned native exceptions and latch one fault", () =>
        {
            foreach (var patch in new[] { typeof(AvatarTalismanSequenceGuardPatch), typeof(AvatarTalismanCompletionPatch) })
            {
                Setup(); Commit(); Admit(); Prepare();
                var failure = new InvalidOperationException("Modeled native sequence exception");
                Check(ReferenceEquals(Call(patch, "Finalizer", failure), failure), "Finalizer lost native exception for " + patch.Name);
                Check(_runtime.FaultReason is not null && !AvatarTalismanPreview.CanStepSequence(false, GremioSlot),
                    "Owned exception did not stop subsequent frames for " + patch.Name);
            }
        });

        Case("Unbound or foreign-owned native exceptions are not claimed by this preview", () =>
        {
            var failure = new InvalidOperationException("Unrelated native exception");
            AvatarTalismanPreview.ObserveSequenceException(failure);
            Check(_runtime.FaultReason is null, "Unbound exception was attributed to Talisman.");
            Commit(); Admit();
            _runtime.Owns = false;
            AvatarTalismanPreview.ObserveSequenceException(failure);
            Check(_runtime.FaultReason is null, "Foreign-owned exception was attributed to this runtime.");
        });

        Console.WriteLine("PASS " + _passed + " linked Talisman preview checks; native matching, Harmony detours, damage formulas and choreography were simulated or not executed.");
    }
}
