extern alias GSD1;

using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace SuikodenRandomizer.AvatarPlugin;

/// <summary>
/// Keep the native Unite matcher and command state machine. Only its temporary
/// participant requirements change: incompatible choreography cannot be offered.
/// No command flag indices, empty-result sentinels or battle step codes are used.
/// </summary>
[HarmonyPatch]
internal static class AvatarUniteCandidatePatch
{
    private static readonly HashSet<string> Reported = new HashSet<string>();

    [HarmonyTargetMethods]
    private static IEnumerable<MethodBase> Targets()
    {
        foreach (string name in new[] { "check_kyoryoku", "make_command_data", "make_kyoryoku_window",
            "command_kyoryoku", "command_kyoryoku01", "command_kyoryoku02", "command_kyoryoku10" })
            yield return AccessTools.Method(typeof(GSD1.BattleBase), name)
                ?? throw new MissingMethodException("BattleBase." + name);
    }

    [HarmonyPrefix]
    private static bool Prefix(MethodBase __originalMethod, ref int __result, out AvatarUniteTableScope? __state)
    {
        __state = null;
        var runtime = AvatarRuntime.Current;
        if (runtime is null) return true;
        // A broken safety preflight must not fall through to unsafe choreography
        // on the next frame merely because IsActive becomes false after Fail().
        if (runtime.FaultReason is not null) { __result = 0; return false; }
        if (!runtime.IsActive) return true;
        try
        {
            __state = AvatarUniteTableScope.Begin();
            if (__state?.MaskedGroupCount > 0)
            {
                string key = runtime.Profile.Fingerprint + ":" + __originalMethod.Name;
                if (Reported.Count < 32 && Reported.Add(key))
                    runtime.Trace("UNITE SAFETY " + __originalMethod.Name + ": incompatible native groups="
                        + __state.MaskedGroupCount + "; compatible groups and native command handling retained.");
            }
            return true;
        }
        catch (Exception exception)
        {
            runtime.Fail("Unite safety preflight failed: " + exception.Message);
            __result = 0;
            return false;
        }
    }

    [HarmonyPostfix]
    private static void Postfix(AvatarUniteTableScope? __state)
    {
        try { __state?.Dispose(); }
        catch (Exception exception)
        {
            AvatarRuntime.Current?.Fail("Unite requirements could not be restored: " + exception.Message);
            throw;
        }
    }

    [HarmonyFinalizer]
    private static Exception? Finalizer(Exception? __exception, AvatarUniteTableScope? __state)
    {
        try { __state?.Dispose(); }
        catch (Exception exception)
        {
            AvatarRuntime.Current?.Fail("Unite requirements could not be restored: " + exception.Message);
            return __exception ?? exception;
        }
        return __exception;
    }
}
