extern alias GSD1;

using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;

namespace SuikodenRandomizer.S1BattlePreview;

[BepInPlugin(PluginId, "Suikoden I Battle Preview", "0.10.1")]
public sealed class BattlePreviewPlugin : BasePlugin
{
    public const string PluginId = "agilen.suikoden1.battle-preview";
    private static ConfigEntry<bool>? _enabled;
    private static ConfigEntry<bool>? _oneShotAttack;
    private static ConfigEntry<bool>? _activeAtb;
    private static ConfigEntry<bool>? _repeatTurns;
    private static ConfigEntry<bool>? _readyCommands;
    private static ConfigEntry<bool>? _nativeReadyMenus;
    private static ConfigEntry<bool>? _nativeSpdMeters;
    private static ConfigEntry<bool>? _activeInput;
    private static ConfigEntry<bool>? _enemyDexCharge;
    private static ConfigEntry<bool>? _openingCharge;
    internal static ManualLogSource? PreviewLog { get; private set; }
    internal static bool Enabled => _enabled?.Value == true;
    internal static bool ActiveAtbEnabled => Enabled && _activeAtb?.Value == true;
    internal static bool RepeatTurnsEnabled => ActiveAtbEnabled && _repeatTurns?.Value == true;
    internal static bool ReadyCommandsEnabled => ActiveAtbEnabled && _readyCommands?.Value == true;
    internal static bool NativeReadyMenusEnabled => ReadyCommandsEnabled && _nativeReadyMenus?.Value == true;
    internal static bool NativeSpdMetersEnabled => Enabled && _nativeSpdMeters?.Value == true;
    internal static bool ActiveInputEnabled => ReadyCommandsEnabled && _activeInput?.Value == true;
    internal static bool EnemyDexChargeEnabled => ActiveAtbEnabled && _enemyDexCharge?.Value == true;
    internal static bool OpeningChargeEnabled => ActiveAtbEnabled && _openingCharge?.Value == true;
    internal static bool OneShotAttackEnabled => Enabled && !ActiveAtbEnabled && _oneShotAttack?.Value == true;

    public override void Load()
    {
        PreviewLog = Log;
        var harmony = new Harmony(PluginId);
        try
        {
            global::SuikodenRandomizer.Plugin.BuildCompatibility.Verify();
            _enabled = Config.Bind("Preview", "Enabled", true, "Display S1 SPD gauges and record native turns.");
            _oneShotAttack = Config.Bind("Preview", "OneShotAttack", false,
                "In the first round, wait for one manually queued normal attack's SPD meter.");
            _activeAtb = Config.Bind("Preview", "ActiveAtb", false,
                "Schedule heroes and enemies by SPD meter in ordinary manual Attack and Defend rounds.");
            _repeatTurns = Config.Bind("Preview", "RepeatTurns", false,
                "Let an eligible hero or enemy repeat a completed action when their SPD meter refills.");
            _readyCommands = Config.Bind("Preview", "ReadyCommands", false,
                "Choose Attack, Defend, Rune, or Item separately for each hero at full SPD charge.");
            _nativeReadyMenus = Config.Bind("Preview", "NativeReadyMenus", false,
                "Use the game's command and targeting windows, with escape and Auto in the ready hero's menu.");
            _nativeSpdMeters = Config.Bind("Preview", "NativeSpdMeters", false,
                "Show SPD meters beside combatants on the native S1 battle canvas.");
            _activeInput = Config.Bind("Preview", "ActiveInput", true,
                "Queue each ready hero's commands while native actions play and meters recharge.");
            _enemyDexCharge = Config.Bind("Preview", "EnemyDexCharge", true,
                "Give enemies 0.75% faster meter charge per point of DEX, capped at 75% and the fastest meter rate.");
            _openingCharge = Config.Bind("Preview", "OpeningCharge", true,
                "Start heroes and enemies with SPD-based charge, small random variation, and a chance of a DEX bonus.");
            harmony.CreateClassProcessor(typeof(BattleUpdateProbe)).Patch();
            harmony.CreateClassProcessor(typeof(NextActorProbe)).Patch();
            harmony.CreateClassProcessor(typeof(BattleStartProbe)).Patch();
            harmony.CreateClassProcessor(typeof(BattleEndProbe)).Patch();
            if (OneShotAttackEnabled) harmony.CreateClassProcessor(typeof(OneShotAttackPatch)).Patch();
            if (ActiveAtbEnabled) harmony.CreateClassProcessor(typeof(ActiveActionProbe)).Patch();
            if (ReadyCommandsEnabled) harmony.CreateClassProcessor(typeof(ReadyCommandBatchProbe)).Patch();
            if (NativeReadyMenusEnabled)
            {
                harmony.CreateClassProcessor(typeof(NativeReadyOuterMenuPatch)).Patch();
                harmony.CreateClassProcessor(typeof(NativeAutoConfirmationPatch)).Patch();
                harmony.CreateClassProcessor(typeof(NativeReadyTurnPatch)).Patch();
                harmony.CreateClassProcessor(typeof(NativeReadyCommandPatch)).Patch();
                harmony.CreateClassProcessor(typeof(NativeReadyBackPatch)).Patch();
            }
            AddComponent<BattlePreviewOverlay>();
            string mode = NativeReadyMenusEnabled ? "Native per-hero SPD command menus enabled."
                : ReadyCommandsEnabled ? "Per-hero SPD command prompts enabled."
                : RepeatTurnsEnabled ? "Same-round hero and enemy SPD repeats enabled."
                : ActiveAtbEnabled ? "Hero and enemy SPD scheduling enabled."
                : OneShotAttackEnabled ? "One-shot SPD attack enabled." : "SPD gauges are observational.";
            Log.LogInfo("Suikoden I Battle Preview loaded. " + mode);
            if (NativeSpdMetersEnabled) Log.LogInfo("Native combatant SPD meters enabled.");
            if (ActiveInputEnabled) Log.LogInfo("Active command input enabled. Ready hero commands can queue during native actions.");
            if (EnemyDexChargeEnabled) Log.LogInfo("Enemy DEX charge modifier enabled.");
            if (OpeningChargeEnabled) Log.LogInfo("Randomized SPD and DEX opening charge enabled.");
        }
        catch (Exception error)
        {
            _enabled = null;
            try { harmony.UnpatchSelf(); } catch (Exception cleanupError) { Log.LogWarning(cleanupError); }
            Log.LogError("Suikoden I Battle Preview could not start. " + error);
        }
    }
}

