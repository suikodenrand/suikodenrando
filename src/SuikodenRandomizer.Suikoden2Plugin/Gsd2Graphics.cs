extern alias GSD2;

using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SuikodenRandomizer.Suikoden2Plugin;

/// <summary>Original loader leases and detached graph assets; cache and story IDs stay native.</summary>
internal static class Gsd2Graphics
{
    [ThreadStatic] private static int _rawDepth;
    private static MonoBehaviour? _host;
    private static Gsd2Runtime? _session;
    private static int _generation;
    private static bool _ready;
    private static IReadOnlyDictionary<int, int>? _preparedMap, _requestedMap;
    private static int[] _npcDonors = Array.Empty<int>();
    private static float _started;
    private static readonly Dictionary<int, GSD2.P_GRAPH_DATA> Originals = new();
    private static readonly Dictionary<int, GSD2.P_GRAPH_DATA> Proxies = new();
    private static readonly Dictionary<int, float> Loading = new();
    private static readonly Dictionary<IntPtr, GSD2.P_GRAPH_DATA> ProxyOriginals = new();
    private static readonly HashSet<IntPtr> LeasedOpdPointers = new();
    private static readonly Gsd2NpcDirectionAdapters.RetryGate NpcAdapterRetries = new();
    // Native renderers may retain graph references after a campaign is retired.
    // Retain each source and each detached projection once across campaign loads.
    private static readonly Dictionary<IntPtr, GSD2.P_GRAPH_DATA> LeasedGraphs = new();
    private static readonly Dictionary<(IntPtr Canonical, IntPtr Donor), GSD2.P_GRAPH_DATA> CachedProxies = new();

    internal static bool IsReady => _ready && ReferenceEquals(_session, Gsd2Runtime.Current)
        && _session?.TablesReady == true && ReferenceEquals(_preparedMap, _session.Assignments);
    internal static Func<bool> PreserveRawResourceIds { get; set; } = () => false;
    internal static bool LoadingRaw => _rawDepth != 0 || PreserveRawResourceIds();
    internal static void Initialize(MonoBehaviour host) => _host = host ?? throw new ArgumentNullException(nameof(host));

    // NPC factories receive a scene-local OPD rather than a party graph. Read
    // the already-prepared canonical proxy to avoid applying a permutation twice.
    internal static global::OPD_DATA? NpcFieldOpd(int canonical)
    {
        var runtime = Gsd2Runtime.Current;
        if (LoadingRaw || !IsReady || runtime?.IsActive != true || !runtime.IsRole(canonical)
            || runtime.MapDisplayId(canonical) == canonical) return null;
        if (!Proxies.TryGetValue(canonical, out var proxy)) return null;
        var source = proxy.field_data?.opd_data;
        int donor = runtime.MapDisplayId(canonical);
        return Gsd2NpcDirectionAdapters.IsSupportedDonor(donor)
            ? Gsd2NpcDirectionAdapters.Find(donor, source) : source;
    }

