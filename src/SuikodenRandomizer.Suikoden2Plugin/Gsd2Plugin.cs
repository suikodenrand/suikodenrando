using System;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using UnityEngine;

namespace SuikodenRandomizer.Suikoden2Plugin;

[BepInPlugin(PluginGuid, "Stars of Destiny - Suikoden II", "0.9.0")]
public sealed class Gsd2Plugin : BasePlugin
{
    public const string PluginGuid = "agilen.suikoden2.recruitment_randomizer";
    internal static ManualLogSource PluginLog { get; private set; } = null!;
    internal static string Seed { get; private set; } = "agilen-080";
    internal static string Mode { get; private set; } = "Shuffle";
    internal static string Companions { get; private set; } = "";
    internal static string JoiningWeaponLevel { get; private set; } = "RiouVariance";
    internal static bool IncludeProtagonist { get; private set; }
    internal static string StartPoint { get; private set; } = "Opening";
    internal static bool NativeCheckpointCapture { get; private set; }
    internal static string NativeCheckpointExportDirectory { get; private set; } = "";
    private Harmony? _harmony;
    public override void Load()
    {
        PluginLog = Log;
        try
        {
            Gsd2ShuffledUnites.Configure(Config.Bind("Battle", "ShuffledUnitesPreview", false,
                "Experimental native Suikoden II unites for the actual displayed fighters. Requires normal turn-based battles and the verified native resource/playback hooks.").Value);
            string target = Config.Bind("Game", "Target", "GSD2", "This separate plugin config targets Suikoden II.").Value.Trim();
            if (!string.Equals(target, "GSD2", StringComparison.Ordinal)) throw new InvalidOperationException("Suikoden II config target must be GSD2.");
            Seed = Config.Bind("Shuffle", "Seed", "agilen-080", "Seed for new Suikoden II campaigns. Saved campaigns retain their mapping.").Value;
            Mode = Config.Bind("Shuffle", "Mode", "Shuffle", "Shuffle, FixedChosen, or FixedRandom. Nuzlocke is unsupported.").Value.Trim();
            Companions = Config.Bind("Shuffle", "Companions", "", "Five distinct, single-cell combat companion IDs for FixedChosen.").Value;
            JoiningWeaponLevel = Config.Bind("Shuffle", "JoiningWeaponLevel", "RiouVariance",
                "New campaigns only: RiouVariance uses Riou's current weapon level with a seeded -3 to +3 offset at first party join; MatchRole matches the original story role; Level1 starts at 1. Natural weapons and protected combat setups retain native handling. Saved campaigns keep their policy.").Value.Trim();
            if (!string.Equals(JoiningWeaponLevel, "RiouVariance", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(JoiningWeaponLevel, "MatchRole", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(JoiningWeaponLevel, "Level1", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("JoiningWeaponLevel must be RiouVariance, MatchRole or Level1.");
            JoiningWeaponLevel = string.Equals(JoiningWeaponLevel, "Level1", StringComparison.OrdinalIgnoreCase) ? "Level1"
                : string.Equals(JoiningWeaponLevel, "MatchRole", StringComparison.OrdinalIgnoreCase) ? "MatchRole" : "RiouVariance";
            IncludeProtagonist = Config.Bind("Shuffle", "IncludeProtagonist", false, "Include the protagonist in the Suikoden II character shuffle.").Value;
            StartPoint = Config.Bind("Start", "Point", "Opening", "New Suikoden II campaign starting point.").Value.Trim();
            if (StartPoint != "Opening" && StartPoint != Gsd2CampStart.Point) throw new InvalidOperationException("Suikoden II start must be Opening or ViktorCamp.");
            NativeCheckpointCapture = Config.Bind("Diagnostics", "NativeCheckpointCapture", false, "Opt-in research mode: retain native Suikoden II behavior and export manually saved snapshots; do not randomize.").Value;
            NativeCheckpointExportDirectory = Config.Bind("Diagnostics", "NativeCheckpointExportDirectory", "", "Absolute export folder, required only in native checkpoint capture mode.").Value.Trim();
            if (NativeCheckpointCapture && StartPoint != "Opening")
                throw new InvalidOperationException("Native checkpoint capture requires Opening; it does not apply a quick-start checkpoint.");
            if (NativeCheckpointCapture && !System.IO.Path.IsPathFullyQualified(NativeCheckpointExportDirectory))
                throw new InvalidOperationException("Native checkpoint capture requires an absolute export folder.");
            global::SuikodenRandomizer.Plugin.BuildCompatibility.Verify();
            Gsd2Runtime.Current = NativeCheckpointCapture ? null : new Gsd2Runtime();
            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll(typeof(Gsd2Plugin).Assembly);
            AddComponent<Gsd2Driver>();
            Log.LogInfo(NativeCheckpointCapture ? "Suikoden II native checkpoint capture enabled; avatar randomization is disabled in this research mode."
                : "Suikoden II 0.9.0 loaded; existing unbound saves retain native behavior.");
        }
        catch (Exception exception) { try { _harmony?.UnpatchSelf(); } catch { } Log.LogError(exception); }
    }
}

internal sealed class Gsd2Driver : MonoBehaviour
{
    public Gsd2Driver(IntPtr pointer) : base(pointer) { }
    private void Start() => Gsd2Graphics.Initialize(this);
    private void Update()
    {
        var runtime = Gsd2Runtime.Current;
        if (runtime is null) return;
        try { runtime.Tick(); Gsd2Graphics.Tick(); }
        catch (Exception exception) { runtime.Fail("Suikoden II frame update: " + exception.Message); }
    }
    private void OnGUI()
    {
        var runtime = Gsd2Runtime.Current;
        if (runtime is not null && (runtime.FaultReason is not null || (runtime.IsAdmitted && !runtime.IsActive)))
            GUI.Label(new Rect(12, 12, 760, 55), runtime.Status);
    }
}
