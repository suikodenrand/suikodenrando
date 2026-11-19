extern alias GSD1;

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using SuikodenRandomizer.Core.Combat;
using SuikodenRandomizer.Shared;

namespace SuikodenRandomizer.S1BattlePreview;

internal static partial class ActiveBattleState
{
    private static CtbTuningProfile? _ctbTuning;
    private static (IntPtr BossActor, int Count, int TotalSpd, BossCeilingFighter?[] Fighters) _ctbBlendParty;
    private readonly record struct BossCeilingFighter(int StarId, bool DoublesBaseSpd, int ArmorSpd);
    private static CtbSoloBossGuide _ctbBlendGuide;
    private static int _ctbBlendedBossSpd;
    private static NormalCtbFormation? _ctbNormalFormation;
    private static string? _ctbNormalSkipReason;
    private sealed record NormalCtbFormation(double Multiplier, NormalCtbEnemy[] Enemies);
    private readonly record struct NormalCtbEnemy(IntPtr Definition, IntPtr Stats, string Source, int? OverrideSpd);
    private static Func<int, int>? _ctbDisplayMapper;
    private static bool _ctbStarLookupFailed;
    private static readonly CtbSpeedBinding[] CtbSpeedBindings = new CtbSpeedBinding[BattlePreviewState.SlotCount];
    private static double OtherCtbBreakDelay => (_ctbTuning?.OtherBreakPercent ?? 50) / 100d;

    // The current native definition stays alive only for this slot's encounter binding.
    private readonly record struct CtbSpeedBinding(IntPtr Actor, GSD1.MONSTER_DATA? Definition, IntPtr Stats, int NativeId, int? Speed);

    private static CtbTuningProfile LoadCtbTuning()
    {
        string path = Path.Combine(Paths.ConfigPath, CtbTuningProfile.FileName);
        try { return CtbTuningProfile.Load(path); }
        catch (Exception error)
        {
            BattlePreviewPlugin.PreviewLog?.LogWarning($"CTB is using default tuning for this encounter. Check {path}: {error.Message}");
            return new CtbTuningProfile();
        }
    }

    private static int ReadTunedCtbSpeed(GSD1.BATTLE_WORK work, int slot, GSD1.PM_DATA actor, int native, bool enemy)
    {
        var profile = _ctbTuning;
        if (profile is null) return native;
        bool blendSlot = enemy && slot == _ctbSoloBossSlot && _ctbBlendParty.Count > 0;
        var normal = enemy ? _ctbNormalFormation : null;
        int baseSpeed = enemy && slot == _ctbSoloBossSlot
            ? CtbTimeline.ApplySoloBossSpeedBonus(native, profile.SoloBossSpdBonusPercent) : native;
        if (normal is null && !blendSlot && !(enemy ? profile.HasEnemyOverrides : profile.HasStarOverrides)) return baseSpeed;

        int id;
        GSD1.MONSTER_DATA? monster = null;
        IntPtr stats = IntPtr.Zero;
        if (enemy)
        {
            var models = work.b_chara;
            if (models is null || slot >= models.Count || models[slot] is not { } model) return baseSpeed;
            id = model.chara_id;
            var table = work.monster_data_table;
            if (table is null || id < 1 || id > table.Count || (monster = table[id - 1]) is null) return baseSpeed;
            stats = monster.noryoku?.Pointer ?? IntPtr.Zero;
            if (stats == IntPtr.Zero) return baseSpeed;
            int index = slot - work.player_kazu - 1;
            if (normal is not null && (uint)index < (uint)normal.Enemies.Length)
            {
                var opening = normal.Enemies[index];
                if (monster.Pointer == opening.Definition && stats == opening.Stats)
                    return opening.OverrideSpd ?? CtbTuningNormalScaling.Apply(native, normal.Multiplier);
            }
        }
        else
        {
            id = ReadCtbStarId(work, slot);
            if (id < 0) return native;
        }

        ref var binding = ref CtbSpeedBindings[slot];
        if (binding.Actor == actor.Pointer && binding.Definition?.Pointer == monster?.Pointer
            && binding.Stats == stats && binding.NativeId == id)
            return binding.Speed ?? baseSpeed;

        int? speed = null;
        bool blended = false;
        string identity;
        try
        {
            if (monster is not null)
            {
                string? source = ReadCtbEnemyDefinitionId(monster);
                // A native phase transition may briefly leave the authored source unresolved.
                // Retry the binding on the next observation without caching the fallback.
                if (blendSlot && source is null) return baseSpeed;
                identity = source ?? throw new InvalidOperationException("the authored enemy record is unavailable");
                if (blendSlot) speed = BindCtbBossBlend(source, actor, profile);
                blended = speed.HasValue;
                speed ??= profile.GetEnemySpd(identity);
            }
            else
            {
                identity = $"Star {id}";
                speed = profile.GetStarSpd(id);
            }
            if (speed.HasValue && !blended)
                Trace($"CTB SPD override for slot {slot} {actor.name}, {identity}: native {native}, effective {speed.Value}.");
        }
        catch (Exception error)
        {
            BattlePreviewPlugin.PreviewLog?.LogWarning($"CTB could not match an individual SPD override for slot {slot} {actor.name}: {error.Message}.");
        }
        binding = new CtbSpeedBinding(actor.Pointer, monster, stats, id, speed);
        return speed ?? baseSpeed;
    }