[HarmonyPatch(typeof(GSD1.BattleBase), nameof(GSD1.BattleBase.command00))]
internal static class ReadyCommandBatchProbe
{
    [HarmonyPrefix]
    private static bool Prefix(int __0, ref int __result)
    {
        if (ActiveBattleState.TryEndNativeReadyMenu(__0, canceled: false, out __result)) return false;
        if (!ActiveBattleState.TrySkipNativeCommandBatch()) return true;
        // End the nested command step. Native player_command treats 0 as Fight.
        __result = 0;
        return false;
    }
}

[HarmonyPatch(typeof(GSD1.BattleBase), nameof(GSD1.BattleBase.BattleUpdate))]
internal static class BattleUpdateProbe
{
    [HarmonyPostfix]
    private static void Postfix() => BattlePreviewState.ObserveFrame();
}

[HarmonyPatch(typeof(GSD1.BattleBase), nameof(GSD1.BattleBase.next_chara))]
internal static class NextActorProbe
{
    [HarmonyPrefix]
    private static bool Prefix(ref int __result, out bool __state)
    {
        __state = ActiveBattleState.TrySelect(out int slot);
        if (!__state) return true;
        __result = slot;
        return false;
    }

    [HarmonyPostfix]
    private static void Postfix(int __result, bool __state)
    {
        if (__state) return;
        ActiveBattleState.ObserveNativeSelection(__result);
        BattlePreviewState.ObserveSelection(__result);
    }
}

[HarmonyPatch(typeof(GSD1.BattleBase), nameof(GSD1.BattleBase.battle_turn02))]
internal static class ActiveActionProbe
{
    [HarmonyPrefix]
    private static void Prefix() => ActiveBattleState.ObserveActionEntry();
}

[HarmonyPatch(typeof(GSD1.BattleBase), nameof(GSD1.BattleBase.battle_end_free))]
internal static class BattleEndProbe
{
    [HarmonyPostfix]
    private static void Postfix() => BattlePreviewState.ResetEncounter();
}

[HarmonyPatch(typeof(GSD1.BattleBase), nameof(GSD1.BattleBase.initialize_battle))]
internal static class BattleStartProbe
{
    [HarmonyPrefix]
    private static void Prefix() => BattlePreviewState.ResetEncounter();
}

[HarmonyPatch(typeof(GSD1.BattleBase), nameof(GSD1.BattleBase.battle_turn01))]
internal static class OneShotAttackPatch
{
    [HarmonyPrefix]
    private static bool Prefix(ref int __result)
    {
        if (!BattlePreviewState.WaitForAttack()) return true;
        // battle_turn01 already returns -1 without advancing when native readiness blocks it.
        __result = -1;
        return false;
    }
}
