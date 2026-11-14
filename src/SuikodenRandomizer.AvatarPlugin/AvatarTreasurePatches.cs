extern alias GSD1;

using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using SuikodenRandomizer.Core.Treasure;
using System;

namespace SuikodenRandomizer.AvatarPlugin;

[HarmonyPatch(typeof(GSD1.Event_c), nameof(GSD1.Event_c.eventItem))]
internal static class AvatarTreasureItemPatch
{
    private sealed class CommandSwap
    {
        internal readonly AvatarRuntime Runtime;
        internal readonly Il2CppStructArray<byte> Original;
        internal readonly Il2CppStructArray<byte> Replacement;
        internal readonly string Description;

        internal CommandSwap(AvatarRuntime runtime, Il2CppStructArray<byte> original,
            Il2CppStructArray<byte> replacement, string description)
        {
            Runtime = runtime;
            Original = original;
            Replacement = replacement;
            Description = description;
        }
    }

    [HarmonyPrefix]
    private static bool Prefix(GSD1.Event_c __instance, out CommandSwap? __state)
    {
        __state = null;
        var runtime = AvatarRuntime.Current;
        if (runtime?.TreasureActive != true || runtime.TreasureProfile is not { } profile) return true;

        try
        {
            var command = __instance.eventComTMP;
            int operand = __instance.eventComTMPindex;
            if (command is null || operand <= 0 || operand >= command.Count || command[operand] >= 0xFD
                || command[operand - 1] != 0x49) return true;
            var nativeChest = __instance.eventWK;
            var placement = nativeChest?.eventPut;
            if (nativeChest is not { flag1: 0x20 } || placement is not { Count: >= 5 }
                || placement[0] != 8 || placement[3] != 6 || placement[4] != 0
                || !S1TreasureCatalog.TryGetChest(placement[1], placement[2], out var chest)
                || !chest.IsShuffleEligible || operand != chest.ItemOperandOffset) return true;

            var bytes = new byte[command.Count];
            for (int index = 0; index < bytes.Length; index++) bytes[index] = command[index];
            if (!chest.MatchesOriginalScript(bytes, operand))
            {
                runtime.Trace("TREASURE SKIPPED chest=" + chest.LocationId + "; native script differs from the admitted catalog.");
                return true;
            }
            if (!profile.TryGetReward(chest.LocationId, out int itemId))
                throw new InvalidOperationException("The saved treasure mapping has no reward for " + chest.LocationId + ".");

            var replacement = AvatarNativeData.CloneBytes(command);
            replacement[operand] = checked((byte)itemId);
            string description = "TREASURE chest=" + chest.LocationId + "; area=" + chest.AreaName
                + "; original=" + ItemName(chest.OriginalItemId) + " (" + chest.OriginalItemId + ")"
                + "; reward=" + ItemName(itemId) + " (" + itemId + ")";
            __state = new CommandSwap(runtime, command, replacement, description);
            // The grant reads this operand once. Its native result and subsequent script keep their original flow.
            __instance.eventComTMP = replacement;
            return true;
        }
        catch (Exception exception)
        {
            runtime.Fail("Treasure reward admission failed: " + exception.Message);
            // The native grant must still initialize its item before the script finalizes the chest.
            return true;
        }
    }

    [HarmonyFinalizer]
    private static Exception? Finalizer(GSD1.Event_c __instance, CommandSwap? __state, Exception? __exception)
    {
        if (__state is null) return __exception;
        try
        {
            // Never overwrite a new script installed by another native handler or patch.
            if (__instance.eventComTMP?.Pointer == __state.Replacement.Pointer)
                __instance.eventComTMP = __state.Original;
            if (__exception is not null)
                __state.Runtime.Fail("Native treasure grant failed: " + __exception.Message);
            else
            {
                int result = __instance.eventKettei;
                if (result is not (0 or 1))
                    throw new InvalidOperationException("The native chest grant returned an unexpected result " + result + ".");
                __state.Runtime.Trace(__state.Description + "; result=" + (result == 1 ? "granted" : "inventory-full"));
            }
        }
        catch (Exception exception) { __state.Runtime.Fail("Treasure command restoration failed: " + exception.Message); }
        return __exception;
    }

    private static string ItemName(int itemId)
    {
        try
        {
            var definitions = GSD1.G_item_c_data.item_data_table;
            if (definitions is not null && itemId > 0 && itemId < definitions.Count && definitions[itemId] is { } item)
                return GSD1.G1_string_id_h.G1_SID_MSG(unchecked((int)item.name));
        }
        catch { }
        return "Item " + itemId;
    }
}