    private static void CaptureCtbBlendParty(GSD1.BATTLE_WORK work, int bossSlot)
    {
        var fighters = new BossCeilingFighter?[work.player_kazu];
        var party = ReadOpeningCtbPartySpeed(work, fighters);
        _ctbBlendParty = (work.pm_data[bossSlot]!.Pointer, party.Count, party.TotalSpd, fighters);
        CtbSpeedBindings[bossSlot] = default; // An unfocused opening may already have sampled fixed tuning.
    }

    private static (int Count, int TotalSpd, bool Ready) ReadOpeningCtbPartySpeed(GSD1.BATTLE_WORK work, BossCeilingFighter?[]? fighters = null)
    {
        int count = 0, partySpd = 0;
        bool ready = true;
        for (int slot = 1; slot <= work.player_kazu; slot++)
        {
            var hero = work.pm_data[slot];
            if (!IsLiving(hero) || GSD1.BattleBase.dead_check(slot)) continue;
            int speed = ReadTunedCtbSpeed(work, slot, hero!, ReadSpeed(hero), false);
            if (speed <= 0) ready = false;
            partySpd += Math.Clamp(speed, 1, CtbTuningProfile.MaxEnemySpd); // Same effective range as the CTB timeline.
            if (fighters is not null) fighters[count] = ReadBossCeilingFighter(work, slot);
            count++;
        }
        return (count, partySpd, ready && count > 0);
    }

    private static BossCeilingFighter? ReadBossCeilingFighter(GSD1.BATTLE_WORK work, int slot)
    {
        try
        {
            int star = ReadCtbStarId(work, slot);
            if (star < 0 || GetHeroBase(work, slot) is not { } hero || hero.monsyo_data is not { } rune
                || hero.item is not { } items || hero.item_kazu > items.Count) return null;
            var table = GSD1.G_item_c_data.item_data_table;
            int armorSpd = 0;
            for (int i = 0; i < hero.item_kazu; i++)
            {
                var item = items[i];
                if (item is null) return null;
                if (item.soubi == 0) continue;
                if (table is null || item.item_id >= table.Count || table[item.item_id] is not { } definition) return null;
                if ((definition.type & 0x80) == 0) continue;
                var bonuses = definition.TryCast<GSD1.BOUGU_DATA>()?.equip_data;
                if (bonuses is null || bonuses.Count <= SpeedIndex) return null;
                armorSpd += bonuses[SpeedIndex];
            }
            // Native calc_noryoku doubles base SPD for rune 0x13 before adding equipped armor.
            return new BossCeilingFighter(star, rune.monsyo_id == 0x13, armorSpd);
        }
        catch (Exception error)
        {
            BattlePreviewPlugin.PreviewLog?.LogWarning($"CTB could not read boss ceiling equipment for slot {slot}: {error.Message}");
            return null;
        }
    }

