using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using SuikodenRandomizer.Core.Suikoden2;
using SuikodenRandomizer.Shared;
using SuikodenRandomizer.Suikoden2Plugin;
using SuikodenRandomizer.Unified;

internal static class Program
{
    private const BindingFlags StaticPrivate = BindingFlags.Static | BindingFlags.NonPublic;
    private static bool _allowed;
    private static long _epoch;

    private static void Check(bool condition, string reason)
    { if (!condition) throw new InvalidOperationException(reason); }

    private static Gsd2Runtime Start(string mode = "Shuffle", bool includeHero = false, string seed = "dialogue-seed")
    {
        Gsd2Plugin.Seed = seed; Gsd2Plugin.Mode = mode; Gsd2Plugin.IncludeProtagonist = includeHero;
        Gsd2Plugin.Companions = "2,3,4,5,6"; Gsd2Plugin.StartPoint = "Opening";
        OldSrcBase.game_work = FixtureBuilder.Fixture(1, 2, 7);
        var runtime = new Gsd2Runtime(); Gsd2Runtime.Current = runtime;
        runtime.BeginNewGame(); runtime.ObserveNativeDataset(); runtime.CompleteNativeNewGame();
        Gsd2Graphics.IsReady = true;
        Check(runtime.IsActive, "Fixture did not admit a campaign");
        return runtime;
    }

    private static NativeDialogueContext Capture() => DialogueS2.Capture()
        ?? throw new InvalidOperationException("Stable campaign could not open a name scope");

    private static int PickRole(Gsd2Runtime runtime) => Enumerable.Range(2, 78)
        .First(role => runtime.IsRole(role) && runtime.MapDisplayId(role) != role);

    private static void Deny()
    { _allowed = false; _epoch++; OldSrcBase.RejectGameAccess = true; }

    private static IDisposable? Enter(Type patch)
    {
        object?[] args = { null };
        patch.GetMethod("Prefix", StaticPrivate)!.Invoke(null, args);
        return (IDisposable?)args[0];
    }

    private static Exception? Exit(Type patch, IDisposable? state, Exception? original = null) =>
        (Exception?)patch.GetMethod("Finalizer", StaticPrivate)!.Invoke(null, new object?[] { original, state });

