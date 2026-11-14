extern alias GSD2;

using System;
using System.Collections.Generic;
using System.Linq;

namespace SuikodenRandomizer.Suikoden2Plugin;

internal static class Gsd2EquipmentBalance
{
    // Read-only managed equivalent of shipped G2_bogu_seigen 0x181a28f90
    // (GameAssembly SHA256 e093f175eaf3734d293ca0a236c652dd7fcc72ea5fa10d7b21103edf12f11c3a).
    // Header/body/shield decode soubi_kanou; accessories decode ckd_info[1].
    internal sealed class Armor
    {
        internal int Id { get; init; }
        internal int Category { get; init; }
        internal int Classes { get; init; }
        internal int SlotMask { get; init; }
        internal int Defense { get; init; }
        internal int Price { get; init; }
        internal int[] Bonuses { get; init; } = Array.Empty<int>();
        internal bool Ordinary { get; init; }
    }

    internal static Armor? Comparable(Armor original, IEnumerable<Armor> catalog, Func<Armor, bool> canWear,
        out bool exceededOriginalTier)
    {
        var compatible = catalog.Where(item => item.Id > 0 && item.Category == original.Category
            && item.Ordinary && canWear(item)).ToArray();
        var withinTier = compatible.Where(item => item.Defense <= original.Defense
            && (original.Price <= 0 || item.Price <= original.Price)).ToArray();
        exceededOriginalTier = withinTier.Length == 0 && compatible.Length != 0;
        return (withinTier.Length == 0 ? compatible : withinTier)
            .OrderBy(item => StatDistance(original, item))
            .ThenBy(item => Math.Abs((long)item.Price - original.Price))
            .ThenBy(item => item.Id).FirstOrDefault();
    }

    private static long StatDistance(Armor original, Armor item)
    {
        if (original.Bonuses.Length != 7 || item.Bonuses.Length != 7)
            throw new InvalidOperationException("Suikoden II armor bonus dimensions are incomplete.");
        long distance = Math.Abs((long)original.Defense - item.Defense) * 4;
        for (int index = 0; index < 7; index++)
            distance += Math.Abs((long)original.Bonuses[index] - item.Bonuses[index]) * 2;
        return distance;
    }

    internal static Gsd2EquipmentSnapshot Plan(GSD2.GAME_WORK game, GSD2.CHARA_DATA baseline,
        int role, int donor, Gsd2EquipmentSnapshot original)
    {
        if (game is null || baseline is null) throw new InvalidOperationException("Equipment metadata is unavailable.");
        original.Validate();
        var native = game.ovit_data?.bogu_data ?? throw new InvalidOperationException("Suikoden II armor catalog is missing.");
        if (native.Count < 2 || native.Count > 256 || donor <= 0 || donor >= baseline.c_kotei_dat.Count
            || donor >= baseline.ckd_info.Count || baseline.ckd_info[donor].Count < 2)
            throw new InvalidOperationException("Suikoden II equipment metadata is incomplete.");
        var catalog = new Dictionary<int, Armor>();
        for (int index = 1; index < native.Count; index++)
        {
            var item = native[index] ?? throw new InvalidOperationException("Suikoden II armor catalog row is missing.");
            if (item.info?.Count != 2 || item.bonus?.Count != 7 || item.atrb?.Count != 4
                || item.set_status?.Count != 3 || item.blk_status?.Count != 3)
                throw new InvalidOperationException("Suikoden II armor catalog dimensions are incomplete.");
            if (item.info[1] == 0) continue; // Empty reserved master-table rows.
            catalog.Add(index, new Armor {
                Id = index, Classes = item.info[0], SlotMask = item.info[1],
                Category = item.info[1] & 0x3f, Defense = item.def_val, Price = item.botch,
                Bonuses = item.bonus.Select(value => (int)value).ToArray(),
                Ordinary = item.atrb.All(value => value == 0) && item.set_status.All(value => value == 0)
                    && item.blk_status.All(value => value == 0) });
        }
        var result = original.Copy();
        var transfers = new List<byte>(); var decisions = new List<string>();
        int mask = baseline.c_kotei_dat[donor].soubi_kanou;
        int accessoryClass = (baseline.ckd_info[donor][1] & 15) switch
        {
            0 => 0x40, 1 => 0x10, 2 => 4, 3 => 0x20, 4 => 8, 5 => 2, 7 => 1, _ => 0
        };
        int boots = 0, gloves = 0;
        for (int place = 1; place <= 6; place++)
        {
            bool accessory = place >= 4;
            int slot = accessory ? place - 4 : place - 1;
            if (accessory && ((original.ItemUses[slot] >> 4) & 7) != 1) continue;
            byte id = accessory ? original.Items[slot] : original.Armor[slot];
            if (id == 0) continue;
            if (!catalog.TryGetValue(id, out var item))
                throw new InvalidOperationException("Suikoden II original armor is outside the admitted master catalog: " + id + ".");
            bool CanWear(Armor candidate)
            {
                if (!accessory)
                {
                    int kind = place == 1 ? mask & 3 : place == 2 ? mask >> 2 & 7 : mask >> 5 & 1;
                    return kind != 0 && (candidate.SlotMask & 1 << (place - 1)) != 0
                        && (candidate.Classes & 1 << (kind - 1)) != 0;
                }
                return (candidate.SlotMask & 8) != 0 && (candidate.Classes & accessoryClass) != 0
                    && ((candidate.SlotMask & 0x10) == 0 || boots < 2)
                    && ((candidate.SlotMask & 0x20) == 0 || gloves < 2);
            }
            Armor? selected = item;
            if (!CanWear(item))
            {
                selected = Comparable(item, catalog.Values, CanWear, out bool exceeded);
                if (selected is null)
                {
                    transfers.Add(id);
                    decisions.Add("place=" + place + "; original=" + id + "; no compatible equipment in category; moved to shared bag");
                }
                else decisions.Add("place=" + place + "; original=" + id + "; substitute=" + selected.Id
                    + (exceeded ? "; closest compatible fallback above original tier" : "; comparable compatible equipment"));
            }
            byte selectedId = (byte)(selected?.Id ?? 0);
            if (accessory)
            {
                result.Items[slot] = selectedId;
                if (selectedId == 0) result.ItemUses[slot] = 0;
                if (selected is not null)
                {
                    if ((selected.SlotMask & 0x10) != 0) boots++;
                    else if ((selected.SlotMask & 0x20) != 0) gloves++;
                }
            }
            else result.Armor[slot] = selectedId;
        }
        result.TransferToBag = transfers.ToArray(); result.Decisions = decisions.ToArray();
        return result;
    }
}
