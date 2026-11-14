using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;

namespace Il2CppInterop.Runtime.InteropTypes.Arrays
{
    public class Il2CppStructArray<T> : NativeObject, IEnumerable<T> where T : struct
    {
        private readonly T[] _data;
        public Il2CppStructArray(int count) { _data = new T[count]; }
        public int Count => _data.Length;
        public T this[int index] { get => _data[index]; set => _data[index] = value; }
        public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)_data).GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
    public class Il2CppReferenceArray<T> : NativeObject, IEnumerable<T> where T : class
    {
        private readonly T[] _data;
        public Il2CppReferenceArray(int count) { _data = new T[count]; }
        public int Count => _data.Length;
        public T this[int index] { get => _data[index]; set => _data[index] = value; }
        public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)_data).GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
namespace Il2CppSystem.Collections.Generic { public class List<T> : System.Collections.Generic.List<T> { } }

public abstract class NativeObject
{
    private static long _next;
    public IntPtr Pointer { get; } = new IntPtr(Interlocked.Increment(ref _next));
}
public sealed class MACHICON : NativeObject { }
public sealed class PARTY_ITEM : NativeObject { public byte item_no { get; set; } public byte use_cnt { get; set; } }
public sealed class BOGU_DATA : NativeObject
{
    public int name { get; set; }
    public Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<byte> info { get; set; } = null!;
    public sbyte def_val { get; set; }
    public ushort botch { get; set; }
    public Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<sbyte> bonus { get; set; } = null!;
    public Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<byte> atrb { get; set; } = null!;
    public Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<byte> set_status { get; set; } = null!;
    public Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<byte> blk_status { get; set; } = null!;
}
public sealed class OVIT_DATA : NativeObject
{
    public Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<BOGU_DATA> bogu_data { get; set; } = null!;
}
public sealed class C_KOTEI_DAT : NativeObject
{
    public Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<byte> seicyo_type { get; set; } = null!;
    public Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<byte> monsyo_seigen { get; set; } = null!;
    public Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<byte> mon_zoku_aisyo { get; set; } = null!;
    public Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<byte> mon_kosu_type { get; set; } = null!;
    public byte soubi_henkou { get; set; }
    public byte soubi_kanou { get; set; }
    public byte okori_panic { get; set; }
}
public sealed class C_VARIA_DAT : NativeObject
{
    public byte level { get; set; } public ushort exp { get; set; } public ushort now_hp { get; set; } public ushort max_hp { get; set; }
    public Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<byte> mp { get; set; } = null!;
    public Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<byte> para { get; set; } = null!;
    public byte buki_lv { get; set; } public byte buki_mon { get; set; }
    public Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<byte> mon_eqp { get; set; } = null!;
    public Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<byte> bogu_eqp { get; set; } = null!;
    public Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<PARTY_ITEM> item_eqp { get; set; } = null!;
    public ushort todome { get; set; }
}
public sealed class IMPORT_GAME_DATA { public sealed class NestDataClass<T> { public Il2CppSystem.Collections.Generic.List<T> data { get; set; } = null!; } }
public sealed class CHARA_DATA : NativeObject
{
    public Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<C_KOTEI_DAT> c_kotei_dat { get; set; } = null!;
    public Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<C_VARIA_DAT> c_varia_dat { get; set; } = null!;
    public Il2CppSystem.Collections.Generic.List<Il2CppSystem.Collections.Generic.List<byte>> ckd_info { get; set; } = null!;
    public Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<sbyte> suki_value { get; set; } = null!;
    public Il2CppSystem.Collections.Generic.List<IMPORT_GAME_DATA.NestDataClass<byte>> _serialized_ckd_info { get; set; } = null!;
}
public sealed class ARMS_DATA : NativeObject
{
    public int name_1 { get; set; } public int name_2 { get; set; } public int name_3 { get; set; }
    public Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<byte> info { get; set; } = null!;
    public byte mon_no { get; set; }
}
public sealed class PARTY_DATA { public Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<byte> party_cha_no { get; set; } = null!; public Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<PARTY_ITEM> party_item { get; set; } = null!; }
public sealed class C_TEG_DAT : NativeObject { public Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<int> name { get; set; } = null!; }
public sealed class GAME_WORK : NativeObject
{
    public CHARA_DATA chara_data { get; set; } = null!;
    public OVIT_DATA ovit_data { get; set; } = null!;
    public PARTY_DATA party_data { get; set; } = null!;
    public C_TEG_DAT c_teg_dat { get; set; } = null!;
    public Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<ARMS_DATA> arms_data { get; set; } = null!;
    public byte[] chara_flag { get; set; } = Array.Empty<byte>();
    public byte[] story_flag { get; set; } = Array.Empty<byte>();
}
public static class OldSrcBase
{
    private static GAME_WORK _game = null!;
    public static bool RejectGameAccess;
    public static int GameReads;
    public static Action AfterGameRead;
    public static GAME_WORK game_work
    {
        get { Interlocked.Increment(ref GameReads); if (RejectGameAccess) throw new InvalidOperationException("Foreign native GAME_WORK getter invoked"); var value = _game; AfterGameRead?.Invoke(); return value; }
        set { if (RejectGameAccess) throw new InvalidOperationException("Foreign native GAME_WORK setter invoked"); _game = value; }
    }
}
