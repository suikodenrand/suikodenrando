using System;
using System.Collections.Generic;
using System.Linq;
using SuikodenRandomizer.Shared;

internal static class Program
{
    private static void Main()
    {
        var tests = new (string, Action)[] {
            ("rapid GSD1/None/GSD2/None/GSD1 rejects every previous generation", RapidChanges),
            ("unknown and inexact scene names have no owner", ExactNames),
            ("same-scene observation is stable; explicit invalidation rejects a same-game load callback", Invalidate),
            ("tokens cannot authorize a different router instance", RouterIdentity),
            ("late and nested callbacks recheck ownership before subsequent phases", NestedCallbacks),
            ("classifier injection and failures route no unintended engine", ClassifierFailures),
            ("one UI/backend is selected and only the current engine ticks", Selection),
            ("F6 has one destination per frame across rapid owner changes", F6Destination),
            ("unfocused/unpressed input and launcher input do not leak to an engine", InputAdmission),
            ("one backend exception stops only that owner/generation", FaultIsolation),
            ("backend fault status permits owned lifecycle cleanup while blocking F6", FaultedStatus),
            ("status callbacks cannot dispatch a stale engine after changing ownership", ReentrantStatus),
            ("late failure is attributed to its captured owner, never the newly active game", LateFault),
            ("an older reentrant failure cannot overwrite a same-game newer-generation failure", NestedFaultGeneration),
            ("F6 exceptions are contained and a later engine still receives input", F6Fault),
            ("duplicate/invalid backend registration cannot create two UI owners", Registration),
            ("legacy game config/binding descriptors are separate, exact and immutable", LegacyPaths),
            ("battle/background active-scene changes preserve the selected loaded game's epoch", LoadedRootBattleContext),
            ("selection without its loaded root cannot admit either engine", SelectedRootRequired),
            ("selection change revokes outgoing game before its root unloads", SelectionBeforeUnload),
            ("both loaded roots are disambiguated by the native game selection", BothRoots),
            ("root unload and same-game reload reject old campaign callbacks", RootUnload),
            ("invalid game selection revokes execution without falling back to a loaded foreign root", InvalidGameContext)
        };
        foreach (var (name, run) in tests) { run(); Console.WriteLine("PASS " + name); }
        Console.WriteLine(tests.Length + "/" + tests.Length + " shared routing tests passed.");
    }
    private static void True(bool condition) { if (!condition) throw new Exception("Assertion failed."); }
    private static void Eq<T>(T a, T b) { if (!EqualityComparer<T>.Default.Equals(a, b)) throw new Exception("Expected " + a + ", got " + b); }
    private static void Reject<T>(Action run) where T : Exception
    { try { run(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
    private static (ActiveGameRouter Router, GameUiCoordinator Host, Backend One, Backend Two) Setup()
    {
        var router = new ActiveGameRouter(); var host = new GameUiCoordinator(router);
        var one = new Backend(ActiveGame.Gsd1, router); var two = new Backend(ActiveGame.Gsd2, router);
        host.Register(one); host.Register(two); return (router, host, one, two);
    }
    private static void RapidChanges()
    {
        var router = new ActiveGameRouter(); var prior = new List<GameOwnershipToken>();
        Eq(ActiveGame.None, router.CurrentGame); True(!router.IsCurrent(router.Capture()));
        string?[] scenes = { "GSD1", null, "GSD2", "Launcher", "GSD1" };
        ActiveGame[] games = { ActiveGame.Gsd1, ActiveGame.None, ActiveGame.Gsd2, ActiveGame.None, ActiveGame.Gsd1 };
        for (int i = 0; i < scenes.Length; i++)
        {
            var current = router.ObserveScene(scenes[i]); Eq(games[i], current.Game); Eq((long)i + 1, current.Generation);
            foreach (var old in prior) True(!router.IsCurrent(old));
            Eq(current.Game != ActiveGame.None, router.IsCurrent(current)); prior.Add(current);
        }
    }
    private static void ExactNames()
    {
        var router = new ActiveGameRouter();
        foreach (string? scene in new[] { null, "", "gsd1", "gsd2", "GSD1 ", " GSD2", "GSD2/Battle", "GSD10", "Title", "Bootstrap" })
        { Eq(ActiveGame.None, router.ObserveScene(scene).Game); True(!router.IsCurrent(router.Capture())); }
        Eq(0L, router.Generation); Eq(ActiveGame.Gsd1, ActiveGameRouter.ClassifyScene("GSD1"));
        Eq(ActiveGame.Gsd2, ActiveGameRouter.ClassifyScene("GSD2"));
        True(!router.TryCapture(ActiveGame.None, out _)); True(!router.TryCapture(ActiveGame.Gsd1, out _));
    }
    private static void Invalidate()
    {
        var router = new ActiveGameRouter(); var first = router.ObserveScene("GSD1");
        Eq(first, router.ObserveScene("GSD1")); True(router.TryCapture(ActiveGame.Gsd1, out var captured)); Eq(first, captured);
        var second = router.Invalidate(); Eq(first.Generation + 1, second.Generation); True(!router.IsCurrent(first)); True(router.IsCurrent(second));
        True(!router.TryCapture(ActiveGame.Gsd2, out _));
        Reject<ArgumentNullException>(() => router.TryRun(second, null!));
    }
    private static void RouterIdentity()
    {
        var a = new ActiveGameRouter(); var b = new ActiveGameRouter();
        var one = a.ObserveScene("GSD1"); var two = b.ObserveScene("GSD1");
        Eq(one.Game, two.Game); Eq(one.Generation, two.Generation); True(one != two);
        True(!a.IsCurrent(two)); True(!b.IsCurrent(one)); True(!a.IsCurrent(default));
    }
    private static void NestedCallbacks()
    {
        var router = new ActiveGameRouter(); var first = router.ObserveScene("GSD1"); var actions = new List<string>();
        Action late = () => router.TryRun(first, () => actions.Add("old"));
        True(router.TryRun(first, () => {
            actions.Add("start1"); router.ObserveScene(null); late();
            var second = router.ObserveScene("GSD2");
            True(!router.TryRun(first, () => actions.Add("stale nested1")));
            True(router.TryRun(second, () => actions.Add("nested2")));
        }));
        router.ObserveScene("GSD1"); late(); Eq("start1,nested2", string.Join(",", actions));
    }
    private static void ClassifierFailures()
    {
        var router = new ActiveGameRouter(scene => scene == "bad" ? throw new InvalidOperationException("classifier failed")
            : scene == "invalid" ? (ActiveGame)9 : scene == "one" ? ActiveGame.Gsd1 : ActiveGame.None);
        var valid = router.ObserveScene("one"); True(router.IsCurrent(valid));
        Eq(ActiveGame.None, router.ObserveScene("bad").Game); True(!router.IsCurrent(valid)); Eq("classifier failed", router.LastClassificationFailure);
        Eq(ActiveGame.None, router.ObserveScene("invalid").Game); True(router.LastClassificationFailure is not null);
        router.ObserveScene("one"); True(router.LastClassificationFailure is null);
    }
    private static void Selection()
    {
        var x = Setup(); True(!x.Host.SelectUi().HasBackend); True(!x.Host.Tick()); Eq(0, x.One.StatusCalls + x.Two.StatusCalls);
        x.Router.ObserveScene("GSD1"); var one = x.Host.SelectUi(); True(one.HasBackend); Eq("One", one.DisplayName); Eq(ActiveGame.Gsd1, one.Game);
        True(x.Host.Tick()); Eq(1, x.One.Ticks); Eq(0, x.Two.Ticks); Eq(0, x.Two.StatusCalls);
        x.Router.ObserveScene("GSD2"); var two = x.Host.SelectUi(); Eq("Two", two.DisplayName); Eq(ActiveGame.Gsd2, two.Game);
        True(x.Host.Tick()); Eq(1, x.One.Ticks); Eq(1, x.Two.Ticks);
        x.Router.ObserveScene(null); True(!x.Host.SelectUi().HasBackend); True(!x.Host.Tick());
    }
    private static void F6Destination()
    {
        var x = Setup(); x.Router.ObserveScene("GSD1");
        True(x.Host.DispatchF6(10, true, true)); True(!x.Host.DispatchF6(10, true, true));
        x.Router.ObserveScene("GSD2"); True(!x.Host.DispatchF6(10, true, true)); True(x.Host.DispatchF6(11, true, true));
        Eq(1, x.One.F6); Eq(1, x.Two.F6);
        True(x.One.LastToken.Game == ActiveGame.Gsd1 && x.Two.LastToken.Game == ActiveGame.Gsd2);
    }
    private static void InputAdmission()
    {
        var x = Setup(); x.Router.ObserveScene("GSD1");
        True(!x.Host.DispatchF6(1, false, true)); True(!x.Host.DispatchF6(1, true, false)); Eq(0, x.One.F6);
        True(x.Host.DispatchF6(1, true, true)); Eq(1, x.One.F6);
        x.Router.ObserveScene(null); True(!x.Host.DispatchF6(2, true, true));
        x.Router.ObserveScene("GSD2"); True(!x.Host.DispatchF6(2, true, true)); Eq(0, x.Two.F6);
    }
    private static void FaultIsolation()
    {
        var x = Setup(); var first = x.Router.ObserveScene("GSD1"); x.One.ThrowTick = true;
        True(!x.Host.Tick()); var failure = x.Host.GetFailure(ActiveGame.Gsd1)!; Eq(first.Generation, failure.Generation); Eq("tick", failure.Operation);
        True(x.Host.SelectUi().Faulted); True(!x.Host.Tick()); True(!x.Host.DispatchF6(1, true, true)); Eq(1, x.One.Ticks);
        x.Router.ObserveScene(null); x.Router.ObserveScene("GSD2"); True(x.Host.Tick()); True(x.Host.DispatchF6(2, true, true));
        Eq(1, x.Two.Ticks); Eq(1, x.Two.F6); True(x.Host.GetFailure(ActiveGame.Gsd2) is null);
        x.Router.ObserveScene("GSD1"); x.One.ThrowTick = false; True(x.Host.Tick()); True(!x.Host.SelectUi().Faulted);
        x.Host.ClearFailure(ActiveGame.Gsd1); True(x.Host.GetFailure(ActiveGame.Gsd1) is null);
    }
    private static void FaultedStatus()
    {
        var x = Setup(); x.Router.ObserveScene("GSD1"); x.One.Faulted = true;
        var view = x.Host.SelectUi(); True(view.HasBackend && view.Faulted); Eq("One status", view.Status);
        True(!x.Host.DispatchF6(3, true, true)); Eq(0, x.One.F6);
        x.One.OnTick = () => x.One.Faulted = false;
        True(x.Host.Tick()); Eq(1, x.One.Ticks); True(!x.Host.SelectUi().Faulted);
        True(x.Host.DispatchF6(4, true, true)); Eq(1, x.One.F6);
        True(x.Host.GetFailure(ActiveGame.Gsd1) is null);
    }
    private static void ReentrantStatus()
    {
        var x = Setup(); x.Router.ObserveScene("GSD1"); x.One.OnStatus = () => x.Router.ObserveScene("GSD2");
        True(x.Host.Tick()); Eq(1, x.One.Ticks); Eq(0, x.One.StatusCalls);
        True(!x.Host.DispatchF6(1, true, true)); Eq(0, x.One.F6); Eq(0, x.Two.F6);
        True(x.Host.Tick()); Eq(1, x.Two.Ticks);
        x.Router.ObserveScene("GSD1"); var snapshot = x.Host.SelectUi(); True(!snapshot.HasBackend); Eq(ActiveGame.Gsd2, snapshot.Game);
    }
    private static void LateFault()
    {
        var x = Setup(); var captured = x.Router.ObserveScene("GSD1");
        x.One.OnStatus = () => x.Router.ObserveScene("GSD2"); x.One.ThrowStatus = true;
        var view = x.Host.SelectUi(); True(!view.HasBackend); Eq(ActiveGame.Gsd2, view.Game);
        var failure = x.Host.GetFailure(ActiveGame.Gsd1)!; Eq(captured.Generation, failure.Generation);
        True(x.Host.GetFailure(ActiveGame.Gsd2) is null); True(x.Host.Tick()); True(!x.Host.SelectUi().Faulted);
    }
    private static void F6Fault()
    {
        var x = Setup(); x.Router.ObserveScene("GSD1"); x.One.ThrowF6 = true;
        True(!x.Host.DispatchF6(1, true, true)); Eq("F6", x.Host.GetFailure(ActiveGame.Gsd1)!.Operation);
        x.Router.ObserveScene("GSD2"); True(x.Host.DispatchF6(2, true, true)); Eq(1, x.Two.F6);
    }
    private static void NestedFaultGeneration()
    {
        var x = Setup(); var first = x.Router.ObserveScene("GSD1");
        x.One.OnStatus = () =>
        {
            x.One.OnStatus = null; x.Router.Invalidate(); x.One.ThrowTick = true;
            True(!x.Host.Tick()); x.One.ThrowStatus = true;
        };
        True(!x.Host.SelectUi().HasBackend);
        var failure = x.Host.GetFailure(ActiveGame.Gsd1)!;
        Eq(first.Generation + 1, failure.Generation); Eq("tick", failure.Operation);
        Eq(1, x.One.Ticks); True(!x.Host.Tick()); Eq(1, x.One.Ticks);
        True(x.Host.SelectUi().Faulted);
    }
    private static void Registration()
    {
        var x = Setup(); Reject<InvalidOperationException>(() => x.Host.Register(new Backend(ActiveGame.Gsd1, x.Router)));
        Reject<ArgumentException>(() => x.Host.Register(new Backend(ActiveGame.None, x.Router)));
        Reject<ArgumentException>(() => x.Host.Register(new Backend((ActiveGame)42, x.Router)));
        Reject<ArgumentNullException>(() => x.Host.Register(null!)); Reject<ArgumentNullException>(() => new GameUiCoordinator(null!));
    }
    private static void LegacyPaths()
    {
        var one = LegacyGamePaths.For(ActiveGame.Gsd1); var two = LegacyGamePaths.For(ActiveGame.Gsd2);
        Eq("agilen.suikoden.recruitment_randomizer.cfg", one.ConfigFileName);
        Eq("agilen.suikoden2.recruitment_randomizer.cfg", two.ConfigFileName);
        Eq("SuikodenRandomizer/avatar-shuffle/save-bindings", one.PrimaryBindingDirectory);
        Eq("SuikodenRandomizer/gsd2-campaigns", two.PrimaryBindingDirectory);
        Eq("gsd1", one.NativeSaveDirectoryName); Eq("gsd2", two.NativeSaveDirectoryName);
        True(one.BindingDirectoriesToPreserve.Contains("SuikodenRandomizer/avatar-shuffle/nuzlocke-save-bindings"));
        True(one.BindingDirectoriesToPreserve.Contains("SuikodenRandomizer/avatar-shuffle/fixed-roster-save-bindings"));
        True(one.BindingDirectoriesToPreserve.Contains("SuikodenRandomizer/avatar-pilot/save-bindings"));
        True(!one.BindingDirectoriesToPreserve.Intersect(two.BindingDirectoriesToPreserve).Any());
        Eq(1, two.BindingDirectoriesToPreserve.Count);
        Reject<NotSupportedException>(() => ((IList<string>)one.BindingDirectoriesToPreserve)[0] = "replacement");
        Reject<ArgumentOutOfRangeException>(() => LegacyGamePaths.For(ActiveGame.None));
        Reject<ArgumentOutOfRangeException>(() => LegacyGamePaths.For((ActiveGame)42));
    }
    private static void LoadedRootBattleContext()
    {
        foreach (var game in new[] { ActiveGame.Gsd1, ActiveGame.Gsd2 })
        {
            var x = Setup();
            var first = x.Router.ObserveGameContext(game, game == ActiveGame.Gsd1, game == ActiveGame.Gsd2);
            int callbacks = 0;
            // Active-scene names deliberately do not enter the ownership API.
            // The game's loaded root and selection remain unchanged through these views.
            string?[] views = { "GSD2", "gsd2-hd-battle-bga0", "Main", null, "", "GSD1", "gsd1-hd-battle-bg00" };
            foreach (string? _ in views)
            {
                var next = x.Router.ObserveGameContext(game, game == ActiveGame.Gsd1, game == ActiveGame.Gsd2);
                Eq(first, next); True(x.Router.IsCurrent(first));
                True(x.Router.TryRun(first, () => callbacks++));
                True(x.Host.Tick()); Eq(game, x.Host.SelectUi().Game);
            }
            Eq(views.Length, callbacks);
            Eq(game == ActiveGame.Gsd1 ? views.Length : 0, x.One.Ticks);
            Eq(game == ActiveGame.Gsd2 ? views.Length : 0, x.Two.Ticks);
            True(x.Host.DispatchF6(99, true, true)); True(!x.Host.DispatchF6(99, true, true));
            Eq(game == ActiveGame.Gsd1 ? 1 : 0, x.One.F6);
            Eq(game == ActiveGame.Gsd2 ? 1 : 0, x.Two.F6);
        }
    }
    private static void SelectedRootRequired()
    {
        var router = new ActiveGameRouter();
        foreach (var selection in new[] { ActiveGame.None, ActiveGame.Gsd1, ActiveGame.Gsd2 })
            Eq(ActiveGame.None, router.ObserveGameContext(selection, false, false).Game);
        Eq(ActiveGame.None, router.ObserveGameContext(ActiveGame.Gsd1, false, true).Game);
        Eq(ActiveGame.None, router.ObserveGameContext(ActiveGame.Gsd2, true, false).Game);
        Eq(ActiveGame.None, router.ObserveGameContext(ActiveGame.None, true, true).Game);
        Eq(0L, router.Generation);
    }
    private static void SelectionBeforeUnload()
    {
        var x = Setup(); var old = x.Router.ObserveGameContext(ActiveGame.Gsd2, false, true);
        var loading = x.Router.ObserveGameContext(ActiveGame.Gsd1, false, true);
        Eq(ActiveGame.None, loading.Game); True(!x.Router.IsCurrent(old)); True(!x.Host.Tick());
        var incoming = x.Router.ObserveGameContext(ActiveGame.Gsd1, true, true);
        Eq(ActiveGame.Gsd1, incoming.Game); True(!x.Router.TryRun(old, () => throw new Exception("stale callback ran")));
        True(x.Host.Tick()); Eq(1, x.One.Ticks); Eq(0, x.Two.Ticks);
    }
    private static void BothRoots()
    {
        var router = new ActiveGameRouter();
        var first = router.ObserveGameContext(ActiveGame.Gsd1, true, true);
        Eq(ActiveGame.Gsd1, first.Game);
        var second = router.ObserveGameContext(ActiveGame.Gsd2, true, true);
        Eq(ActiveGame.Gsd2, second.Game); Eq(first.Generation + 1, second.Generation);
        True(!router.IsCurrent(first)); True(router.IsCurrent(second));
    }
    private static void RootUnload()
    {
        var router = new ActiveGameRouter(); var before = router.ObserveGameContext(ActiveGame.Gsd2, false, true);
        Eq(ActiveGame.None, router.ObserveGameContext(ActiveGame.Gsd2, false, false).Game);
        var after = router.ObserveGameContext(ActiveGame.Gsd2, false, true);
        True(!router.IsCurrent(before)); True(router.IsCurrent(after)); Eq(before.Generation + 2, after.Generation);
    }
    private static void InvalidGameContext()
    {
        var router = new ActiveGameRouter(); var before = router.ObserveGameContext(ActiveGame.Gsd2, false, true);
        var invalid = router.ObserveGameContext((ActiveGame)42, true, true);
        Eq(ActiveGame.None, invalid.Game); True(!router.IsCurrent(before));
        True(router.LastClassificationFailure is not null);
        router.ObserveGameContext(ActiveGame.Gsd2, false, true); True(router.LastClassificationFailure is null);
    }
    private sealed class Backend : IGameUiBackend
    {
        private readonly ActiveGameRouter _router;
        public ActiveGame Game { get; }
        public string DisplayName => Game == ActiveGame.Gsd1 ? "One" : "Two";
        internal int Ticks, F6, StatusCalls;
        internal bool ThrowTick, ThrowF6, ThrowStatus, Faulted;
        internal Action? OnStatus, OnTick;
        internal GameOwnershipToken LastToken;
        internal Backend(ActiveGame game, ActiveGameRouter router) { Game = game; _router = router; }
        public EngineStatus GetStatus(GameOwnershipToken ownership)
        { StatusCalls++; True(_router.IsCurrent(ownership)); LastToken = ownership; OnStatus?.Invoke(); if (ThrowStatus) throw new InvalidOperationException("status fault"); return new(DisplayName + " status", Faulted); }
        public void Tick(GameOwnershipToken ownership)
        { True(_router.IsCurrent(ownership)); LastToken = ownership; Ticks++; OnTick?.Invoke(); if (ThrowTick) throw new InvalidOperationException("tick fault"); }
        public void HandleF6(GameOwnershipToken ownership)
        { True(_router.IsCurrent(ownership)); LastToken = ownership; F6++; if (ThrowF6) throw new InvalidOperationException("input fault"); }
    }
}
