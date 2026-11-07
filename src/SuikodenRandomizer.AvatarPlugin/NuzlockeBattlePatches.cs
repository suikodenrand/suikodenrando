extern alias GSD1;

using HarmonyLib;
using Il2CppInterop.Runtime;
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
        catch (Exception error) { runtime.Fail("Nuzlocke knockout observation failed: " + error.Message); }
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
            runtime.ObserveNuzlockeKnockouts();
            var work = GSD1.BattleBase.battle_work;
            __state = work?.flg_table is not null && work.flg_table.Count > 2 && work.flg_table[2];
        }
        catch (Exception error) { runtime.Fail("Nuzlocke result capture failed: " + error.Message); }
    }

    [HarmonyPostfix]
    private static void After(bool __state)
    {
        var runtime = AvatarRuntime.Current;
        if (runtime?.IsNuzlocke != true || runtime.FaultReason is not null || GSD1.BattleBase.battle_work is not null) return;
        try { runtime.FinishNuzlockeBattle(__state); }
        catch (Exception error) { runtime.Fail("Nuzlocke replacement failed: " + error.Message); }
    }
}

[HarmonyPatch(typeof(GSD1.BattleBase), "battle_game_over")]
internal static class NuzlockeRetryPatch
{
    // Keep the native delegate rooted through scene teardown and the next load.
    private static readonly Il2CppSystem.Func<int, int> Retry =
        DelegateSupport.ConvertDelegate<Il2CppSystem.Func<int, int>>(new Func<int, int>(GSD1.BattleBase.battle))
            ?? throw new InvalidOperationException("The native retry delegate could not be created.");

    [HarmonyPrefix]
    private static bool Prefix(int __0, ref int __result)
    {
        var runtime = AvatarRuntime.Current;
        if (runtime?.IsNuzlocke != true) return true;
        if (runtime.FaultReason is not null) { __result = -1; return false; }
        try
        {
            if (!runtime.TakeNuzlockeRetry()) return true;
            GSD1.BattleBase.NextGameStep(Retry, __0, "Nuzlocke reserve team retry");
            __result = -1;
            return false;
        }
        catch (Exception error)
        {
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
            global::UIManager.FadeIn(0f);
    }
}
