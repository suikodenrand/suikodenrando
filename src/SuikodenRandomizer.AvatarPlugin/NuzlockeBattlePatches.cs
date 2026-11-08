extern alias GSD1;

using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace SuikodenRandomizer.AvatarPlugin;

[HarmonyPatch]
internal static class NuzlockeBattlePatches
{
    [HarmonyTargetMethods]
    private static IEnumerable<MethodBase> Targets()
    {
        foreach (string name in new[] { "dead_player", "calc_hp", "battle_loop" })
            yield return AccessTools.Method(typeof(GSD1.BattleBase), name)
                ?? throw new MissingMethodException("BattleBase." + name);
    }

    [HarmonyPostfix]
    private static void Observe()
    {
        var runtime = AvatarRuntime.Current;
        if (runtime?.IsNuzlocke != true || runtime.FaultReason is not null) return;
        try { runtime.ObserveNuzlockeKnockouts(); }
        catch (Exception error) { CrashDiagnostics.Exception("Nuzlocke knockout observation", error); runtime.Fail("Nuzlocke knockout observation failed: " + error.Message); }
    }
}

[HarmonyPatch(typeof(GSD1.BattleBase), "battle_end_free")]
internal static class NuzlockeBattleCleanupPatch
{
    [HarmonyPrefix]
    private static void Before(out bool __state)
    {
        __state = false;
        var runtime = AvatarRuntime.Current;
        if (runtime?.IsNuzlocke != true) return;
        try
        {
            CrashDiagnostics.Breadcrumb("nuz.cleanup.before");
            runtime.ObserveNuzlockeKnockouts();
            var work = GSD1.BattleBase.battle_work;
            __state = work?.flg_table is not null && work.flg_table.Count > 2 && work.flg_table[2];
        }
        catch (Exception error) { CrashDiagnostics.Exception("Nuzlocke result capture", error); runtime.Fail("Nuzlocke result capture failed: " + error.Message); }
    }

    [HarmonyPostfix]
    private static void After(bool __state)
    {
        var runtime = AvatarRuntime.Current;
        if (runtime?.IsNuzlocke != true || runtime.FaultReason is not null || GSD1.BattleBase.battle_work is not null) return;
        try { CrashDiagnostics.Breadcrumb("nuz.cleanup.after"); runtime.FinishNuzlockeBattle(__state); }
        catch (Exception error) { CrashDiagnostics.Exception("Nuzlocke replacement", error); runtime.Fail("Nuzlocke replacement failed: " + error.Message); }
    }
}

[HarmonyPatch(typeof(GSD1.BattleBase), "battle_game_over")]
internal static class NuzlockeRetryPatch
{
    [HarmonyPrefix]
    private static bool Prefix(int __0, ref int __result)
    {
        var runtime = AvatarRuntime.Current;
        if (runtime?.IsNuzlocke != true) return true;
        if (runtime.FaultReason is not null) { __result = -1; return false; }
        try
        {
            if (!runtime.TakeNuzlockeRetry()) return true;
            CrashDiagnostics.Breadcrumb("nuz.retry.schedule.before", "step=" + __0);
            NuzlockeRetryResources.Begin(__0);
            CrashDiagnostics.Breadcrumb("nuz.retry.schedule.after");
            __result = -1;
            return false;
        }
        catch (Exception error)
        {
            CrashDiagnostics.Exception("Nuzlocke retry", error);
            runtime.Fail("Nuzlocke retry failed: " + error.Message);
            __result = -1;
            return false;
        }
    }
}

[HarmonyPatch(typeof(GSD1.BattleBase), "get_command")]
internal static class NuzlockeRetryFadePatch
{
    [HarmonyPrefix]
    private static void Prefix()
    {
        if (AvatarRuntime.Current?.TakeNuzlockeRetryFade() == true)
        {
            CrashDiagnostics.Breadcrumb("nuz.retry.fade.before");
            global::UIManager.FadeIn(0f);
            CrashDiagnostics.Breadcrumb("nuz.retry.fade.after");
        }
    }
}
