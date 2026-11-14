using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using System;

namespace SuikodenRandomizer.AvatarPlugin;

[BepInPlugin(PluginGuid, DisplayName, Version)]
public sealed class AvatarPlugin : BasePlugin
{
    public const string PluginGuid = "agilen.suikoden.recruitment_randomizer";
    internal const string DisplayName = "Stars of Destiny - Character Randomizer";
    internal const string Version = "0.8.7";
    internal static ManualLogSource PluginLog { get; private set; } = null!;
    internal static string ConfiguredSeed { get; private set; } = "agilen-080";
    internal static string ConfiguredMode { get; private set; } = "Shuffle";
    internal static string ConfiguredCompanions { get; private set; } = "";
    internal static string ConfiguredJoiningWeaponLevel { get; private set; } = "TirVariance";
    internal static bool ConfiguredIncludeTir { get; private set; }
    internal static bool ConfiguredTreasureShuffle { get; private set; }
    internal static bool ConfiguredTreasureEnabled { get; private set; }
    internal static bool SkipToViktor { get; private set; }
    private Harmony? _harmony;

    public override void Load()
    {
        PluginLog = Log;
        try
        {
            var seed = Config.Bind("Shuffle", "Seed", "agilen-080",
                "Seed for future NEW GAME campaigns (1 to 128 characters). Saved campaigns retain their complete mapping.");
            ConfiguredSeed = seed.Value;
            AvatarTalismanPreview.Configure(Config.Bind("Battle", "ShuffledTalismanPreview", false,
                "Experimental native turn-based Talisman for the actual Gremio and Pahn avatars. ATB and other shuffled Unites are excluded.").Value);
            AvatarShuffledUnites.Configure(Config.Bind("Battle", "ShuffledUnitesPreview", false,
                "Experimental native turn-based support for all 32 S1 character-Unite recipes. Uses the actual displayed fighters; overrides Talisman-only mode. ATB is excluded.").Value);
            ConfiguredMode = Config.Bind("Shuffle", "Mode", "Shuffle",
                "New games only: Shuffle, FixedChosen, or FixedRandom.").Value.Trim();
            ConfiguredCompanions = Config.Bind("Shuffle", "Companions", "",
                "Five distinct combat hero IDs, comma separated, for FixedChosen. Tir is included automatically.").Value;
            ConfiguredJoiningWeaponLevel = Config.Bind("Shuffle", "JoiningWeaponLevel", "TirVariance",
                "New campaigns only: TirVariance uses Tir's current weapon level with a seeded -3 to +3 offset at first join; MatchRole matches the original role; Level1 starts at 1. Saved campaigns retain their policy and upgrades.").Value.Trim();
            if (!string.Equals(ConfiguredJoiningWeaponLevel, "TirVariance", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(ConfiguredJoiningWeaponLevel, "MatchRole", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(ConfiguredJoiningWeaponLevel, "Level1", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("JoiningWeaponLevel must be TirVariance, MatchRole or Level1.");
            ConfiguredJoiningWeaponLevel = string.Equals(ConfiguredJoiningWeaponLevel, "Level1", StringComparison.OrdinalIgnoreCase) ? "Level1"
                : string.Equals(ConfiguredJoiningWeaponLevel, "MatchRole", StringComparison.OrdinalIgnoreCase) ? "MatchRole" : "TirVariance";
            ConfiguredIncludeTir = Config.Bind("Shuffle", "IncludeTir", false,
                "Include Tir in Character Shuffle for new games.").Value;
            ConfiguredTreasureShuffle = Config.Bind("Treasure", "ShuffleChests", false,
                "Shuffle ordinary S1 chest items for new or previously unbound treasure campaigns. Saved treasure profiles retain their settings.").Value;
            ConfiguredTreasureEnabled = Config.Bind("Treasure", "Enabled", true,
                "Allow chest shuffle in campaigns with a saved treasure mapping. Disable to pause it while keeping that mapping.").Value;
            string start = Config.Bind("Start", "Point", "Opening",
                "New games only: Opening or BeforeViktor.").Value.Trim();
            SkipToViktor = string.Equals(start, "BeforeViktor", StringComparison.OrdinalIgnoreCase);
            if (!SkipToViktor && !string.Equals(start, "Opening", StringComparison.OrdinalIgnoreCase))
                Log.LogWarning("Unknown [Start] Point. Using Opening.");
            global::SuikodenRandomizer.Plugin.BuildCompatibility.Verify();
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                if (assembly.GetType("Suikoden_Fix.Patches.StartingPartyVirtualizerPatch", false) is not null)
                    throw new InvalidOperationException("The old embedded Suikoden Fix randomizer is loaded. Restore the unmodified Fix before testing canonical story avatars.");
            AvatarRuntime.Current = new AvatarRuntime();
            _harmony = new Harmony(PluginGuid);
            // This is a separate assembly project: no 0.6 party-ID, roster,
            // recruitment, story-operand or membership patches are compiled in.
            _harmony.PatchAll(typeof(AvatarPlugin).Assembly);
            AddComponent<AvatarOverlay>();
            AvatarRuntime.Current.Trace(Version + " loaded. New Game mode=" + ConfiguredMode
                + "; start=" + (SkipToViktor ? "BeforeViktor" : "Opening")
                + ". Saved campaigns restore their own profile.");
        }
        catch (Exception exception)
        {
            try { _harmony?.UnpatchSelf(); } catch { }
            AvatarRuntime.Current?.Fail("Pilot initialization failed: " + exception.Message);
            Log.LogError(exception);
        }
    }
}
