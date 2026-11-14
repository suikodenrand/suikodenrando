using System;
using System.Collections.Generic;
using System.Threading;

public class NativeObject
{
    private static int _next;
    public static IntPtr AllocatePointer() => new(Interlocked.Increment(ref _next));
    public IntPtr Pointer = AllocatePointer();
    private string _name = "";
    public bool ThrowName;
    public string name { get => ThrowName ? throw new InvalidOperationException("injected name read") : _name; set => _name = value; }
}
public sealed class OPD_DATA : UnityEngine.ScriptableObject
{
    public string id = "", path = "";
    public int anime_kazu;
    public int pattern_kazu;
    public Il2CppSystem.Collections.Generic.List<AnimeTable>? anime_table;
    public Il2CppSystem.Collections.Generic.List<PatternTable>? pattern_table;
    public NativeObject? atlas, normal_atlas;
    public Il2CppSystem.Collections.Generic.Dictionary<string, UnityEngine.Sprite>? sprites = new(), normal_sprites = new();
    public sealed class AnimeTable : NativeObject
    {
        public int timer_kazu;
        public int abe_flg, timer_flg;
        public uint loop_flg;
        public Il2CppSystem.Collections.Generic.List<Timer>? timer_table;
        public class Timer : NativeObject { public int pattern_no; public byte timer; }
    }
    public sealed class TimerTable : AnimeTable.Timer { }
    public sealed class PatternTable : NativeObject
    {
        public int flag, center_x, center_y;
        public ushort size_x, size_y, padding0, padding1;
        public Il2CppSystem.Collections.Generic.List<int> atari_data = new();
        public Il2CppSystem.Collections.Generic.List<Obj> obj_table = new();
        public bool isOverriteBlendMode;
        public sealed class Obj : NativeObject
        {
            public uint flag;
            public int offset_x, offset_y, size_x, size_y, u, v, overwriteBlendMode, shadow_y;
            public string sprite_name = "", tmp_sprite_name = "";
        }
    }
}
public sealed class EVENT_HUMAN : NativeObject
{
    public byte otyp;
    public int ano;
    public ushort fpno;
    public OPD_DATA? opd;
    public ANIME? anime;
    public int x, y, motion, flags;
}
public sealed class EVENTCON : NativeObject
{
    public int okaz;
    public List<EVENT_HUMAN>? ehuman;
    public void EventHitoAnimeCreate() { }
}
public sealed class ANIME : NativeObject
{
    public OPD_DATA? data;
    public static ANIME AnimeCreate(OPD_DATA? opd_data, int anime_no, int type, bool IsCharacter, string name, bool isCreatePattern) =>
        new() { data = opd_data, name = name };
}

namespace Il2CppSystem.Collections.Generic
{
    public sealed class List<T> : System.Collections.Generic.List<T>
    {
        public IntPtr Pointer = NativeObject.AllocatePointer();
        public List() { }
        public List(System.Collections.Generic.IEnumerable<T> values) : base(values) { }
    }
    public sealed class Dictionary<TKey, TValue> : System.Collections.Generic.Dictionary<TKey, TValue> where TKey : notnull
    { public IntPtr Pointer = NativeObject.AllocatePointer(); }
}
namespace UnityEngine
{
    [Flags] public enum HideFlags { None = 0, DontUnloadUnusedAsset = 32 }
    public class Object : NativeObject
    {
        public HideFlags hideFlags;
        public static readonly HashSet<IntPtr> Destroyed = new();
        public static Action<Object>? BeforeDestroy;
        public static void Destroy(Object value)
        {
            BeforeDestroy?.Invoke(value);
            if (value is OPD_DATA opd) { opd.sprites?.Clear(); opd.normal_sprites?.Clear(); }
            Destroyed.Add(value.Pointer);
        }
    }
    public class ScriptableObject : Object
    {
        public static Action<ScriptableObject>? CreationHook;
        public static bool ThrowCreation;
        public static T CreateInstance<T>() where T : ScriptableObject, new()
        {
            if (ThrowCreation) throw new InvalidOperationException("injected Unity allocation failure");
            var value = new T(); CreationHook?.Invoke(value); return value;
        }
    }
    public sealed class Sprite : Object { }
}
