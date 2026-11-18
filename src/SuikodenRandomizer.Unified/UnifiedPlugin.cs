using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using SuikodenRandomizer.Shared;
using SuikodenRandomizer.AvatarPlugin;
using SuikodenRandomizer.Suikoden2Plugin;
using UnityEngine.SceneManagement;
using S1Configuration = SuikodenRandomizer.AvatarPlugin.AvatarPlugin;

namespace SuikodenRandomizer.Unified;

internal enum PanelView { Full, Compact, Hidden }

[BepInPlugin(PluginGuid, "Stars of Destiny - Suikoden I & II", Version)]
public sealed class UnifiedPlugin : BasePlugin
{
    public const string PluginGuid = "agilen.starsofdestiny";
    public const string Version = "0.14.5";
    internal static readonly ActiveGameRouter Router = new();
    internal static readonly GameUiCoordinator Ui = new(Router);
    internal static ManualLogSource HostLog = null!;
    // F6 changes this session's view without rewriting the startup preference.
    internal static PanelView ViewMode { get; set; } = PanelView.Hidden;
    private static bool _observing;
    private static string? _lastProbeFailure;
    private static ActiveGame _lastKnownSelectedGame;

    public override void Load()
    {
        HostLog = Log;
        try
        {
            if (!Config.Bind("General", "Enabled", true, "Enable the Stars of Destiny randomizer and its in-game interface.").Value)
            {
                Log.LogInfo("Stars of Destiny is disabled in the launcher settings.");
                return;
            }
            RejectLegacyBootstraps();
            global::SuikodenRandomizer.Plugin.BuildCompatibility.Verify();
            string startupView = Config.Bind("Interface", "ViewMode", "Hidden",
                "Startup text visibility: Hidden by default. F6 cycles Full, Compact, and Hidden for this session only.").Value?.Trim() ?? "";
            ViewMode = string.Equals(startupView, nameof(PanelView.Full), StringComparison.OrdinalIgnoreCase) ? PanelView.Full
                : string.Equals(startupView, nameof(PanelView.Compact), StringComparison.OrdinalIgnoreCase) ? PanelView.Compact
                : PanelView.Hidden;
            // Configure ownership before either engine can admit a campaign.
            Gsd2Runtime.SetExecutionOwnership(() => Observe().Game == ActiveGame.Gsd2, () => Router.Generation,
                () => _lastKnownSelectedGame == ActiveGame.Gsd2);
            LoadEngine(ActiveGame.Gsd1, "Suikoden I", () =>
            {
                var log = Logger.CreateLogSource("Stars of Destiny - Suikoden I");
                S1Configuration.Configure(LegacyConfig(ActiveGame.Gsd1), log);
                var runtime = new AvatarRuntime();
                runtime.SetExecutionOwnership(() => Observe().Game == ActiveGame.Gsd1, () => Router.Generation);
                AvatarRuntime.Current = runtime;
                return new S1Backend();
            }, "SuikodenRandomizer.AvatarPlugin");
            LoadEngine(ActiveGame.Gsd2, "Suikoden II", () =>
            {
                var log = Logger.CreateLogSource("Stars of Destiny - Suikoden II");
                Gsd2Plugin.Configure(LegacyConfig(ActiveGame.Gsd2), log);
                Gsd2Runtime.Current = Gsd2Plugin.NativeCheckpointCapture ? null : new Gsd2Runtime();
                return new S2Backend();
            }, "SuikodenRandomizer.Suikoden2Plugin");
            DialogueNative.Install(Config);
            Observe();
            AddComponent<UnifiedOverlay>();
            Log.LogInfo("Stars of Destiny " + Version + " loaded. F6 cycles the active game's Full, Compact, and Hidden views.");
        }
        catch (Exception error) { Log.LogError(error); }
    }

    private static ConfigFile LegacyConfig(ActiveGame game) => new(Path.Combine(Paths.ConfigPath,
        LegacyGamePaths.For(game).ConfigFileName), true);

    private static void LoadEngine(ActiveGame game, string name, Func<IGameUiBackend> create, string engineNamespace)
    {
        var harmony = new Harmony(PluginGuid + "." + game);
        try
        {
            var backend = create();
            // Never PatchAll this merged assembly twice. Each class belongs to exactly one engine.
            var patches = typeof(UnifiedPlugin).Assembly.GetTypes()
                .Where(type => type.Namespace == engineNamespace && type.IsDefined(typeof(HarmonyPatch), false));
            ApplyPatches(harmony, patches.OrderBy(type => type.FullName, StringComparer.Ordinal));
            Ui.Register(backend);
            HostLog.LogInfo(name + " engine ready; native patches installed.");
        }
        catch (Exception error)
        {
            try { harmony.UnpatchSelf(); } catch { }
            if (game == ActiveGame.Gsd1) AvatarRuntime.Current = null;
            else Gsd2Runtime.Current = null;
            Ui.Register(new FailedBackend(game, name, error.Message));
            HostLog.LogError(name + " engine disabled: " + error);
        }
    }