    internal static void Tick()
    {
        var runtime = Gsd2Runtime.Current;
        if (runtime?.TablesReady != true) return;
        try
        {
            if (!ReferenceEquals(_session, runtime)) { Reset(); _session = runtime; _started = Time.realtimeSinceStartup; }
            // Both roster implementations publish a new immutable dictionary when assignments change.
            var map = runtime.Assignments;
            if (_ready && ReferenceEquals(map, _preparedMap)) { PrepareNpcAdapters(); RefreshResidents(); return; }
            var assignments = map.Where(pair => pair.Key != pair.Value).ToArray();
            if (!ReferenceEquals(map, _requestedMap))
            {
                if (assignments.Any(pair => pair.Key < 1 || pair.Key > 83 || pair.Value < 1 || pair.Value > 83))
                    throw new InvalidOperationException("Avatar mapping has no native combat graph.");
                _npcDonors = assignments.Select(pair => pair.Value).Where(Gsd2NpcDirectionAdapters.IsSupportedDonor).Distinct().ToArray();
                _requestedMap = map; _started = Time.realtimeSinceStartup;
            }
            _ready = false;
            if (_host is null) throw new InvalidOperationException("Avatar graphics coroutine host is unavailable.");
            var required = new HashSet<int>(assignments.Select(pair => pair.Key));
            required.UnionWith(assignments.Select(pair => pair.Value));
            foreach (int id in required.OrderBy(id => id))
            {
                if (Originals.ContainsKey(id) || Loading.ContainsKey(id)) continue;
                if (Loading.Count >= 4) break;
                StartLoad(id, runtime);
            }
            if (required.Any(id => !Originals.ContainsKey(id)))
            {
                if (Time.realtimeSinceStartup - _started > 120f)
                    throw new InvalidOperationException("Timed out loading original avatar graph resources.");
                return;
            }
            PrepareNpcAdapters();
            // Validate and allocate the complete next projection before publishing it.
            var next = new Dictionary<int, GSD2.P_GRAPH_DATA>();
            foreach (var assignment in assignments)
            {
                var canonical = Originals[assignment.Key];
                var donor = Originals[assignment.Value];
                var sources = (canonical.Pointer, donor.Pointer);
                if (!CachedProxies.TryGetValue(sources, out var proxy))
                {
                    proxy = CreateProxy(canonical, donor);
                    CachedProxies.Add(sources, proxy);
                }
                next.Add(assignment.Key, proxy);
            }
            Proxies.Clear();
            foreach (var entry in next) Proxies.Add(entry.Key, entry.Value);
            _preparedMap = map;
            RefreshResidents();
            _ready = true;
            runtime.Trace("GSD2 graphics ready: " + Proxies.Count + " detached canonical graph proxies; original loader caches preserved.");
        }
        catch (Exception error) { _ready = false; runtime.Fail("Suikoden II avatar graphics preparation failed: " + error.Message); }
    }

    private static void StartLoad(int id, Gsd2Runtime runtime)
    {
        int generation = _generation;
        long epoch = runtime.ExecutionEpoch;
        Loading.Add(id, Time.realtimeSinceStartup);
        var callback = DelegateSupport.ConvertDelegate<Il2CppSystem.Action<GSD2.P_GRAPH_DATA>>(
            new Action<GSD2.P_GRAPH_DATA>(asset =>
            {
                if (generation != _generation || !ReferenceEquals(runtime, Gsd2Runtime.Current)
                    || !runtime.MatchesExecution(epoch)) return;
                Loading.Remove(id);
                // A native lookup may already have supplied a newer source while this preload was pending.
                try { if (!Originals.ContainsKey(id)) Remember(id, asset); }
                catch (Exception error) { runtime.Fail("Original S2 character graph failed validation: " + error.Message); }
            })) ?? throw new InvalidOperationException("Could not bridge the original graphics-loader callback.");
        _rawDepth++;
        try
        {
            // The original IEnumerator must run under Unity, not be synchronously
            // drained: it yields the Addressables request and owns its load counter.
            var routine = GSD2.PlayerDataLoader.LoadPlayerData(id, false, callback)
                ?? throw new InvalidOperationException("Original character loader returned no coroutine.");
            _host!.StartCoroutine(routine);
        }
        finally { _rawDepth--; }
    }

    private static void Remember(int id, GSD2.P_GRAPH_DATA asset)
    {
        if (asset is null || asset.Pointer == IntPtr.Zero || asset.chara_id != id || ProxyOriginals.ContainsKey(asset.Pointer))
            throw new InvalidOperationException("Loader returned an incorrect or projected character identity " + id + ".");
        if (Originals.TryGetValue(id, out var previous) && previous.Pointer == asset.Pointer) return;
        if (asset.battle_data?.opd_data is null || asset.field_data?.opd_data is null
            || asset.battle_data.action_table is null || asset.battle_data.action_table.Count == 0)
            throw new InvalidOperationException("Original character graph is incomplete for " + id + ".");
        Originals[id] = asset;
        if (previous is not null) _ready = false;
        LeasedGraphs[asset.Pointer] = asset;
        foreach (var opd in Opds(asset)) if (opd is not null) LeasedOpdPointers.Add(opd.Pointer);
    }

