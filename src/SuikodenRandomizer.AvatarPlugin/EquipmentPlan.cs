using System;
using System.Collections.Generic;
using System.Linq;

namespace SuikodenRandomizer.AvatarPlugin;

internal sealed class EquipmentPlan
{
    private const int Pockets = 9, Slots = 5, Storage = -1, Empty = -2;
    internal JoiningEquipmentInventory[] Bags { get; }
    internal IReadOnlyDictionary<ushort, int> WarehouseDelta { get; }
    internal int Upgrades { get; }
    internal bool Changed => Upgrades != 0;

    private EquipmentPlan(JoiningEquipmentInventory[] bags, Dictionary<ushort, int> delta, int upgrades)
        => (Bags, WarehouseDelta, Upgrades) = (bags, delta, upgrades);

    // Native player_soubi_change compares defense (stat 7). Preserve worn gear on a tie,
    // then fill empty slots, compare other numeric bonuses and prefer an item already on hand.
    private readonly record struct Rank(int Defense, int Kept, int Filled, int Bonuses, int Locality) : IComparable<Rank>
    {
        public int CompareTo(Rank other) => (Defense, Kept, Filled, Bonuses, Locality)
            .CompareTo((other.Defense, other.Kept, other.Filled, other.Bonuses, other.Locality));
        public static Rank operator +(Rank a, Rank b) => new(a.Defense + b.Defense, a.Kept + b.Kept,
            a.Filled + b.Filled, a.Bonuses + b.Bonuses, a.Locality + b.Locality);
    }

    private readonly record struct Choice(int Owner, int Pocket, ushort Id, Rank Rank);

