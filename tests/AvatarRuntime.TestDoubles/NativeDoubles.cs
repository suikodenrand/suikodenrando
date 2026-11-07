using System;
using System.Collections.Generic;
using System.Collections;
using System.Threading;



public abstract class NativeObject
{
    private static long _next;
    public IntPtr Pointer { get; } = new IntPtr(Interlocked.Increment(ref _next));
}
public sealed class OPD_DATA : NativeObject { }
public sealed class PLAYER_FIELD { public OPD_DATA? opd_data { get; set; } = new OPD_DATA(); }
public sealed class PLAYER_BATTLE_SO { public OPD_DATA? opd_data { get; set; } = new OPD_DATA(); }
public sealed class PLAYER_MONSYO
{
    public byte monsyo_id { get; set; }
    public byte monsyo_level { get; set; }
    public ushort monsyo_exp { get; set; }
}
public sealed class PLAYER_BASE : NativeObject
{
    public int chara_no { get; set; }
    public byte level { get; set; } = 1;
    public ushort hp { get; set; }
    public ushort max_hp { get; set; }
    public ushort exp { get; set; }
    public ushort status { get; set; }
    public List<byte> noryoku { get; set; } = new List<byte>(new byte[6]);
    public PLAYER_MONSYO? monsyo_data { get; set; } = new PLAYER_MONSYO();
    public byte[] seicho_type { get; set; } = new byte[7];
    public bool IsComplete { get; set; } = true;
    public int Equipment { get; set; }
    public int Rune { get; set; }
    public int[] Items { get; set; } = Array.Empty<int>();
}
// Count modelled native array accesses so hot-path regressions are observable
// without pretending these managed timings measure the actual game.
public sealed class CountingBaseRecords : IList<PLAYER_BASE?>
{
    private readonly List<PLAYER_BASE?> _items = new List<PLAYER_BASE?>();
    public long Reads { get; private set; }
    public void ResetReads() => Reads = 0;
    public PLAYER_BASE? this[int index] { get { Reads++; return _items[index]; } set => _items[index] = value; }
    public int Count => _items.Count;
    public bool IsReadOnly => false;
    public void Add(PLAYER_BASE? item) => _items.Add(item);
    public void Clear() => _items.Clear();
    public bool Contains(PLAYER_BASE? item) => _items.Contains(item);
    public void CopyTo(PLAYER_BASE?[] array, int index) => _items.CopyTo(array,index);
    public int IndexOf(PLAYER_BASE? item) => _items.IndexOf(item);
    public void Insert(int index, PLAYER_BASE? item) => _items.Insert(index,item);
    public bool Remove(PLAYER_BASE? item) => _items.Remove(item);
    public void RemoveAt(int index) => _items.RemoveAt(index);
    public IEnumerator<PLAYER_BASE?> GetEnumerator() { for (int n=0;n<_items.Count;n++) yield return this[n]; }
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
public sealed class CountingPlayerCache : Dictionary<int, PLAYER_DATA?>
{
    public long Lookups { get; private set; }
    public void ResetLookups() => Lookups = 0;
    public new bool TryGetValue(int id, out PLAYER_DATA? value) { Lookups++; return base.TryGetValue(id,out value); }
}
public sealed class PLAYER_DATA : NativeObject
{
    public byte chara_id { get; set; }
    public uint chara_name { get; set; }
    public PLAYER_BASE? player_base { get; set; }
    public PLAYER_FIELD? field_data { get; set; } = new PLAYER_FIELD();
    public PLAYER_BATTLE_SO? battle_data { get; set; } = new PLAYER_BATTLE_SO();
    public byte[] seicho_type { get; set; } = new byte[7];
}
public sealed class PARTY_DATA
{
    private int _x;
    public List<short> chara_code { get; set; } = new List<short> { 1,2,6,8,108,-1 };
    public List<sbyte> narabi { get; set; } = new List<sbyte> { 0,1,2,3,4,-1 };
    public byte player_kazu { get; set; } = 5;
    public byte vil_no { get; set; }
    public byte map_no { get; set; }
    public byte area_no { get; set; }
    public int x
    {
        get => _x;
        set
        {
            if (FailXSetterOnce)
            {
                FailXSetterOnce = false;
                throw new InvalidOperationException("Injected position setter failure.");
            }
            _x = value;
        }
    }
    public int y { get; set; }
    public int mochi_kin { get; set; }
    public bool FailXSetterOnce { get; set; }
}
public sealed class GAME_WORK : NativeObject
{
    public PARTY_DATA party_data { get; set; } = new PARTY_DATA();
    public CountingBaseRecords player_base { get; set; } = new CountingBaseRecords();
    public List<PLAYER_DATA?> player_data { get; set; } = new List<PLAYER_DATA?>();
    public List<byte> member_flag { get; set; } = new List<byte>(new byte[128]);
    public List<byte> tmpEventFlagS { get; set; } = new List<byte>(new byte[240]);
    public List<byte> storyFlagS { get; set; } = new List<byte>(new byte[20]);
    public string playerName { get; set; } = string.Empty;
    public byte difficulty { get; set; }
}
public sealed class PlayerManager : NativeObject { public CountingPlayerCache player_cache { get; set; } = new CountingPlayerCache(); }
public sealed class GlobalWork
{
    public static GlobalWork? Instance { get; set; }
    public PlayerManager? player_manager { get; set; }
}
public static class OldSrcBase { public static GAME_WORK game_work { get; set; } = null!; }
public class Chapter { public T? TryCast<T>() where T : class => this as T; }
public sealed class TitleChapter : Chapter { }
public sealed class GameInit { }
public sealed class Village_c { }
public sealed class ChapterManager
{
    public static ChapterManager? GR1Instance { get; set; }
    public Chapter? activeChapter { get; set; }
    public static int GetGameStepSeq(int stepLevel) => stepLevel;
}
public sealed class SaveDataManager
{
    public sealed class SaveData { public string Identity { get; set; } = new string('a',64); }
}
public sealed class P_BATTLE_DATA { public PLAYER_DATA player_data { get; set; } = null!; }
public sealed class PM_DATA
{
    public ushort hp { get; set; }
    public bool IsDead { get; set; }
    public byte dead_flg { get; set; }
    public byte finished_flg { get; set; }
}
public sealed class BATTLE_WORK
{
    public byte player_kazu { get; set; }
    public List<P_BATTLE_DATA> p_battle_data { get; set; } = new();
    public List<PM_DATA?> pm_data { get; set; } = new();
}
public static class BattleBase { public static BATTLE_WORK? battle_work { get; set; } }
