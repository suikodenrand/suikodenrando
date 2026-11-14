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
    public byte status { get; set; }
    public byte finished_flg { get; set; }
    public sbyte c_koudou { get; set; }
    public sbyte c_item { get; set; }
    public sbyte c_mokuhyo { get; set; }
}
public sealed class BATTLE_WORK
{
    public IntPtr Pointer { get; set; } = TestIdentity.Next();
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
}
public sealed class KMAGIC01_WORK
{
    public Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<int> p_id { get; set; } = null!;
}
public static class BattleBase
{
    public static BATTLE_WORK? battle_work { get; set; }
    public static NativeArray<KYORYOKU_DATA> kyoryoku_data_table { get; set; } = null!;
    public static KMAGIC01_WORK? kmagic01_work { get; set; }
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
