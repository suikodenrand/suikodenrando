extern alias GSD2;

using System;
using System.IO;
using System.Linq;

namespace SuikodenRandomizer.Suikoden2Plugin;

/// <summary>Detached equipment state only; weapons, runes and progression are not included.</summary>
internal sealed class Gsd2EquipmentSnapshot
{
    public byte[] Armor { get; set; } = Array.Empty<byte>();
    public byte[] Items { get; set; } = Array.Empty<byte>();
    public byte[] ItemUses { get; set; } = Array.Empty<byte>();
    // Planning output only. Pending bindings retain the original full loadout;
    // committed native snapshots store these armor items in the shared bag.
    internal byte[] TransferToBag { get; set; } = Array.Empty<byte>();
    internal string[] Decisions { get; set; } = Array.Empty<string>();

    internal static Gsd2EquipmentSnapshot Capture(GSD2.C_VARIA_DAT source)
    {
        if (source?.bogu_eqp is null || source.item_eqp is null
            || source.bogu_eqp.Count != 3 || source.item_eqp.Count != 3)
            throw new InvalidOperationException("Suikoden II equipment dimensions are incomplete.");
        var result = new Gsd2EquipmentSnapshot { Armor = source.bogu_eqp.ToArray(), Items = new byte[3], ItemUses = new byte[3] };
        for (int index = 0; index < 3; index++)
        {
            var item = source.item_eqp[index] ?? throw new InvalidOperationException("Suikoden II equipment item is missing.");
            result.Items[index] = item.item_no;
            result.ItemUses[index] = item.use_cnt;
        }
        return result;
    }

    internal void Validate()
    {
        if (Armor?.Length != 3 || Items?.Length != 3 || ItemUses?.Length != 3)
            throw new InvalidDataException("Saved Suikoden II recruitment equipment has invalid dimensions.");
    }

    internal Gsd2EquipmentSnapshot Copy()
    {
        Validate();
        return new Gsd2EquipmentSnapshot { Armor = (byte[])Armor.Clone(), Items = (byte[])Items.Clone(), ItemUses = (byte[])ItemUses.Clone() };
    }

    internal void Apply(GSD2.C_VARIA_DAT destination)
    {
        Validate();
        // Update values in place. Native event and battle coroutines may retain
        // the character row, armor array and individual item object references.
        _ = Capture(destination);
        for (int index = 0; index < 3; index++)
        {
            destination.bogu_eqp[index] = Armor[index];
            destination.item_eqp[index].item_no = Items[index];
            destination.item_eqp[index].use_cnt = ItemUses[index];
        }
    }

    internal bool Same(Gsd2EquipmentSnapshot other)
    {
        Validate(); other.Validate();
        return Armor.SequenceEqual(other.Armor) && Items.SequenceEqual(other.Items) && ItemUses.SequenceEqual(other.ItemUses);
    }
}
