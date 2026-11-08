extern alias GSD1;

using System;
using System.Collections.Generic;
using System.Linq;

namespace SuikodenRandomizer.AvatarPlugin;

internal static class StartingGearBalancer
{
    private static readonly object Gate = new();
    private static IReadOnlyDictionary<ushort, StartingGearCatalogEntry>? _catalog;

    internal static FixedRosterBaseSnapshot Balance(FixedRosterBaseSnapshot incoming,
        FixedRosterBaseSnapshot reference, GSD1.PLAYER_DATA donor)
    {
        if (incoming is null || reference is null || donor is null) throw new ArgumentNullException("Starting gear requires an incoming fighter, reference and donor.");
        incoming.ValidateState(); reference.ValidateState();
        var adjusted = StartingGearBalancePolicy.Apply(
            incoming.Items.Select(item => new StartingGearItem(item.Id, item.Equipped, item.Data)).ToArray(), incoming.ItemCount,
            reference.Items.Select(item => new StartingGearItem(item.Id, item.Equipped, item.Data)).ToArray(), reference.ItemCount,
            Catalog(), donor.soubi_type, donor.sex);
        foreach (string warning in adjusted.Warnings)
            CrashDiagnostics.Breadcrumb("gear.balance.fallback", "fighter=" + donor.chara_id + "; " + warning);
        return new FixedRosterBaseSnapshot
        {
            CharacterId = incoming.CharacterId, MaxHp = incoming.MaxHp, Hp = incoming.Hp,
            Exp = incoming.Exp, Status = incoming.Status, Level = incoming.Level,
            ItemCount = checked((byte)adjusted.ItemCount),
            MagicPoints = (byte[])incoming.MagicPoints.Clone(), Stats = (byte[])incoming.Stats.Clone(),
            Growth = (byte[])incoming.Growth.Clone(),
            Items = adjusted.Items.Select(item => new FixedRosterItemSnapshot { Id = item.Id, Equipped = item.Equipped, Data = item.Data }).ToArray(),
            WeaponId = incoming.WeaponId, WeaponLevel = (byte)Math.Clamp((int)reference.WeaponLevel, 1, 16),
            WeaponRunes = (byte[])incoming.WeaponRunes.Clone(),
            RuneId = incoming.RuneId, RuneLevel = incoming.RuneLevel, RuneExp = incoming.RuneExp
        };
    }

    private static IReadOnlyDictionary<ushort, StartingGearCatalogEntry> Catalog()
    {
        lock (Gate)
        {
            if (_catalog is not null) return _catalog;
            var table = GSD1.G_item_c_data.item_data_table
                ?? throw new InvalidOperationException("The native item catalog is not ready for starting gear balance.");
            if (table.Count < 2 || table.Count > ushort.MaxValue)
                throw new InvalidOperationException("The native item catalog has an unsupported size.");
            var catalog = new Dictionary<ushort, StartingGearCatalogEntry>();
            for (int id = 1; id < table.Count; id++)
            {
                var item = table[id];
                if (item is null) continue;
                var armor = item.TryCast<GSD1.BOUGU_DATA>();
                byte[]? bonuses = null;
                if (armor is not null)
                {
                    var nativeBonuses = armor.equip_data;
                    if (nativeBonuses is null || nativeBonuses.Count < 8)
                        throw new InvalidOperationException("Native armor " + id + " has incomplete stat metadata.");
                    bonuses = new byte[8];
                    for (int stat = 0; stat < bonuses.Length; stat++) bonuses[stat] = nativeBonuses[stat];
                }
                catalog.Add((ushort)id, new StartingGearCatalogEntry((ushort)id, item.nedan, item.type,
                    armor?.soubi_type ?? 0, armor?.func_no ?? 0, bonuses));
            }
            _catalog = catalog;
            return _catalog;
        }
    }
}
