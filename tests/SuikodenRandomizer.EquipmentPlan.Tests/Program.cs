using SuikodenRandomizer.AvatarPlugin;

internal static class Program
{
    private static readonly byte[] Types = { 0, 0x80, 0xa0, 0xc0, 0xe0, 0xe0 };
    private const ushort Medicine = 25;
    private static readonly Dictionary<ushort, JoiningEquipmentCatalogEntry> Rows = new();
    private static JoiningEquipmentItem Item(ushort id, byte equipped = 0, byte data = 0)
        => new() { Id = id, Equipped = equipped, Data = data };
    private static JoiningEquipmentInventory Bag(params JoiningEquipmentItem[] items) => new()
    {
        Count = checked((byte)items.Length), Items = items.Concat(Enumerable.Range(0, 9 - items.Length)
            .Select(_ => Item(0))).ToArray()
    };
    private static JoiningEquipmentInventory Full(params JoiningEquipmentItem[] items) =>
        Bag(items.Concat(Enumerable.Range(0, 9 - items.Length).Select(_ => Item(Medicine, data: 3))).ToArray());
    private static void Add(ushort id, int slot, byte defense, byte stat = 0)
    {
        var bonuses = new byte[8];
        bonuses[0] = stat; bonuses[7] = defense;
        Rows.Add(id, new(id, 0, Types[slot], 255, 0, bonuses));
    }
    private static void Check(bool value, string message)
    {
        if (!value) throw new Exception(message);
    }
    private static EquipmentPlan Plan(JoiningEquipmentInventory[] bags, Dictionary<ushort, int>? stock = null,
        bool party = true, Func<IReadOnlyDictionary<ushort, int>, bool>? capacity = null)
    {
        stock ??= new();
        return EquipmentPlan.Create(bags, 0, Rows, Types, id => id != 44, stock, party,
            (id, data) => data == 0 && Rows.ContainsKey(id),
            delta => delta.All(p => stock.GetValueOrDefault(p.Key) + p.Value >= 0) && (capacity?.Invoke(delta) ?? true));
    }
    private static ushort Worn(JoiningEquipmentInventory b, int slot) => b.Items.Take(b.Count)
        .SingleOrDefault(i => (i.Equipped & 0x7f) == slot)?.Id ?? 0;
    private static void Conserve(JoiningEquipmentInventory[] before, EquipmentPlan plan)
    {
        static Dictionary<(ushort, byte),int> Counts(JoiningEquipmentInventory[] bags) => bags
            .SelectMany(b => b.Items.Take(b.Count)).GroupBy(i => (i.Id, i.Data)).ToDictionary(g => g.Key, g => g.Count());
        var totals = Counts(plan.Bags);
        foreach (var (id, change) in plan.WarehouseDelta)
            totals[(id,0)] = totals.GetValueOrDefault((id,(byte)0)) + change;
        var expected = Counts(before);
        Check(expected.Keys.Union(totals.Keys).All(k => expected.GetValueOrDefault(k) == totals.GetValueOrDefault(k)),
            "Gear or carried data was lost or duplicated");
    }

