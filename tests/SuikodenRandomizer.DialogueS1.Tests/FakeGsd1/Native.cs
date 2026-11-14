public abstract class NativeObject
{
    private static long _next;
    public IntPtr Pointer { get; } = new IntPtr(System.Threading.Interlocked.Increment(ref _next));
}
public sealed class GAME_WORK : NativeObject { }
public sealed class PLAYER_DATA : NativeObject { }
public sealed class PlayerManager : NativeObject { }
public static class OldSrcBase
{
    private static GAME_WORK? _game;
    public static int Reads;
    public static bool ThrowOnRead;
    public static GAME_WORK? game_work
    {
        get { Reads++; if (ThrowOnRead) throw new InvalidOperationException("Foreign native read"); return _game; }
        set { _game = value; }
    }
}
public sealed class GlobalWork
{
    private static GlobalWork? _instance;
    public static int Reads;
    public static GlobalWork? Instance { get { Reads++; return _instance; } set { _instance = value; } }
    public PlayerManager? player_manager;
}
public sealed class Event_c { public void SetEventMsg() { } public void SetEventMsg2() { } }
public sealed class WindowManager { public string ConvertTagToStringFromLogData(string jsonData) => jsonData; }
public static class Fhuman_c
{
    public static readonly List<(int Id, bool Short)> Calls = new();
    public static Func<int, bool, string> Resolve = (id, _) => "Name" + id;
    public static string GetCharacterName(int hno, bool need_short_name)
    { Calls.Add((hno, need_short_name)); return Resolve(hno, need_short_name); }
    public static int GetCharacterNameTextID(int hno, bool need_short_name) => hno;
}
public static class G1_event_h
{
    public static int Value = 961, Reads;
    public static bool ThrowOnRead;
    public static int GetETalkWinSZ { get { Reads++; if (ThrowOnRead) throw new InvalidOperationException("Getter unavailable"); return Value; } }
}
public static class SaveDataManager
{
    public sealed class SaveData { }
    public static bool CopySaveDataToGameWork(SaveData save_data, GAME_WORK game_work) => true;
    public static bool SaveGameWork(string filename, Il2CppSystem.Action<bool, SaveData> callback, bool someFlag) => true;
}
namespace Il2CppSystem { public delegate void Action<T, U>(T first, U second); }