    private static void PrepareNpcAdapters()
    {
        foreach (int donor in _npcDonors)
        {
            if (!Originals.TryGetValue(donor, out var graph)) continue;
            var source = graph.field_data?.opd_data;
            if (source is null || source.Pointer == IntPtr.Zero) continue;
            var adapter = Gsd2NpcDirectionAdapters.Find(donor, source);
            if (adapter is null)
            {
                // Missing dependencies remain cheap/retryable. Validation or
                // allocation failures get at most one attempt per second.
                if (source.atlas is null || source.atlas.Pointer == IntPtr.Zero
                    || source.sprites is null || source.normal_sprites is null
                    || !NpcAdapterRetries.Begin(donor, source.Pointer, Time.realtimeSinceStartup)) continue;
                adapter = Gsd2NpcDirectionAdapters.Prepare(donor, source);
            }
            if (adapter is not null)
            {
                NpcAdapterRetries.Complete(donor, source.Pointer);
                LeasedOpdPointers.Add(adapter.Pointer);
            }
        }
    }

    internal static GSD2.P_GRAPH_DATA? Project(int canonical, bool companion, GSD2.P_GRAPH_DATA? original)
    {
        if (original is null || LoadingRaw) return original;
        var runtime = Gsd2Runtime.Current;
        if (runtime?.TablesReady != true) return original;
        bool isRole = runtime.IsRole(canonical);
        if (!companion && !ProxyOriginals.ContainsKey(original.Pointer)
            && (isRole || Originals.ContainsKey(canonical) || Loading.ContainsKey(canonical)))
        {
            bool wasReady = _ready;
            Remember(canonical, original);
            if (wasReady && !_ready) Tick();
        }
        if (!isRole || !IsReady || runtime.MapDisplayId(canonical) == canonical) return original;
        if (!Proxies.TryGetValue(canonical, out var proxy))
            throw new InvalidOperationException("An admitted avatar has no prepared canonical graph proxy.");
        if (proxy.chara_id != canonical) throw new InvalidOperationException("Avatar graph lost its canonical character ID.");
        return proxy;
    }

    internal static void WrapCallback(int canonical, bool companion, ref Il2CppSystem.Action<GSD2.P_GRAPH_DATA> callback)
    {
        var runtime = Gsd2Runtime.Current;
        if (LoadingRaw || callback is null || runtime?.TablesReady != true) return;
        var nativeCallback = callback;
        int generation = _generation;
        long epoch = runtime.ExecutionEpoch;
        callback = DelegateSupport.ConvertDelegate<Il2CppSystem.Action<GSD2.P_GRAPH_DATA>>(
            new Action<GSD2.P_GRAPH_DATA>(asset =>
            {
                // Preserve the original loader's completion/cleanup, but never
                // project an old call through a different campaign or title epoch.
                if (generation != _generation || !ReferenceEquals(runtime, Gsd2Runtime.Current)
                    || !runtime.MatchesExecution(epoch)) { nativeCallback.Invoke(asset); return; }
                try
                {
                    var projected = Project(canonical, companion, asset)
                        ?? throw new InvalidOperationException("Native character callback received no graph asset.");
                    nativeCallback.Invoke(projected);
                }
                catch (Exception error) { runtime.Fail("Native S2 character callback projection failed: " + error.Message); throw; }
            })) ?? throw new InvalidOperationException("Could not bridge the native graphics callback.");
    }

    private static void RefreshResidents()
    {
        if (Gsd2Runtime.Current?.OwnsExecution != true) return;
        var residents = GSD2.OldSrcBase.game_work?.party_data?._cha_gra_adrs_graphdata;
        if (residents is null) return;
        for (int slot = 0; slot < residents.Count; slot++)
        {
            var graph = residents[slot];
            if (graph is null) continue;
            if (Proxies.TryGetValue(graph.chara_id, out var proxy)) residents[slot] = proxy;
            else if (ProxyOriginals.ContainsKey(graph.Pointer) && Originals.TryGetValue(graph.chara_id, out var original)) residents[slot] = original;
        }
    }

    internal static void Reset()
    {
        if (Gsd2Runtime.Current?.OwnsExecution != true) { Suspend(); return; }
        var residents = GSD2.OldSrcBase.game_work?.party_data?._cha_gra_adrs_graphdata;
        if (residents is not null)
            for (int slot = 0; slot < residents.Count; slot++)
            {
                var graph = residents[slot];
                if (graph is not null && ProxyOriginals.TryGetValue(graph.Pointer, out var original))
                    residents[slot] = original;
            }
        Suspend();
    }

