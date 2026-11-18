// Compile-only API shapes. Every fake native method throws if accidentally run.
public static class G2_SYS { public static int G2_pamem_in(int role, int party, int slot) => throw new NotSupportedException(); }
public sealed class GRChapterManager { public void Update() => throw new NotSupportedException(); }
public sealed class TitleChapter
{
    public int step = 0;
    public IntPtr Pointer = IntPtr.Zero;
    public sealed class _MapSetup_d__43 { public bool isNewgame = false; public bool MoveNext() => throw new NotSupportedException(); }
}
public sealed class BootProgram
{
    public void BoNewGameSet() => throw new NotSupportedException();
    public sealed class _gamework_dataset_d__95 { public bool MoveNext() => throw new NotSupportedException(); }
}
public sealed class UISaveLoad2 { public int ConfirmDecide() => throw new NotSupportedException(); }
public sealed class GSD2SaveData
{
    public sealed class TmpSave : Il2CppSystem.Object
    {
        public static System.Action<string, int>? BeforePositionWrite, AfterPositionWrite;
        private int _px, _py;
        public int version = 100;
        public int px { get => _px; set { BeforePositionWrite?.Invoke("px",value); _px=value; AfterPositionWrite?.Invoke("px",value); } }
        public int py { get => _py; set { BeforePositionWrite?.Invoke("py",value); _py=value; AfterPositionWrite?.Invoke("py",value); } }
        public GAME_DATA game_data = new();
        public object UntouchedPayload = new();
    }
    public static bool SaveFile(string path, Il2CppSystem.Action<bool, TmpSave> callback, bool isClearSave) => throw new NotSupportedException();
    public static void LoadDataCore(string json, bool isTmpPosReload) => throw new NotSupportedException();
}
public static class SystemSave
{
    public enum DataSize { Any }
    public static bool Save(string path, string json, DataSize size, Il2CppSystem.Action<bool> cb, bool isConsume) => throw new NotSupportedException();
}
public sealed class GAME_DATA { public int area_no, town_no, map_no, mem_type, area_no2, town_no2; }
public sealed class GAME_WORK { public SYS_WORK sys_work = new(); }
public sealed class SYS_WORK { public int game_mode = 1; public List<int> game_step = Enumerable.Repeat(0,13).ToList(); public MACHICON? mcon; }
public sealed class EVENTCON { public int px, py; }
public sealed class MACHICON : Il2CppSystem.Object
{
    public int ano, vno, mno, memtyp, px, py;
    public EVENTCON? econ;
    public static MACHICON MachiInit(sbyte type) => throw new NotSupportedException();
    public int MachiMain() => throw new NotSupportedException();
}
namespace LitJson { public static class JsonMapper { public static string ToJson(Il2CppSystem.Object obj) => throw new NotSupportedException(); } }
namespace Il2CppSystem
{
    public class Object
    {
        private static long _next;
        public IntPtr Pointer { get; set; } = new(Interlocked.Increment(ref _next));
        public T? TryCast<T>() where T : class => this as T;
    }
    public delegate void Action<T>(T value); public delegate void Action<T1,T2>(T1 first,T2 second);
}