    internal static EquipmentPlan Create(JoiningEquipmentInventory[] bags, int target,
        IReadOnlyDictionary<ushort, JoiningEquipmentCatalogEntry> catalog, byte[] slotTypes,
        Func<ushort, bool> canWear, IReadOnlyDictionary<ushort, int> warehouse, bool useParty,
        Func<ushort, byte, bool> canStore, Func<IReadOnlyDictionary<ushort, int>, bool> canApply)
    {
        if (target < 0 || target >= bags.Length || slotTypes.Length != Slots + 1)
            throw new ArgumentException("Invalid target or native equipment slots.");
        foreach (var bag in bags)
        {
            bag.Validate();
            if (bag.Count > Pockets) throw new ArgumentException("Too many carried items.");
        }
        var original = bags[target];
        var unchanged = new EquipmentPlan(bags, new(), 0);
        var worn = Enumerable.Repeat(-1, Slots + 1).ToArray();
        for (int i = 0; i < original.Count; i++)
        {
            var item = original.Items[i];
            int slot = item.Equipped & 0x7f;
            if (slot == 0 || slot > Slots) continue;
            if (worn[slot] != -1) return unchanged; // Preserve an ambiguous imported loadout.
            worn[slot] = i;
        }
        var choices = new Choice[Slots + 1][];
        var selected = new Choice[Slots + 1];
        var upper = new Rank[Slots + 2];
        var movableArmor = original.Items.Take(original.Count).Where(item =>
            (item.Equipped & 0x80) == 0 && (item.Equipped & 0x7f) <= Slots
            && catalog.TryGetValue(item.Id, out var row) && row.IsArmor).ToArray();
        int capacity = Pockets - original.Count + movableArmor.Length;
        int storageCapacity = Pockets - original.Count + movableArmor.Count(item => canStore(item.Id, item.Data));
        if (useParty) storageCapacity += bags.Where((_, i) => i != target).Sum(b => Pockets - b.Count);
        Rank best = default;
        for (int slot = Slots; slot >= 1; slot--)
        {
            var options = new List<Choice>();
            int current = worn[slot];
            if (current < 0) options.Add(new Choice(Empty, -1, 0, default));
            else
            {
                var item = original.Items[current];
                catalog.TryGetValue(item.Id, out var row);
                var rank = new Rank(row?.Bonuses?[7] ?? 0, 1, 1, 0, 0);
                best += rank;
                options.Add(new Choice(target, current, item.Id, rank));
                // The native unequip command refuses negative (high-bit) soubi flags.
                if ((item.Equipped & 0x80) != 0 || row?.Bonuses == null)
                {
                    choices[slot] = options.ToArray();
                    upper[slot] = upper[slot + 1] + rank;
                    continue;
                }
            }

            void Offer(int owner, int pocket, ushort id)
            {
                if (!catalog.TryGetValue(id, out var row) || row.Bonuses is not { } stats
                    || (row.Type & 0xe0) != slotTypes[slot] || !canWear(id)) return;
                int bonuses = 0;
                for (int i = 0; i < 7; i++) bonuses += stats[i];
                int locality = owner == target ? 2 : owner == Storage ? 0 : 1;
                options.Add(new Choice(owner, pocket, id, new Rank(stats[7], 0, 1, bonuses, locality)));
            }
            for (int owner = 0; owner < bags.Length; owner++)
            {
                if (owner != target && !useParty) continue;
                var bag = bags[owner];
                for (int i = 0; i < bag.Count; i++)
                    if (bag.Items[i].Equipped == 0) Offer(owner, i, bag.Items[i].Id);
            }
            foreach (var entry in warehouse.OrderBy(e => e.Key))
                if (entry.Value > 0) Offer(Storage, -1, entry.Key);

            choices[slot] = options.OrderByDescending(c => c.Rank).ToArray();
            upper[slot] = upper[slot + 1] + choices[slot][0].Rank;
        }

        var result = unchanged;
        void Search(int slot, Rank score, int needed, int fromStorage)
        {
            if ((score + upper[slot]).CompareTo(best) <= 0) return;
            if (slot > Slots)
            {
                var plan = Materialize(bags, target, worn, selected, catalog, useParty, canStore, canApply);
                if (plan != null) { best = score; result = plan; }
                return;
            }
            foreach (var choice in choices[slot])
            {
                Rank next = score + choice.Rank;
                if ((next + upper[slot + 1]).CompareTo(best) <= 0) break;
                bool needsPocket = choice.Owner != Empty && (choice.Owner != target
                    || (original.Items[choice.Pocket].Equipped & 0x80) == 0
                    && catalog.TryGetValue(choice.Id, out var existing) && existing.IsArmor);
                int used = needed + (needsPocket ? 1 : 0);
                if (used > capacity) continue;
                int withdrawals = fromStorage + (choice.Owner == Storage ? 1 : 0);
                if (withdrawals > storageCapacity) continue;
                if (choice.Owner != Empty)
                {
                    int previous = 0;
                    for (int i = 1; i < slot; i++)
                        if (selected[i].Owner == choice.Owner && selected[i].Pocket == choice.Pocket
                            && selected[i].Id == choice.Id) previous++;
                    int count = choice.Owner == Storage ? warehouse[choice.Id] : 1;
                    if (previous >= count) continue;
                }
                selected[slot] = choice;
                Search(slot + 1, next, used, withdrawals);
            }
        }
        Search(1, default, 0, 0);
        return result;
    }

