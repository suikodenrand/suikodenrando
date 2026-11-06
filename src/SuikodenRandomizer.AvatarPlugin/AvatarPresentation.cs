extern alias GSD1;

using HarmonyLib;
using SuikodenRandomizer.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace SuikodenRandomizer.AvatarPlugin;

/// <summary>One projection per native display call chain; no actor or party ID writes.</summary>
internal static class AvatarPresentation
{
    [ThreadStatic] private static int _nameDepth;
    [ThreadStatic] private static int _faceDepth;
    internal static bool ResolvingFaces => _faceDepth > 0;
    internal static bool ResolvingNames => _nameDepth > 0;
    private static readonly HashSet<string> Reported = new HashSet<string>(StringComparer.Ordinal);

    internal static bool TryGetActive([NotNullWhen(true)] out AvatarRuntime? runtime)
    {
        runtime = AvatarRuntime.Current;
        return runtime is not null && runtime.IsActive;
    }
    internal static void Reset()
    {
        Reported.Clear();
    }

    internal static bool BeginName(ref int characterId)
    {
        if (!TryGetActive(out var runtime)) return false;
        // GetCharacterName may call GetCharacterNameTextID internally. Every
        // eligible donor is also a source, so a nested second projection would
        // apply the permutation twice. Scope covers protected IDs as well.
        if (_nameDepth++ == 0)
        {
            // The protagonist is player-named and referenced by story UI.
            if (characterId != CombatCatalog.TirId)
                characterId = runtime.MapDisplayId(characterId);
            Once(runtime, "name", "Native name chain projects exactly once; canonical caller IDs unchanged.");
        }
        return true;
    }
    internal static void EndName(bool entered) { if (entered) _nameDepth--; }

    internal static bool BeginFaceCharacter(ref ushort characterId)
    {
        if (!TryGetActive(out var runtime)) return false;
        // The instance converter may invoke the static converter. Both see
        // character IDs, so map only the outermost request. Keep the scope
        // active throughout native conversion: a PLAYER_DATA lookup must read
        // the untouched donor asset, not that donor role's shuffled proxy.
        if (_faceDepth++ == 0)
        {
            ushort source = characterId;
            int donor = runtime.MapDisplayId(source);
            characterId = checked((ushort)donor);
            if (runtime.IsRole(source))
                Once(runtime, "face-character:" + source, "Portrait character projection role=" + source
                    + " -> donor=" + donor + "; native face conversion retained; sprite IDs are not remapped.");
        }
        return true;
    }
    internal static void EndFace(bool entered) { if (entered) _faceDepth--; }

    internal static void ObserveFaceResult(ushort characterId, short faceId, bool entered)
    {
        if (!entered || _faceDepth != 1 || !TryGetActive(out var runtime)
            || !runtime.IsRole(characterId)) return;
        Once(runtime, "face-result:" + characterId, "Native portrait conversion donor=" + characterId
            + "; face=" + faceId + "; sprite lookup left native.");
    }

