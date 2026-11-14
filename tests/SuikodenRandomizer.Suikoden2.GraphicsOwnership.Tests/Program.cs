using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using SuikodenRandomizer.Suikoden2Plugin;

internal static class Program
{
    private static void Main()
    {
        var cases = new (string, Action)[] {
            ("owned loader creates a canonical graph with donor field data", FreshReady),
            ("Suspend retires references and clears publication without any native/Unity read", SuspendManagedOnly),
            ("foreign Reset performs only managed suspension and leaves residents untouched", ForeignReset),
            ("owned Reset restores canonical resident resources", OwnedReset),
            ("owned Reset restores an old proxy even after managed retirement cleared Originals", RetiredProxyReset),
            ("fresh original preload completions prepare normally", FreshPreload),
            ("preload callbacks after Suspend cannot read malformed assets or fault another game", StalePreloadSuspend),
            ("old preload callbacks cannot remove a new session's in-flight IDs", StalePreloadNewSession),
            ("same-runtime epoch changes reject an old preload independently of generation", StalePreloadEpoch),
            ("runtime replacement rejects an old preload independently of epoch", StalePreloadRuntime),
            ("fresh wrapped native callback receives the projected canonical graph", FreshWrapped),
            ("stale wrapped callback preserves raw native completion without projection", StaleWrappedSuspend),
            ("epoch/runtime changes preserve raw callback completion without foreign reads", StaleWrappedEpochOrRuntime),
            ("a stale native cleanup exception is not blamed on the new runtime", StaleWrappedException),
            ("retired graph and OPD pointer leases remain effective outside GSD2", RetainedLeases),
            ("foreign Tick/GetData/callback preparation do no native work", ForeignIdle),
            ("loader-start exceptions restore the raw-scope depth", RawScopeFailure),
            ("unite raw-resource graph lookup bypasses projection and native reads", UniteRawProject),
            ("callbacks created in raw-resource scope retain their original identity and completion", UniteRawCallbackCreation),
            ("already wrapped callbacks honor a later unite raw-resource scope", UniteRawCallbackReturn),
            ("raw-resource NPC lookup avoids native reads and normal projection resumes", UniteRawNpc)
        };
        foreach (var (name, run) in cases) { run(); Console.WriteLine("PASS " + name); }
        Console.WriteLine(cases.Length + "/" + cases.Length + " production-linked graphics ownership tests passed.");
    }
    private static void True(bool value) { if (!value) throw new Exception("Assertion failed."); }
    private static void Eq<T>(T expected, T actual) { if (!Equals(expected, actual)) throw new Exception("Expected " + expected + ", got " + actual); }
    private static int Count(string field) => ((ICollection)typeof(Gsd2Graphics).GetField(field, BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!).Count;
    private static int Generation => (int)typeof(Gsd2Graphics).GetField("_generation", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
    private static P_GRAPH_DATA Graph(int id)
    {
        var actions = new Il2CppReferenceArray<ACTION_TABLE_DATA>(1);
        actions[0] = new ACTION_TABLE_DATA { action_num = 1, overlay_func_index = 2, action = new Il2CppStructArray<short>(1) };
        actions[0].action[0] = 7;
        return new P_GRAPH_DATA { chara_id = id, battle_data = new PLAYER_BATTLE { opd_data = new(), action_table = actions },
            field_data = new PLAYER_FIELD { otyp = 1, opd_data = new() } };
    }
    private static (Gsd2Runtime Runtime, UnityEngine.MonoBehaviour Host, PARTY_DATA Party, P_GRAPH_DATA Canonical, P_GRAPH_DATA Donor) Setup(bool auto = true)
    {
        Gsd2Graphics.PreserveRawResourceIds = () => false;
        Gsd2Graphics.Suspend(); NativeAccess.Clear();
        PlayerDataLoader.Requests.Clear(); PlayerDataLoader.Assets.Clear(); PlayerDataLoader.ActiveLoads = 0; PlayerDataLoader.Completions = 0;
        PlayerDataLoader.AutoComplete = auto; Gsd2Runtime.OwnerEpoch++;
        var runtime = new Gsd2Runtime(); runtime.Map[28] = 22; Gsd2Runtime.Current = runtime;
        var canonical = Graph(28); var donor = Graph(22); PlayerDataLoader.Assets[28] = canonical; PlayerDataLoader.Assets[22] = donor;
        var party = new PARTY_DATA(); party._cha_gra_adrs_graphdata[0] = canonical; OldSrcBase.game_work = new GAME_WORK { party_data = party };
        var host = new UnityEngine.MonoBehaviour(); Gsd2Graphics.Initialize(host); UnityEngine.Time.Value = 1;
        NativeAccess.Clear(); return (runtime, host, party, canonical, donor);
    }
    private static P_GRAPH_DATA Ready()
    {
        Gsd2Graphics.Tick(); True(Gsd2Graphics.IsReady);
        return OldSrcBase.game_work!.party_data._cha_gra_adrs_graphdata[0];
    }
    private static void FreshReady()
    {
        var x = Setup(); var proxy = Ready(); True(proxy.Pointer != x.Canonical.Pointer); Eq(28, proxy.chara_id);
        Eq(x.Donor.field_data.opd_data.Pointer, proxy.field_data.opd_data.Pointer);
        True(proxy.field_data.Pointer != x.Donor.field_data.Pointer); True(proxy.battle_data.Pointer != x.Donor.battle_data.Pointer);
        Eq(2, Count("Originals")); Eq(1, Count("Proxies")); Eq(0, Count("Loading")); Eq(0, x.Runtime.Failures.Count);
    }
    private static void SuspendManagedOnly()
    {
        var x = Setup(); var proxy = Ready(); int generation = Generation, retired = Count("Retired");
        NativeAccess.Clear(); NativeAccess.Block = true; Gsd2Graphics.Suspend();
        Eq(0, NativeAccess.Calls.Count); Eq(generation + 1, Generation); True(Count("Retired") >= retired + 3);
        Eq(0, Count("Originals")); Eq(0, Count("Proxies")); Eq(0, Count("Loading"));
        Eq(proxy.Pointer, x.Party._cha_gra_adrs_graphdata[0].Pointer); True(!UnityEngine.Object.Destroyed.Contains(proxy.Pointer));
    }
    private static void ForeignReset()
    {
        var x = Setup(); var proxy = Ready(); x.Runtime.Allowed = false;
        NativeAccess.Clear(); NativeAccess.Block = true; Gsd2Graphics.Reset();
        Eq(0, NativeAccess.Calls.Count); Eq(proxy.Pointer, x.Party._cha_gra_adrs_graphdata[0].Pointer); Eq(0, Count("Proxies"));
    }
    private static void OwnedReset()
    {
        var x = Setup(); Ready(); Gsd2Graphics.Reset();
        Eq(x.Canonical.Pointer, x.Party._cha_gra_adrs_graphdata[0].Pointer); Eq(0, Count("Proxies")); True(!Gsd2Graphics.IsReady);
    }
    private static void RetiredProxyReset()
    {
        var x = Setup(); var proxy = Ready(); x.Runtime.Allowed = false;
        NativeAccess.Clear(); NativeAccess.Block = true; Gsd2Graphics.Suspend(); Eq(0, NativeAccess.Calls.Count);
        Eq(proxy.Pointer, x.Party._cha_gra_adrs_graphdata[0].Pointer);
        NativeAccess.Block = false; x.Runtime.Allowed = true; x.Runtime.ExecutionEpoch = ++Gsd2Runtime.OwnerEpoch;
        Gsd2Graphics.Reset(); Eq(x.Canonical.Pointer, x.Party._cha_gra_adrs_graphdata[0].Pointer);
    }
    private static void FreshPreload()
    {
        var x = Setup(false); Gsd2Graphics.Tick(); Eq(2, Count("Loading")); Eq(0, Count("Originals"));
        foreach (var request in PlayerDataLoader.Requests.ToArray()) request.Complete(PlayerDataLoader.Assets[request.CharacterId]);
        Eq(0, Count("Loading")); Eq(2, Count("Originals")); Eq(2, PlayerDataLoader.Completions); Ready(); Eq(0, x.Runtime.Failures.Count);
    }
    private static void StalePreloadSuspend()
    {
        var x = Setup(false); Gsd2Graphics.Tick(); var old = PlayerDataLoader.Requests.ToArray();
        Gsd2Graphics.Suspend(); x.Runtime.Allowed = false; NativeAccess.Clear(); NativeAccess.Block = true;
        foreach (var request in old) request.Complete(new P_GRAPH_DATA());
        Eq(0, NativeAccess.Calls.Count); Eq(0, Count("Originals")); Eq(0, Count("Loading")); Eq(0, x.Runtime.Failures.Count);
        Eq(2, PlayerDataLoader.Completions); Eq(0, PlayerDataLoader.ActiveLoads);
    }
    private static void StalePreloadNewSession()
    {
        var x = Setup(false); Gsd2Graphics.Tick(); var old = PlayerDataLoader.Requests.ToArray(); Gsd2Graphics.Suspend();
        var next = new Gsd2Runtime { ExecutionEpoch = ++Gsd2Runtime.OwnerEpoch }; next.Map[28] = 42; Gsd2Runtime.Current = next;
        PlayerDataLoader.Assets[42] = Graph(42); Gsd2Graphics.Tick(); var fresh = PlayerDataLoader.Requests.Except(old).ToArray();
        Eq(2, Count("Loading")); NativeAccess.Clear(); NativeAccess.Block = true;
        foreach (var request in old) request.Complete(new P_GRAPH_DATA());
        Eq(0, NativeAccess.Calls.Count); Eq(2, Count("Loading")); Eq(0, Count("Originals")); Eq(0, next.Failures.Count); Eq(0, x.Runtime.Failures.Count);
        NativeAccess.Block = false;
        foreach (var request in fresh) request.Complete(PlayerDataLoader.Assets[request.CharacterId]);
        var proxy = Ready(); Eq(PlayerDataLoader.Assets[42].field_data.opd_data.Pointer, proxy.field_data.opd_data.Pointer);
    }
    private static void StalePreloadEpoch()
    {
        var x = Setup(false); Gsd2Graphics.Tick(); int generation = Generation;
        x.Runtime.ExecutionEpoch = ++Gsd2Runtime.OwnerEpoch; NativeAccess.Clear(); NativeAccess.Block = true;
        foreach (var request in PlayerDataLoader.Requests.ToArray()) request.Complete(new P_GRAPH_DATA());
        Eq(generation, Generation); Eq(0, Count("Originals")); Eq(2, Count("Loading")); Eq(0, x.Runtime.Failures.Count); Eq(0, NativeAccess.Calls.Count);
    }
    private static void StalePreloadRuntime()
    {
        var x = Setup(false); Gsd2Graphics.Tick(); var next = new Gsd2Runtime(); next.Map[28] = 42; Gsd2Runtime.Current = next;
        NativeAccess.Clear(); NativeAccess.Block = true;
        foreach (var request in PlayerDataLoader.Requests.ToArray()) request.Complete(new P_GRAPH_DATA());
        Eq(0, Count("Originals")); Eq(0, next.Failures.Count); Eq(0, x.Runtime.Failures.Count); Eq(0, NativeAccess.Calls.Count);
    }
    private static Il2CppSystem.Action<P_GRAPH_DATA> Wrapped(Action<P_GRAPH_DATA> callback)
    {
        var wrapped = new Il2CppSystem.Action<P_GRAPH_DATA>(callback); Gsd2Graphics.WrapCallback(28, false, ref wrapped); return wrapped;
    }
    private static void FreshWrapped()
    {
        var x = Setup(); var proxy = Ready(); P_GRAPH_DATA? delivered = null;
        Wrapped(asset => delivered = asset).Invoke(x.Canonical); Eq(proxy.Pointer, delivered!.Pointer); Eq(0, x.Runtime.Failures.Count);
    }
    private static void StaleWrappedSuspend()
    {
        var x = Setup(); Ready(); int cleanup = 0; P_GRAPH_DATA? delivered = null;
        var callback = Wrapped(asset => { cleanup++; delivered = asset; }); Gsd2Graphics.Suspend(); x.Runtime.Allowed = false;
        var raw = new P_GRAPH_DATA(); NativeAccess.Clear(); NativeAccess.Block = true; callback.Invoke(raw);
        Eq(1, cleanup); Eq(raw.Pointer, delivered!.Pointer); Eq(0, NativeAccess.Calls.Count); Eq(0, x.Runtime.Failures.Count);
    }
    private static void StaleWrappedEpochOrRuntime()
    {
        foreach (bool replace in new[] { false, true })
        {
            var x = Setup(); Ready(); P_GRAPH_DATA? delivered = null; var callback = Wrapped(asset => delivered = asset);
            if (replace) Gsd2Runtime.Current = new Gsd2Runtime(); else x.Runtime.ExecutionEpoch = ++Gsd2Runtime.OwnerEpoch;
            var current = Gsd2Runtime.Current!; var raw = new P_GRAPH_DATA(); NativeAccess.Clear(); NativeAccess.Block = true;
            callback.Invoke(raw); Eq(raw.Pointer, delivered!.Pointer); Eq(0, current.Failures.Count); Eq(0, x.Runtime.Failures.Count); Eq(0, NativeAccess.Calls.Count);
        }
    }
    private static void StaleWrappedException()
    {
        var x = Setup(); Ready(); var expected = new InvalidOperationException("original cleanup failure");
        var callback = Wrapped(_ => throw expected); Gsd2Graphics.Suspend(); Gsd2Runtime.Current = new Gsd2Runtime();
        NativeAccess.Clear(); NativeAccess.Block = true;
        try { callback.Invoke(new P_GRAPH_DATA()); throw new Exception("Expected native cleanup exception."); }
        catch (InvalidOperationException error) { True(ReferenceEquals(expected, error)); }
        Eq(0, Gsd2Runtime.Current.Failures.Count); Eq(0, x.Runtime.Failures.Count); Eq(0, NativeAccess.Calls.Count);
    }
    private static void RetainedLeases()
    {
        var x = Setup(); var proxy = Ready(); var field = x.Donor.field_data.opd_data; var battle = x.Canonical.battle_data.opd_data;
        Gsd2Graphics.Suspend(); x.Runtime.Allowed = false; NativeAccess.Clear(); NativeAccess.Block = true;
        True(Gsd2Graphics.MustRetain(x.Canonical)); True(Gsd2Graphics.MustRetain(x.Donor)); True(Gsd2Graphics.MustRetain(proxy));
        True(Gsd2Graphics.MustRetainResource(field)); True(Gsd2Graphics.MustRetainResource(battle));
        True(!Gsd2Graphics.MustRetainResource(new UnityEngine.Object())); Eq(0, NativeAccess.Calls.Count);
    }
    private static void ForeignIdle()
    {
        var x = Setup(); Ready(); x.Runtime.Allowed = false; var raw = new P_GRAPH_DATA();
        var callback = new Il2CppSystem.Action<P_GRAPH_DATA>(_ => { }); var originalCallback = callback;
        NativeAccess.Clear(); NativeAccess.Block = true; Gsd2Graphics.Tick();
        Eq(raw.Pointer, Gsd2Graphics.Project(28, false, raw)!.Pointer); Gsd2Graphics.WrapCallback(28, false, ref callback);
        True(ReferenceEquals(originalCallback, callback)); True(Gsd2Graphics.NpcFieldOpd(28) is null);
        Eq(0, NativeAccess.Calls.Count); Eq(0, x.Runtime.Failures.Count);
    }
    private static void RawScopeFailure()
    {
        var x = Setup(false); x.Host.ThrowStart = true; Gsd2Graphics.Tick();
        True(!Gsd2Graphics.LoadingRaw); Eq(1, x.Runtime.Failures.Count); True(!Gsd2Graphics.IsReady);
    }
    private static void UniteRawProject()
    {
        var x = Setup(); var proxy = Ready(); var raw = new P_GRAPH_DATA();
        x.Runtime.Map[28] = -1; // Any attempted mapping/projection now fails independently of native-read guards.
        Gsd2Graphics.PreserveRawResourceIds = () => true;
        try
        {
            NativeAccess.Clear(); NativeAccess.Block = true;
            True(ReferenceEquals(raw, Gsd2Graphics.Project(28, false, raw)));
            Eq(0, NativeAccess.Calls.Count); Eq(0, x.Runtime.Failures.Count);
        }
        finally { Gsd2Graphics.PreserveRawResourceIds = () => false; NativeAccess.Block = false; x.Runtime.Map[28] = 22; }
        True(ReferenceEquals(proxy, Gsd2Graphics.Project(28, false, x.Canonical)));
    }
    private static void UniteRawCallbackCreation()
    {
        var x = Setup(); Ready(); P_GRAPH_DATA? delivered = null;
        var callback = new Il2CppSystem.Action<P_GRAPH_DATA>(asset => delivered = asset); var original = callback;
        Gsd2Graphics.PreserveRawResourceIds = () => true;
        try
        {
            NativeAccess.Clear(); NativeAccess.Block = true;
            Gsd2Graphics.WrapCallback(28, false, ref callback); True(ReferenceEquals(original, callback));
            var raw = new P_GRAPH_DATA(); callback.Invoke(raw); True(ReferenceEquals(raw, delivered));
            Eq(0, NativeAccess.Calls.Count); Eq(0, x.Runtime.Failures.Count);
        }
        finally { Gsd2Graphics.PreserveRawResourceIds = () => false; NativeAccess.Block = false; }
        // Native completion may arrive after the outer raw-resource scope ended.
        callback.Invoke(x.Canonical); True(ReferenceEquals(x.Canonical, delivered));
        True(!Gsd2Graphics.LoadingRaw);
    }
    private static void UniteRawCallbackReturn()
    {
        var x = Setup(); var proxy = Ready(); P_GRAPH_DATA? delivered = null;
        var callback = Wrapped(asset => delivered = asset); var raw = new P_GRAPH_DATA();
        x.Runtime.Map[28] = -1; Gsd2Graphics.PreserveRawResourceIds = () => true;
        try
        {
            NativeAccess.Clear(); NativeAccess.Block = true; callback.Invoke(raw);
            True(ReferenceEquals(raw, delivered)); Eq(0, NativeAccess.Calls.Count); Eq(0, x.Runtime.Failures.Count);
        }
        finally { Gsd2Graphics.PreserveRawResourceIds = () => false; NativeAccess.Block = false; x.Runtime.Map[28] = 22; }
        callback.Invoke(x.Canonical); True(ReferenceEquals(proxy, delivered));
    }
    private static void UniteRawNpc()
    {
        var x = Setup(); Ready(); x.Runtime.Map[28] = -1; Gsd2Graphics.PreserveRawResourceIds = () => true;
        try
        {
            NativeAccess.Clear(); NativeAccess.Block = true; True(Gsd2Graphics.NpcFieldOpd(28) is null);
            Eq(0, NativeAccess.Calls.Count); Eq(0, x.Runtime.Failures.Count);
        }
        finally { Gsd2Graphics.PreserveRawResourceIds = () => false; NativeAccess.Block = false; x.Runtime.Map[28] = 22; }
        True(Gsd2Graphics.NpcFieldOpd(28) is not null);
    }
}
