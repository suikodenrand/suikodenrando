extern alias GSD1;

using HarmonyLib;
using System;

namespace SuikodenRandomizer.AvatarPlugin;

[HarmonyPatch(typeof(GSD1.SaveDataManager), "SaveGameWork")]
internal static class AvatarSavePreparationPatch
{
    [HarmonyPrefix]
    private static bool Prefix(ref bool __result, Il2CppSystem.Action<bool, GSD1.SaveDataManager.SaveData> callback,
        out AvatarSaveCall? __state)
    {
        __state = null;
        var runtime = AvatarRuntime.Current;
        if (runtime is not null && !runtime.OwnsExecution) return true;
        try
        {
            if (runtime is null || !runtime.IsAdmitted || runtime.FaultReason is not null)
                throw new InvalidOperationException(runtime?.FaultReason ?? "This shuffle saves only its own New Game/bound campaign; pilot/older saves are read-only.");
            if (!runtime.EnsureProxy("save preparation", false, force: true))
                throw new InvalidOperationException("Complete shuffle roster is not ready; save again after native initialization completes.");
            runtime.VerifyFinalParty("save preparation");
            runtime.CheckCampaignBindingWritable();
            runtime.CheckTreasureBindingWritable();
            runtime.CheckJoiningWeaponBindingWritable();
            __state = new AvatarSaveCall { Previous = AvatarSaveCall.Current, Runtime = runtime,
                ExecutionEpoch = runtime.ExecutionEpoch };
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
            var runtime = call.Runtime;
            if (!runtime.MatchesExecution(call.ExecutionEpoch)) return;
            if (!__result || !runtime.IsActive) throw new InvalidOperationException("Native snapshot/active pilot unavailable.");
            string identity = runtime.WriteCampaignBinding(save_data);
            runtime.WriteTreasureBinding(save_data);
            runtime.WriteJoiningWeaponBinding(save_data);
            call.BindingFailed = false;
            runtime.Trace("SAVE BOUND: canonical native snapshot=" + identity + "; profile=" + runtime.CampaignFingerprint);
        }
        catch (Exception exception) { call.BindingFailed = true; call.Failure = exception.Message; }
    }
}

[HarmonyPatch(typeof(GSD1.SaveDataManager), "CopySaveDataToGameWork")]
internal static class AvatarSaveRestorePatch
{
    internal sealed class LoadCall
    {
        internal AvatarRuntime Runtime = null!;
        internal long ExecutionEpoch;
    }
    [HarmonyPrefix]
    private static void Prefix(GSD1.SaveDataManager.SaveData save_data, out LoadCall? __state)
    {
        __state = null;
        var runtime = AvatarRuntime.Current;
        if (runtime is null || !runtime.OwnsExecution) return;
        ViktorStart.Cancel();
        runtime.PrepareLoad(save_data);
        __state = new LoadCall { Runtime = runtime, ExecutionEpoch = runtime.ExecutionEpoch };
    }
    [HarmonyPostfix]
    private static void Postfix(bool __result, LoadCall? __state)
    {
        if (__state is null || !__state.Runtime.MatchesExecution(__state.ExecutionEpoch)) return;
        var runtime = __state.Runtime;
        runtime.CompleteFixedLoad();
        if (!__result) runtime.Fail("Native save restoration failed.");
        else if (runtime.IsAdmitted) runtime.EnsureProxy("bound save restore (no donor stats copied)", false, force: true);
    }
    [HarmonyFinalizer]
    private static Exception? Finalizer(Exception? __exception, LoadCall? __state)
    {
        if (__exception is not null && __state is not null && __state.Runtime.MatchesExecution(__state.ExecutionEpoch))
            __state.Runtime.Fail("Native save restoration threw: " + __exception.Message);
        return __exception;
    }
}
