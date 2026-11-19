using BepInEx.Configuration;

namespace SuikodenRandomizer.AvatarPlugin;

internal static class S1QualityOfLife
{
    internal static bool WarehouseMenu { get; private set; } = true;
    internal static bool SharedBattleItems { get; private set; } = true;
    internal static bool OptimizeEquipment { get; private set; } = true;
    internal static bool MatchViktorStarterLevels { get; private set; } = true;

    internal static void Configure(ConfigFile config)
    {
        const string section = "QualityOfLife";
        WarehouseMenu = config.Bind(section, nameof(WarehouseMenu), true, "Show Roc's Warehouse in the field Items menu after recruitment.").Value;
        SharedBattleItems = config.Bind(section, nameof(SharedBattleItems), true, "Let each fighter use battle items carried by living active teammates.").Value;
        OptimizeEquipment = config.Bind(section, nameof(OptimizeEquipment), true, "Show Optimize on the field Equipment screen.").Value;
        MatchViktorStarterLevels = config.Bind(section, nameof(MatchViktorStarterLevels), true, "Level the two starting companions to Tir on a new Viktor quick start.").Value;
    }
}