    private static EquipmentPlan? Materialize(JoiningEquipmentInventory[] bags, int target, int[] worn,
        Choice[] selected, IReadOnlyDictionary<ushort, JoiningEquipmentCatalogEntry> catalog, bool useParty,
        Func<ushort, byte, bool> canStore, Func<IReadOnlyDictionary<ushort, int>, bool> canApply)
    {
        var targetBag = bags[target];
        var holes = new List<Choice>();
        var incoming = new List<int>();
        var selectedLocal = new HashSet<int>();
        var displaced = new HashSet<int>();
        var delta = new Dictionary<ushort, int>();
        int upgrades = 0;
        for (int slot = 1; slot <= Slots; slot++)
        {
            var choice = selected[slot];
            if (choice.Owner == Empty) continue;
            if (choice.Owner == target) selectedLocal.Add(choice.Pocket);
            if (choice.Owner == target && choice.Pocket == worn[slot]) continue;
            upgrades++;
            if (worn[slot] >= 0) displaced.Add(worn[slot]);
            if (choice.Owner == target) continue;
            incoming.Add(slot);
            if (choice.Owner == Storage) Adjust(delta, choice.Id, -1);
            else holes.Add(choice);
        }
        int returns = Math.Max(0, targetBag.Count + incoming.Count - Pockets);
        // Fill free pockets first. With a full bag, exchange armor only. Prefer the
        // item just removed from its equipment slot over the player's other loose gear.
        var movable = Enumerable.Range(0, targetBag.Count)
            .Where(i => !selectedLocal.Contains(i) && (displaced.Contains(i) || targetBag.Items[i].Equipped == 0)
                && catalog.TryGetValue(targetBag.Items[i].Id, out var row) && row.IsArmor)
            .OrderByDescending(i => displaced.Contains(i)).ToArray();
        if (returns > movable.Length) return null;
        int preferredStorage = Math.Max(0, returns - holes.Count);
        int spareParty = useParty ? bags.Where((_, i) => i != target).Sum(b => Pockets - b.Count) : 0;
        int toStorage = preferredStorage;
        var stored = new List<int>();
        bool ArrangeReturns(int start)
        {
            if (stored.Count == toStorage) return delta.Count == 0 || canApply(delta);
            for (int i = start; i <= movable.Length - (toStorage - stored.Count); i++)
            {
                int pocket = movable[i];
                var item = targetBag.Items[pocket];
                if (!canStore(item.Id, item.Data)) continue;
                stored.Add(pocket);
                Adjust(delta, item.Id, 1);
                if (ArrangeReturns(i + 1)) return true;
                Adjust(delta, item.Id, -1);
                stored.RemoveAt(stored.Count - 1);
            }
            return false;
        }
        // If the warehouse cannot accept a displaced piece, use a teammate's free
        // pocket when the player enabled the party source.
        while (!ArrangeReturns(0))
            if (--toStorage < Math.Max(0, preferredStorage - spareParty)) return null;

        var copy = bags.Select(b => b.Clone()).ToArray();
        var targetCopy = copy[target];
        foreach (int old in displaced) targetCopy.Items[old].Equipped = 0;
        for (int slot = 1; slot <= Slots; slot++)
            if (selected[slot].Owner == target && selected[slot].Pocket != worn[slot])
                targetCopy.Items[selected[slot].Pocket].Equipped = (byte)slot;
        var toParty = movable.Except(stored).Take(returns - toStorage).ToArray();
        for (int i = 0; i < Math.Min(toParty.Length, holes.Count); i++)
            copy[holes[i].Owner].Items[holes[i].Pocket] = targetCopy.Items[toParty[i]].Clone();
        int nextOwner = 0;
        foreach (int pocket in toParty.Skip(holes.Count))
        {
            while (nextOwner == target || copy[nextOwner].Count == Pockets) nextOwner++;
            var receiver = copy[nextOwner];
            receiver.Items[receiver.Count++] = targetCopy.Items[pocket].Clone();
        }
        foreach (var hole in holes.Skip(toParty.Length).OrderByDescending(h => h.Pocket))
        {
            var bag = copy[hole.Owner];
            Array.Copy(bag.Items, hole.Pocket + 1, bag.Items, hole.Pocket, bag.Count - hole.Pocket - 1);
            bag.Items[--bag.Count] = new JoiningEquipmentItem();
        }
        var cleared = new Queue<int>(toParty.Concat(stored));
        foreach (int slot in incoming)
        {
            var c = selected[slot];
            var item = c.Owner == Storage ? new JoiningEquipmentItem { Id = c.Id } : bags[c.Owner].Items[c.Pocket].Clone();
            item.Equipped = (byte)slot;
            int index = cleared.Count > 0 ? cleared.Dequeue() : targetCopy.Count++;
            targetCopy.Items[index] = item;
        }
        return new EquipmentPlan(copy, delta, upgrades);
    }

    private static void Adjust(Dictionary<ushort, int> delta, ushort id, int change)
    {
        int next = delta.GetValueOrDefault(id) + change;
        if (next == 0) delta.Remove(id);
        else delta[id] = next;
    }
}
