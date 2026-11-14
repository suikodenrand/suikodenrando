extern alias GSD2;

using System;
using Il2CppInterop.Runtime.InteropTypes.Arrays;

namespace SuikodenRandomizer.Suikoden2Plugin;

internal sealed class Gsd2FighterRecord
{
    public byte Level { get; set; }
    public ushort Experience { get; set; }
    public ushort HitPoints { get; set; }
    public ushort MaximumHitPoints { get; set; }
    public byte[] MagicPoints { get; set; } = Array.Empty<byte>();
    public byte[] Parameters { get; set; } = Array.Empty<byte>();
    public byte WeaponLevel { get; set; }
    public byte WeaponRune { get; set; }
    public byte[] Runes { get; set; } = Array.Empty<byte>();
    public byte[] Armor { get; set; } = Array.Empty<byte>();
    public byte[] Items { get; set; } = Array.Empty<byte>();
    public byte[] ItemUses { get; set; } = Array.Empty<byte>();
    public ushort FinishingBlows { get; set; }
    private static byte[] Copy(Il2CppStructArray<byte> source)
    {
        var result = new byte[source.Count];
        for (int i = 0; i < result.Length; i++) result[i] = source[i];
        return result;
    }
    private static Il2CppStructArray<byte> Native(byte[] source)
    {
        var result = new Il2CppStructArray<byte>(source.Length);
        for (int i = 0; i < source.Length; i++) result[i] = source[i];
        return result;
    }
    internal static Gsd2FighterRecord Capture(GSD2.C_VARIA_DAT value)
    {
        var result = new Gsd2FighterRecord
        {
            Level = value.level, Experience = value.exp, HitPoints = value.now_hp, MaximumHitPoints = value.max_hp,
            MagicPoints = Copy(value.mp), Parameters = Copy(value.para), WeaponLevel = value.buki_lv, WeaponRune = value.buki_mon,
            Runes = Copy(value.mon_eqp), Armor = Copy(value.bogu_eqp), FinishingBlows = value.todome,
            Items = new byte[value.item_eqp.Count], ItemUses = new byte[value.item_eqp.Count]
        };
        for (int i = 0; i < result.Items.Length; i++) { result.Items[i] = value.item_eqp[i].item_no; result.ItemUses[i] = value.item_eqp[i].use_cnt; }
        result.Validate(); return result;
    }
    internal void Validate()
    {
        if (MagicPoints?.Length != 4 || Parameters?.Length != 7 || Runes?.Length != 3 || Armor?.Length != 3
            || Items?.Length != 3 || ItemUses?.Length != 3 || Level == 0 || Level > 99 || MaximumHitPoints == 0 || WeaponLevel > 16)
            throw new InvalidOperationException("Suikoden II saved fighter record has invalid native dimensions or level.");
    }
    internal GSD2.C_VARIA_DAT CreateNative()
    {
        Validate(); var items = new Il2CppReferenceArray<GSD2.PARTY_ITEM>(3);
        for (int i = 0; i < 3; i++) items[i] = new GSD2.PARTY_ITEM { item_no = Items[i], use_cnt = ItemUses[i] };
        return new GSD2.C_VARIA_DAT
        {
            level = Level, exp = Experience, now_hp = HitPoints, max_hp = MaximumHitPoints,
            mp = Native(MagicPoints), para = Native(Parameters), buki_lv = WeaponLevel, buki_mon = WeaponRune,
            mon_eqp = Native(Runes), bogu_eqp = Native(Armor), item_eqp = items, todome = FinishingBlows
        };
    }
}
