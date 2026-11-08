extern alias GSD1;

using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;

namespace SuikodenRandomizer.AvatarPlugin;

[HarmonyPatch]
internal static class CrashTransitionPatches
{
    private static readonly Dictionary<MethodBase, long> LastEntry = new();

    [HarmonyTargetMethods]
    private static IEnumerable<MethodBase> Targets()
    {
        yield return AccessTools.Method(typeof(GSD1.ChapterManager), "BattleEndInternal", Type.EmptyTypes)
            ?? throw new MissingMethodException("ChapterManager.BattleEndInternal");
        foreach (string name in new[] { "battle", "initialize_battle", "player_initialize", "calc_exp", "battle_end_free", "battle_game_over" })
            yield return AccessTools.Method(typeof(GSD1.BattleBase), name)
                ?? throw new MissingMethodException("BattleBase." + name);
    }

    [HarmonyPrefix, HarmonyPriority(Priority.First)]
    private static void Before(MethodBase __originalMethod, out bool __state)
    {
        // Some native step functions repeat every frame. Preserve entry/exit
        // evidence without forcing a disk flush on every initialization poll.
        long now = Stopwatch.GetTimestamp();
        lock (LastEntry)
        {
            __state = !LastEntry.TryGetValue(__originalMethod, out long previous)
                || now - previous >= Math.Max(1L, Stopwatch.Frequency / 4);
            if (__state) LastEntry[__originalMethod] = now;
        }
        if (!__state) return;
        string name = __originalMethod.DeclaringType?.Name + "." + __originalMethod.Name;
        CrashDiagnostics.Breadcrumb("native.before", name);
        AvatarRuntime.Current?.DiagnosticCheckpoint(name + ".before");
    }

    [HarmonyPostfix, HarmonyPriority(Priority.Last)]
    private static void After(MethodBase __originalMethod, bool __state)
    {
        if (!__state) return;
        string name = __originalMethod.DeclaringType?.Name + "." + __originalMethod.Name;
        CrashDiagnostics.Breadcrumb("native.after", name);
        AvatarRuntime.Current?.DiagnosticCheckpoint(name + ".after");
    }

    [HarmonyFinalizer]
    private static Exception? Failed(MethodBase __originalMethod, Exception? __exception)
    {
        if (__exception is not null)
            CrashDiagnostics.Exception("native boundary " + __originalMethod.DeclaringType?.Name + "." + __originalMethod.Name, __exception);
        return __exception;
    }
}
