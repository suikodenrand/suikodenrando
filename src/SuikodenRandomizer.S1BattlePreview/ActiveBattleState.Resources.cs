extern alias GSD1;

using System;
using System.Collections.Generic;

namespace SuikodenRandomizer.S1BattlePreview;

internal sealed record HeroResourceOption(HeroCommandResource Resource, string Name, int? Uses, HeroCommandTarget[] Targets);

internal static partial class ActiveBattleState
{
    private const int Rune = 2;
    private const int Item = 3;
    private const int BattleItemFlag = 4;
    private const int RuneCommandBlocked = 0xE0;

    private static GSD1.PLAYER_BASE? GetHeroBase(GSD1.BATTLE_WORK work, int slot)
        => work.p_battle_data is { } party && slot < party.Count ? party[slot]?.player_data?.player_base : null;

    private static HeroResourceOption[] ReadRunes(GSD1.BATTLE_WORK work, int slot, GSD1.PM_DATA actor)
    {
        if ((actor.status & RuneCommandBlocked) != 0 || GetHeroBase(work, slot) is not { } hero
            || hero.monsyo_data is not { monsyo_id: > 0 } equipped
            || GSD1.G_monsyo_c_data.monsyo_data_table is not { } runes || equipped.monsyo_id >= runes.Count
            || runes[equipped.monsyo_id] is not { } rune || rune.type is not (0 or 1)
            || rune.magic is not { } spells)
            return Array.Empty<HeroResourceOption>();

        // Native availability mode calculates unlocked tiers without opening a window.
        int unlocked = GSD1.BattleBase.make_magic_window(slot, false);
        if (unlocked <= 0) return Array.Empty<HeroResourceOption>();

        bool commandRune = rune.type == 1;
        var table = commandRune ? GSD1.G_monsyo_c_data.tokusyu_kogeki_table : GSD1.BattleBase.magic_data_table;
        if (table is null) return Array.Empty<HeroResourceOption>();
        var options = new List<HeroResourceOption>();
        var charges = hero.magic_point;
        int first = commandRune ? 0 : 1;
        int last = commandRune ? 0 : Math.Min(unlocked, 4);
        for (int tier = first; tier <= last && tier < spells.Count; tier++)
        {
            int spellId = spells[tier];
            if (spellId == 0 || spellId >= table.Count || table[spellId] is not { magic_func: not null } spell)
                continue;
            if (!commandRune && (spellId == 6 || charges is null || tier >= charges.Count || charges[tier] == 0
                || (tier == 4 && HasOtherQueuedLevelFourSpell(work, slot))))
                continue;
            if (!TryReadEffectTargets(work, spell.type, out var targets)) continue;

            int? uses = commandRune ? null : charges![tier];
            string name = ReadResourceName(spell.name, commandRune ? "Rune action" : $"Level {tier} spell");
            options.Add(new HeroResourceOption(new HeroCommandResource(tier, spellId, equipped.monsyo_id), name, uses, targets));
        }
        return options.ToArray();
    }

    private static HeroResourceOption[] ReadItems(GSD1.BATTLE_WORK work, int slot)
    {
        if (GetHeroBase(work, slot) is not { item: { } items } hero
            || GSD1.G_item_c_data.item_data_table is not { } table)
            return Array.Empty<HeroResourceOption>();

        var options = new List<HeroResourceOption>();
        for (int index = 0; index < hero.item_kazu && index < items.Count; index++)
        {
            var item = items[index];
            if (item is null || item.item_id == 0 || item.item_id >= table.Count
                || table[item.item_id] is not { } row || (row.type & BattleItemFlag) == 0
                || row.TryCast<GSD1.ITEM_DATA>() is not { item_func: not null } effect
                || (item.data == 0 && effect.times != 0)
                || !TryReadEffectTargets(work, row.type, out var targets))
                continue;

            int? uses = item.data == 0 ? null : item.data;
            string name = ReadResourceName(unchecked((int)row.name), $"Item {item.item_id}");
            options.Add(new HeroResourceOption(new HeroCommandResource(index, item.item_id), name, uses, targets));
        }
        return options.ToArray();
    }

    private static bool TryReadEffectTargets(GSD1.BATTLE_WORK work, int flags, out HeroCommandTarget[] targets)
    {
        targets = Array.Empty<HeroCommandTarget>();
        int targetMode = flags & 3;
        if (targetMode < 2) return true;
        if (work.pm_data is not { } rows) return false;

        bool allies = targetMode == 2;
        int first = allies ? 1 : work.player_kazu + 1;
        int last = allies ? work.player_kazu : work.pm_kazu;
        var living = new List<HeroCommandTarget>();
        for (int slot = first; slot <= last && slot < rows.Count; slot++)
        {
            var target = rows[slot];
            if (!IsLiving(target) || GSD1.BattleBase.dead_check(slot)) continue;
            living.Add(new HeroCommandTarget(slot, target!.Pointer, target.name ?? $"Slot {slot}"));
        }
        targets = living.ToArray();
        return targets.Length > 0;
    }

    private static bool HasOtherQueuedLevelFourSpell(GSD1.BATTLE_WORK work, int slot)
    {
        if (work.pm_data is not { } rows) return true;
        for (int other = 1; other <= work.player_kazu && other < rows.Count; other++)
            if (other != slot && rows[other] is { finished_flg: 0, c_koudou: Rune, c_item: 4 })
                return true;
        return false;
    }

    private static string ReadResourceName(int id, string fallback)
    {
        try
        {
            string? name = GSD1.G1_string_id_h.G1_SID_MSG(id);
            return string.IsNullOrWhiteSpace(name) ? fallback : name;
        }
        catch (Exception) { return fallback; }
    }
}
