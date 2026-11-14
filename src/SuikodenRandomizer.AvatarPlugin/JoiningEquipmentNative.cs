extern alias GSD1;

using Il2CppInterop.Runtime.InteropTypes.Arrays;
using System;
using System.Collections.Generic;

namespace SuikodenRandomizer.AvatarPlugin;

internal static class JoiningEquipmentNative
{
    internal static bool Ready => GSD1.G_item_c_data.item_data_table is { } table && table.Count >= 2;

    internal static JoiningEquipmentInventory Capture(GSD1.PLAYER_BASE source)
    {
        AvatarNativeData.ValidateBase(source);
        var items = source.item;
        var copy = new JoiningEquipmentItem[items.Count];
        for (int i = 0; i < copy.Length; i++)
        {
            var item = items[i] ?? throw new InvalidOperationException("Native joining inventory has a missing item record.");
            copy[i] = new() { Id = item.item_id, Equipped = item.soubi, Data = item.data };
        }
        var result = new JoiningEquipmentInventory { Count = source.item_kazu, Items = copy };
        result.Validate(); return result;
    }

    internal static string Apply(GSD1.PLAYER_BASE target, JoiningEquipmentInventory reference,
        GSD1.PLAYER_DATA donor, int role, int fighter)
    {
        var plan = JoiningEquipmentPlan.Create(reference, Catalog(), donor.soubi_type, donor.sex);
        var inventory = plan.Inventory;
        var native = new Il2CppReferenceArray<GSD1.PLAYER_ITEM>(inventory.Items.Length);
        for (int i = 0; i < native.Count; i++)
        {
            var item = inventory.Items[i];
            native[i] = new GSD1.PLAYER_ITEM { item_id = item.Id, soubi = item.Equipped, data = item.Data };
        }
        target.item = native;
        target.item_kazu = inventory.Count;
        return "JOINING EQUIPMENT INITIALIZED: role=" + role + "; donor=" + fighter + "; policy=MatchRole"
            + "; inventory count=" + inventory.Count + "; " + (plan.Changes.Length == 0 ? "original role items retained" : string.Join("; ", plan.Changes));
    }

    private static IReadOnlyDictionary<ushort, JoiningEquipmentCatalogEntry> Catalog()
    {
        var table = GSD1.G_item_c_data.item_data_table
            ?? throw new InvalidOperationException("Native item definitions are not ready for joining equipment.");
        if (table.Count < 2 || table.Count > ushort.MaxValue)
            throw new InvalidOperationException("Native item definitions have an unsupported count.");
        var entries = new Dictionary<ushort, JoiningEquipmentCatalogEntry>();
        for (int id = 1; id < table.Count; id++)
        {
            var item = table[id];
            if (item is null) continue;
            var armor = item.TryCast<GSD1.BOUGU_DATA>();
            byte[]? bonuses = null;
            if (armor is not null)
            {
                if (armor.equip_data is null || armor.equip_data.Count < 8)
                    throw new InvalidOperationException("Native armor bonuses are incomplete for item " + id + ".");
                bonuses = new byte[8];
                for (int stat = 0; stat < bonuses.Length; stat++) bonuses[stat] = armor.equip_data[stat];
            }
            entries.Add((ushort)id, new((ushort)id, item.nedan, item.type,
                armor?.soubi_type ?? 0, armor?.func_no ?? 0, bonuses));
        }
        return entries;
    }
}