    // Managed-only retirement. Outgoing native objects and all pointer leases
    // remain rooted; no foreign-scene resident arrays or Unity getters are read.
    internal static void Suspend()
    {
        _generation++;
        _ready = false;
        Proxies.Clear(); Originals.Clear(); Loading.Clear(); NpcAdapterRetries.Clear();
        _session = null; _preparedMap = null; _requestedMap = null; _npcDonors = Array.Empty<int>(); _started = 0;
        // Do not StopCoroutine: the native loader must finish and decrement its
        // own counter. Generation-bound callbacks cannot enter the new session.
    }

    internal static bool MustRetain(GSD2.P_GRAPH_DATA asset)
    {
        if (ProxyOriginals.ContainsKey(asset.Pointer) || LeasedGraphs.ContainsKey(asset.Pointer)) return true;
        // Normal and companion graph variants can reference the same OPD. The
        // destructive native method must also spare an otherwise unleased graph
        // when any of its resources is already used by a published proxy.
        if (!Opds(asset).Any(opd => opd is not null && LeasedOpdPointers.Contains(opd.Pointer))) return false;
        LeasedGraphs.Add(asset.Pointer, asset);
        foreach (var opd in Opds(asset)) if (opd is not null) LeasedOpdPointers.Add(opd.Pointer);
        return true;
    }

    internal static bool MustRetainResource(UnityEngine.Object? resource)
    {
        if (resource is null) return false;
        if (LeasedOpdPointers.Contains(resource.Pointer)) return true;
        var graph = resource.TryCast<GSD2.P_GRAPH_DATA>();
        return graph is not null && MustRetain(graph);
    }

    private static IEnumerable<Il2CppSystem.Object?> Opds(GSD2.P_GRAPH_DATA asset)
    {
        yield return asset.battle_data?.opd_data;
        yield return asset.field_data?.opd_data;
        var hero = asset.TryCast<GSD2.SHU_GRAPH_DATA>();
        if (hero is null) yield break;
        yield return hero.opd_data1; yield return hero.opd_data2;
        yield return hero.opd_data3; yield return hero.opd_data4; yield return hero.opd_data4_hd;
    }

    private static GSD2.P_GRAPH_DATA CreateProxy(GSD2.P_GRAPH_DATA canonical, GSD2.P_GRAPH_DATA donor)
    {
        var battle = CloneBattle(donor.battle_data);
        var field = new GSD2.PLAYER_FIELD { otyp = donor.field_data.otyp, dummy0 = donor.field_data.dummy0,
            dummy1 = donor.field_data.dummy1, dummy2 = donor.field_data.dummy2, opd_data = donor.field_data.opd_data };
        // Keep SHU_GRAPH_DATA's extra OPDs if either side needs that subtype.
        var basis = donor.TryCast<GSD2.SHU_GRAPH_DATA>() is not null ? donor : canonical;
        UnityEngine.Object? allocated = null;
        try
        {
            allocated = UnityEngine.Object.Instantiate(basis.Cast<UnityEngine.Object>());
            if (allocated is null) throw new InvalidOperationException("Unity did not clone the character graph.");
            var proxy = allocated.Cast<GSD2.P_GRAPH_DATA>();
            if (proxy.Pointer == canonical.Pointer || proxy.Pointer == donor.Pointer || proxy.Pointer == IntPtr.Zero)
                throw new InvalidOperationException("Character graph clone aliases an original.");
            proxy.chara_id = canonical.chara_id; proxy.battle_data = battle; proxy.field_data = field;
            if (proxy.chara_id != canonical.chara_id || proxy.battle_data.Pointer == donor.battle_data.Pointer
                || proxy.field_data.Pointer == donor.field_data.Pointer)
                throw new InvalidOperationException("Detached avatar graph failed identity/ownership validation.");
            ProxyOriginals.Add(proxy.Pointer, canonical);
            return proxy;
        }
        catch
        {
            if (allocated is not null && allocated.Pointer != canonical.Pointer && allocated.Pointer != donor.Pointer)
                UnityEngine.Object.Destroy(allocated);
            throw;
        }
    }

