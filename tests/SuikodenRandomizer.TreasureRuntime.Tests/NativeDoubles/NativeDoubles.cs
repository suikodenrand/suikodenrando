using System;
using System.Threading;
using Il2CppInterop.Runtime.InteropTypes.Arrays;

namespace Il2CppInterop.Runtime.InteropTypes.Arrays
{
    public sealed class Il2CppStructArray<T>
    {
        private static long _nextPointer;
        private readonly T[] _values;

        public Il2CppStructArray(int count)
        {
            _values = new T[count];
            Pointer = new IntPtr(Interlocked.Increment(ref _nextPointer));
        }

        public IntPtr Pointer { get; }
        public int Count => _values.Length;
        public T this[int index] { get => _values[index]; set => _values[index] = value; }
    }

    public sealed class Il2CppReferenceArray<T>
    {
        private readonly T?[] _values;
        public Il2CppReferenceArray(int count) => _values = new T?[count];
        public int Count => _values.Length;
        public T? this[int index] { get => _values[index]; set => _values[index] = value; }
    }
}

public sealed class EVENT_WK
{
    public ushort flag1;
    public Il2CppStructArray<byte>? eventPut;
}

public sealed class Event_c
{
    public EVENT_WK? eventWK;
    public Il2CppStructArray<byte>? eventComTMP;
    public int eventComTMPindex;
    public sbyte eventKettei;
    public void eventItem() { }
}

public sealed class ITEM_BASE
{
    public uint name;
}

public static class G_item_c_data
{
    public static Il2CppReferenceArray<ITEM_BASE>? item_data_table;
}

public static class G1_string_id_h
{
    public static string G1_SID_MSG(int name) => "native-name-" + name;
}
