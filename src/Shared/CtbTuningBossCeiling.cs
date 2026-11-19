using System;
using System.Collections.Generic;

namespace SuikodenRandomizer.Shared;

internal static class CtbTuningBossCeiling
{
    // Original Default_c SPD growth code, indexed by fighter ID. Zero marks a noncombat Star.
    // Initialization data SHA-256: 70f2f8827070edfafcc6f9df9f5ffe516961f43596ee5f878d906aeb7936bb2a.
    private static readonly byte[] GrowthCodes =
    {
        6, 6, 3, 5, 6, 4, 2, 4, 7, 6, // 0–9
        7, 5, 3, 4, 5, 8, 3, 6, 4, 6,
        4, 2, 7, 8, 0, 2, 5, 0, 4, 3,
        4, 6, 5, 5, 0, 5, 3, 3, 6, 4,
        3, 4, 3, 6, 0, 5, 0, 3, 4, 5,
        0, 6, 2, 6, 3, 6, 5, 0, 0, 3,
        0, 4, 6, 5, 5, 1, 2, 3, 5, 4,
        4, 5, 4, 3, 1, 8, 4, 0, 0, 0,
        3, 3, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 2, 3, 0, 0, 4, 0, 0, 0, 0,
        0, 4, 5, 0, 0, 0, 0, 3, 6, 8, // 100–109, including Ted and Odessa
    };

    // Each row is the native fixed-point gain for destination levels 2–19, 20–59 and 60–99.
    // All 78 combatants' original SPD codes are in the range 1–8.
    private static readonly (int Early, int Middle, int Late)[] GrowthGains =
    {
        (336, 224, 124), (431, 288, 144), (525, 364, 144), (646, 435, 144),
        (741, 499, 157), (835, 563, 177), (970, 614, 216), (1118, 672, 249),
    };

    private static readonly Dictionary<int, double[]> Averages = BuildAverages();

    internal static int ReferenceLevel(int recommendedLevel)
    {
        if (recommendedLevel is < 1 or > 99) throw new ArgumentOutOfRangeException(nameof(recommendedLevel));
        return Math.Min(99, recommendedLevel + 4);
    }

    internal static bool TryGetAverageSpd(int fighterId, int level, out double averageSpd)
    {
        averageSpd = 0;
        if (level is < 1 or > 99 || !Averages.TryGetValue(fighterId, out var curve)) return false;
        averageSpd = curve[level - 1];
        return true;
    }

    private static Dictionary<int, double[]> BuildAverages()
    {
        var averages = new Dictionary<int, double[]>();
        foreach (var star in CtbTuningCatalog.Stars)
            if (star.BaseSpd is int start) averages.Add(star.Id, BuildCurve(start, GrowthCodes[star.Id]));
        averages.Add(108, BuildCurve(15, GrowthCodes[108])); // Ted
        averages.Add(109, BuildCurve(18, GrowthCodes[109])); // Odessa
        return averages;
    }

    private static double[] BuildCurve(int start, int code)
    {
        var means = new double[99];
        means[0] = start;
        var probability = new double[256];
        var next = new double[256];
        probability[start] = 1;
        var gains = GrowthGains[code - 1];
        int maximumPossible = start;
        for (int level = 2; level <= 99; level++)
        {
            int coefficient = level < 20 ? gains.Early : level < 60 ? gains.Middle : gains.Late;
            int wholeGain = coefficient / 256;
            double chanceOfExtraPoint = (coefficient % 256) / 256d;
            Array.Clear(next, 0, next.Length);
            // g1_level_up saturates each individual roll at 255 before the next level.
            for (int value = 0; value < probability.Length; value++)
            {
                int lower = Math.Min(255, value + wholeGain);
                int upper = Math.Min(255, lower + 1);
                next[lower] += probability[value] * (1 - chanceOfExtraPoint);
                next[upper] += probability[value] * chanceOfExtraPoint;
            }
            // Preserve the exact fixed-point expectation before any roll can exceed the cap.
            maximumPossible += (coefficient + 255) / 256;
            double mean = means[level - 2] + coefficient / 256d;
            if (maximumPossible > 255)
            {
                mean = 0;
                for (int value = 1; value < next.Length; value++) mean += value * next[value];
            }
            means[level - 1] = mean;
            (probability, next) = (next, probability);
        }
        return means;
    }
}
