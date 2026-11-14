// Managed state models only. These do not run native helpers, Harmony detours or game code.
using System;
using System.Threading;

public static class TestIdentity
{
    private static long _next = 10000;
    public static IntPtr Next() => new IntPtr(Interlocked.Increment(ref _next));
}

public class NativeArray<T>
{
    private readonly T[] _values;
    public NativeArray(params T[] values) { _values = values; }
    public IntPtr Pointer { get; } = TestIdentity.Next();
    public int Count => _values.Length;
    public int Writes { get; private set; }
    public int FailOnWrite { get; set; } = -1;
    public T this[int index]
    {
        get => _values[index];
        set
        {
            Writes++;
            if (Writes == FailOnWrite) throw new InvalidOperationException("Injected native setter failure");
            _values[index] = value;
        }
    }
}

namespace Il2CppInterop.Runtime.InteropTypes.Arrays
{
    public sealed class Il2CppStructArray<T> : NativeArray<T> where T : struct
    {
        public Il2CppStructArray(params T[] values) : base(values) { }
    }
}

public sealed class PLAYER_DATA { public int chara_id { get; set; } }
public sealed class P_BATTLE_DATA { public PLAYER_DATA player_data { get; set; } = null!; }
public sealed class B_CHARA
{
    public int chara_id { get; set; }
    public NativeArray<Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<short>> action_data_table { get; set; } = null!;
}
public sealed class PM_DATA
{
    public IntPtr Pointer { get; set; } = TestIdentity.Next();
    public int hp { get; set; } = 100;
    public short damage { get; set; }
    public byte dead_flg { get; set; }
    public ushort status { get; set; }
    public ushort max_hp { get; set; } = 999;
    public byte unbalance_cnt { get; set; }
    public byte finished_flg { get; set; }
    public sbyte c_koudou { get; set; }
    public sbyte c_item { get; set; }
    public sbyte c_mokuhyo { get; set; }
}
public sealed class BATTLE_WORK
{
    public IntPtr Pointer { get; set; } = TestIdentity.Next();
    public object? kyoryoku_func { get; set; }
    public int player_kazu { get; set; }
    public int pm_kazu { get; set; }
    public NativeArray<P_BATTLE_DATA> p_battle_data { get; set; } = null!;
    public NativeArray<PM_DATA> pm_data { get; set; } = null!;
    public NativeArray<B_CHARA> b_chara { get; set; } = null!;
}
public sealed class KYORYOKU_DATA
{
    public byte player_kazu { get; set; }
    public NativeArray<byte> player_id { get; set; } = null!;
    public byte func_no { get; set; }
    public ushort type { get; set; }
    public int name { get; set; }
    public int help { get; set; }
    public NativeArray<int>? opd_nums { get; set; }
}
public sealed class KMAGIC01_WORK
{
    public Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<int> p_id { get; set; } = null!;
    public Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<int> p_flg { get; set; } = null!;
}
public static class BattleBase
{
    public static BATTLE_WORK? battle_work { get; set; }
    public static NativeArray<KYORYOKU_DATA> kyoryoku_data_table { get; set; } = null!;
    public static KMAGIC01_WORK? kmagic01_work { get; set; }
    public static KMAGIC02_WORK? kmagic02_work { get; set; }
    public static KMAGIC03_WORK? kmagic03_work { get; set; }
    public static KMAGIC04_WORK? kmagic04_work { get; set; }
    public static KMAGIC05_WORK? kmagic05_work { get; set; }
    public static KMAGIC06_WORK? kmagic06_work { get; set; }
    public static KMAGIC07_WORK? kmagic07_work { get; set; }
    public static KMAGIC08_WORK? kmagic08_work { get; set; }
    public static KMAGIC09_WORK? kmagic09_work { get; set; }
    public static KMAGIC10_WORK? kmagic10_work { get; set; }
    public static KMAGIC13_WORK? kmagic13_work { get; set; }
    public static KMAGIC15_WORK? kmagic15_work { get; set; }
    public static KMAGIC16_WORK? kmagic16_work { get; set; }
    public static KMAGIC17_WORK? kmagic17_work { get; set; }
    public static KMAGIC18_WORK? kmagic18_work { get; set; }
    public static KMAGIC19_WORK? kmagic19_work { get; set; }
    public static KMAGIC20_WORK? kmagic20_work { get; set; }
    public static KMAGIC21_WORK? kmagic21_work { get; set; }
    public static KMAGIC22_WORK? kmagic22_work { get; set; }
    public static KMAGIC23_WORK? kmagic23_work { get; set; }
    public static KMAGIC24_WORK? kmagic24_work { get; set; }
    public static KMAGIC25_WORK? kmagic25_work { get; set; }
    public static Func<int, bool> DeadCheck { get; set; } = _ => false;
    public static bool dead_check(int slot) => DeadCheck(slot);
    public static int check_kyoryoku(int player_no) => throw new NotSupportedException("Native matching is modeled independently by the fixture.");
    public static int make_command_data(int player_no) => throw new NotSupportedException();
    public static int make_kyoryoku_window(int player_no) => throw new NotSupportedException();
    public static int command_kyoryoku(int step_level) => throw new NotSupportedException();
    public static int command_kyoryoku01(int step_level) => throw new NotSupportedException();
    public static int command_kyoryoku02(int step_level) => throw new NotSupportedException();
    public static int command_kyoryoku10(int step_level) => throw new NotSupportedException();
    public static int command_kyoryoku99(int step_level) => throw new NotSupportedException();
    public static int player_kyoryoku_sub(int player_no) => throw new NotSupportedException();
    public static void FinishFunc(int player_no, Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<int> participants) => throw new NotSupportedException();
    public static void DamageFunc(int player_no, int count, int multiplier, Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<int> participants) => throw new NotSupportedException();
    public static int kmagic01_func03(int player_no) => throw new NotSupportedException();
    public static int kmagic01_func02(int player_no) => throw new NotSupportedException();
    public static int battle_turn_kyoryoku(int player_no) => throw new NotSupportedException();
    public static void initialize_battle() => throw new NotSupportedException();
    public static void battle_end_free() => throw new NotSupportedException();
}

