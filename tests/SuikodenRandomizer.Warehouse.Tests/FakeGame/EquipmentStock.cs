using System;
using System.Linq;

public sealed class SHIRO_DATA
{
    private static int _next;
    public IntPtr Pointer { get; } = new(++_next);
    public byte[][] azukari_kantei = Enumerable.Range(0, 256).Select(_ => new byte[2]).ToArray();
}
public class ITEM_BASE
{
    public ushort type;
    public T? TryCast<T>() where T : class => this as T;
}
public sealed class BOUGU_DATA : ITEM_BASE { }
public static class G_item_c_data
{
    public static ITEM_BASE?[] item_data_table = new ITEM_BASE?[256];
}
