using System;
using System.Collections.Generic;
using System.Linq;

namespace SuikodenRandomizer.AvatarPlugin;

internal readonly struct StartingGearItem
{
    internal ushort Id { get; }
    internal byte Equipped { get; }
    internal byte Data { get; }
    internal StartingGearItem(ushort id, byte equipped, byte data) { Id = id; Equipped = equipped; Data = data; }
}

internal sealed class StartingGearCatalogEntry
{
    internal ushort Id { get; }
    internal int Price { get; }
    internal ushort Type { get; }
    internal byte EquipMask { get; }
    internal byte FuncNo { get; }
    internal byte[]? Bonuses { get; }
    internal bool IsArmor => Bonuses is not null;
    internal int Category => (EquipMask & 3) != 0 ? 1 : (EquipMask & 60) != 0 ? 2
        : (EquipMask & 64) != 0 ? 3 : (EquipMask & 128) != 0 ? 5 : 0;
    internal StartingGearCatalogEntry(ushort id, int price, ushort type, byte equipMask, byte funcNo, byte[]? bonuses)
    {
        if (bonuses is not null && bonuses.Length != 8) throw new ArgumentException("Armor requires eight native stat bonuses.", nameof(bonuses));
        Id = id; Price = price; Type = type; EquipMask = equipMask; FuncNo = funcNo;
        Bonuses = bonuses is null ? null : (byte[])bonuses.Clone();
    }
}

internal sealed class StartingGearBalanceResult
{
    internal StartingGearItem[] Items { get; }
    internal int ItemCount { get; }
    internal string[] Warnings { get; }
    internal StartingGearBalanceResult(StartingGearItem[] items, int count, IEnumerable<string> warnings)
    { Items = items; ItemCount = count; Warnings = warnings.ToArray(); }
}

