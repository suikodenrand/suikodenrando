extern alias GSD2;

using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace SuikodenRandomizer.Suikoden2Plugin;

[HarmonyPatch(typeof(GSD2.GRChapterManager), "Update")]
internal static class Gsd2GraphicsAdmissionPatch
{
    [HarmonyPrefix]
    private static bool Prefix() => Gsd2Runtime.Current?.HoldNativeUpdate != true;
}

[HarmonyPatch(typeof(GSD2.TitleChapter), "TitleMain")]
internal static class Gsd2NewGamePatch
{
    private static IntPtr _instance;
    private static int _step = -1;
    private static long _epoch = long.MinValue;
    [HarmonyPrefix]
    private static void Prefix(GSD2.TitleChapter __instance)
    {
        try
        {
            if (!Gsd2Runtime.ExecutionAllowed()) return;
            Gsd2Runtime.Current?.ObserveOwnedBoundary();
            long epoch = Gsd2Runtime.OwnershipEpoch();
            int step = __instance.step;
            if (_instance == __instance.Pointer && _step == step && _epoch == epoch) return;
            _instance = __instance.Pointer; _step = step; _epoch = epoch;
            // Original GSD2 TitleChapter.State.DifficultyInit, independently decoded from metadata.
            if (step == 17) Gsd2Runtime.Current?.BeginNewGame();
            else if (step == 9 || step == 10) Gsd2Runtime.Current?.CancelPendingNewGame();
        }
        catch (Exception exception) { Gsd2Runtime.Current?.Fail("Suikoden II New Game boundary: " + exception.Message); }
    }
}

[HarmonyPatch(typeof(GSD2.TitleChapter._MapSetup_d__43), "MoveNext")]
internal static class Gsd2ModernNewGamePatch
{
    [ThreadStatic] internal static int NativeNewGameDepth;
    [ThreadStatic] private static NativeCall? _call;
    private sealed class NativeCall
    {
        internal NativeCall? Previous;
        internal Gsd2Runtime? Runtime;
        internal long Epoch;
        internal bool IsCurrent => Gsd2Runtime.ExecutionAllowed() && Epoch == Gsd2Runtime.OwnershipEpoch()
            && (Runtime is null ? Gsd2Runtime.Current is null : ReferenceEquals(Runtime, Gsd2Runtime.Current) && Runtime.MatchesExecution(Epoch));
    }
    internal static bool CurrentBoundaryIsOwned => _call?.IsCurrent == true;
    [HarmonyPrefix]
    private static void Prefix(GSD2.TitleChapter._MapSetup_d__43 __instance, out bool __state)
    {
        __state = Gsd2Runtime.ExecutionAllowed() && __instance.isNewgame;
        if (__state)
        {
            _call = new NativeCall { Previous = _call, Runtime = Gsd2Runtime.Current, Epoch = Gsd2Runtime.OwnershipEpoch() };
            NativeNewGameDepth++;
        }
    }
    [HarmonyPostfix]
    private static void Postfix(GSD2.TitleChapter._MapSetup_d__43 __instance, bool __result)
    {
        if (!CurrentBoundaryIsOwned || __result || !__instance.isNewgame) return;
        try { Gsd2Runtime.Current?.CompleteNativeNewGame(true); }
        catch (Exception exception) { Gsd2Runtime.Current?.Fail("Suikoden II modern start initialization: " + exception.Message); }
    }
    [HarmonyFinalizer]
    private static Exception? Finalizer(Exception? __exception, bool __state)
    {
        if (__state) { _call = _call?.Previous; NativeNewGameDepth--; }
        return __exception;
    }
}

[HarmonyPatch]
internal static class Gsd2NativeDatasetPatch
{
    [HarmonyTargetMethod]
    private static MethodBase Target() => typeof(GSD2.BootProgram).GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)
        .Where(type => type.Name.Contains("gamework_dataset", StringComparison.Ordinal))
        .Select(type => AccessTools.Method(type, "MoveNext")).Single(method => method is not null)!;
    [HarmonyPostfix]
    private static void Postfix(bool __result)
    {
        if (__result || Gsd2Runtime.Current?.OwnsExecution != true) return;
        try { Gsd2Runtime.Current?.ObserveNativeDataset(); }
        catch (Exception exception) { Gsd2Runtime.Current?.Fail("Suikoden II original template capture: " + exception.Message); }
    }
}

