using System;
using BepInEx.Configuration;
using BepInEx.Logging;

// These facades preserve the stable engines' config/API names, but are not BepInEx plugins.
namespace SuikodenRandomizer.AvatarPlugin
{
    internal static class AvatarPlugin
    {
        public const string PluginGuid = "agilen.suikoden.recruitment_randomizer";
        internal const string DisplayName = "Stars of Destiny - Suikoden I";
        internal const string Version = "0.8.7";
        internal static ManualLogSource PluginLog { get; private set; } = null!;
        internal static string ConfiguredSeed { get; private set; } = "agilen-080";
        internal static string ConfiguredMode { get; private set; } = "Shuffle";
        internal static string ConfiguredCompanions { get; private set; } = "";
        internal static string ConfiguredJoiningWeaponLevel { get; private set; } = "TirVariance";
        internal static string ConfiguredJoiningEquipment { get; private set; } = "MatchRole";
        internal static bool ConfiguredIncludeTir { get; private set; }
        internal static bool ConfiguredTreasureShuffle { get; private set; }
        internal static bool ConfiguredTreasureEnabled { get; private set; }
        internal static bool SkipToViktor { get; private set; }
        internal static void Configure(ConfigFile config, ManualLogSource log)
        {
            PluginLog = log;
            AvatarTalismanPreview.Configure(config.Bind("Battle", "ShuffledTalismanPreview", false,
                "Experimental native turn-based Talisman for the actual Gremio and Pahn avatars. ATB and other shuffled Unites are excluded.").Value);
            AvatarShuffledUnites.Configure(config.Bind("Battle", "ShuffledUnitesPreview", false,
                "Experimental native turn-based support for all 32 S1 character-Unite recipes. Uses the actual displayed fighters; overrides Talisman-only mode. ATB is excluded.").Value);
            ConfiguredSeed = config.Bind("Shuffle", "Seed", "agilen-080", "New Suikoden I campaigns; saved campaigns retain their profile.").Value;
            ConfiguredMode = config.Bind("Shuffle", "Mode", "Shuffle", "Shuffle, FixedChosen or FixedRandom.").Value.Trim();
            ConfiguredCompanions = config.Bind("Shuffle", "Companions", "", "Five distinct combat companion IDs for FixedChosen.").Value;
            ConfiguredJoiningWeaponLevel = config.Bind("Shuffle", "JoiningWeaponLevel", "TirVariance",
                "New campaigns only: TirVariance uses Tir's current weapon level with a seeded -3 to +3 offset at first join; MatchRole uses the original role; Level1 starts at 1. Saved campaigns retain their policy and upgrades.").Value.Trim();
            if (!string.Equals(ConfiguredJoiningWeaponLevel, "TirVariance", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(ConfiguredJoiningWeaponLevel, "MatchRole", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(ConfiguredJoiningWeaponLevel, "Level1", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("S1 JoiningWeaponLevel must be TirVariance, MatchRole or Level1.");
            ConfiguredJoiningWeaponLevel = string.Equals(ConfiguredJoiningWeaponLevel, "Level1", StringComparison.OrdinalIgnoreCase) ? "Level1"
                : string.Equals(ConfiguredJoiningWeaponLevel, "MatchRole", StringComparison.OrdinalIgnoreCase) ? "MatchRole" : "TirVariance";
            ConfiguredJoiningEquipment = config.Bind("Shuffle", "JoiningEquipment", "MatchRole",
                "New campaigns only: MatchRole uses the original story role's starting inventory and equipment, with comparable compatible gear when needed; Donor keeps the replacement's starting inventory. Saved campaigns retain their policy and acquired items.").Value.Trim();
            if (!string.Equals(ConfiguredJoiningEquipment, "MatchRole", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(ConfiguredJoiningEquipment, "Donor", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("JoiningEquipment must be MatchRole or Donor.");
            ConfiguredJoiningEquipment = string.Equals(ConfiguredJoiningEquipment, "Donor", StringComparison.OrdinalIgnoreCase) ? "Donor" : "MatchRole";
            ConfiguredIncludeTir = config.Bind("Shuffle", "IncludeTir", false, "Include Tir in new Suikoden I shuffles.").Value;
            ConfiguredTreasureShuffle = config.Bind("Treasure", "ShuffleChests", false,
                "Shuffle ordinary S1 chest items for new or previously unbound treasure campaigns. Saved treasure profiles retain their settings.").Value;
            ConfiguredTreasureEnabled = config.Bind("Treasure", "Enabled", true,
                "Allow chest shuffle in campaigns with a saved treasure mapping. Disable to pause it while keeping that mapping.").Value;
            string start = config.Bind("Start", "Point", "Opening", "Opening or BeforeViktor.").Value.Trim();
            SkipToViktor = string.Equals(start, "BeforeViktor", StringComparison.OrdinalIgnoreCase);
            if (!SkipToViktor && !string.Equals(start, "Opening", StringComparison.OrdinalIgnoreCase))
                log.LogWarning("Unknown S1 start point; using Opening.");
        }
    }
}

namespace SuikodenRandomizer.Suikoden2Plugin
{
    internal static class Gsd2Plugin
    {
        public const string PluginGuid = "agilen.suikoden2.recruitment_randomizer";
        internal const string Version = "0.9.0";
        internal static ManualLogSource PluginLog { get; private set; } = null!;
        internal static string Seed { get; private set; } = "agilen-080";
        internal static string Mode { get; private set; } = "Shuffle";
        internal static string Companions { get; private set; } = "";
        internal static string JoiningWeaponLevel { get; private set; } = "RiouVariance";
        internal static string JoiningEquipment { get; private set; } = "MatchRole";
        internal static bool IncludeProtagonist { get; private set; }
        internal static string StartPoint { get; private set; } = "Opening";
        internal static bool NativeCheckpointCapture { get; private set; }
        internal static string NativeCheckpointExportDirectory { get; private set; } = "";
        internal static void Configure(ConfigFile config, ManualLogSource log)
        {
            PluginLog = log;
            string target = config.Bind("Game", "Target", "GSD2", "Suikoden II game settings.").Value.Trim();
            if (target != "GSD2") throw new InvalidOperationException("S2 configuration target must be GSD2.");
            Gsd2ShuffledUnites.Configure(config.Bind("Battle", "ShuffledUnitesPreview", false,
                "Experimental native Suikoden II unites for the actual displayed fighters. Requires normal turn-based battles and the verified native resource/playback hooks.").Value);
            Seed = config.Bind("Shuffle", "Seed", "agilen-080", "New Suikoden II campaigns; saved campaigns retain their profile.").Value;
            string mode = config.Bind("Shuffle", "Mode", "Shuffle", "Shuffle, FixedChosen or FixedRandom. Nuzlocke is excluded.").Value.Trim();
            Mode = MatchOption(mode, "Shuffle", "FixedChosen", "FixedRandom");
            Companions = config.Bind("Shuffle", "Companions", "", "Five distinct single-cell companions for FixedChosen.").Value;
            JoiningWeaponLevel = config.Bind("Shuffle", "JoiningWeaponLevel", "RiouVariance",
                "New campaigns only: RiouVariance uses Riou's current weapon level with a seeded -3 to +3 offset at first party join; MatchRole uses the original story role; Level1 starts at 1. Natural weapons and story-protected combat setups retain native handling. Saved campaigns keep their policy.").Value.Trim();
            if (!string.Equals(JoiningWeaponLevel, "RiouVariance", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(JoiningWeaponLevel, "MatchRole", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(JoiningWeaponLevel, "Level1", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("S2 JoiningWeaponLevel must be RiouVariance, MatchRole or Level1.");
            JoiningWeaponLevel = string.Equals(JoiningWeaponLevel, "Level1", StringComparison.OrdinalIgnoreCase) ? "Level1"
                : string.Equals(JoiningWeaponLevel, "MatchRole", StringComparison.OrdinalIgnoreCase) ? "MatchRole" : "RiouVariance";
            JoiningEquipment = config.Bind("Shuffle", "JoiningEquipment", "MatchRole",
                "New campaigns only: MatchRole uses the original story role's starting inventory and equipment, with comparable compatible gear when needed; Donor keeps the replacement's starting inventory. Saved campaigns retain their policy and acquired items.").Value.Trim();
            if (!string.Equals(JoiningEquipment, "MatchRole", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(JoiningEquipment, "Donor", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("JoiningEquipment must be MatchRole or Donor.");
            JoiningEquipment = string.Equals(JoiningEquipment, "Donor", StringComparison.OrdinalIgnoreCase) ? "Donor" : "MatchRole";
            IncludeProtagonist = config.Bind("Shuffle", "IncludeProtagonist", false, "Include the protagonist in new shuffles.").Value;
            string start = config.Bind("Start", "Point", "Opening", "Opening or ViktorCamp.").Value.Trim();
            StartPoint = MatchOption(start, "Opening", Gsd2CampStart.Point);
            if (StartPoint != "Opening" && StartPoint != Gsd2CampStart.Point) throw new InvalidOperationException("Unknown S2 start point.");
            NativeCheckpointCapture = config.Bind("Diagnostics", "NativeCheckpointCapture", false, "Opt-in native checkpoint research; disables S2 randomization.").Value;
            NativeCheckpointExportDirectory = config.Bind("Diagnostics", "NativeCheckpointExportDirectory", "", "Absolute output folder for native checkpoint research.").Value.Trim();
            if (NativeCheckpointCapture && (StartPoint != "Opening" || !System.IO.Path.IsPathFullyQualified(NativeCheckpointExportDirectory)))
                throw new InvalidOperationException("Native checkpoint capture requires Opening and an absolute export folder.");
        }

        private static string MatchOption(string value, params string[] options)
        {
            foreach (string option in options)
                if (string.Equals(value, option, StringComparison.OrdinalIgnoreCase)) return option;
            return value;
        }
    }
}
