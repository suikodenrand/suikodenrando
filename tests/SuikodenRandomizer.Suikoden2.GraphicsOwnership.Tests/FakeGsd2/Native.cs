using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

public static class NativeAccess
{
    public static bool Block;
    public static readonly List<string> Calls = new();
    public static T Read<T>(string field, T value)
    { Calls.Add(field); if (Block) throw new InvalidOperationException("Foreign native read: " + field); return value; }
    public static void Write(string field)
    { Calls.Add(field); if (Block) throw new InvalidOperationException("Foreign native write: " + field); }
    public static void Clear() { Calls.Clear(); Block = false; }
}
namespace Il2CppSystem
{
    public class Object
    {
        private static long _next;
        public IntPtr Pointer { get; } = new(Interlocked.Increment(ref _next));
        public T Cast<T>() where T : class => this as T ?? throw new InvalidCastException();
        public T? TryCast<T>() where T : class => this as T;
    }
    public sealed class Action<T> : Object
    {
        private readonly System.Action<T> _callback;
        public Action(System.Action<T> callback) => _callback = callback;
        public void Invoke(T value) => _callback(value);
    }
}
namespace Il2CppSystem.Collections { public interface IEnumerator { } }
namespace Il2CppInterop.Runtime
{
    public static class DelegateSupport
    {
        public static T? ConvertDelegate<T>(Delegate callback) where T : class => (T?)Activator.CreateInstance(typeof(T), callback);
    }
}
namespace Il2CppInterop.Runtime.InteropTypes.Arrays
{
    public sealed class Il2CppReferenceArray<T> : Il2CppSystem.Object, IEnumerable<T> where T : class
    {
        private readonly T[] _values;
        public Il2CppReferenceArray(int size) => _values = new T[size];
        public int Count => _values.Length;
        public T this[int index] { get => _values[index]; set => _values[index] = value; }
        public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)_values).GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
    public sealed class Il2CppStructArray<T> : Il2CppSystem.Object where T : struct
    {
        private readonly T[] _values;
        public Il2CppStructArray(int size) => _values = new T[size];
        public int Count => _values.Length;
        public T this[int index] { get => _values[index]; set => _values[index] = value; }
    }
}
namespace UnityEngine
{
    public class Object : Il2CppSystem.Object
    {
        public static readonly HashSet<IntPtr> Destroyed = new();
        public static Object Instantiate(Object original)
        {
            NativeAccess.Write("Unity.Instantiate");
            if (original is not P_GRAPH_DATA graph) throw new InvalidOperationException("Only graph allocation is modeled.");
            P_GRAPH_DATA copy = graph is SHU_GRAPH_DATA hero ? new SHU_GRAPH_DATA {
                opd_data1 = hero.opd_data1, opd_data2 = hero.opd_data2, opd_data3 = hero.opd_data3,
                opd_data4 = hero.opd_data4, opd_data4_hd = hero.opd_data4_hd } : new P_GRAPH_DATA();
            copy.chara_id = graph.chara_id; copy.battle_data = graph.battle_data; copy.field_data = graph.field_data;
            return copy;
        }
        public static void Destroy(Object original) { NativeAccess.Write("Unity.Destroy"); Destroyed.Add(original.Pointer); }
    }
    public sealed class MonoBehaviour : Object
    {
        public bool ThrowStart;
        public int StartCalls;
        public readonly List<Il2CppSystem.Collections.IEnumerator> Routines = new();
        public void StartCoroutine(Il2CppSystem.Collections.IEnumerator routine)
        {
            NativeAccess.Write("StartCoroutine"); StartCalls++; if (ThrowStart) throw new InvalidOperationException("injected coroutine host failure");
            Routines.Add(routine);
            if (PlayerDataLoader.AutoComplete && routine is FakeLoadRoutine load)
                load.Request.Complete(PlayerDataLoader.Assets[load.Request.CharacterId]);
        }
    }
    public static class Time
    {
        public static float Value;
        public static float realtimeSinceStartup => NativeAccess.Read("Time.realtimeSinceStartup", Value);
    }
}
public sealed class OPD_DATA : UnityEngine.Object
{
    public UnityEngine.Object? atlas = new UnityEngine.Object();
    public object? sprites = new(), normal_sprites = new();
}
public class P_GRAPH_DATA : UnityEngine.Object
{
    private int _id;
    private PLAYER_BATTLE _battle = null!;
    private PLAYER_FIELD _field = null!;
    public int chara_id { get => NativeAccess.Read("graph.chara_id", _id); set { NativeAccess.Write("graph.chara_id="); _id = value; } }
    public PLAYER_BATTLE battle_data { get => NativeAccess.Read("graph.battle_data", _battle); set { NativeAccess.Write("graph.battle_data="); _battle = value; } }
    public PLAYER_FIELD field_data { get => NativeAccess.Read("graph.field_data", _field); set { NativeAccess.Write("graph.field_data="); _field = value; } }
    public void DesroySObject() { }
}
public sealed class SHU_GRAPH_DATA : P_GRAPH_DATA
{
    public OPD_DATA? opd_data1, opd_data2, opd_data3, opd_data4, opd_data4_hd;
    public new void DesroySObject() { }
}
public sealed class PLAYER_BATTLE : Il2CppSystem.Object
{
    public OPD_DATA opd_data = null!;
    public Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<ACTION_TABLE_DATA> action_table = null!;
    public object? tokitama_data, data1, data2, data3;
    public int tokitama_prob, shadow_offset_x, shadow_offset_y, victory_action_no;
}
public sealed class PLAYER_FIELD : Il2CppSystem.Object
{
    public byte otyp, dummy0, dummy1, dummy2;
    public OPD_DATA opd_data = null!;
}
public sealed class ACTION_TABLE_DATA : Il2CppSystem.Object
{
    public Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<short> action = null!;
    public int action_num, overlay_func_index;
}
public sealed class PARTY_DATA : Il2CppSystem.Object
{
    public Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<P_GRAPH_DATA> _cha_gra_adrs_graphdata = new(6);
}
public sealed class GAME_WORK : Il2CppSystem.Object
{
    private PARTY_DATA _party = new();
    public PARTY_DATA party_data { get => NativeAccess.Read("game.party_data", _party); set => _party = value; }
}
public static class OldSrcBase
{
    private static GAME_WORK? _game;
    public static GAME_WORK? game_work { get => NativeAccess.Read("OldSrcBase.game_work", _game); set => _game = value; }
}
public sealed class LoadRequest
{
    public readonly int CharacterId;
    public readonly Il2CppSystem.Action<P_GRAPH_DATA> Callback;
    public bool Completed;
    public LoadRequest(int id, Il2CppSystem.Action<P_GRAPH_DATA> callback) { CharacterId = id; Callback = callback; }
    public void Complete(P_GRAPH_DATA asset)
    {
        if (Completed) throw new InvalidOperationException("Load completed twice.");
        Completed = true;
        try { Callback.Invoke(asset); } finally { PlayerDataLoader.Completions++; PlayerDataLoader.ActiveLoads--; }
    }
}
public sealed class FakeLoadRoutine : Il2CppSystem.Collections.IEnumerator
{
    public readonly LoadRequest Request;
    public FakeLoadRoutine(LoadRequest request) => Request = request;
}
public static class PlayerDataLoader
{
    public static bool AutoComplete;
    public static int ActiveLoads, Completions;
    public static readonly Dictionary<int, P_GRAPH_DATA> Assets = new();
    public static readonly List<LoadRequest> Requests = new();
    public static bool IsLoadEnd => ActiveLoads == 0;
    public static P_GRAPH_DATA GetData(int id, bool companion) => Assets[id];
    public static Il2CppSystem.Collections.IEnumerator LoadPlayerData(int id, bool companion, Il2CppSystem.Action<P_GRAPH_DATA> callback)
    {
        NativeAccess.Write("LoadPlayerData"); var request = new LoadRequest(id, callback);
        Requests.Add(request); ActiveLoads++; return new FakeLoadRoutine(request);
    }
}
public static class ReleaseCheck { public static void Unload(UnityEngine.Object asset, bool isCache) { } }
public static class ResourceLoader { public static void UnloadAddressSingle(UnityEngine.Object asset, bool isCache) { } }
