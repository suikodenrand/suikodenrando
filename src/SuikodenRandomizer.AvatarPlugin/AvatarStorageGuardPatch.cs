using HarmonyLib;
using System;

namespace SuikodenRandomizer.AvatarPlugin;

[HarmonyPatch(typeof(global::SystemSave), nameof(global::SystemSave.Save),
    new Type[] { typeof(string), typeof(string), typeof(global::SystemSave.DataSize), typeof(Il2CppSystem.Action<bool>), typeof(bool) })]
internal static class AvatarStorageGuardPatch
{
    [HarmonyPrefix]
    private static bool Prefix(Il2CppSystem.Action<bool> cb, ref bool __result)
    {
        var call = AvatarSaveCall.Current;
        if (call is null || !call.Runtime.MatchesExecution(call.ExecutionEpoch) || !call.BindingFailed) return true;
        __result = false;
        call.Runtime.Trace("SAVE STORAGE BLOCKED: " + call.Failure);
        try { cb?.Invoke(false); }
        catch (Exception callbackFailure) { call.Runtime.Trace("Storage-failure callback threw: " + callbackFailure.Message); }
        return false;
    }
}
