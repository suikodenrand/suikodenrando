using Il2CppInterop.Runtime.InteropTypes.Arrays;

namespace Il2CppInterop.Runtime.InteropTypes.Arrays
{
    public class Il2CppReferenceArray<T>(int length)
    {
        private readonly T[] _values = new T[length];
        public int Count => _values.Length;
        public T this[int index] { get => _values[index]; set => _values[index] = value; }
    }
    public class Il2CppStructArray<T>(int length) : Il2CppReferenceArray<T>(length) where T : struct { }
}

namespace UnityEngine
{
    public class Object : NativeObject
    {
        public static void Destroy(Object value) { }
    }
    public class ScriptableObject : Object
    {
        public static T CreateInstance<T>() where T : ScriptableObject, new() => new();
    }
}

public class NativeObject
{
    private static int _nextPointer;
    public IntPtr Pointer { get; } = Interlocked.Increment(ref _nextPointer);
}
public class PLAYER_ITEM : NativeObject { public ushort item_id; public byte soubi, data; }
public class PLAYER_BASE : NativeObject
{
    public int chara_no;
    public byte item_kazu;
    public Il2CppReferenceArray<PLAYER_ITEM> item = new(9);
}
public class PLAYER_DATA : UnityEngine.ScriptableObject { public PLAYER_BASE player_base = new(); }
public class P_BATTLE_DATA { public PLAYER_DATA player_data = new(); }
public class PM_DATA : NativeObject
{
    public ushort hp = 100, status;
    public byte dead_flg, unbalance_cnt;
    public bool IsAway;
    public sbyte c_item, c_mokuhyo, c_koudou;
    public string name = "";
}
public class ITEM_BASE
{
    public uint name;
    public ushort type;
    public T? TryCast<T>() where T : class => this as T;
}
public class ITEM_DATA : ITEM_BASE { public byte times; public Func<int, int>? item_func; }
public static class G_item_c_data { public static Il2CppReferenceArray<ITEM_BASE> item_data_table = new(80); }
public class BATTLE_WORK : NativeObject
{
    public int player_kazu, pm_kazu;
    public Il2CppReferenceArray<PM_DATA> pm_data = new(20);
    public Il2CppReferenceArray<P_BATTLE_DATA> p_battle_data = new(7);
}
public static class BattleBase
{
    public static BATTLE_WORK? battle_work;
    public static WINDOW_WORK? window_work;
    public class WINDOW_WORK
    {
        public int selected_player, item_kazu, item_hyouji_kaishi, window_index;
        public Il2CppStructArray<int> item_window_data = new(9), item_name = new(9);
        public PLAYER_DATA? selected_player_data;
    }
}
public static class G_item_c
{
    // The native bag compacts references and recycles the removed record after the active items.
    public static PLAYER_ITEM del_item_base(PLAYER_BASE bag, int index)
    {
        var removed = bag.item[index];
        bag.item_kazu--;
        for (int i = index; i < bag.item_kazu; i++) bag.item[i] = bag.item[i + 1];
        bag.item[bag.item_kazu] = removed;
        return removed;
    }
}