[HarmonyPatch(typeof(GSD2.BootProgram), "BoNewGameSet")]
internal static class Gsd2NativeNewGamePatch
{
    [HarmonyPostfix]
    private static void Postfix()
    {
        if (Gsd2Runtime.Current?.OwnsExecution != true) return;
        try { Gsd2Runtime.Current?.CompleteNativeNewGame(); }
        catch (Exception exception) { Gsd2Runtime.Current?.Fail("Suikoden II start initialization: " + exception.Message); }
    }
}

// Native G2_pamem_in only publishes party occupancy. Run the first-join
// transaction before the recruiting event continues with later item grants.
[HarmonyPatch(typeof(GSD2.G2_SYS), "G2_pamem_in", new[] { typeof(int), typeof(int), typeof(int) })]
internal static class Gsd2RecruitmentEquipmentBoundaryPatch
{
    [HarmonyPostfix]
    private static void Postfix() => Gsd2Runtime.Current?.Tick();
}

[HarmonyPatch(typeof(GSD2.UISaveLoad2), "ConfirmDecide")]
internal static class Gsd2ManualSaveCaptureScope
{
    [ThreadStatic] internal static int Depth;
    [HarmonyPrefix]
    private static void Prefix(out bool __state)
    {
        __state = Gsd2Runtime.ExecutionAllowed() && Gsd2Plugin.NativeCheckpointCapture;
        if (__state) Depth++;
    }
    [HarmonyFinalizer]
    private static Exception? Finalizer(Exception? __exception, bool __state)
    {
        if (__state) Depth--;
        return __exception;
    }
}

[HarmonyPatch(typeof(GSD2.GSD2SaveData), "SaveFile")]
internal static class Gsd2SavePreparationPatch
{
    [HarmonyPrefix]
    private static bool Prefix(Il2CppSystem.Action<bool, GSD2.GSD2SaveData.TmpSave> callback, bool __2, ref bool __result, out Gsd2SaveScope? __state)
    {
        __state = null;
        if (!Gsd2Runtime.ExecutionAllowed())
        {
            var retired = Gsd2Runtime.Current;
            if (!Gsd2Plugin.NativeCheckpointCapture && retired?.HasCampaignSaveProtection == true && Gsd2Runtime.MayOwnSaveContext())
                return RefuseSuspended(retired, callback, ref __result);
            return true;
        }
        if (Gsd2Plugin.NativeCheckpointCapture)
        {
            __state = new Gsd2SaveScope { Previous = Gsd2SaveScope.Current,
                CaptureAllowed = Gsd2ManualSaveCaptureScope.Depth > 0 && !__2 };
            Gsd2SaveScope.Current = __state;
            return true;
        }
        var runtime = Gsd2Runtime.Current;
        if (runtime is not null && !runtime.OwnsExecution)
        {
            if (runtime.HasCampaignSaveProtection && Gsd2Runtime.MayOwnSaveContext())
                return RefuseSuspended(runtime, callback, ref __result);
            return true;
        }
        if (runtime is null) return true;
        if (!runtime.IsAdmitted && runtime.FaultReason is null)
        {
            if (runtime.HasAdmittedCampaignBeforeSuspend && Gsd2Runtime.MayOwnSaveContext())
                return RefuseSuspended(runtime, callback, ref __result);
            return true;
        }
        try
        {
            // The original coroutine's only GSD2 save occurs after import and
            // final hero leveling, immediately before its false completion.
            // Initialize at that exact boundary so the first autosave is bound.
            if (Gsd2ModernNewGamePatch.CurrentBoundaryIsOwned && !__2)
                runtime.CompleteNativeNewGame(true);
            long epoch = runtime.ExecutionEpoch;
            runtime.BeginSave();
            if (!ReferenceEquals(runtime, Gsd2Runtime.Current) || !runtime.MatchesExecution(epoch))
            {
                if (Gsd2Runtime.MayOwnSaveContext()) return RefuseSuspended(runtime, callback, ref __result);
                return true;
            }
            __state = new Gsd2SaveScope { Previous = Gsd2SaveScope.Current, Runtime = runtime, BindCampaign = true,
                InitializeCampPosition = Gsd2ModernNewGamePatch.CurrentBoundaryIsOwned && !__2 && runtime.NeedsCampInitialSavePosition };
            Gsd2SaveScope.Current = __state; return true;
        }
        catch (Exception exception)
        {
            __result = false; runtime.Trace("SAVE REFUSED: " + exception.Message);
            try { callback?.Invoke(false, null!); } catch { }
            return false;
        }
    }
    private static bool RefuseSuspended(Gsd2Runtime runtime,
        Il2CppSystem.Action<bool, GSD2.GSD2SaveData.TmpSave> callback, ref bool result)
    {
        result = false;
        runtime.Trace("SAVE REFUSED: the Suikoden II campaign lost randomizer admission; load a bound save or start a New Game before saving.");
        try { callback?.Invoke(false, null!); } catch { }
        return false;
    }
    [HarmonyFinalizer]
    private static Exception? Finalizer(Exception? __exception, Gsd2SaveScope? __state)
    {
        if (__state is not null)
        {
            try { if (__state.IsCurrent) __state.Runtime?.EndSave(); }
            finally { Gsd2SaveScope.Current = __state.Previous; }
        }
        return __exception;
    }
}

