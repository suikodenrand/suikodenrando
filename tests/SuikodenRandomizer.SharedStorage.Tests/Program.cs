using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using SuikodenRandomizer.AvatarPlugin;
using SuikodenRandomizer.Suikoden2Plugin;

internal static class Program
{
    private static int _assertions;
    private static readonly MethodInfo S1 = typeof(AvatarStorageGuardPatch).GetMethod("Prefix", BindingFlags.NonPublic | BindingFlags.Static)!;
    private static readonly MethodInfo S2 = typeof(Gsd2StorageBindingPatch).GetMethod("Prefix", BindingFlags.NonPublic | BindingFlags.Static)!;
    private static void Check(bool value, string reason)
    { Interlocked.Increment(ref _assertions); if (!value) throw new Exception(reason); }
    private sealed record Result(bool Native, bool Value, bool[] Callbacks, bool Mirror);

    // Evaluate the actual production prefixes in either order, with both strict
    // stop-on-veto and adversarial continue-after-veto composition. No native call.
    private static Result Store(bool firstS1, bool stopOnVeto, string json = "exact native JSON", bool throwingCallback = false)
    {
        bool result = true, runOriginal = true; var callbacks = new List<bool>();
        Il2CppSystem.Action<bool> callback = success => { callbacks.Add(success); if (throwingCallback) throw new InvalidOperationException("consumer callback"); };
        foreach (bool isS1 in firstS1 ? new[] { true, false } : new[] { false, true })
        {
            if (stopOnVeto && !runOriginal) continue;
            object?[] args = isS1 ? new object?[] { callback, result } : new object?[] { json, callback, result };
            bool allowed = (bool)(isS1 ? S1 : S2).Invoke(null, args)!;
            result = (bool)args[^1]!; runOriginal &= allowed;
        }
        if (runOriginal) { result = true; callback(true); }
        // Harmony runs postfixes even after a prefix veto. Compose the actual
        // predicate installed on the verified Fix managed mirror writer too.
        bool mirror = Gsd2RejectedSaveMirrorGuard.AllowMirrorWrite();
        return new Result(runOriginal, result, callbacks.ToArray(), mirror);
    }
    private static (AvatarRuntime One, Gsd2Runtime Two) Setup(string owner = "GSD1", long epoch = 1)
    {
        Owner.Game = owner; Owner.Epoch = epoch;
        AvatarSaveCall.Current = null; Gsd2SaveScope.Current = null;
        Gsd2InitialSavePosition.Reject = false; Gsd2CheckpointCapture.Reject = false;
        var one = new AvatarRuntime(); var two = new Gsd2Runtime(); Gsd2Runtime.Current = two;
        return (one, two);
    }
    private static void Saved(Result result, string message)
        => Check(result.Native && result.Value && result.Mirror && result.Callbacks.SequenceEqual(new[] { true }), message);
    private static void Refused(Result result, string message)
        => Check(!result.Native && !result.Value && result.Callbacks.SequenceEqual(new[] { false }), message);
    private static void RefusedS2(Result result, string message)
    {
        Refused(result,message);
        Check(!result.Mirror,message+"; rejected mirror write also blocked");
    }

