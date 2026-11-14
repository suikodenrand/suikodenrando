using System;
using System.Collections;
using System.Collections.Generic;

public static class NativeWrites
{
    public static Action<string, object> Before;
    public static Action<string, object> After;
    public static void Assign<T>(ref T field, T value, string name)
    {
        Before?.Invoke(name, value);
        field = value;
        After?.Invoke(name, value);
    }
    public static void Reset() { Before = null; After = null; }
}

namespace Il2CppInterop.Runtime.InteropTypes.Arrays
{
    public sealed class Il2CppStructArray<T> : IEnumerable<T> where T : struct
    {
        private readonly T[] _items;
        public string Tag { get; set; } = "array";
        public Il2CppStructArray(int count) { _items = new T[count]; }
        public int Count => _items.Length;
        public T this[int index]
        {
            get => _items[index];
            set => NativeWrites.Assign(ref _items[index], value, Tag + "[" + index + "]");
        }
        public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)_items).GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}

public sealed class GAME_DATA
{
    private int _area, _town, _map, _area2, _town2;
    private byte _memory;
    public int area_no { get => _area; set => NativeWrites.Assign(ref _area, value, "game_data.area_no"); }
    public int town_no { get => _town; set => NativeWrites.Assign(ref _town, value, "game_data.town_no"); }
    public int map_no { get => _map; set => NativeWrites.Assign(ref _map, value, "game_data.map_no"); }
    public int area_no2 { get => _area2; set => NativeWrites.Assign(ref _area2, value, "game_data.area_no2"); }
    public int town_no2 { get => _town2; set => NativeWrites.Assign(ref _town2, value, "game_data.town_no2"); }
    public byte mem_type { get => _memory; set => NativeWrites.Assign(ref _memory, value, "game_data.mem_type"); }
    public Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<int> deru_pos { get; set; }
    public string bozu_name { get; set; }
    public string macd_name { get; set; }
    public string m_base_name { get; set; }
    public int difficulty { get; set; }
    public int language { get; set; }
    public int nakam_1_num { get; set; }
    public object untouched_settings { get; set; }
    public int[] play_time { get; set; }
    public int hanyou_po { get; set; }
    public int[][] event_time { get; set; }
}

public sealed class PARTY_DATA
{
    private byte _info;
    public byte party_info { get => _info; set => NativeWrites.Assign(ref _info, value, "party_data.party_info"); }
    public Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<byte> party_cha_no { get; set; }
    public uint gold { get; set; }
    public object party_item { get; set; }
    public object event_item { get; set; }
    public object graphics { get; set; }
}

public sealed class GAME_WORK
{
    public GAME_DATA game_data { get; set; }
    public PARTY_DATA party_data { get; set; }
    public Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<byte> chara_flag { get; set; }
    public Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<byte> town_flag { get; set; }
    public Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<byte> event_flag { get; set; }
    public Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<byte> scpoi_flag { get; set; }
    public Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<byte> t_box_flag { get; set; }
    public object chara_data { get; set; }
    public object c_teg_dat { get; set; }
    public object arms_data { get; set; }
    public object shop_data { get; set; }
    public object war_data { get; set; }
    public object back_log_list { get; set; }
}
