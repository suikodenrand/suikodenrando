extern alias GSD1;

using System;
using System.Collections.Generic;

namespace SuikodenRandomizer.AvatarPlugin;

// S1's warehouse indexes each item ID by identification status. Armor uses bucket
// zero and withdraws as an unequipped item with a zero data byte. Medicine uses a
// separate charge pool, and unidentified antiques use bucket one.
internal sealed class EquipmentWarehouse
{
    private const int LastWarehouseId = 178;
    private const int Capacity = 240;
    private const int MaxCopies = 254;
    private readonly GSD1.SHIRO_DATA _castle;
    private readonly Dictionary<ushort, int> _stock = new();
    private readonly bool[] _armor = new bool[LastWarehouseId + 1];
    private readonly Dictionary<ushort, byte> _undo = new();
    private readonly int _total;

    internal IReadOnlyDictionary<ushort, int> Counts => _stock;

    private EquipmentWarehouse(GSD1.SHIRO_DATA castle, int total)
    {
        _castle = castle;
        _total = total;
    }

    internal static EquipmentWarehouse? Capture()
    {
        if (!WarehouseMenu.RocRecruited || GSD1.OldSrcBase.game_work?.shiro_data is not { } castle
            || castle.azukari_kantei is not { } buckets || buckets.Length <= LastWarehouseId
            || GSD1.G_item_c_data.item_data_table is not { } catalog) return null;
        int total = 0;
        // D_azukar_c.GetSyohinNum counts both buckets for IDs 1 through 178.
        for (int id = 1; id <= LastWarehouseId; id++)
        {
            if (buckets[id] is not { Length: >= 2 } row) return null;
            total += row[0] + row[1];
        }
        var result = new EquipmentWarehouse(castle, total);
        for (ushort id = 1; id <= LastWarehouseId && id < catalog.Length; id++)
        {
            var item = catalog[id];
            if (item == null || (item.type & 0x700c) != 0 || item.TryCast<GSD1.BOUGU_DATA>() == null) continue;
            result._armor[id] = true;
            int count = buckets[id][0];
            if (count > 0) result._stock.Add(id, count);
        }
        return result;
    }

    internal bool CanStore(ushort id, byte data) => id <= LastWarehouseId && _armor[id] && data == 0;

    internal bool CanApply(IReadOnlyDictionary<ushort, int> deltas)
    {
        long total = _total;
        foreach (var (id, change) in deltas)
        {
            if (change == 0) continue;
            if (!CanStore(id, 0)) return false;
            long end = (long)_stock.GetValueOrDefault(id) + change;
            if (end < 0 || (change > 0 && end > MaxCopies)) return false;
            total += change;
        }
        return total <= Capacity || total < _total;
    }

    internal void Apply(IReadOnlyDictionary<ushort, int> deltas)
    {
        if (_undo.Count > 0 || !CanApply(deltas) || !WarehouseMenu.RocRecruited
            || GSD1.OldSrcBase.game_work?.shiro_data?.Pointer != _castle.Pointer
            || GSD1.D_azukar_c.GetSyohinNum() != _total)
            throw new InvalidOperationException("Warehouse stock changed before Optimize could finish.");
        var buckets = _castle.azukari_kantei;
        foreach (var (id, change) in deltas)
            if (change != 0 && buckets[id][0] != _stock.GetValueOrDefault(id))
                throw new InvalidOperationException("The selected warehouse item is no longer available.");
        try
        {
            foreach (var (id, change) in deltas)
            {
                if (change == 0) continue;
                var row = buckets[id];
                _undo.Add(id, row[0]);
                row[0] = checked((byte)(row[0] + change));
            }
        }
        catch
        {
            Restore();
            throw;
        }
    }

    internal void Restore()
    {
        foreach (var (id, before) in _undo) _castle.azukari_kantei[id][0] = before;
        _undo.Clear();
    }
}
