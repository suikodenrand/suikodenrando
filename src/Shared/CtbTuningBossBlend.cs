using System;

namespace SuikodenRandomizer.Shared;

internal readonly record struct CtbSoloBossGuide(int RecommendedLevel, double AverageStarSpd);

internal static class CtbTuningSoloBossGuide
{
    // Equal-weight native SPD growth expectations for the 76 playable Stars, without equipment.
    // Use authored enemy IDs: the field Clay Doll and the first Neclord fight have different rules.
    internal static bool TryGet(string? sourceId, out CtbSoloBossGuide guide)
    {
        guide = sourceId switch
        {
            "va4_map_03_battle_01_monster_data_00" => new(6, 26.24850945723684),    // Clay Doll
            "vb5g_map_01_battle_01_monster_data_00" => new(17, 55.30838815789474), // Zombie Dragon
            "vc3_map_29_battle_01_monster_data_00" => new(24, 69.4378597861842),   // Gigantes
            "vc6_map_13_battle_01_monster_data_00" => new(26, 72.9762027138158),   // Dragon (Pannu)
            "ve3_map_20_battle_01_monster_data_00" => new(47, 110.12880345394737), // Castle Neclord
            "vf2_map_09_battle_01_monster_data_00" or
            "vf2_map_09_battle_01_monster_data_01" => new(51, 117.20548930921052), // Crystal Core phases
            "vs1_map_02_battle_01_monster_data_00" => new(55, 124.28217516447368), // Venus Shell
            "vs1_map_02_battle_01_monster_data_01" => new(56, 126.05134662828948), // Sonya
            "vac_map_28_battle_01_monster_data_00" => new(57, 127.82051809210526), // Ain Gide
            _ => default
        };
        return guide.RecommendedLevel > 0;
    }

    internal static (double RawSpeed, int Speed) Calculate(CtbSoloBossGuide guide, int livingPartyCount,
        double effectivePartySpd, int partyWeightPercent, double variationPercent)
    {
        if (livingPartyCount is < 1 or > 6) throw new ArgumentOutOfRangeException(nameof(livingPartyCount));
        if (!double.IsFinite(effectivePartySpd) || effectivePartySpd < livingPartyCount || effectivePartySpd > livingPartyCount * CtbTuningProfile.MaxEnemySpd)
            throw new ArgumentOutOfRangeException(nameof(effectivePartySpd));
        if (partyWeightPercent is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(partyWeightPercent));
        if (!double.IsFinite(variationPercent) || Math.Abs(variationPercent) > CtbTuningProfile.MaxVariationPercent)
            throw new ArgumentOutOfRangeException(nameof(variationPercent));

        double guidePartySpd = guide.AverageStarSpd * livingPartyCount;
        double blendedPartySpd = (effectivePartySpd * partyWeightPercent + guidePartySpd * (100 - partyWeightPercent)) / 100;
        double raw = blendedPartySpd / 3 * (1 + variationPercent / 100);
        return (raw, Math.Clamp((int)Math.Round(raw, MidpointRounding.AwayFromZero), 1, CtbTuningProfile.MaxEnemySpd));
    }
}