[HarmonyPatch(typeof(GSD2.MACHICON), "MachiInit")]
internal static class Gsd2CampMapObservationPatch
{
    [HarmonyPostfix]
    private static void Postfix(GSD2.MACHICON __result)
    {
        if (__result is not null && Gsd2Runtime.Current?.OwnsExecution == true) Gsd2Runtime.Current.ObserveCampMap(__result);
    }
}

[HarmonyPatch(typeof(GSD2.MACHICON), "MachiMain")]
internal static class Gsd2CampPlayerObservationPatch
{
    [HarmonyPrefix]
    private static bool Prefix(GSD2.MACHICON __instance)
    {
        var runtime = Gsd2Runtime.Current;
        if (runtime?.OwnsExecution != true) return true;
        runtime.VerifyCampPlayer(__instance);
        return runtime.FaultReason is null;
    }
}

[HarmonyPatch(typeof(GSD2.GSD2SaveData), "LoadDataCore")]
internal static class Gsd2LoadPatch
{
    internal sealed class Call
    {
        internal Gsd2Runtime Runtime = null!;
        internal long Epoch;
        internal bool Prepared;
        internal bool IsCurrent => ReferenceEquals(Runtime, Gsd2Runtime.Current) && Runtime.MatchesExecution(Epoch);
    }
    [HarmonyPrefix]
    private static bool Prefix(string __0, out Call? __state)
    {
        __state = null;
        if (!Gsd2Runtime.ExecutionAllowed()) return true;
        var runtime = Gsd2Runtime.Current;
        if (runtime is null || !runtime.ObserveOwnedBoundary()) return true;
        __state = new Call { Runtime = runtime, Epoch = runtime.ExecutionEpoch };
        try { runtime.PrepareLoad(__0); __state.Prepared = true; return true; }
        catch (Exception exception) { runtime.Fail("Suikoden II save binding rejected: " + exception.Message); return false; }
    }
    [HarmonyPostfix]
    private static void Postfix(Call? __state)
    {
        if (__state?.IsCurrent != true || !__state.Prepared) return;
        try { __state.Runtime.CompleteLoad(true); }
        catch (Exception exception) { __state.Runtime.Fail("Suikoden II save reconstruction: " + exception.Message); }
    }
    [HarmonyFinalizer]
    private static Exception? Finalizer(Exception? __exception, Call? __state)
    {
        if (__exception is not null && __state?.IsCurrent == true) __state.Runtime.CompleteLoad(false);
        return __exception;
    }
}