    private static double? OpeningPartySpdCeiling(int level)
    {
        double total = 0;
        for (int i = 0; i < _ctbBlendParty.Count; i++)
        {
            if (_ctbBlendParty.Fighters[i] is not { } fighter
                || !CtbTuningBossCeiling.TryGetAverageSpd(fighter.StarId, level, out double mean)) return null;
            total += (fighter.DoublesBaseSpd ? 2 * mean : mean) + fighter.ArmorSpd;
        }
        return total;
    }

    private static string? CaptureNormalCtbFormation(GSD1.BATTLE_WORK work, int enemyCount)
    {
        // Use all opening enemies when finding the median, including duplicate species.
        // Publish the whole formation together so a boss, a staged fight or a partial roster
        // cannot scale some of the enemies before CTB has identified the others.
        if (enemyCount is < 1 or > 6 || enemyCount != work.pm_kazu - work.player_kazu) return "incomplete enemy roster";
        try
        {
            var party = ReadOpeningCtbPartySpeed(work);
            if (!party.Ready) return "party SPD not yet initialized";
            if (work.monster_data_table is not { } table) return "missing enemy definitions";
            var levels = new int[enemyCount];
            var enemies = new NormalCtbEnemy[enemyCount];
            for (int i = 0; i < enemyCount; i++)
            {
                int slot = work.player_kazu + i + 1;
                int id = work.b_chara[slot]?.chara_id ?? 0;
                if (id < 1 || id > table.Count || table[id - 1] is not { } monster) return $"unidentified enemy in slot {slot}";
                if ((monster.seishitsu_flg & GSD1.Game_h.SEISHITSU_BOSS) != 0) return $"boss trait in slot {slot}";
                string? source = ReadCtbEnemyDefinitionId(monster);
                if (source is null || !CtbTuningNormalScaling.TryGetEnemyLevel(source, out levels[i]))
                    return $"slot {slot} has no ordinary enemy guide ({source ?? "unidentified source"})";
                enemies[i] = new NormalCtbEnemy(monster.Pointer, monster.noryoku!.Pointer, source, _ctbTuning!.GetEnemySpd(source));
            }
            var rule = CtbTuningNormalScaling.Calculate(party.Count, party.TotalSpd, levels);
            _ctbNormalFormation = new NormalCtbFormation(rule.Multiplier, enemies);
            Trace(FormattableString.Invariant($"CTB normal enemy scaling: {party.Count} living party, total effective SPD {party.TotalSpd}, {enemyCount} enemies, median level {rule.MedianLevel:0.#}, reference Star SPD {rule.ReferenceSpd:F2}, multiplier {rule.Multiplier:F4}."));
            for (int i = 0; i < enemies.Length; i++)
            {
                int slot = work.player_kazu + i + 1;
                var actor = work.pm_data[slot];
                int native = ReadSpeed(actor);
                var entry = enemies[i];
                string effective = entry.OverrideSpd.HasValue ? $"fixed SPD {entry.OverrideSpd.Value}"
                    : native > 0 ? $"effective SPD {CtbTuningNormalScaling.Apply(native, rule.Multiplier)}" : "waiting for native SPD";
                Trace($"CTB normal enemy slot {slot} {actor?.name}, {entry.Source}: native SPD {native}, {effective}.");
            }
            return null;
        }
        catch (Exception error)
        {
            _ctbNormalFormation = null;
            return error.Message;
        }
    }