    internal static void ApplyPatches(Harmony harmony, IEnumerable<Type> patches)
    {
        foreach (var patch in patches) harmony.CreateClassProcessor(patch).Patch();
    }

    private static void RejectLegacyBootstraps()
    {
        var ownFile = typeof(UnifiedPlugin).Assembly.Location;
        if (string.IsNullOrWhiteSpace(ownFile))
            throw new InvalidOperationException("Cannot locate the combined plugin to verify duplicate randomizers.");
        var conflicts = PluginInventoryAudit.FindConflicts(Paths.PluginPath, ownFile);
        if (conflicts.Length != 0)
            throw new InvalidOperationException("Other randomizer plugins are present. Move these copies out of BepInEx/plugins before starting: "
                + string.Join("; ", conflicts));
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly == typeof(UnifiedPlugin).Assembly) continue;
            if (assembly.GetType("SuikodenRandomizer.AvatarPlugin.AvatarPlugin", false) is not null
                || assembly.GetType("SuikodenRandomizer.Suikoden2Plugin.Gsd2Plugin", false) is not null
                || assembly.GetType("Suikoden_Fix.Patches.StartingPartyVirtualizerPatch", false) is not null)
                throw new InvalidOperationException("Another randomizer bootstrap is loaded. Use the combined installer and move duplicate randomizer copies out of BepInEx/plugins.");
        }
    }

    internal static GameOwnershipToken Observe()
    {
        if (_observing) return Router.Capture();
        _observing = true;
        try
        {
            var before = Router.Capture();
            GameOwnershipToken current;
            var selected = ActiveGame.None;
            bool gsd1Loaded = false, gsd2Loaded = false;
            try
            {
                selected = global::SystemObject.mode switch
                {
                    global::SystemObject.Mode.GSD1 => ActiveGame.Gsd1,
                    global::SystemObject.Mode.GSD2 => ActiveGame.Gsd2,
                    _ => ActiveGame.None
                };
                if (selected != ActiveGame.None) _lastKnownSelectedGame = selected;
                var gsd1Root = SceneManager.GetSceneByName("GSD1");
                var gsd2Root = SceneManager.GetSceneByName("GSD2");
                gsd1Loaded = gsd1Root.IsValid() && gsd1Root.isLoaded;
                gsd2Loaded = gsd2Root.IsValid() && gsd2Root.isLoaded;
                current = Router.ObserveGameContext(selected, gsd1Loaded, gsd2Loaded);
                _lastProbeFailure = null;
            }
            catch (Exception error)
            {
                current = Router.ObserveScene(null);
                string failure = error.GetType().Name + ": " + error.Message;
                if (_lastProbeFailure != failure)
                    HostLog?.LogWarning("Game ownership context unavailable; execution revoked: " + failure);
                _lastProbeFailure = failure;
            }
            if (current.Generation != before.Generation)
            {
                // Outgoing engines invalidate admission/callbacks without touching foreign native state.
                AvatarRuntime.Current?.SuspendExecution();
                Gsd2Runtime.Current?.SuspendExecution();
                HostLog?.LogInfo("Game ownership " + before.Game + "/" + before.Generation + " -> "
                    + current.Game + "/" + current.Generation + "; selected=" + selected
                    + "; loaded roots GSD1=" + gsd1Loaded + ", GSD2=" + gsd2Loaded + ".");
            }
            return current;
        }
        finally { _observing = false; }
    }

    private static string CampaignSummary(string mode, string seed)
    {
        string label = mode.ToUpperInvariant() switch
        {
            "FIXEDCHOSEN" => "Fixed Roster (Chosen)",
            "FIXEDRANDOM" => "Fixed Roster (Random)",
            "FIXED ROSTER" => "Fixed Roster",
            _ => mode
        };
        return label + " | Seed: " + (string.IsNullOrWhiteSpace(seed) ? "Unavailable" : seed.Replace('\r', ' ').Replace('\n', ' '));
    }

    private static EngineStatus UnboundCampaign() => new(
        "No randomized campaign loaded.\nStart a new game or load a bound save.");

    private sealed class S1Backend : IGameUiBackend
    {
        public ActiveGame Game => ActiveGame.Gsd1;
        public string DisplayName => "Suikoden I";
        public EngineStatus GetStatus(GameOwnershipToken token)
        {
            var runtime = AvatarRuntime.Current;
            if (runtime?.OwnsExecution != true) return new EngineStatus("Waiting for Suikoden I's title or campaign.");
            if (runtime.FaultReason is { } reason) return new EngineStatus(reason, true);
            if (runtime.FixedRosterIdentity is { } roster)
                return new EngineStatus(CampaignSummary(roster.Mode, roster.Seed) + "\n"
                    + (runtime.IsActive ? "Tir and five companions." : "Preparing the native roster.")
                    + (runtime.TreasureActive ? "\nChests shuffled." : ""));
            if (!runtime.IsAdmitted) return UnboundCampaign();
            return new EngineStatus(CampaignSummary("Shuffle", runtime.Profile.Seed) + "\n"
                + (runtime.IsActive ? runtime.Profile.RoleIds.Count + " combat roles. Story IDs retained." : runtime.Status)
                + (runtime.TreasureActive ? "\nChests shuffled." : ""));
        }
        public void Tick(GameOwnershipToken token)
        { if (Router.IsCurrent(token)) AvatarRuntime.Current?.Tick(); }
        public void HandleF6(GameOwnershipToken token)
        {
            var runtime = AvatarRuntime.Current;
            if (!Router.IsCurrent(token) || runtime?.OwnsExecution != true) return;
            runtime.Trace("MANUAL CHECKPOINT: " + GetStatus(token).Text.Replace('\n', ' '));
            runtime.Trace("PERFORMANCE CHECKPOINT: " + runtime.PerformanceStatus);
            if (runtime.TreasureActive)
                runtime.Trace("TREASURE CHECKPOINT: mapping=" + runtime.TreasureProfile!.Fingerprint);
            DialogueNative.TraceStatus(Game);
            runtime.TracePartyLayout("shared F6 checkpoint");
            if (S1Configuration.SkipToViktor) ViktorStart.TraceState("shared F6");
            if (runtime.IsActive)
                try { runtime.VerifyFinalParty("shared F6 checkpoint"); }
                catch (Exception error) { runtime.Trace("Checkpoint during transition: " + error.Message); }
        }
    }

    private sealed class S2Backend : IGameUiBackend
    {
        public ActiveGame Game => ActiveGame.Gsd2;
        public string DisplayName => "Suikoden II";
        public EngineStatus GetStatus(GameOwnershipToken token)
        {
            if (Gsd2Plugin.NativeCheckpointCapture) return new EngineStatus("Native checkpoint capture mode");
            var runtime = Gsd2Runtime.Current;
            if (runtime?.HasAdmittedCampaignBeforeSuspend == true)
                return new EngineStatus("Suikoden II campaign suspended after a game-context change. Load a bound save or start a new game. Saving is blocked until the campaign is restored.", true);
            if (runtime?.OwnsExecution != true) return new EngineStatus("Waiting for Suikoden II's title or campaign.");
            if (runtime.FaultReason is { } reason) return new EngineStatus(reason, true);
            if (!runtime.IsAdmitted) return UnboundCampaign();
            return new EngineStatus(CampaignSummary(runtime.Profile.Mode, runtime.Profile.Seed) + "\n"
                + (runtime.IsActive ? "Character replacements ready." : "Preparing original character resources."));
        }
        public void Tick(GameOwnershipToken token)
        {
            if (!Router.IsCurrent(token) || Gsd2Runtime.Current is null) return;
            Gsd2Runtime.Current.Tick();
            Gsd2Graphics.Tick();
        }
        public void HandleF6(GameOwnershipToken token)
        {
            var runtime = Gsd2Runtime.Current;
            if (Router.IsCurrent(token) && runtime?.OwnsExecution == true)
            {
                runtime.Trace("MANUAL CHECKPOINT: " + GetStatus(token).Text.Replace('\n', ' '));
                DialogueNative.TraceStatus(Game);
            }
        }
    }

    private sealed class FailedBackend : IGameUiBackend
    {
        public ActiveGame Game { get; }
        public string DisplayName { get; }
        private readonly string _message;
        internal FailedBackend(ActiveGame game, string name, string message)
        { Game = game; DisplayName = name; _message = message; }
        public EngineStatus GetStatus(GameOwnershipToken token) => new("Randomizer unavailable: " + _message, true);
        public void Tick(GameOwnershipToken token) { }
        public void HandleF6(GameOwnershipToken token) { }
    }
}
