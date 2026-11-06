// Test doubles only. This assembly does not implement or verify native game APIs.
using System;

public sealed class Fhuman_c { }
public sealed class Ws_face_c
{
    public static Func<ushort, short> Converter = id => checked((short)(id + 1000));
    public static int StaticCalls;
    public static int InstanceCalls;
    public static short CharacterIDConvertToFaceIDStatic(ushort id) { StaticCalls++; return Converter(id); }
    public short CharacterIDConvertToFaceID(ushort id) { InstanceCalls++; return Converter(id); }
}
public sealed class OPD_DATA
{
    public IntPtr Pointer { get; set; }
}
public sealed class PLAYER_FIELD
{
    public OPD_DATA opd_data { get; set; }
}
public sealed class PLAYER_DATA
{
    public PLAYER_FIELD field_data { get; set; }
}
public sealed class OPDSprite
{
    public OPD_DATA data { get; set; }
}
public sealed class AnimeSPRITE
{
    public OPD_DATA opd_data { get; set; }
    public OPDSprite opd_sprite { get; set; }
    public int anime_no { get; set; }
    public int pattern_kazu { get; set; }
    public int pattern_no { get; set; }
    public object pattern_table { get; set; }
}
public sealed class Village_c
{
    public int Calls { get; private set; }
    public OPD_DATA LastOpd { get; private set; }
    public int LastDirection { get; private set; }
    public Func<OPD_DATA, int, AnimeSPRITE> Factory { get; set; }
    public AnimeSPRITE Create2DAnimeHuman(OPD_DATA opd, int direction)
    {
        Calls++; LastOpd = opd; LastDirection = direction;
        return Factory is null ? ValidSprite(opd, direction) : Factory(opd, direction);
    }
    public static AnimeSPRITE ValidSprite(OPD_DATA opd, int direction) => new AnimeSPRITE
    {
        opd_data = opd, opd_sprite = new OPDSprite { data = opd },
        anime_no = direction, pattern_kazu = 1, pattern_no = 0,
        pattern_table = new object()
    };
}
public static class AnimeSpriteManager
{
    public static int DestroyCalls;
    public static bool ThrowOnDestroy;
    public static void Destroy2DAnime(ref AnimeSPRITE sprite)
    {
        DestroyCalls++;
        if (ThrowOnDestroy) throw new InvalidOperationException("test cleanup failure");
        sprite = null;
    }
}
