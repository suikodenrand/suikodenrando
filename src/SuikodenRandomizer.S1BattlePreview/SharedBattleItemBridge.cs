using System;
using System.Linq;
using System.Reflection;
using SuikodenRandomizer.Shared;

namespace SuikodenRandomizer.S1BattlePreview;

// The battle preview also runs as a standalone plugin, without the shared-inventory host.
internal static class SharedBattleItemBridge
{
    private sealed record Host(Func<bool> Enabled, Func<int, int, bool> Reserve,
        Action<int> Release, Func<int, bool> IsPending, Func<int, int, int> ReservedPocket, Func<int, int, int> AvailableUses);

    private static readonly Lazy<Host?> Api = new(Bind);
    internal static bool Enabled => Api.Value?.Enabled() == true;
    internal static bool Reserve(int slot, int token) => Enabled
        ? Api.Value!.Reserve(slot, token) : token is >= 0 and < S1BattleItemAddress.Pockets;
    internal static bool IsPending(int slot) => Api.Value?.IsPending(slot) == true;
    internal static int ReservedPocket(int slot, int token) => Api.Value?.ReservedPocket(slot, token) ?? -1;
    internal static int AvailableUses(int slot, int token) => Api.Value?.AvailableUses(slot, token) ?? 0;
    internal static void Release(int slot) => Api.Value?.Release(slot);

    private static Host? Bind()
    {
        var type = AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetType("SuikodenRandomizer.AvatarPlugin.SharedBattleItems", false))
            .FirstOrDefault(t => t is not null);
        const BindingFlags flags = BindingFlags.Public | BindingFlags.Static;
        if (type?.GetProperty("Enabled", flags)?.GetMethod is not { } enabled
            || type.GetMethod("ReserveOverlayItem", flags, new[] { typeof(int), typeof(int) }) is not { } reserve
            || type.GetMethod("ReleaseItem", flags, new[] { typeof(int) }) is not { } release
            || type.GetMethod("IsItemPending", flags, new[] { typeof(int) }) is not { } pending
            || type.GetMethod("ReservedItemPocket", flags, new[] { typeof(int), typeof(int) }) is not { } pocket
            || type.GetMethod("AvailableUses", flags, new[] { typeof(int), typeof(int) }) is not { } uses) return null;
        return new(enabled.CreateDelegate<Func<bool>>(), reserve.CreateDelegate<Func<int, int, bool>>(),
            release.CreateDelegate<Action<int>>(), pending.CreateDelegate<Func<int, bool>>(),
            pocket.CreateDelegate<Func<int, int, int>>(), uses.CreateDelegate<Func<int, int, int>>());
    }
}