    private static void Main()
    {
        foreach (bool firstS1 in new[] { false, true }) foreach (bool stop in new[] { false, true })
        {
            var (one, two) = Setup();
            Saved(Store(firstS1, stop), "No-scope shared storage must pass once");
            Check(one.Traces.IsEmpty && two.Bindings.IsEmpty, "No-scope call acquired a game's side effects");

            AvatarSaveCall.Current = new AvatarSaveCall { Runtime = one, ExecutionEpoch = 1, BindingFailed = false };
            Saved(Store(firstS1, stop), "Ready S1 save was blocked by S2");
            Check(two.Bindings.IsEmpty, "S1 save generated S2 campaign binding");
            AvatarSaveCall.Current.BindingFailed = true;
            Refused(Store(firstS1, stop), "Failed S1 binding was not refused exactly once");
            Check(two.Bindings.IsEmpty, "Failed S1 save entered S2 binding");
            Refused(Store(firstS1, stop, throwingCallback: true), "Throwing S1 failure callback escaped or double-fired");

            Owner.Game = "GSD2";
            var s2Scope = new Gsd2SaveScope { Runtime = two, BindCampaign = true };
            Gsd2SaveScope.Current = s2Scope;
            string raw = new(new[] { '{', ' ', '}', '\n' });
            Saved(Store(firstS1, stop, raw), "Foreign failed S1 scope blocked owned S2 storage");
            Check(s2Scope.Ready && two.Bindings.TryDequeue(out string? bound) && ReferenceEquals(bound, raw), "S2 binding lost exact string or readiness");
            Check(two.Bindings.IsEmpty, "S2 bound a single storage call more than once");

            two.ThrowBind = true;
            RefusedS2(Store(firstS1, stop), "S2 binding error did not refuse exactly once");
            Check(!s2Scope.Ready && two.Bindings.IsEmpty, "Rejected S2 save retained ready flag or wrong binding");
            RefusedS2(Store(firstS1, stop, throwingCallback: true), "S2 failure callback exception escaped or double-fired");
            two.ThrowBind = false;

            two.BindReturnsFalse = true;
            RefusedS2(Store(firstS1, stop), "A no-op S2 binder admitted unbound native storage");
            Check(!s2Scope.Ready && two.Bindings.IsEmpty, "A no-op S2 binder claimed readiness");
            two.BindReturnsFalse = false;
            two.DuringBind = () => Owner.Epoch++;
            RefusedS2(Store(firstS1, stop), "S2 ownership loss during binding admitted stale storage");
            Check(!s2Scope.Ready && two.Faults.IsEmpty, "Stale binding claimed readiness or faulted another campaign");
            two.DuringBind = null; Owner.Epoch = 1;
            two.Bindings.Clear();
            two.DuringBind = () => Owner.Game = "GSD1";
            AvatarSaveCall.Current.BindingFailed = false;
            Saved(Store(firstS1, stop), "A true foreign handoff during binding was blocked by S2");
            Check(!s2Scope.Ready, "Foreign handoff marked the old S2 binding ready");
            two.DuringBind = null; Owner.Game = "GSD2"; AvatarSaveCall.Current.BindingFailed = true;
            two.Bindings.Clear();

            s2Scope.InitializeCampPosition = true; Gsd2InitialSavePosition.Reject = true;
            RefusedS2(Store(firstS1, stop), "Camp validation refusal reached native shared storage");
            Check(two.CampBindings == 0 && two.Bindings.IsEmpty && !two.Faults.IsEmpty, "Camp refusal consumed initial receipt or missed fault");
            Gsd2InitialSavePosition.Reject = false;
            RefusedS2(Store(firstS1, stop), "Faulted owned camp scope became ready after a retry");
            Check(!s2Scope.Ready && two.Bindings.IsEmpty, "Faulted retry produced a false ready state");
            two = new Gsd2Runtime(); Gsd2Runtime.Current = two;
            s2Scope = new Gsd2SaveScope { Runtime = two, BindCampaign = true, InitializeCampPosition = true };
            Gsd2SaveScope.Current = s2Scope;
            Saved(Store(firstS1, stop), "Valid initial camp save was blocked");
            Check(two.CampBindings == 1 && two.Bindings.Count == 1, "Initial camp save did not bind exactly once");
            RefusedS2(Store(firstS1, stop), "Duplicate initial camp storage was accepted");
            Check(two.CampBindings == 1 && two.Bindings.Count == 1, "Duplicate initial camp storage wrote another binding");

            int s1Traces = one.Traces.Count, s2Binds = two.Bindings.Count;
            Owner.Game = "GSD1";
            AvatarSaveCall.Current.BindingFailed = false;
            Saved(Store(firstS1, stop), "Foreign failed S2 scope blocked ready S1 save");
            Check(two.Bindings.Count == s2Binds && one.Traces.Count == s1Traces, "Foreign S2 scope mutated another game's binding");
            Owner.Game = "Main";
            AvatarSaveCall.Current.BindingFailed = true;
            Saved(Store(firstS1, stop), "Launcher storage was blocked by stale title scopes");
            Owner.Game = "GSD2"; Owner.Epoch++;
            RefusedS2(Store(firstS1, stop), "Stale S2 campaign scope admitted unbound storage in a new S2 epoch");
            Check(two.Bindings.Count == s2Binds, "Stale S2 scope bound a new epoch string");

            // Captured runtime identity is checked independently from the epoch.
            Owner.Epoch = 1; var replacement = new Gsd2Runtime(); Gsd2Runtime.Current = replacement;
            RefusedS2(Store(firstS1, stop), "Old S2 runtime scope admitted unbound replacement storage");
            Check(replacement.Bindings.IsEmpty && two.Bindings.Count == s2Binds, "Stale runtime bound into either campaign");
            Owner.Game = "Main"; Gsd2Runtime.MayOwnSaveContext = () => true;
            RefusedS2(Store(firstS1, stop), "Cached selected-S2 lost root admitted an unbound native save");
            Check(replacement.Bindings.IsEmpty && !s2Scope.Ready, "Lost root rebound a stale campaign");
            Gsd2Runtime.MayOwnSaveContext = () => Owner.Game == "GSD2";
            Owner.Game = "GSD1"; AvatarSaveCall.Current.BindingFailed = false;
            Saved(Store(firstS1, stop), "Confirmed foreign game was blocked by cached S2 refusal");

            Setup("GSD2"); int exports = Gsd2CheckpointCapture.Exports.Count;
            Gsd2SaveScope.Current = new Gsd2SaveScope { CaptureAllowed = true };
            Saved(Store(firstS1, stop, raw), "Native capture blocked ordinary native storage");
            Check(Gsd2CheckpointCapture.Exports.Count == exports + 1, "Capture did not export exactly once");
            Gsd2CheckpointCapture.Reject = true;
            Saved(Store(firstS1, stop, raw), "Capture export failure changed native save flow");
            Check(!Gsd2Plugin.PluginLog.Warnings.IsEmpty, "Capture error was not reported");

            // Nested logical contexts keep the outer stale scope neutral while
            // the new current owner saves; scopes are explicitly unwound by host.
            (one, two) = Setup("GSD1", 10);
            var outer = new AvatarSaveCall { Runtime = one, ExecutionEpoch = 10, BindingFailed = true };
            AvatarSaveCall.Current = outer; Owner.Game = "GSD2"; Owner.Epoch = 11;
            two = new Gsd2Runtime(); Gsd2Runtime.Current = two;
            var inner = new Gsd2SaveScope { Runtime = two, BindCampaign = true };
            Gsd2SaveScope.Current = inner;
            Saved(Store(firstS1, stop), "Nested foreign failed outer scope blocked current owner");
            Check(ReferenceEquals(AvatarSaveCall.Current, outer) && ReferenceEquals(Gsd2SaveScope.Current, inner), "Storage guards changed scope stack ownership");
            Gsd2SaveScope.Current = inner.Previous; Owner.Game = "GSD1"; Owner.Epoch = 12;
            Saved(Store(firstS1, stop), "Unwound stale outer epoch blocked unrelated storage");
        }

        // ThreadStatic scope isolation, with independent fake owner contexts.
        var shared = Setup("GSD1", 99); Exception? error = null;
        var barrier = new Barrier(2);
        Thread ThreadFor(string game, bool firstS1) => new(() =>
        {
            try
            {
                Owner.Game = game; Owner.Epoch = 99;
                Check(AvatarSaveCall.Current is null && Gsd2SaveScope.Current is null, "New thread inherited another scope");
                if (game == "GSD1") AvatarSaveCall.Current = new AvatarSaveCall { Runtime = shared.One, ExecutionEpoch = 99, BindingFailed = true };
                else Gsd2SaveScope.Current = new Gsd2SaveScope { Runtime = shared.Two, BindCampaign = true };
                Check(barrier.SignalAndWait(TimeSpan.FromSeconds(5)), "Thread scope barrier timed out");
                var result = Store(firstS1, false);
                if (game == "GSD1") Refused(result, "Thread S1 refusal lost its scope"); else Saved(result, "Thread S2 save was blocked by other thread");
                Check(game == "GSD1" ? Gsd2SaveScope.Current is null : AvatarSaveCall.Current is null, "Foreign thread scope leaked");
                AvatarSaveCall.Current = null; Gsd2SaveScope.Current = null;
            }
            catch (Exception ex) { Interlocked.CompareExchange(ref error, ex, null); }
        }) { IsBackground = true };
        var a = ThreadFor("GSD1", false); var b = ThreadFor("GSD2", true); a.Start(); b.Start();
        Check(a.Join(TimeSpan.FromSeconds(10)) && b.Join(TimeSpan.FromSeconds(10)), "Thread scope test did not complete");
        if (error is not null) throw error;
        Check(shared.Two.Bindings.Count == 1, "Thread-isolated S2 native snapshot not bound once");
        Check(AvatarSaveCall.Current is null && Gsd2SaveScope.Current is null, "Child thread scope entered main thread");
        Console.WriteLine($"PASS {_assertions} actual-linked shared-storage composition assertions; both prefix orders and veto modes.");
    }
}
