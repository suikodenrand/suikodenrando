extern alias GSD2;

using System;
using System.Reflection;
using HarmonyLib;

namespace SuikodenRandomizer.Suikoden2Plugin;

/// <summary>
/// Fancy Lad's native iterator requests original donor face IDs 32, 33 and 2.
/// Scope each actual MoveNext so those requested resource IDs are not
/// projected through another role's mapping.
/// </summary>
[HarmonyPatch]
internal static class Gsd2UniteRawResourcesPatch
{
    internal const string OwnerMemberName = nameof(GSD2.Unite.Unite41._Load_d__19.__4__this);

    [HarmonyPrepare]
    private static bool Prepare() => Gsd2ShuffledUnites.Enabled;

    [HarmonyTargetMethod]
    internal static MethodBase Target()
        => ResolveTarget(typeof(GSD2.Unite.Unite41), typeof(GSD2.Unite.Unite41._Load_d__19));

    // Keep the resolver independent of native execution so the identical checks
    // can run against the installed interop assembly in MetadataLoadContext.
    internal static MethodBase ResolveTarget(Type parent, Type iterator)
    {
        if (parent is null) throw new ArgumentNullException(nameof(parent));
        if (iterator is null) throw new ArgumentNullException(nameof(iterator));
        if (iterator.DeclaringType != parent)
            throw new InvalidOperationException("The S2 Fancy Lad iterator has an unexpected owner type.");
        const BindingFlags members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        var owner = iterator.GetProperty(OwnerMemberName, members)
            ?? throw new MissingMemberException(iterator.FullName, OwnerMemberName);
        var getter = owner.GetGetMethod(true);
        if (owner.PropertyType != parent || owner.GetIndexParameters().Length != 0
            || getter is null || getter.IsStatic || getter.ReturnType != parent || getter.GetParameters().Length != 0)
            throw new InvalidOperationException("The S2 Fancy Lad iterator owner property has an unexpected signature.");
        var method = iterator.GetMethod("MoveNext", members, null, Type.EmptyTypes, null)
            ?? throw new MissingMethodException(iterator.FullName, "MoveNext");
        // Metadata-only Type objects belong to their own type universe. Require
        // the real primitive Boolean rather than comparing across universes.
        if (method.IsStatic || !method.ReturnType.IsPrimitive || method.ReturnType.FullName != "System.Boolean"
            || method.DeclaringType != iterator)
            throw new InvalidOperationException("The S2 Fancy Lad iterator MoveNext has an unexpected signature.");
        return method;
    }

    [HarmonyPrefix]
    private static void Prefix(GSD2.Unite.Unite41._Load_d__19 __instance, out Gsd2ShuffledUnites.RawResourceScope? __state)
    {
        __state = null;
        var parent = __instance.__4__this
            ?? throw new InvalidOperationException("The S2 Fancy Lad resource iterator lost its native owner.");
        if (parent.Pointer == IntPtr.Zero)
            throw new InvalidOperationException("The S2 Fancy Lad resource iterator has an invalid native owner.");
        __state = Gsd2ShuffledUnites.BeginRawResourceScope(parent.Pointer);
    }

    [HarmonyFinalizer]
    private static Exception? Finalizer(Exception? __exception, Gsd2ShuffledUnites.RawResourceScope? __state)
        => Gsd2ShuffledUnites.Finish(__state, __exception);
}
