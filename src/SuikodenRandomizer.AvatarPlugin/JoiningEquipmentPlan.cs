using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SuikodenRandomizer.AvatarPlugin;

internal enum JoiningEquipmentPolicy { Donor, MatchRole }

internal sealed class JoiningEquipmentItem
{
    public ushort Id { get; set; }
    public byte Equipped { get; set; }
    public byte Data { get; set; }
    internal JoiningEquipmentItem Clone() => new() { Id = Id, Equipped = Equipped, Data = Data };
}

internal sealed class JoiningEquipmentInventory
{
    public byte Count { get; set; }
    public JoiningEquipmentItem[] Items { get; set; } = Array.Empty<JoiningEquipmentItem>();
    internal void Validate()
    {
        if (Items is null || Items.Length < 9 || Items.Length > byte.MaxValue || Count > Items.Length
            || Items.Any(item => item is null))
            throw new InvalidDataException("Joining equipment inventory is incomplete.");
    }
    internal JoiningEquipmentInventory Clone()
    {
        Validate();
        return new() { Count = Count, Items = Items.Select(item => item.Clone()).ToArray() };
    }
    internal bool Same(JoiningEquipmentInventory other) => Count == other.Count && Items.Length == other.Items.Length
        && Items.Zip(other.Items, (a, b) => a.Id == b.Id && a.Equipped == b.Equipped && a.Data == b.Data).All(value => value);
}

internal sealed class JoiningEquipmentCatalogEntry
{
    internal ushort Id { get; }
    internal int Price { get; }
    internal ushort Type { get; }
    internal byte EquipMask { get; }
    internal byte Function { get; }
    internal byte[]? Bonuses { get; }
    internal bool IsArmor => Bonuses is not null;
    internal int Category => (EquipMask & 3) != 0 ? 1 : (EquipMask & 60) != 0 ? 2
        : (EquipMask & 64) != 0 ? 3 : (EquipMask & 128) != 0 ? 5 : 0;
    internal JoiningEquipmentCatalogEntry(ushort id, int price, ushort type, byte equipMask, byte function, byte[]? bonuses)
    {
        if (bonuses is not null && bonuses.Length != 8) throw new ArgumentException("Armor requires eight native bonuses.");
        Id = id; Price = price; Type = type; EquipMask = equipMask; Function = function;
        Bonuses = bonuses is null ? null : (byte[])bonuses.Clone();
    }
    internal bool Compatible(byte allowedMask, byte sex)
    {
        if ((EquipMask & allowedMask) == 0) return false;
        if ((Type & 0xF00) == 0) return true;
        int sexMask = sex == 0 ? 0x100 : sex == 1 ? 0x200 : sex == 2 ? 0x800 : sex == 3 ? 0x400 : 0;
        return sexMask != 0 && (Type & sexMask) != 0;
    }
}

internal sealed class JoiningEquipmentPlan
{
    internal JoiningEquipmentInventory Inventory { get; }
    internal string[] Changes { get; }
    private JoiningEquipmentPlan(JoiningEquipmentInventory inventory, IEnumerable<string> changes)
    { Inventory = inventory; Changes = changes.ToArray(); }

    // Copy the original role's complete carried inventory. Character progression,
    // weapon identity/level/runes and the attached rune record are separate fields.
    internal static JoiningEquipmentPlan Create(JoiningEquipmentInventory reference,
        IReadOnlyDictionary<ushort, JoiningEquipmentCatalogEntry> catalog, byte allowedMask, byte sex)
    {
        reference.Validate();
        var copy = reference.Clone();
        var changes = new List<string>();
        for (int index = 0; index < copy.Count; index++)
        {
            var item = copy.Items[index];
            if (item.Id == 0) continue;
            if (!catalog.TryGetValue(item.Id, out var original))
                throw new InvalidDataException("Original role inventory contains unknown item " + item.Id + ".");
            if (!original.IsArmor || original.Compatible(allowedMask, sex)) continue;
            // Never convert a shield into an occupied body/accessory slot. The
            // same-category pool also preserves native duplicate-slot handling.
            var compatible = catalog.Values.Where(value => value.IsArmor && value.Category != 0
                && value.Category == original.Category && value.Compatible(allowedMask, sex)
                && value.Function == 0).ToArray();
            var bounded = compatible.Where(value => value.Bonuses![7] <= original.Bonuses![7]
                && (original.Price <= 0 || value.Price <= original.Price)).ToArray();
            bool budgetFallback = bounded.Length == 0 && compatible.Length != 0;
            var candidates = budgetFallback ? compatible : bounded;
            var replacement = candidates.OrderBy(value => Distance(value.Bonuses!, original.Bonuses!))
                .ThenBy(value => Math.Abs((long)value.Price - original.Price)).ThenBy(value => value.Id).FirstOrDefault();
            if (replacement is null)
            {
                // No legal slot/class equivalent exists. Keep the role's item
                // owned, but clear both equipped and lock flags rather than
                // force equipment the displayed fighter cannot wear.
                item.Equipped = 0;
                changes.Add("no compatible category/tier item for " + item.Id + "; retained unequipped without lock flags");
            }
            else
            {
                ushort before = item.Id;
                item.Id = replacement.Id;
                changes.Add("compatible replacement " + before + "->" + item.Id
                    + (budgetFallback ? "; closest legal basic item required above role budget" : "")
                    + (original.Price <= 0 ? "; source has no shop price; closest native stats selected before price" : ""));
            }
        }
        return new JoiningEquipmentPlan(copy, changes);
    }

    private static long Distance(byte[] candidate, byte[] reference)
    {
        long distance = 0;
        for (int stat = 0; stat < 8; stat++) distance += Math.Abs(candidate[stat] - reference[stat]) * (stat == 7 ? 4L : 2L);
        return distance;
    }
}
