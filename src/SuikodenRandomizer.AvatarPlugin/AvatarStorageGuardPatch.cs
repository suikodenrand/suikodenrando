using HarmonyLib;
using System;

namespace SuikodenRandomizer.AvatarPlugin;

[HarmonyPatch(typeof(global::SystemSave), nameof(global::SystemSave.Save),
    new Type[] { typeof(string), typeof(string), typeof(global::SystemSave.DataSize), typeof(Il2CppSystem.Action<bool>), typeof(bool) })]
internal static class AvatarStorageGuardPatch
{
    internal static bool AllowMirrorWrite()
    {
        var call = AvatarSaveCall.Current;
        return call is null || call.NativePassthrough || !call.Runtime.MatchesExecution(call.ExecutionEpoch)
            || (!call.BindingFailed && !call.NativeStorageFailed);
    }

    // Fix's ordinary-priority SystemSave.Save postfix runs even when a
    // prefix vetoed storage. Record the actual result before that writer runs.
    [HarmonyPostfix, HarmonyPriority(Priority.First)]
    private static void Postfix(bool __result)
    {
        var call = AvatarSaveCall.Current;
        if (call is not null && !call.NativePassthrough && call.Runtime.MatchesExecution(call.ExecutionEpoch))
            call.NativeStorageFailed = !__result;
    }

    [HarmonyPrefix]
    private static bool Prefix(Il2CppSystem.Action<bool> cb, ref bool __result)
    {
        var call = AvatarSaveCall.Current;
        if (call is null || call.NativePassthrough || !call.Runtime.MatchesExecution(call.ExecutionEpoch) || !call.BindingFailed) return true;
        __result = false;
        call.Runtime.Trace("SAVE STORAGE BLOCKED: " + call.Failure);
        try { cb?.Invoke(false); }
        catch (Exception callbackFailure) { call.Runtime.Trace("Storage-failure callback threw: " + callbackFailure.Message); }
        return false;
    }
}
