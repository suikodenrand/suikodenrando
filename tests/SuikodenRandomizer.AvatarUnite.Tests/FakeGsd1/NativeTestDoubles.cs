// Explicit managed models only. No native matching or detour execution is certified.
using System;
using System.Threading;

public sealed class NativeArray<T>
{
    private static long _nextPointer = 100;
    private readonly T[] _values;
    public NativeArray(params T[] values) { _values = values; Pointer = new IntPtr(Interlocked.Increment(ref _nextPointer)); }
    public IntPtr Pointer { get; }
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
public sealed class PLAYER_DATA { public int chara_id { get; set; } }
public sealed class P_BATTLE_DATA { public PLAYER_DATA? player_data { get; set; } }
public sealed class BATTLE_WORK { public NativeArray<P_BATTLE_DATA?>? p_battle_data { get; set; } }
public sealed class KYORYOKU_DATA
{
    public byte player_kazu { get; set; }
    public NativeArray<byte>? player_id { get; set; }
    public byte func_no { get; set; }
    public ushort type { get; set; }
    public int name { get; set; }
    public int help { get; set; }
}
public static class BattleBase
{
    public static BATTLE_WORK? battle_work { get; set; }
    public static NativeArray<KYORYOKU_DATA?>? kyoryoku_data_table { get; set; }
    public static int check_kyoryoku(int player_no) => throw new NotSupportedException("Test double: native matching is simulated separately.");
    public static int make_command_data(int player_no) => throw new NotSupportedException();
    public static int make_kyoryoku_window(int player_no) => throw new NotSupportedException();
    public static int command_kyoryoku(int step_level) => throw new NotSupportedException();
    public static int command_kyoryoku01(int step_level) => throw new NotSupportedException();
    public static int command_kyoryoku02(int step_level) => throw new NotSupportedException();
    public static int command_kyoryoku10(int step_level) => throw new NotSupportedException();
}
