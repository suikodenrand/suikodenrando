using SuikodenRandomizer.Shared;

internal static class BossCeilingTests
{
    internal static void Run()
    {
        void Check(bool value, string message)
        {
            if (!value) throw new InvalidOperationException(message);
        }
        double Average(int id, int level)
        {
            Check(CtbTuningBossCeiling.TryGetAverageSpd(id, level, out var value), $"Missing fighter {id}, level {level}.");
            return value;
        }

        int shellLevel = CtbTuningBossCeiling.ReferenceLevel(55);
        int dragonLevel = CtbTuningBossCeiling.ReferenceLevel(17);
        Check(shellLevel == 59 && dragonLevel == 21 && CtbTuningBossCeiling.ReferenceLevel(99) == 99,
            "The regional ceiling uses four levels above the guide, up to level 99.");
        Check(!CtbTuningBossCeiling.TryGetAverageSpd(27, shellLevel, out _) &&
              !CtbTuningBossCeiling.TryGetAverageSpd(-1, shellLevel, out _) &&
              !CtbTuningBossCeiling.TryGetAverageSpd(8, 100, out _), "Invalid ceiling reference accepted.");
        foreach (var star in CtbTuningCatalog.Stars)
        {
            bool found = CtbTuningBossCeiling.TryGetAverageSpd(star.Id, 1, out double mean);
            Check(found == star.BaseSpd.HasValue && (!found || mean == star.BaseSpd), $"Growth roster mismatch for {star.Name}.");
        }
        Check(Average(108, shellLevel) == 161.6796875 && Average(109, shellLevel) == 201.609375,
            "Playable guests must have their own growth curves.");
        Check(Math.Abs(Average(23, 99) - 246.501026163555) < 1e-10, "Maximum-level Kasumi lost per-roll SPD saturation.");
        int[] shellParty = { 8, 35, 6, 1, 23, 66 };
        double shellCeiling = shellParty.Sum(id => Average(id, shellLevel)) + 60;
        Check(shellCeiling == 914.109375, "Venus Shell party ceiling must include gear once and preserve fractions.");
        int[] dragonParty = { 8, 2, 35, 1, 3, 32 };
        Check(dragonParty.Sum(id => Average(id, dragonLevel)) + 20 == 434.8671875, "Zombie Dragon party ceiling differs from its regional growth data.");
    }
}
