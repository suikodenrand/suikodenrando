extern alias GSD2;

using BepInEx.Unity.IL2CPP.Utils.Collections;
using HarmonyLib;
using System;
using System.Collections;

namespace SuikodenRandomizer.Suikoden2Plugin;

/// <summary>Display-only canonical-to-donor projection, once per native call chain.</summary>
internal static class Gsd2Presentation
{
    internal static Func<bool> PreserveRawResourceIds { get; set; } = () => false;
    [ThreadStatic] private static int _faceDepth;
    [ThreadStatic] private static int _offsetDepth;

    // Runtime publishes originalNames[donor] at each canonical role's name-table
    // index. G2_name_get/_org must retain their original canonical argument:
    // projecting that argument again would read the next role in the shuffle.

    internal static bool BeginFace(ref int id)
    {
        var runtime = Gsd2Runtime.Current;
        if (runtime?.IsActive != true) return false;
        if (_faceDepth++ == 0 && !PreserveRawResourceIds()) id = runtime.MapDisplayId(id);
        return true;
    }
    internal static void EndFace(bool entered) { if (entered) _faceDepth--; }
    internal static bool BeginCharacterFace(ref byte id)
    {
        int value = id;
        bool entered = BeginFace(ref value);
        if (entered) id = checked((byte)value);
        return entered;
    }

    internal static bool BeginFaceOffset(ref ushort id)
    {
        var runtime = Gsd2Runtime.Current;
        if (runtime?.IsActive != true) return false;
        // This receives the UI's captured canonical character ID, not the
        // already-projected resource key inside ImageLoader's coroutine.
        if (_offsetDepth++ == 0 && !PreserveRawResourceIds()) id = checked((ushort)runtime.MapDisplayId(id));
        return true;
    }
    internal static void EndFaceOffset(bool entered) { if (entered) _offsetDepth--; }

    internal static Il2CppSystem.Collections.IEnumerator ScopeAsyncFace(Il2CppSystem.Collections.IEnumerator original)
        => ScopedFaceMoves(original).WrapToIl2Cpp();

    private static IEnumerator ScopedFaceMoves(Il2CppSystem.Collections.IEnumerator original)
    {
        if (original is null) yield break;
        try
        {
            while (true)
            {
                bool moved;
                Il2CppSystem.Object? yielded;
                // The constructor mapped its stored face ID. Unity executes this
                // MoveNext on later frames, after the constructor patch returned.
                // Re-enter only the no-remap scope around each actual native step.
                _faceDepth++;
                try { moved = original.MoveNext(); yielded = moved ? original.Current : null; }
                finally { _faceDepth--; }
                if (!moved) yield break;
                yield return yielded;
            }
        }
        finally { original.TryCast<Il2CppSystem.IDisposable>()?.Dispose(); }
    }
}

[HarmonyPatch(typeof(GSD2.ImageLoader), "LoadImage", new Type[] { typeof(int) })]
internal static class Gsd2SynchronousPortraitPatch
{
    [HarmonyPrefix] private static void Prefix(ref int __0, out bool __state) => __state = Gsd2Presentation.BeginFace(ref __0);
    [HarmonyFinalizer] private static Exception? Finalizer(Exception? __exception, bool __state)
    { Gsd2Presentation.EndFace(__state); return __exception; }
}
[HarmonyPatch(typeof(GSD2.ImageLoader), "GetCacheSprite", new Type[] { typeof(int) })]
internal static class Gsd2CachedPortraitPatch
{
    [HarmonyPrefix] private static void Prefix(ref int __0, out bool __state) => __state = Gsd2Presentation.BeginFace(ref __0);
    [HarmonyFinalizer] private static Exception? Finalizer(Exception? __exception, bool __state)
    { Gsd2Presentation.EndFace(__state); return __exception; }
}
[HarmonyPatch(typeof(GSD2.ImageLoader), "GetImagePath", new Type[] { typeof(int) })]
internal static class Gsd2PortraitResourcePathPatch
{
    [HarmonyPrefix] private static void Prefix(ref int __0, out bool __state) => __state = Gsd2Presentation.BeginFace(ref __0);
    [HarmonyFinalizer] private static Exception? Finalizer(Exception? __exception, bool __state)
    { Gsd2Presentation.EndFace(__state); return __exception; }
}
[HarmonyPatch(typeof(GSD2.ImageLoader), "LoadAsyncImage")]
internal static class Gsd2AsynchronousPortraitPatch
{
    [HarmonyPrefix] private static void Prefix(ref int __0, out bool __state) => __state = Gsd2Presentation.BeginFace(ref __0);
    [HarmonyPostfix] private static void Postfix(ref Il2CppSystem.Collections.IEnumerator __result, bool __state)
    { if (__state) __result = Gsd2Presentation.ScopeAsyncFace(__result); }
    [HarmonyFinalizer] private static Exception? Finalizer(Exception? __exception, bool __state)
    { Gsd2Presentation.EndFace(__state); return __exception; }
}
[HarmonyPatch(typeof(GSD2.Tc_face), "tc_InitFace", new Type[] { typeof(byte), typeof(byte) })]
internal static class Gsd2ClassicDialoguePortraitPatch
{
    // This API accepts a canonical character and then converts it through its
    // native face-ID table. Its nested ImageLoader constructor and later native
    // iterator steps must keep that resulting donor face key unchanged.
    [HarmonyPrefix] private static void Prefix(ref byte __0, out bool __state) => __state = Gsd2Presentation.BeginCharacterFace(ref __0);
    [HarmonyFinalizer] private static Exception? Finalizer(Exception? __exception, bool __state)
    { Gsd2Presentation.EndFace(__state); return __exception; }
}
[HarmonyPatch(typeof(GSD2.WindowManager), "GetPartyFaceOffset", new Type[] { typeof(ushort) })]
internal static class Gsd2PortraitOffsetPatch
{
    [HarmonyPrefix] private static void Prefix(ref ushort __0, out bool __state) => __state = Gsd2Presentation.BeginFaceOffset(ref __0);
    [HarmonyFinalizer] private static Exception? Finalizer(Exception? __exception, bool __state)
    { Gsd2Presentation.EndFaceOffset(__state); return __exception; }
}
