extern alias GSD1;

using Il2CppInterop.Runtime.InteropTypes.Arrays;
using System;
using System.IO;
using System.Linq;

namespace SuikodenRandomizer.AvatarPlugin;

/// <summary>A detached copy of the persistent native fields. No native pointers are serialized.</summary>
internal sealed class FixedRosterBaseSnapshot
{
    public int CharacterId { get; set; }
    public ushort MaxHp { get; set; }
    public ushort Hp { get; set; }
    public ushort Exp { get; set; }
    public ushort Status { get; set; }
    public byte Level { get; set; }
    public byte ItemCount { get; set; }
    public byte[] MagicPoints { get; set; } = Array.Empty<byte>();
    public byte[] Stats { get; set; } = Array.Empty<byte>();
    public byte[] Growth { get; set; } = Array.Empty<byte>();
    public FixedRosterItemSnapshot[] Items { get; set; } = Array.Empty<FixedRosterItemSnapshot>();
    public byte WeaponId { get; set; }
    public byte WeaponLevel { get; set; }
    public byte[] WeaponRunes { get; set; } = Array.Empty<byte>();
    public byte RuneId { get; set; }
    public byte RuneLevel { get; set; }
    public ushort RuneExp { get; set; }

    public FixedRosterBaseSnapshot() { }

    internal static FixedRosterBaseSnapshot Capture(GSD1.PLAYER_BASE source)
    {
        AvatarNativeData.ValidateBase(source);
        var inventory = source.item;
        var items = new FixedRosterItemSnapshot[inventory.Count];
        for (int i = 0; i < items.Length; i++)
        {
            var item = inventory[i] ?? throw new InvalidDataException("Fixed roster inventory contains an uninitialized item record.");
            items[i] = new FixedRosterItemSnapshot { Id = item.item_id, Equipped = item.soubi, Data = item.data };
        }
        var weapon = source.buki_data;
        var rune = source.monsyo_data;
        return new FixedRosterBaseSnapshot
        {
            CharacterId = source.chara_no, MaxHp = source.max_hp, Hp = source.hp, Exp = source.exp,
            Status = source.status, Level = source.level, ItemCount = source.item_kazu,
            MagicPoints = CaptureBytes(source.magic_point), Stats = CaptureBytes(source.noryoku),
            Growth = CaptureBytes(source.seicho_type), Items = items, WeaponId = weapon.buki_id,
            WeaponLevel = weapon.level, WeaponRunes = CaptureBytes(weapon.monsyo), RuneId = rune.monsyo_id,
            RuneLevel = rune.monsyo_level, RuneExp = rune.monsyo_exp
        };
    }

    internal void ValidateState()
    {
        if (CharacterId < 0 || CharacterId > 109 || Level == 0 || MaxHp == 0
            || !ValidLength(MagicPoints, 5) || !ValidLength(Stats, 6) || !ValidLength(Growth, 7)
            || !ValidLength(WeaponRunes, 6) || Items is null || Items.Length < 9 || Items.Length > byte.MaxValue
            || ItemCount > Items.Length || Items.Any(item => item is null))
            throw new InvalidDataException("Fixed roster snapshot contains invalid or incomplete fighter data.");
    }

    internal GSD1.PLAYER_BASE ToNative()
    {
        ValidateState();
        var items = new Il2CppReferenceArray<GSD1.PLAYER_ITEM>(Items.Length);
        for (int i = 0; i < Items.Length; i++)
        {
            var item = Items[i];
            items[i] = new GSD1.PLAYER_ITEM { item_id = item.Id, soubi = item.Equipped, data = item.Data };
        }
        return new GSD1.PLAYER_BASE
        {
            chara_no = CharacterId, max_hp = MaxHp, hp = Hp, exp = Exp, status = Status, level = Level,
            item_kazu = ItemCount, magic_point = ToNativeBytes(MagicPoints), noryoku = ToNativeBytes(Stats),
            seicho_type = ToNativeBytes(Growth), item = items,
            buki_data = new GSD1.PLAYER_BUKI
            { buki_id = WeaponId, level = WeaponLevel, monsyo = ToNativeBytes(WeaponRunes) },
            monsyo_data = new GSD1.PLAYER_MONSYO { monsyo_id = RuneId, monsyo_level = RuneLevel, monsyo_exp = RuneExp }
        };
    }

    private static bool ValidLength(byte[]? bytes, int minimum) =>
        bytes is not null && bytes.Length >= minimum && bytes.Length <= byte.MaxValue;

    private static byte[] CaptureBytes(Il2CppStructArray<byte> source)
    {
        var result = new byte[source.Count];
        for (int i = 0; i < result.Length; i++) result[i] = source[i];
        return result;
    }

    private static Il2CppStructArray<byte> ToNativeBytes(byte[] source)
    {
        var result = new Il2CppStructArray<byte>(source.Length);
        for (int i = 0; i < source.Length; i++) result[i] = source[i];
        return result;
    }
}

internal sealed class FixedRosterItemSnapshot
{
    public ushort Id { get; set; }
    public byte Equipped { get; set; }
    public byte Data { get; set; }

    public FixedRosterItemSnapshot() { }
}
