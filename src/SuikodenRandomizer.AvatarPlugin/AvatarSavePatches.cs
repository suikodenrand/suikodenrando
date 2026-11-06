extern alias GSD1;

using HarmonyLib;
using System;

namespace SuikodenRandomizer.AvatarPlugin;

internal sealed class AvatarSaveCall
{
    [ThreadStatic] internal static AvatarSaveCall? Current;
    internal AvatarSaveCall? Previous;
    internal bool BindingFailed = true;
    internal string Failure = "No verified avatar binding was recorded for the snapshot.";
}

[HarmonyPatch(typeof(GSD1.SaveDataManager), "SaveGameWork")]
internal static class AvatarSavePreparationPatch
{
    [HarmonyPrefix]
    private static bool Prefix(ref bool __result, Il2CppSystem.Action<bool, GSD1.SaveDataManager.SaveData> callback,
        out AvatarSaveCall? __state)
    {
        __state = null;
        var runtime = AvatarRuntime.Current;
        try
        {
            if (runtime is null || !runtime.IsAdmitted || runtime.FaultReason is not null)
                throw new InvalidOperationException(runtime?.FaultReason ?? "This shuffle saves only its own New Game/bound campaign; pilot/older saves are read-only.");
            if (!runtime.EnsureProxy("save preparation", false, force: true))
                throw new InvalidOperationException("Complete shuffle roster is not ready; save again after native initialization completes.");
            runtime.VerifyFinalParty("save preparation");
            runtime.CheckCampaignBindingWritable();
            __state = new AvatarSaveCall { Previous = AvatarSaveCall.Current };
            AvatarSaveCall.Current = __state;
            return true;
        }
        catch (Exception exception)
        {
            __result = false;
            runtime?.Trace("SAVE REFUSED: " + exception.Message);
            try { callback?.Invoke(false, null!); }
            catch (Exception callbackFailure) { runtime?.Trace("Save-failure callback threw: " + callbackFailure.Message); }
            return false;
        }
    }
    [HarmonyFinalizer]
    private static Exception? Finalizer(Exception? __exception, AvatarSaveCall? __state)
    {
        if (__state is not null) AvatarSaveCall.Current = __state.Previous;
        return __exception;
    }
}

[HarmonyPatch(typeof(GSD1.SaveDataManager), "CopyGameWorkToSaveData")]
internal static class AvatarSaveCapturePatch
{
    [HarmonyPostfix]
    private static void Postfix(GSD1.SaveDataManager.SaveData save_data, bool __result)
    {
        var call = AvatarSaveCall.Current;
        if (call is null) return;
        try
        {
            var runtime = AvatarRuntime.Current ?? throw new InvalidOperationException("Avatar runtime unavailable.");
            if (!__result || !runtime.IsActive) throw new InvalidOperationException("Native snapshot/active pilot unavailable.");
            string identity = runtime.WriteCampaignBinding(save_data);
            call.BindingFailed = false;
            runtime.Trace("SAVE BOUND: canonical native snapshot=" + identity + "; profile=" + runtime.CampaignFingerprint);
        }
        catch (Exception exception) { call.BindingFailed = true; call.Failure = exception.Message; }
    }
}

[HarmonyPatch(typeof(global::SystemSave), nameof(global::SystemSave.Save),
    new Type[] { typeof(string), typeof(string), typeof(global::SystemSave.DataSize), typeof(Il2CppSystem.Action<bool>), typeof(bool) })]
internal static class AvatarStorageGuardPatch
{
    [HarmonyPrefix]
    private static bool Prefix(Il2CppSystem.Action<bool> cb, ref bool __result)
    {
        var call = AvatarSaveCall.Current;
        if (call is null || !call.BindingFailed) return true;
        __result = false;
        AvatarRuntime.Current?.Trace("SAVE STORAGE BLOCKED: " + call.Failure);
        try { cb?.Invoke(false); }
        catch (Exception callbackFailure) { AvatarRuntime.Current?.Trace("Storage-failure callback threw: " + callbackFailure.Message); }
        return false;
    }
}

[HarmonyPatch(typeof(GSD1.SaveDataManager), "CopySaveDataToGameWork")]
internal static class AvatarSaveRestorePatch
{
    [HarmonyPrefix]
    private static void Prefix(GSD1.SaveDataManager.SaveData save_data)
    {
        ViktorStart.Cancel();
        AvatarRuntime.Current?.PrepareLoad(save_data);
    }
    [HarmonyPostfix]
    private static void Postfix(bool __result)
    {
        var runtime = AvatarRuntime.Current;
        if (runtime is null) return;
        runtime.CompleteFixedLoad();
        if (!__result) runtime.Fail("Native save restoration failed.");
        else if (runtime.IsAdmitted) runtime.EnsureProxy("bound save restore (no donor stats copied)", false, force: true);
    }
    [HarmonyFinalizer]
    private static Exception? Finalizer(Exception? __exception)
    {
        if (__exception is not null) AvatarRuntime.Current?.Fail("Native save restoration threw: " + __exception.Message);
        return __exception;
    }
}