    private static GSD2.PLAYER_BATTLE CloneBattle(GSD2.PLAYER_BATTLE source)
    {
        var actions = source.action_table;
        var table = new Il2CppReferenceArray<GSD2.ACTION_TABLE_DATA>(actions.Count);
        for (int index = 0; index < actions.Count; index++)
        {
            var action = actions[index] ?? throw new InvalidOperationException("A donor action table is uninitialized.");
            var words = action.action ?? throw new InvalidOperationException("A donor action stream is unavailable.");
            var copy = new Il2CppStructArray<short>(words.Count);
            for (int word = 0; word < words.Count; word++) copy[word] = words[word];
            table[index] = new GSD2.ACTION_TABLE_DATA { action = copy, action_num = action.action_num, overlay_func_index = action.overlay_func_index };
        }
        return new GSD2.PLAYER_BATTLE { action_table = table, opd_data = source.opd_data,
            tokitama_data = source.tokitama_data, tokitama_prob = source.tokitama_prob,
            shadow_offset_x = source.shadow_offset_x, shadow_offset_y = source.shadow_offset_y,
            victory_action_no = source.victory_action_no, data1 = source.data1, data2 = source.data2, data3 = source.data3 };
    }
}

[HarmonyPatch(typeof(GSD2.PlayerDataLoader), "GetData", new Type[] { typeof(int), typeof(bool) })]
internal static class Gsd2GetGraphPatch
{
    [HarmonyPostfix] private static void Postfix(int __0, bool __1, ref GSD2.P_GRAPH_DATA? __result)
    {
        try { __result = Gsd2Graphics.Project(__0, __1, __result); }
        catch (Exception error) { Gsd2Runtime.Current?.Fail("S2 graph lookup failed: " + error.Message); throw; }
    }
}
[HarmonyPatch(typeof(GSD2.PlayerDataLoader), "LoadPlayerData")]
internal static class Gsd2LoadGraphCallbackPatch
{
    [HarmonyPrefix] private static void Prefix(int __0, bool __1, ref Il2CppSystem.Action<GSD2.P_GRAPH_DATA> __2) =>
        Gsd2Graphics.WrapCallback(__0, __1, ref __2);
}
[HarmonyPatch(typeof(GSD2.PlayerDataLoader), "get_IsLoadEnd")]
internal static class Gsd2GraphCompletionPatch
{
    [HarmonyPostfix] private static void Postfix(ref bool __result)
    {
        if (Gsd2Runtime.Current?.TablesReady == true && !Gsd2Graphics.IsReady) __result = false;
    }
}
[HarmonyPatch(typeof(GSD2.P_GRAPH_DATA), "DesroySObject")]
internal static class Gsd2GraphResourceLeasePatch
{
    // Native DesroySObject destroys its battle/field OPDs, which these detached
    // records deliberately reuse. Let only unleased native assets use that path.
    [HarmonyPrefix] private static bool Prefix(GSD2.P_GRAPH_DATA __instance) => !Gsd2Graphics.MustRetain(__instance);
}
[HarmonyPatch(typeof(GSD2.SHU_GRAPH_DATA), "DesroySObject")]
internal static class Gsd2HeroGraphResourceLeasePatch
{
    [HarmonyPrefix] private static bool Prefix(GSD2.SHU_GRAPH_DATA __instance) => !Gsd2Graphics.MustRetain(__instance);
}

[HarmonyPatch(typeof(global::ReleaseCheck), "Unload", new Type[] { typeof(UnityEngine.Object), typeof(bool) })]
internal static class Gsd2GraphReleaseLeasePatch
{
    // Remove/ClearRemovedChara release Addressables directly without calling
    // DesroySObject. A managed reference does not retain an Addressables handle.
    [HarmonyPrefix] private static bool Prefix(UnityEngine.Object __0) => !Gsd2Graphics.MustRetainResource(__0);
}

[HarmonyPatch(typeof(global::ResourceLoader), "UnloadAddressSingle", new Type[] { typeof(UnityEngine.Object), typeof(bool) })]
internal static class Gsd2GraphAddressableLeasePatch
{
    [HarmonyPrefix] private static bool Prefix(UnityEngine.Object __0) => !Gsd2Graphics.MustRetainResource(__0);
}
