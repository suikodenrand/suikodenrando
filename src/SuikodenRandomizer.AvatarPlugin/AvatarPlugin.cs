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
    internal const string Version = "0.8.10";
    internal static ManualLogSource PluginLog { get; private set; } = null!;
    internal static string ConfiguredSeed { get; private set; } = "agilen-080";
    internal static string ConfiguredMode { get; private set; } = "Shuffle";
    internal static string ConfiguredCompanions { get; private set; } = "";
    internal static bool ConfiguredIncludeTir { get; private set; }
    internal static bool SkipToViktor { get; private set; }
    private Harmony? _harmony;

    public override void Load()
    {
        PluginLog = Log;
        CrashDiagnostics.Initialize(Log);
        try
        {
            var seed = Config.Bind("Shuffle", "Seed", "agilen-080",
                "Seed for future NEW GAME campaigns (1 to 128 characters). Saved campaigns retain their complete mapping.");
            ConfiguredSeed = seed.Value;
            ConfiguredMode = Config.Bind("Shuffle", "Mode", "Shuffle",
                "New games only: Shuffle, FixedChosen, FixedRandom, or Nuzlocke.").Value.Trim();
            ConfiguredCompanions = Config.Bind("Shuffle", "Companions", "",
                "Five distinct combat hero IDs, comma separated, for FixedChosen. Tir is included automatically.").Value;
            ConfiguredIncludeTir = Config.Bind("Shuffle", "IncludeTir", false,
                "Include Tir in Character Shuffle for new games.").Value;
            string start = Config.Bind("Start", "Point", "Opening",
                "New games only: Opening or BeforeViktor.").Value.Trim();
            SkipToViktor = string.Equals(start, "BeforeViktor", StringComparison.OrdinalIgnoreCase);
            if (!SkipToViktor && !string.Equals(start, "Opening", StringComparison.OrdinalIgnoreCase))
                Log.LogWarning("Unknown [Start] Point. Using Opening.");
            CrashDiagnostics.Breadcrumb("plugin.settings", "version=" + Version + "; mode=" + ConfiguredMode
                + "; seed=" + ConfiguredSeed + "; includeTir=" + ConfiguredIncludeTir
                + "; beforeViktor=" + SkipToViktor);
            global::SuikodenRandomizer.Plugin.BuildCompatibility.Verify();
            CrashDiagnostics.Breadcrumb("plugin.game-build.verified");
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                if (assembly.GetType("Suikoden_Fix.Patches.StartingPartyVirtualizerPatch", false) is not null)
                    throw new InvalidOperationException("The old embedded Suikoden Fix randomizer is loaded. Restore the unmodified Fix before testing canonical story avatars.");
            AvatarRuntime.Current = new AvatarRuntime();
            _harmony = new Harmony(PluginGuid);
            // This is a separate assembly project: no 0.6 party-ID, roster,
            // recruitment, story-operand or membership patches are compiled in.
            _harmony.PatchAll(typeof(AvatarPlugin).Assembly);
            CrashDiagnostics.Breadcrumb("plugin.patches.installed");
            AddComponent<AvatarOverlay>();
            AvatarRuntime.Current.Trace(Version + " loaded. New Game mode=" + ConfiguredMode
                + "; start=" + (SkipToViktor ? "BeforeViktor" : "Opening")
                + ". Saved campaigns restore their own profile.");
        }
        catch (Exception exception)
        {
            CrashDiagnostics.Exception("plugin initialization", exception);
            try { _harmony?.UnpatchSelf(); } catch { }
            AvatarRuntime.Current?.Fail("Pilot initialization failed: " + exception.Message);
            Log.LogError(exception);
        }
    }
}