public sealed class KMAGIC02_WORK { public Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<int> p_id { get; set; } = null!; public Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<int> p_flg { get; set; } = null!; }

public sealed class KMAGIC03_WORK { public Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<int> p_id { get; set; } = null!; public Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<int> p_flg { get; set; } = null!; }

public sealed class KMAGIC04_WORK { public Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<int> p_id { get; set; } = null!; public Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<int> p_flg { get; set; } = null!; }

public sealed class KMAGIC05_WORK { public Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<int> p_id { get; set; } = null!; public Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<int> p_flg { get; set; } = null!; }

public sealed class KMAGIC06_WORK { public Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<int> p_id { get; set; } = null!; public Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<int> p_flg { get; set; } = null!; }

public sealed class KMAGIC07_WORK { public Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<int> p_id { get; set; } = null!; public Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<int> p_flg { get; set; } = null!; }

public sealed class KMAGIC08_WORK { public Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<int> p_id { get; set; } = null!; public Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<int> p_flg { get; set; } = null!; }

public sealed class KMAGIC09_WORK { public Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<int> p_id { get; set; } = null!; public Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<int> p_flg { get; set; } = null!; }

public sealed class KMAGIC10_WORK { public Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<int> p_id { get; set; } = null!; public Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<int> p_flg { get; set; } = null!; }

public sealed class KMAGIC13_WORK { public Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<int> p_id { get; set; } = null!; public Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<int> p_flg { get; set; } = null!; }

public sealed class KMAGIC15_WORK { public Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<int> p_id { get; set; } = null!; public Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<int> p_flg { get; set; } = null!; }

public sealed class KMAGIC16_WORK { public Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<int> p_id { get; set; } = null!; public Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<int> p_flg { get; set; } = null!; }

public sealed class KMAGIC17_WORK { public Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<int> p_id { get; set; } = null!; public Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<int> p_flg { get; set; } = null!; }

public sealed class KMAGIC18_WORK { public Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<int> p_id { get; set; } = null!; public Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<int> p_flg { get; set; } = null!; }

public sealed class KMAGIC19_WORK { public Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<int> p_id { get; set; } = null!; public Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<int> p_flg { get; set; } = null!; }

public sealed class KMAGIC20_WORK { public Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<int> p_id { get; set; } = null!; public Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<int> p_flg { get; set; } = null!; }

public sealed class KMAGIC21_WORK { public Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<int> p_id { get; set; } = null!; public Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<int> p_flg { get; set; } = null!; }

public sealed class KMAGIC22_WORK { public Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<int> p_id { get; set; } = null!; public Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<int> p_flg { get; set; } = null!; }

public sealed class KMAGIC23_WORK { public Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<int> p_id { get; set; } = null!; public Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<int> p_flg { get; set; } = null!; }

public sealed class KMAGIC24_WORK { public Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<int> p_id { get; set; } = null!; public Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<int> p_flg { get; set; } = null!; }

public sealed class KMAGIC25_WORK { public Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<int> p_id { get; set; } = null!; public Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<int> p_flg { get; set; } = null!; }
