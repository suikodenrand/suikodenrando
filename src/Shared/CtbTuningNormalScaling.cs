using System;
using System.Collections.Generic;
using System.Linq;

namespace SuikodenRandomizer.Shared;

internal static class CtbTuningNormalScaling
{
    // Native growth, levels 1–99. Each level is the equal-weight mean of the 76 combat
    // Stars' expected unequipped SPD, including the native 255 cap on each growth outcome.
    private static readonly double[] AverageStarSpd =
    {
        13.039473684210526, 15.68128083881579, 18.32308799342105, 20.964895148026315, 23.60670230263158,
        26.248509457236842, 28.890316611842106, 31.53212376644737, 34.17393092105263, 36.8157380756579,
        39.45754523026316, 42.09935238486842, 44.741159539473685, 47.382966694078945, 50.02477384868421,
        52.66658100328947, 55.30838815789474, 57.9501953125, 60.59200246710526, 62.361173930921055,
        64.13034539473684, 65.89951685855263, 67.66868832236842, 69.4378597861842, 71.20703125,
        72.9762027138158, 74.74537417763158, 76.51454564144737, 78.28371710526316, 80.05288856907895,
        81.82206003289474, 83.59123149671052, 85.36040296052632, 87.12957442434211, 88.89874588815789,
        90.66791735197368, 92.43708881578948, 94.20626027960526, 95.97543174342105, 97.74460320723684,
        99.51377467105263, 101.28294613486842, 103.0521175986842, 104.8212890625, 106.5904605263158,
        108.35963199013158, 110.12880345394737, 111.89797491776316, 113.66714638157895, 115.43631784539474,
        117.20548930921052, 118.97466077302632, 120.74383223684211, 122.51300370065789, 124.28217516447368,
        126.05134662828948, 127.82051809210526, 129.58968955592104, 131.35886101973685, 131.97866981907896,
        132.59847861842104, 133.21828741776315, 133.83809621710526, 134.45790501644737, 135.07771381578948,
        135.6975226151316, 136.31733141447367, 136.93714021381578, 137.5569490131579, 138.1767578125,
        138.7965666118421, 139.41637541118422, 140.03618421052633, 140.6559930098684, 141.27580180921052,
        141.89561060855263, 142.51541940789474, 143.13522820723685, 143.75503700657896, 144.37484580592104,
        144.99465460526315, 145.61446340460526, 146.23427220394734, 146.8540810032892, 147.4738898026285,
        148.09369860194695, 148.71350740112106, 149.33331619944008, 149.95312499335512, 150.57293376730945,
        151.19274246085945, 151.81255086423093, 152.4323583229617, 153.05216299190982, 153.67196014961675,
        154.29173879134996, 154.91147548541923, 155.5311245497301, 156.15060429817575,
    };

    private static readonly Dictionary<string, int> EnemyLevels = CtbTuningCatalog.Enemies
        .Where(enemy => IsRandomEncounterSource(enemy.SourceId))
        .ToDictionary(enemy => enemy.SourceId, enemy => enemy.Level, StringComparer.Ordinal);

    // The supported game's regular encounter formations all have party_flg 100.
    // Several story formations reuse enemy definitions from the random encounter tables.
    internal static bool IsOrdinaryFormation(int partyFlags) => partyFlags == 100;

    private static bool IsRandomEncounterSource(string sourceId)
    {
        // Verified against all 417 authored formations. Most field/dungeon encounters use
        // battle_00 definitions. The underground factory also reuses three battle_02 rows.
        return sourceId switch
        {
            "vb3_map_05_battle_02_monster_data_00" or "vb3_map_05_battle_02_monster_data_01" or
            "vb3_map_05_battle_02_monster_data_02" => true,
            // The soldiers below and all three Hydra heads occur only in scripted battles.
            "vb6_map_01_battle_00_monster_data_04" or "vd5_map_01_battle_00_monster_data_04" or
            "vd5_map_01_battle_00_monster_data_05" => false,
            _ => sourceId.Contains("_battle_00_monster_data_", StringComparison.Ordinal)
                && !sourceId.StartsWith("vzv_map_00_battle_00_", StringComparison.Ordinal)
        };
    }

    internal static bool TryGetEnemyLevel(string? sourceId, out int level)
    {
        level = 0;
        return sourceId is not null && EnemyLevels.TryGetValue(sourceId, out level);
    }

    internal static (double MedianLevel, double ReferenceSpd, double Multiplier) Calculate(
        int partyCount, int partyTotalSpd, int[] enemyLevels)
    {
        if (partyCount is < 1 or > 6) throw new ArgumentOutOfRangeException(nameof(partyCount));
        if (partyTotalSpd < partyCount || partyTotalSpd > partyCount * CtbTuningProfile.MaxEnemySpd)
            throw new ArgumentOutOfRangeException(nameof(partyTotalSpd));
        if (enemyLevels is not { Length: >= 1 and <= 6 } || enemyLevels.Any(level => level < 1 || level > AverageStarSpd.Length))
            throw new ArgumentOutOfRangeException(nameof(enemyLevels));

        var levels = (int[])enemyLevels.Clone();
        Array.Sort(levels);
        double median = (levels[(levels.Length - 1) / 2] + levels[levels.Length / 2]) / 2d;
        double referenceSpd = (AverageStarSpd[(int)Math.Floor(median) - 1] + AverageStarSpd[(int)Math.Ceiling(median) - 1]) / 2;
        double partyMean = (double)partyTotalSpd / partyCount;
        double multiplier = Math.Clamp(1 + 0.5 * (partyMean / referenceSpd - 1), 0.90, 1.25);
        return (median, referenceSpd, multiplier);
    }

    internal static int Apply(int native, double multiplier)
    {
        if (!double.IsFinite(multiplier) || multiplier is < 0.9 or > 1.25)
            throw new ArgumentOutOfRangeException(nameof(multiplier));
        if (native <= 0) return native;
        return (int)Math.Clamp(Math.Round(native * multiplier, MidpointRounding.AwayFromZero), 1, CtbTuningProfile.MaxEnemySpd);
    }
}