    private static void Main()
    {
        Add(1,1,1); Add(2,1,20); Add(3,1,20,9); Add(4,1,15); Add(44,1,99);
        Add(5,2,2); Add(6,2,10); Add(7,2,18); Add(8,3,11);
        Add(9,4,1); Add(10,4,5); Add(11,4,9); Add(12,4,0,8); Add(13,4,10);
        var bags = new[] { Bag(Item(1,1), Item(4), Item(44)), Bag(Item(2), Item(3,1), Item(6,0,11)) };
        var wallet = new Dictionary<ushort,int> { [7] = 1 };
        var plan = Plan(bags, wallet);
        Check(Worn(plan.Bags[0],1) == 2 && Worn(plan.Bags[0],2) == 7 && plan.Upgrades == 2 &&
            plan.Bags[1].Items[0].Equipped == 1 && plan.Bags[1].Items[0].Id == 3, "Missed one of the sources or took worn armor");
        Check(plan.Bags[0].Items[0].Id == 1 && plan.Bags[0].Items[0].Equipped == 0, "Displaced gear did not stay in free pockets");
        Check(bags[0].Count == 3 && bags[0].Items[0].Equipped == 1 && bags[1].Items[0].Id == 2,
            "The planning pass wrote into its inputs");
        Conserve(bags, plan);
        Check(Worn(Plan(bags, party:false).Bags[0],1)==4 && Worn(Plan(bags, party:false).Bags[0],2)==0, "Personal source leaked");
        Check(Worn(Plan(bags,wallet,party:false).Bags[0],1)==4 && Worn(Plan(bags,wallet,party:false).Bags[0],2)==7,
            "Personal plus warehouse toggles were ignored");
        Check(Worn(Plan(bags).Bags[0],2)==6, "Loose party armor unavailable without warehouse");

        bags = new[] { Bag(Item(1,129), Item(5,130), Item(12,4)), Bag(Item(2), Item(7)) };
        plan = Plan(bags,new(){[11]=2});
        Check(plan.Bags[0].Items[0].Equipped==129 && plan.Bags[0].Items[1].Equipped==130 &&
            Worn(plan.Bags[0],4)==11 && Worn(plan.Bags[0],5)==11 && plan.WarehouseDelta[11]==-2,
            "Locked slot, stock quantity or duplicate accessory handling failed");
        Conserve(bags,plan);
        plan = Plan(new[] {Bag(Item(3,1),Item(2),Item(12,4)),Bag(Item(11,0x84))},new(){[12]=1});
        Check(Worn(plan.Bags[0],1)==3 && Worn(plan.Bags[0],4)==12 && Worn(plan.Bags[0],5)==12,
            "Existing gear lost a defense tie, a locked teammate was stripped, or a utility accessory was ignored");
        Check(!Plan(plan.Bags).Changed, "Repeat optimization unnecessarily replaced gear");

        bags = new[]{ Full(Item(1,1,6)), Full(Item(2,0,9)) };
        plan=Plan(bags);
        Check(Worn(plan.Bags[0],1)==2 && plan.Bags[0].Items[0].Data==9 && plan.Bags[1].Items[0].Id==1
            && plan.Bags[1].Items[0].Data==6 && plan.Bags[1].Items[0].Equipped==0, "Full bags failed to exchange original item records");
        Conserve(bags,plan);
        bags=new[]{Full(Item(1,1)),Full()};
        plan=Plan(bags,new(){[2]=1});
        Check(Worn(plan.Bags[0],1)==2 && plan.WarehouseDelta[1]==1 && plan.WarehouseDelta[2]==-1,
            "Full target did not exchange armor with the warehouse");
        Conserve(bags,plan);
        Check(!Plan(bags,new(){[2]=1},capacity:d=>!d.ContainsKey(1)).Changed,"Deposited an item beyond a bucket limit");
        bags=new[]{Full(Item(1,1,9)),Bag()};
        plan=Plan(bags,new(){[2]=1});
        Check(plan.Bags[1].Count==1 && plan.Bags[1].Items[0].Data==9 && plan.WarehouseDelta.Count==1,
            "A warehouse withdrawal lost the displaced armor's data instead of using free party space");
        Check(!Plan(bags,new(){[2]=1},party:false).Changed,"Warehouse-only selection moved armor into a teammate pocket");
        Conserve(bags,plan);
        bags=new[]{Full(),Bag(Item(2))};
        Check(!Plan(bags,new(){[7]=1}).Changed,"Full consumable bag was overwritten");
        bags=new[]{Full(Item(9)),Full()};
        plan=Plan(bags,new(){[2]=1,[7]=1});
        Check(Worn(plan.Bags[0],1)==2 && Worn(plan.Bags[0],2)==0,"Single movable pocket was spent on a weaker global combination");
        Conserve(bags,plan);
        bags=new[]{Bag(Item(177,2),Item(1,1)),Bag(Item(2),Item(7))};
        plan=Plan(bags);
        Check(Worn(plan.Bags[0],2)==177 && Worn(plan.Bags[0],1)==2, "Unrecognized worn gear was moved");
        var random = new Random(23);
        for (int iteration = 0; iteration < 200; iteration++)
        {
            var ids = Rows.Keys.ToArray();
            JoiningEquipmentItem Loose() => Item(ids[random.Next(ids.Length)]);
            bags = new[] { Bag(Loose(), Loose()), Bag(Loose(), Loose(), Loose(), Item(13,4)), Bag(Loose(), Loose(), Loose()) };
            var stock = new Dictionary<ushort,int>();
            for (int i = 0; i < 8; i++)
            {
                ushort id = Loose().Id;
                stock[id] = stock.GetValueOrDefault(id) + 1;
            }
            var available = bags.SelectMany(b => b.Items.Take(b.Count)).Where(item => item.Equipped == 0).Select(item => item.Id)
                .Concat(stock.SelectMany(kv => Enumerable.Repeat(kv.Key, kv.Value))).Where(id => id != 44);
            int expectedDefense = available.GroupBy(id => Rows[id].Type & 0xe0)
                .Sum(group => group.Select(id => (int)Rows[id].Bonuses![7]).OrderByDescending(n => n)
                    .Take(group.Key == Types[4] ? 2 : 1).Sum());
            plan = Plan(bags,stock);
            var optimized = plan.Bags[0];
            int actualDefense = optimized.Items.Take(optimized.Count).Where(item => item.Equipped != 0).Sum(item => Rows[item.Id].Bonuses![7]);
            Check(actualDefense == expectedDefense, "Failed to find the strongest compatible combination in a generated party");
            Conserve(bags,plan);
            foreach (var (id, d) in plan.WarehouseDelta) stock[id] = stock.GetValueOrDefault(id) + d;
            Check(!Plan(plan.Bags,stock).Changed, "A second pass disturbed an already optimized party");
        }
        Console.WriteLine("Equipment planner checks passed for all sources, wearability, ties, locks, full pockets, stock limits and 200 generated gear combinations.");
    }
}
