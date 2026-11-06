using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using System;

namespace SuikodenRandomizer.AvatarPlugin;

[BepInPlugin(PluginGuid, "Suikoden Story Avatars Shuffle", "0.8.6")]
public sealed class AvatarPlugin : BasePlugin
{
    public const string PluginGuid = "agilen.suikoden.recruitment_randomizer";
    internal static ManualLogSource PluginLog { get; private set; } = null!;
    internal static string ConfiguredSeed { get; private set; } = "agilen-080";
    internal static string ConfiguredMode { get; private set; } = "Shuffle";
    internal static string ConfiguredCompanions { get; private set; } = "";
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
            ConfiguredMode = Config.Bind("Shuffle", "Mode", "Shuffle",
                "New games only: Shuffle, FixedChosen, or FixedRandom. Fixed rosters are experimental.").Value.Trim();
            ConfiguredCompanions = Config.Bind("Shuffle", "Companions", "",
                "Five distinct combat hero IDs, comma separated, for FixedChosen. Tir is included automatically.").Value;
            string start = Config.Bind("Start", "Point", "Opening",
                "New games only: Opening or BeforeViktor (experimental).").Value.Trim();
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
            AvatarRuntime.Current.Trace("0.8.6 Before Viktor test loaded. New Game mode=" + ConfiguredMode
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
