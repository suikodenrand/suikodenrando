extern alias GSD1;

using HarmonyLib;
using SuikodenRandomizer.AvatarPlugin;
using SuikodenRandomizer.Shared;
using System;

namespace SuikodenRandomizer.Unified;

/// <summary>Scoped S1 original-text adapter. No actor IDs, resources or campaign writes.</summary>
internal static class DialogueS1
{
    [ThreadStatic] private static TransactionDepth? _transactions;
    private sealed class TransactionDepth { internal int Value; }
    private sealed class TransactionScope : IDisposable
    {
        private TransactionDepth? _depth;
        internal TransactionScope(TransactionDepth depth) { _depth = depth; depth.Value++; }
        public void Dispose()
        {
            var depth = _depth;
            if (depth is null) return;
            _depth = null;
            depth.Value--;
        }
    }

    internal static Type[] PatchTypes => new[]
    {
        typeof(DialogueS1EventMessagePatch), typeof(DialogueS1EventMessage2Patch),
        typeof(DialogueS1BacklogPatch), typeof(DialogueS1NamePatch), typeof(DialogueS1NameTextIdPatch),
        typeof(DialogueS1LoadBarrierPatch), typeof(DialogueS1SaveBarrierPatch)
    };

    internal static NativeDialogueContext? Capture()
    {
        try
        {
            if (_transactions?.Value > 0) return null;
            var runtime = AvatarRuntime.Current;
            var publication = runtime?.CaptureDialoguePublication();
            if (runtime is null || publication is null) return null;
            bool IsCurrent() => _transactions?.Value is not > 0 && publication();
            bool ShouldReplace(int role) => IsCurrent() && runtime.IsRole(role) && runtime.MapDisplayId(role) != role;
            string? Resolve(int role)
            {
                if (!IsCurrent() || !runtime.IsRole(role)) return null;
                try
                {
                    // Existing AvatarPresentation maps this canonical argument
                    // once and resolves the untouched donor asset. Never map it
                    // here first. Suppression also protects nested name lookups.
                    using (DialogueNative.SuppressNames())
                    {
                        string? value = GSD1.Fhuman_c.GetCharacterName(role, false);
                        return IsCurrent() ? value : null;
                    }
                }
                catch (Exception) { return null; }
            }
            int? MaxLength()
            {
                if (!IsCurrent()) return 0;
                try
                {
                    int value = GSD1.G1_event_h.GetETalkWinSZ;
                    // A failed or unexpected getter is not an unbounded sink.
                    return IsCurrent() && (value == 961 || value == 481) ? value : 0;
                }
                catch (Exception) { return 0; }
            }
            return new NativeDialogueContext("GSD1", IsCurrent, Resolve, MaxLength, ShouldReplace);
        }
        catch (Exception) { return null; }
    }

    internal static IDisposable? Enter()
    {
        try { return DialogueNative.Enter(ActiveGame.Gsd1, Capture); }
        catch (Exception) { return null; }
    }
    internal static IDisposable? SuppressNames()
    {
        try { return DialogueNative.SuppressNames(); }
        catch (Exception) { return null; }
    }
    internal static IDisposable? EnterTransaction()
    {
        try { return new TransactionScope(_transactions ??= new TransactionDepth()); }
        catch (Exception) { return null; }
    }
    internal static Exception? Exit(Exception? original, IDisposable? scope)
    {
        // A presentation cleanup failure must never replace a native failure
        // or turn a successful native dispatch into a campaign fault.
        try { scope?.Dispose(); }
        catch (Exception) { }
        return original;
    }
}

[HarmonyPatch(typeof(GSD1.Event_c), "SetEventMsg", new Type[] { })]
internal static class DialogueS1EventMessagePatch
{
    [HarmonyPrefix] private static void Prefix(out IDisposable? __state) => __state = DialogueS1.Enter();
    [HarmonyFinalizer] private static Exception? Finalizer(Exception? __exception, IDisposable? __state)
        => DialogueS1.Exit(__exception, __state);
}
[HarmonyPatch(typeof(GSD1.Event_c), "SetEventMsg2", new Type[] { })]
internal static class DialogueS1EventMessage2Patch
{
    [HarmonyPrefix] private static void Prefix(out IDisposable? __state) => __state = DialogueS1.Enter();
    [HarmonyFinalizer] private static Exception? Finalizer(Exception? __exception, IDisposable? __state)
        => DialogueS1.Exit(__exception, __state);
}
[HarmonyPatch(typeof(GSD1.WindowManager), "ConvertTagToStringFromLogData", new Type[] { typeof(string) })]
internal static class DialogueS1BacklogPatch
{
    [HarmonyPrefix] private static void Prefix(out IDisposable? __state) => __state = DialogueS1.Enter();
    [HarmonyFinalizer] private static Exception? Finalizer(Exception? __exception, IDisposable? __state)
        => DialogueS1.Exit(__exception, __state);
}
[HarmonyPatch(typeof(GSD1.Fhuman_c), "GetCharacterName", new Type[] { typeof(int), typeof(bool) })]
internal static class DialogueS1NamePatch
{
    [HarmonyPrefix] private static void Prefix(out IDisposable? __state) => __state = DialogueS1.SuppressNames();
    [HarmonyFinalizer] private static Exception? Finalizer(Exception? __exception, IDisposable? __state)
        => DialogueS1.Exit(__exception, __state);
}
[HarmonyPatch(typeof(GSD1.Fhuman_c), "GetCharacterNameTextID", new Type[] { typeof(int), typeof(bool) })]
internal static class DialogueS1NameTextIdPatch
{
    [HarmonyPrefix] private static void Prefix(out IDisposable? __state) => __state = DialogueS1.SuppressNames();
    [HarmonyFinalizer] private static Exception? Finalizer(Exception? __exception, IDisposable? __state)
        => DialogueS1.Exit(__exception, __state);
}
[HarmonyPatch(typeof(GSD1.SaveDataManager), "CopySaveDataToGameWork", new Type[] { typeof(GSD1.SaveDataManager.SaveData), typeof(GSD1.GAME_WORK) })]
internal static class DialogueS1LoadBarrierPatch
{
    [HarmonyPrefix, HarmonyPriority(int.MaxValue)]
    private static void Prefix(out IDisposable? __state) => __state = DialogueS1.EnterTransaction();
    [HarmonyFinalizer] private static Exception? Finalizer(Exception? __exception, IDisposable? __state)
        => DialogueS1.Exit(__exception, __state);
}
[HarmonyPatch(typeof(GSD1.SaveDataManager), "SaveGameWork", new Type[] { typeof(string), typeof(Il2CppSystem.Action<bool, GSD1.SaveDataManager.SaveData>), typeof(bool) })]
internal static class DialogueS1SaveBarrierPatch
{
    [HarmonyPrefix, HarmonyPriority(int.MaxValue)]
    private static void Prefix(out IDisposable? __state) => __state = DialogueS1.EnterTransaction();
    [HarmonyFinalizer] private static Exception? Finalizer(Exception? __exception, IDisposable? __state)
        => DialogueS1.Exit(__exception, __state);
}