    private static int? BindCtbBossBlend(string source, GSD1.PM_DATA actor, CtbTuningProfile profile)
    {
        bool guided = CtbTuningSoloBossGuide.TryGet(source, out var guide);
        // The phase-two Crystal Core has the same guide. An unrelated replacement in this slot keeps its own tuning.
        if (_ctbBlendedBossSpd > 0) return guided && guide == _ctbBlendGuide ? _ctbBlendedBossSpd : null;
        if (actor.Pointer != _ctbBlendParty.BossActor) return null;
        if (!guided)
        {
            _ctbBlendParty = default;
            Trace($"CTB solo boss blend skipped for {source}: no recommended-level guide. Using fixed tuning.");
            return null;
        }
        double variation = profile.SoloBossVariationPercent == 0 ? 0
            : (2 * System.Random.Shared.NextDouble() - 1) * profile.SoloBossVariationPercent;
        int referenceLevel = CtbTuningBossCeiling.ReferenceLevel(guide.RecommendedLevel);
        double? ceiling = OpeningPartySpdCeiling(referenceLevel);
        double partyInput = _ctbBlendParty.TotalSpd;
        if (ceiling.HasValue)
        {
            partyInput = Math.Min(partyInput, ceiling.Value);
            Trace(FormattableString.Invariant($"CTB solo boss party ceiling: reference level {referenceLevel}, natural equipped total {ceiling:F2}, opening total {_ctbBlendParty.TotalSpd}, party SPD used {partyInput:F2}."));
        }
        else Trace("CTB solo boss party ceiling unavailable: an opening fighter's growth or equipment could not be resolved. Preserving the uncapped blend.");
        var blend = CtbTuningSoloBossGuide.Calculate(guide, _ctbBlendParty.Count, partyInput, profile.SoloBossPartyWeightPercent, variation);
        _ctbBlendedBossSpd = blend.Speed;
        _ctbBlendGuide = guide;
        Trace(FormattableString.Invariant($"CTB solo boss blend for slot {_ctbSoloBossSlot} {actor.name}, {source}: {_ctbBlendParty.Count} living party, total effective SPD {_ctbBlendParty.TotalSpd}, guide level {guide.RecommendedLevel} average SPD {guide.AverageStarSpd:F2}, party weight {profile.SoloBossPartyWeightPercent}%, variation {variation:+0.00;-0.00;0.00}%, raw SPD {blend.RawSpeed:F2}, effective SPD {blend.Speed}."));
        return blend.Speed;
    }

    private static string? ReadCtbEnemyDefinitionId(GSD1.MONSTER_DATA monster)
    {
        // The native converter assigns the authored stat array itself to the live definition.
        // Crystal Core can reorder the converted rows, so follow the array, not the original index.
        var sources = GSD1.OldSrcBase.game_work?.battle_data?.battle_ground?.monster_data;
        if (sources is null || monster.noryoku is not { } stats || stats.Pointer == IntPtr.Zero) return null;
        string? id = null;
        for (int index = 0; index < sources.Count; index++)
        {
            var source = sources[index];
            if (source?.noryoku?.Pointer != stats.Pointer) continue;
            string candidate = source.id;
            if (string.IsNullOrEmpty(candidate) || (id is not null && id != candidate)) return null;
            id = candidate;
        }
        return id;
    }

    private static int ReadCtbStarId(GSD1.BATTLE_WORK work, int slot)
    {
        if (_ctbStarLookupFailed || slot < 1 || slot > work.player_kazu) return -1;
        var party = work.p_battle_data;
        var models = work.b_chara;
        if (party is null || slot >= party.Count || party[slot]?.player_data is not { } player
            || models is null || slot >= models.Count || models[slot] is not { } model
            || model.chara_id != player.chara_id || player.player_base?.chara_no != player.chara_id) return -1;
        try { return CtbDisplayId(player.chara_id); }
        catch (Exception error)
        {
            _ctbStarLookupFailed = true;
            BattlePreviewPlugin.PreviewLog?.LogWarning("CTB could not identify the displayed Star: " + error.Message);
            return -1;
        }
    }

    private static int CtbDisplayId(int role)
    {
        _ctbDisplayMapper ??= BindCtbDisplayMapper();
        return _ctbDisplayMapper(role);
    }

    private static Func<int, int> BindCtbDisplayMapper()
    {
        // Avatar Shuffle is optional. Its runtime owns both fixed-roster and campaign donor identities.
        Type? type = AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType("SuikodenRandomizer.AvatarPlugin.AvatarRuntime", false))
            .FirstOrDefault(candidate => candidate is not null);
        if (type is null) return static role => role;
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Public;
        var current = type.GetProperty("Current", flags | BindingFlags.Static)
            ?? throw new MissingMemberException(type.FullName, "Current");
        if (current.GetValue(null) is not { } runtime) return static role => role;
        var method = type.GetMethod("MapDisplayId", flags | BindingFlags.Instance)
            ?? throw new MissingMethodException(type.FullName, "MapDisplayId");
        return method.CreateDelegate<Func<int, int>>(runtime);
    }
}
