using System;

public sealed class EVENTCON
{
    public class ___WindowCreate_d__413 { public virtual bool MoveNext() => throw new NotSupportedException(); }
}
public sealed class WindowManager
{
    public string ConvertTagToStringFromLogData(string jsonData) => throw new NotSupportedException();
}
public static class G2_SYS
{
    public static int NameCalls, LastCanonical;
    public static Func<int, string> NameReader;
    public static Action AfterName;
    public static string G2_name_get(int canonical)
    {
        NameCalls++; LastCanonical = canonical;
        string name = NameReader is null ? "Native name " + OldSrcBase.game_work.c_teg_dat.name[canonical] : NameReader(canonical);
        AfterName?.Invoke();
        return name;
    }
    public static int G2_name_get_org(int canonical) => throw new NotSupportedException();
}