/// <summary>Detached inventory policy; it never reads or writes native objects.</summary>
internal static class StartingGearBalancePolicy
{
    internal static StartingGearBalanceResult Apply(StartingGearItem[] incoming, int incomingCount,
        StartingGearItem[] reference, int referenceCount,
        IReadOnlyDictionary<ushort, StartingGearCatalogEntry> catalog, byte allowedMask, byte sex)
    {
        if (incoming is null || reference is null || catalog is null) throw new ArgumentNullException("Starting gear inputs are required.");
        if (incomingCount < 0 || incomingCount > incoming.Length || referenceCount < 0 || referenceCount > reference.Length)
            throw new ArgumentOutOfRangeException("Starting inventory count exceeds its native storage.");
        var referenceTotal = new int[8];
        var categoryTargets = new Dictionary<int, int[]>();
        var categoryPrices = new Dictionary<int, int>();
        int referenceMaxPrice = 0;
        for (int i = 0; i < referenceCount; i++)
        {
            var item = reference[i];
            var entry = Read(catalog, item.Id);
            if (entry?.IsArmor != true || item.Equipped == 0) continue;
            Add(referenceTotal, entry.Bonuses!);
            if (!categoryTargets.TryGetValue(entry.Category, out var target))
                categoryTargets.Add(entry.Category, target = new int[8]);
            for (int stat = 0; stat < 8; stat++) target[stat] = Math.Max(target[stat], entry.Bonuses![stat]);
            categoryPrices[entry.Category] = Math.Max(categoryPrices.TryGetValue(entry.Category, out int prior) ? prior : 0, entry.Price);
            referenceMaxPrice = Math.Max(referenceMaxPrice, entry.Price);
        }
        var budget = referenceTotal.Select((value, stat) => value + (stat == 7 ? 3 : 2)).ToArray();
        var used = new int[8];
        var result = (StartingGearItem[])incoming.Clone();
        var removed = new HashSet<int>();
        var warnings = new List<string>();
        var ordinary = new List<int>();
        for (int i = 0; i < incomingCount; i++)
        {
            var entry = Read(catalog, incoming[i].Id);
            if (entry?.IsArmor != true) continue;
            if (entry.Price <= 0 || entry.FuncNo != 0 || entry.Category == 0)
            {
                warnings.Add("Preserved special armor " + entry.Id + " without a normal shop tier.");
                if (incoming[i].Equipped != 0) Add(used, entry.Bonuses!);
            }
            else ordinary.Add(i);
        }
        // A locked piece must remain equipped. Give it first claim on the modest
        // budget; removable surplus armor can be omitted rather than exceed it.
        var worn = ordinary.Where(i => incoming[i].Equipped != 0)
            .OrderByDescending(i => (incoming[i].Equipped & 128) != 0).ThenBy(i => i);
        foreach (int i in worn) BalanceOne(i, true);
        // Spare armor is balanced as well, so the old late-game item cannot be
        // re-equipped or sold after downgrading only the worn kit.
        foreach (int i in ordinary.Where(i => incoming[i].Equipped == 0)) BalanceOne(i, false);

        int count = 0;
        for (int i = 0; i < incomingCount; i++)
            if (!removed.Contains(i)) result[count++] = result[i];
        for (int i = count; i < incomingCount; i++) result[i] = default;
        return new StartingGearBalanceResult(result, count, warnings);

        void BalanceOne(int index, bool equipped)
        {
            var item = incoming[index];
            var original = catalog[item.Id];
            var compatible = catalog.Values.Where(entry => entry.IsArmor && entry.Price > 0 && entry.FuncNo == 0
                && entry.Category == original.Category && Compatible(entry, allowedMask, sex)).ToArray();
            var remaining = equipped ? budget.Select((limit, stat) => Math.Max(0, limit - used[stat])).ToArray() : budget;
            var withinStats = compatible.Where(entry => Fits(entry, remaining)).ToArray();
            long priceLimit = 2L * (categoryPrices.TryGetValue(original.Category, out int price) ? price : referenceMaxPrice);
            var target = categoryTargets.TryGetValue(original.Category, out var categoryTarget) ? categoryTarget : new int[8];
            var affordable = withinStats.Where(entry => entry.Price <= priceLimit).ToArray();
            StartingGearCatalogEntry? chosen = affordable.OrderBy(entry => Distance(entry, target))
                .ThenBy(entry => Math.Abs((long)entry.Price - (priceLimit / 2))).ThenBy(entry => entry.Id).FirstOrDefault();
            if (chosen is null)
            {
                chosen = withinStats.OrderBy(entry => entry.Price).ThenBy(entry => entry.Bonuses!.Sum(value => value))
                    .ThenBy(entry => entry.Id).FirstOrDefault();
                if (chosen is not null) warnings.Add("Used basic compatible armor " + chosen.Id + " outside the reference price tier.");
            }
            if (chosen is null && (item.Equipped & 128) != 0)
            {
                chosen = compatible.OrderBy(entry => entry.Price).ThenBy(entry => entry.Bonuses!.Sum(value => value))
                    .ThenBy(entry => entry.Id).FirstOrDefault();
                if (chosen is null) throw new InvalidOperationException("No compatible basic replacement exists for locked armor " + item.Id + ".");
                warnings.Add("Locked armor required the cheapest compatible basic item " + chosen.Id + " beyond the reference stat budget.");
            }
            if (chosen is null) { removed.Add(index); return; }
            result[index] = new StartingGearItem(chosen.Id, item.Equipped, item.Data);
            if (equipped) Add(used, chosen.Bonuses!);
        }
    }

    private static StartingGearCatalogEntry? Read(IReadOnlyDictionary<ushort, StartingGearCatalogEntry> catalog, ushort id)
    {
        if (id == 0) return null;
        if (!catalog.TryGetValue(id, out var entry))
            throw new InvalidOperationException("Starting inventory contains unknown item " + id + "; no equipment changes were published.");
        return entry;
    }

    private static bool Compatible(StartingGearCatalogEntry entry, byte mask, byte sex)
    {
        if ((entry.EquipMask & mask) == 0) return false;
        if ((entry.Type & 0xF00) == 0) return true;
        int sexMask = sex == 0 ? 0x100 : sex == 1 ? 0x200 : sex == 2 ? 0x800 : sex == 3 ? 0x400 : 0;
        return sexMask != 0 && (entry.Type & sexMask) != 0;
    }
    private static bool Fits(StartingGearCatalogEntry entry, int[] remaining)
    {
        for (int stat = 0; stat < 8; stat++) if (entry.Bonuses![stat] > remaining[stat]) return false;
        return true;
    }
    private static int Distance(StartingGearCatalogEntry entry, int[] target)
    {
        int distance = 0;
        for (int stat = 0; stat < 8; stat++) distance += Math.Abs(entry.Bonuses![stat] - target[stat]) * (stat == 7 ? 4 : 2);
        return distance;
    }
    private static void Add(int[] total, byte[] bonuses)
    { for (int stat = 0; stat < 8; stat++) total[stat] += bonuses[stat]; }
}