    internal static bool TryCreateFieldSprite(GSD1.Village_c village, int canonicalImageId,
        int direction, ref GSD1.AnimeSPRITE result)
    {
        if (!TryGetActive(out var runtime) || !runtime.IsRole(canonicalImageId)) return false;
        int donorId = runtime.MapDisplayId(canonicalImageId);
        if (donorId == canonicalImageId) return false;
        if (direction < 0 || direction > 3)
        {
            Once(runtime, "field-pose-fallback", "Native field sprite retained for unsupported initial direction/pose.");
            return false;
        }
        GSD1.AnimeSPRITE? prepared = null;
        try
        {
            // cache[donor] now belongs to that donor's assigned avatar. Read
            // the pristine original donor asset to avoid a second shuffle.
            if (!runtime.TryGetOriginal(donorId, out var donor) || donor.field_data?.opd_data is null)
            {
                Once(runtime, "field-asset-fallback", "Native field sprite retained: original donor field asset unavailable.");
                return false;
            }
            var opd = donor.field_data.opd_data;
            prepared = village.Create2DAnimeHuman(opd, direction);
            if (prepared is null || prepared.opd_data?.Pointer != opd.Pointer
                || prepared.opd_sprite is null || prepared.opd_sprite.data?.Pointer != opd.Pointer
                || prepared.anime_no != direction || prepared.pattern_kazu <= 0
                || prepared.pattern_no < 0 || prepared.pattern_no >= prepared.pattern_kazu
                || prepared.pattern_table is null)
            {
                Once(runtime, "field-factory-fallback", "Native field sprite retained: donor factory postcondition failed.");
                return false;
            }
            result = prepared; prepared = null;
            Once(runtime, "field-sprite", "Original donor field sprite supplied; actor role/image IDs stay canonical.");
            return true;
        }
        catch (Exception exception)
        {
            Once(runtime, "field-exception", "Native field sprite retained after " + exception.GetType().Name + ".");
            return false;
        }
        finally
        {
            if (prepared is not null)
            {
                try { GSD1.AnimeSpriteManager.Destroy2DAnime(ref prepared); }
                catch (Exception exception) { Once(runtime, "field-cleanup", "Unused sprite cleanup: " + exception.GetType().Name); }
            }
        }
    }
    private static void Once(AvatarRuntime runtime, string key, string message)
    {
        if (Reported.Count >= 32 || !Reported.Add(key)) return;
        runtime.Trace("[AVATAR PRESENTATION] " + message);
    }
}

[HarmonyPatch(typeof(GSD1.Fhuman_c), "GetCharacterName", new Type[] { typeof(int), typeof(bool) })]
internal static class AvatarNamePatch
{
    [HarmonyPrefix] private static void Prefix(ref int __0, out bool __state) => __state = AvatarPresentation.BeginName(ref __0);
    [HarmonyFinalizer] private static Exception? Finalizer(Exception? __exception, bool __state)
    { AvatarPresentation.EndName(__state); return __exception; }
}
[HarmonyPatch(typeof(GSD1.Fhuman_c), "GetCharacterNameTextID", new Type[] { typeof(int), typeof(bool) })]
internal static class AvatarNameTextIdPatch
{
    [HarmonyPrefix] private static void Prefix(ref int __0, out bool __state) => __state = AvatarPresentation.BeginName(ref __0);
    [HarmonyFinalizer] private static Exception? Finalizer(Exception? __exception, bool __state)
    { AvatarPresentation.EndName(__state); return __exception; }
}
[HarmonyPatch(typeof(GSD1.Ws_face_c), "CharacterIDConvertToFaceIDStatic", new Type[] { typeof(ushort) })]
internal static class AvatarStaticFaceCharacterPatch
{
    [HarmonyPrefix] private static void Prefix(ref ushort __0, out bool __state) => __state = AvatarPresentation.BeginFaceCharacter(ref __0);
    [HarmonyPostfix] private static void Postfix(ushort __0, short __result, bool __state) => AvatarPresentation.ObserveFaceResult(__0, __result, __state);
    [HarmonyFinalizer] private static Exception? Finalizer(Exception? __exception, bool __state)
    { AvatarPresentation.EndFace(__state); return __exception; }
}
[HarmonyPatch(typeof(GSD1.Ws_face_c), "CharacterIDConvertToFaceID", new Type[] { typeof(ushort) })]
internal static class AvatarFaceCharacterPatch
{
    [HarmonyPrefix] private static void Prefix(ref ushort __0, out bool __state) => __state = AvatarPresentation.BeginFaceCharacter(ref __0);
    [HarmonyPostfix] private static void Postfix(ushort __0, short __result, bool __state) => AvatarPresentation.ObserveFaceResult(__0, __result, __state);
    [HarmonyFinalizer] private static Exception? Finalizer(Exception? __exception, bool __state)
    { AvatarPresentation.EndFace(__state); return __exception; }
}
[HarmonyPatch(typeof(GSD1.Village_c), "h2DACreate", new Type[] { typeof(int), typeof(int) })]
internal static class AvatarFieldSpritePatch
{
    [HarmonyPrefix]
    private static bool Prefix(GSD1.Village_c __instance, int __0, int __1, ref GSD1.AnimeSPRITE __result) =>
        !AvatarPresentation.TryCreateFieldSprite(__instance, __0, __1, ref __result);
}
