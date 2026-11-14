using System;
using HarmonyLib;

namespace SuikodenRandomizer.Suikoden2Plugin;

[HarmonyPatch(typeof(global::SystemSave), nameof(global::SystemSave.Save), new Type[] { typeof(string), typeof(string), typeof(global::SystemSave.DataSize), typeof(Il2CppSystem.Action<bool>), typeof(bool) })]
internal static class Gsd2StorageBindingPatch
{
    [HarmonyPrefix]
    private static bool Prefix(string __1, Il2CppSystem.Action<bool> cb, ref bool __result)
    {
        var scope = Gsd2SaveScope.Current;
        if (scope is null) return true;
        if (!scope.IsCurrent)
        {
            if (scope.BindCampaign && Gsd2Runtime.MayOwnSaveContext())
                return RefuseChangedOwnership(scope, cb, ref __result);
            return true;
        }
        if (!scope.BindCampaign)
        {
            try { if (scope.CaptureAllowed) Gsd2CheckpointCapture.Export(__1); }
            catch (Exception exception) { Gsd2Plugin.PluginLog.LogWarning("Native checkpoint export failed; the original manual save continues: " + exception.Message); }
            return true;
        }
        try
        {
            scope.StorageRejected = false;
            if (scope.InitializeCampPosition && scope.Ready)
                throw new InvalidOperationException("The initial camp snapshot has already reached storage.");
            scope.Ready = false;
            Gsd2InitialSavePosition.ValidateStorage(__1, scope);
            var runtime = scope.Runtime ?? throw new InvalidOperationException("The Suikoden II save scope has no runtime owner.");
            bool bound = runtime.BindSnapshot(__1);
            if (!bound || !scope.IsCurrent)
            {
                if (!Gsd2Runtime.MayOwnSaveContext()) return true;
                return RefuseChangedOwnership(scope, cb, ref __result);
            }
            if (scope.InitializeCampPosition) runtime.MarkCampInitialSnapshotBound();
            scope.Ready = true; return true;
        }
        catch (Exception exception)
        {
            scope.Failure = exception.Message; scope.Ready = false; scope.StorageRejected = true; __result = false;
            if (scope.InitializeCampPosition) scope.Runtime?.Fail("Suikoden II camp initial save refused: " + exception.Message);
            scope.Runtime?.Trace("SAVE STORAGE REFUSED: " + exception.Message);
            try { cb?.Invoke(false); } catch { }
            return false;
        }
    }
    private static bool RefuseChangedOwnership(Gsd2SaveScope scope, Il2CppSystem.Action<bool> callback, ref bool result)
    {
        scope.Ready = false;
        scope.StorageRejected = true;
        scope.Failure = "Suikoden II save ownership changed before its campaign binding was confirmed.";
        result = false;
        scope.Runtime?.Trace("SAVE STORAGE REFUSED: " + scope.Failure);
        try { callback?.Invoke(false); } catch { }
        return false;
    }
}