    private static int Main()
    {
        (string Name, Action Run)[] tests =
        {
            ("canonical getter once, from live projected table", () =>
            {
                var runtime = Start();
                int role = Enumerable.Range(2, 78).First(r => runtime.MapDisplayId(r) != r
                    && runtime.MapDisplayId(runtime.MapDisplayId(r)) != r
                    && runtime.MapDisplayId(runtime.MapDisplayId(r)) != runtime.MapDisplayId(r));
                int donor = runtime.MapDisplayId(role);
                var context = Capture();
                var game = OldSrcBase.game_work; var chars = game.chara_data; var names = game.c_teg_dat.name; var arms = game.arms_data;
                var fighter = chars.c_varia_dat[role]; var exp = fighter.exp;
                G2_SYS.NameReader = id =>
                {
                    Check(DialogueNative.NameDepth == 1, "Native name lookup was not suppressed");
                    return "Native name " + names[id];
                };
                Check(context.Game == "GSD2" && context.MaxUtf16Length() is null, "Wrong context or invented native buffer cap");
                Check(context.ShouldReplaceRole!(role) && context.ResolveName(role) == "Native name " + (10000 + donor), "Projected name wrong");
                Check(G2_SYS.NameCalls == 1 && G2_SYS.LastCanonical == role, "Getter was called twice or with donor ID");
                Check(DialogueNative.NameDepth == 0 && ReferenceEquals(chars, game.chara_data)
                    && ReferenceEquals(names, game.c_teg_dat.name) && ReferenceEquals(arms, game.arms_data)
                    && fighter.exp == exp, "Name lookup changed campaign state");
            }),
            ("fixed assignment and protected source roles", () =>
            {
                var runtime = Start("FixedChosen"); var context = Capture();
                Check(runtime.IsRole(2) && runtime.MapDisplayId(2) == 2 && !runtime.IsRole(33), "Wrong fixed roster fixture");
                foreach (int role in new[] { -1, 0, 1, 2, 33, 80, 81, 82, 83, 128 })
                    Check(!context.ShouldReplaceRole!(role) && context.ResolveName(role) is null, "Rewrote protected or identity role " + role);
                Check(G2_SYS.NameCalls == 0, "Unassigned role reached the getter");
                int assigned = PickRole(runtime);
                Check(context.ResolveName(assigned) == "Native name " + (10000 + runtime.MapDisplayId(assigned)), "Fixed donor did not resolve");
            }),
            ("native hero label may be donor but never source", () =>
            {
                string seed = Enumerable.Range(0, 64).Select(i => "hero-name-" + i)
                    .First(s => Gsd2AvatarProfile.Create(s, true).Assignments.Any(p => p.Key >= 2 && p.Value == 1));
                var runtime = Start(includeHero: true, seed: seed); var context = Capture();
                int role = runtime.Profile.Assignments.Single(p => p.Key >= 2 && p.Value == 1).Key;
                G2_SYS.NameReader = id => OldSrcBase.game_work.c_teg_dat.name[id] == 10001 ? "Native Hero Label" : "Wrong identity";
                Check(!context.ShouldReplaceRole!(1) && context.ResolveName(1) is null, "Source hero was rewritten");
                Check(context.ResolveName(role) == "Native Hero Label" && G2_SYS.LastCanonical == role, "Hero donor did not use current native label");
            }),
            ("new scope requires graphics but stable name snapshot does not", () =>
            {
                var runtime = Start(); var context = Capture(); int role = PickRole(runtime);
                Gsd2Graphics.IsReady = false;
                Check(DialogueS2.Capture() is null, "Captured an inactive campaign");
                Check(context.IsCurrent() && context.ResolveName(role) is not null, "Stable name identity was tied to graphics");
            }),
            ("save and transitional states reject before native reads", () =>
            {
                var runtime = Start(); var context = Capture(); runtime.BeginSave();
                int reads = OldSrcBase.GameReads;
                Check(DialogueS2.Capture() is null && !context.IsCurrent() && OldSrcBase.GameReads == reads,
                    "Read names in a canonicalizing save");
                runtime.EndSave();
                foreach (string flag in new[] { "_loading", "_busy", "_newGamePending" })
                {
                    var field = typeof(Gsd2Runtime).GetField(flag, BindingFlags.Instance | BindingFlags.NonPublic)!;
                    field.SetValue(runtime, true); reads = OldSrcBase.GameReads;
                    Check(DialogueS2.Capture() is null && OldSrcBase.GameReads == reads, "Read names during " + flag);
                    field.SetValue(runtime, false);
                }
            }),
            ("foreign owner and same-epoch replacement are rejected", () =>
            {
                var runtime = Start(); var context = Capture(); int role = PickRole(runtime);
                Gsd2Runtime.Current = new Gsd2Runtime();
                int reads = OldSrcBase.GameReads;
                Check(!context.IsCurrent() && OldSrcBase.GameReads == reads, "Runtime replacement read stale native data");
                Gsd2Runtime.Current = runtime; Deny();
                Check(DialogueS2.Capture() is null && context.ResolveName(role) is null
                    && OldSrcBase.GameReads == reads && G2_SYS.NameCalls == 0, "Foreign owner read native data");
            }),
            ("resident name and character identities invalidate snapshot", () =>
            {
                var runtime = Start(); var context = Capture(); int role = PickRole(runtime);
                var game = OldSrcBase.game_work; var owner = game.c_teg_dat; var chars = game.chara_data; var names = owner.name;
                owner.name = new Il2CppStructArray<int>(names.Count);
                Check(!context.IsCurrent() && context.ResolveName(role) is null && DialogueS2.Capture() is null,
                    "Replaced name table accepted by an old or new scope");
                owner.name = names; game.c_teg_dat = new C_TEG_DAT { name = names };
                Check(!context.IsCurrent(), "Replaced name owner accepted");
                game.c_teg_dat = owner; game.chara_data = new CHARA_DATA();
                Check(!context.IsCurrent(), "Replaced character publication accepted");
                game.chara_data = chars; OldSrcBase.game_work = new GAME_WORK { chara_data = chars, c_teg_dat = owner };
                Check(!context.IsCurrent() && G2_SYS.NameCalls == 0, "Replaced game accepted");
            }),
            ("same-seed new campaign cannot reuse old snapshot", () =>
            {
                var runtime = Start(); var old = Capture(); var profile = runtime.Profile;
                runtime.BeginNewGame(); OldSrcBase.game_work = FixtureBuilder.Fixture(1, 7); runtime.CompleteNativeNewGame();
                Gsd2Graphics.IsReady = true;
                Check(profile.Fingerprint == runtime.Profile.Fingerprint && !ReferenceEquals(profile, runtime.Profile), "Fixture failed to repeat seed");
                Check(!old.IsCurrent() && Capture().IsCurrent(), "Old context survived a new campaign");
            }),
            ("ownership loss at native read or suppression boundary", () =>
            {
                var runtime = Start(); var context = Capture(); int role = PickRole(runtime);
                int reads = OldSrcBase.GameReads;
                OldSrcBase.AfterGameRead = Deny;
                Check(!context.IsCurrent() && OldSrcBase.GameReads == reads + 1, "Continued reading after handoff");
                OldSrcBase.AfterGameRead = null; OldSrcBase.RejectGameAccess = false; _allowed = true;
                runtime = Start(); context = Capture(); role = PickRole(runtime);
                DialogueNative.DuringSuppress = Deny;
                Check(context.ResolveName(role) is null && G2_SYS.NameCalls == 0 && DialogueNative.NameDepth == 0,
                    "Called name getter after handoff or leaked scope");
            }),
            ("ownership loss during native getter discards result", () =>
            {
                var runtime = Start(); var context = Capture(); int role = PickRole(runtime); int readsAtExit = -1;
                G2_SYS.AfterName = () => { readsAtExit = OldSrcBase.GameReads; Deny(); };
                Check(context.ResolveName(role) is null && G2_SYS.NameCalls == 1
                    && OldSrcBase.GameReads == readsAtExit && DialogueNative.NameDepth == 0,
                    "Stale result escaped or foreign state was read");
            }),
            ("native getter and scope errors stay presentation-only", () =>
            {
                var runtime = Start(); var context = Capture(); int role = PickRole(runtime);
                G2_SYS.NameReader = _ => throw new InvalidOperationException("injected native getter failure");
                Check(context.ResolveName(role) is null && DialogueNative.NameDepth == 0 && runtime.FaultReason is null,
                    "Name error escaped, faulted campaign, or leaked scope");
                DialogueNative.ThrowEnter = true; DialogueNative.ThrowSuppress = true;
                Check(Enter(typeof(DialogueS2WindowStepPatch)) is null && Enter(typeof(DialogueS2NamePatch)) is null,
                    "Bridge error interrupted original native flow");
            }),
            ("MoveNext and backlog scopes restore prior state on exceptions", () =>
            {
                Start();
                Type step = typeof(DialogueS2WindowStepPatch), backlog = typeof(DialogueS2BacklogPatch);
                var original = new InvalidOperationException("native render failure");
                var outer = Enter(step); var prior = DialogueNative.Current;
                Check(prior is not null && DialogueNative.LastEnteredGame == ActiveGame.Gsd2, "MoveNext did not enter S2");
                var inner = Enter(backlog);
                Check(DialogueNative.Current is not null && !ReferenceEquals(prior, DialogueNative.Current), "Backlog did not nest");
                DialogueNative.ThrowDispose = true;
                Check(ReferenceEquals(Exit(backlog, inner, original), original) && ReferenceEquals(DialogueNative.Current, prior),
                    "Backlog failure replaced native exception or leaked scope");
                Check(Exit(step, outer) is null && DialogueNative.Current is null, "MoveNext scope survived step boundary");
                Check(DialogueS2.PatchTypes.Length == 4 && step.GetCustomAttribute<HarmonyPatch>()!.MethodName == "MoveNext"
                    && backlog.GetCustomAttribute<HarmonyPatch>()!.ArgumentTypes.Single() == typeof(string), "Unexpected scope targets");
            }),
            ("both native name hooks only enter suppression", () =>
            {
                Start();
                Type name = typeof(DialogueS2NamePatch), id = typeof(DialogueS2NameIdPatch);
                int reads = OldSrcBase.GameReads;
                var s1 = Enter(name); var s2 = Enter(id);
                Check(DialogueNative.NameDepth == 2 && DialogueNative.Current is null && G2_SYS.NameCalls == 0
                    && OldSrcBase.GameReads == reads, "Name hook remapped or read game data");
                foreach (var type in new[] { name, id })
                    Check(type.GetMethod("Prefix", StaticPrivate)!.GetParameters().Single().IsOut
                        && type.GetCustomAttribute<HarmonyPatch>()!.ArgumentTypes.Single() == typeof(int), "Suppression prefix exposes an ID argument");
                var original = new InvalidOperationException();
                Check(ReferenceEquals(Exit(id, s2, original), original) && DialogueNative.NameDepth == 1, "Nested suppression did not unwind");
                Check(Exit(name, s1) is null && DialogueNative.NameDepth == 0, "Name suppression leaked");
            }),
        };
        int failed = 0;
        foreach (var test in tests)
        {
            _allowed = true; _epoch = 100;
            Gsd2Runtime.SetExecutionOwnership(() => _allowed, () => _epoch);
            try { test.Run(); Console.WriteLine("PASS " + test.Name); }
            catch (Exception error) { failed++; Console.WriteLine("FAIL " + test.Name + ": " + error); }
            finally
            {
                OldSrcBase.AfterGameRead = null; OldSrcBase.RejectGameAccess = false;
                G2_SYS.NameReader = null; G2_SYS.AfterName = null; G2_SYS.NameCalls = 0;
                DialogueNative.Current = null; DialogueNative.NameDepth = 0; DialogueNative.DuringSuppress = null;
                DialogueNative.ThrowEnter = DialogueNative.ThrowDispose = DialogueNative.ThrowSuppress = false;
                Gsd2Runtime.SetExecutionOwnership(() => true, () => 0); Gsd2Runtime.Current = null;
            }
        }
        Console.WriteLine($"S2 dialogue adapter: {tests.Length - failed}/{tests.Length} checks passed.");
        return failed == 0 ? 0 : 1;
    }
}
