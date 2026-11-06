using System;
using System.Collections.Generic;

namespace SuikodenRandomizer.Core;

/// <summary>Read-only observation of one of the six native resident slots.</summary>
public sealed class PartySlotState
{
    public PartySlotState(int code, int? dataId, long assetPointer, int? baseId)
    {
        Code = code;
        DataId = dataId;
        AssetPointer = assetPointer;
        BaseId = baseId;
    }

    public int Code { get; }
    public int? DataId { get; }
    public long AssetPointer { get; }
    public int? BaseId { get; }
}

/// <summary>
/// Invariants shared by the runtime guards and dependency-free tests. Resident
/// occupancy and active membership are distinct. Empty slots may legitimately
/// retain stale data pointers after native DelPlayer; those cells are ignored.
/// This validator reports errors and never modifies the party.
/// </summary>
public static class PartyInvariants
{
    public const int SlotCount = 6;

    public static IReadOnlyList<string> Validate(IReadOnlyList<PartySlotState> slots,
        IReadOnlyList<int> activeOrder, int activeCount, bool requireActiveBases = true)
    {
        var errors = new List<string>();
        if (slots is null) errors.Add("resident slots are unavailable");
        if (activeOrder is null) errors.Add("active order is unavailable");
        if (slots is null || activeOrder is null) return errors;
        if (slots.Count != SlotCount)
            errors.Add($"expected {SlotCount} resident slots, found {slots.Count}");
        if (activeCount < 0 || activeCount > SlotCount)
            errors.Add($"active count {activeCount} is outside 0..{SlotCount}");
        if (activeOrder.Count < Math.Max(0, activeCount))
            errors.Add("active order is shorter than the active count");

        var occupiedIds = new HashSet<int>();
        var occupiedPointers = new HashSet<long>();
        for (int index = 0; index < slots.Count; index++)
        {
            PartySlotState slot = slots[index];
            if (slot is null) { errors.Add($"slot {index} has no observation"); continue; }
            if (slot.Code == -1) continue;
            if (slot.Code < -1)
            {
                errors.Add($"slot {index} has invalid empty code {slot.Code}; only -1 denotes a free slot");
                continue;
            }
            if (slot.DataId != slot.Code)
                errors.Add($"occupied slot {index} code {slot.Code} disagrees with PLAYER_DATA ID {slot.DataId?.ToString() ?? "null"}");
            if (slot.AssetPointer == 0)
                errors.Add($"occupied slot {index} has no character asset pointer");
            if (!occupiedIds.Add(slot.Code))
                errors.Add($"character ID {slot.Code} occupies more than one resident slot");
            if (slot.AssetPointer != 0 && !occupiedPointers.Add(slot.AssetPointer))
                errors.Add("two occupied slots share one character asset pointer");
        }

        var seenActiveSlots = new HashSet<int>();
        int inspectCount = Math.Min(Math.Max(activeCount, 0), Math.Min(SlotCount, activeOrder.Count));
        for (int index = 0; index < inspectCount; index++)
        {
            int residentIndex = activeOrder[index];
            if (residentIndex < 0 || residentIndex >= SlotCount || residentIndex >= slots.Count)
            {
                errors.Add($"active order position {index} references invalid slot {residentIndex}");
                continue;
            }
            if (!seenActiveSlots.Add(residentIndex))
                errors.Add($"resident slot {residentIndex} appears twice in the active order");
            PartySlotState slot = slots[residentIndex];
            if (slot is null) continue;
            if (slot.Code < 0)
            {
                errors.Add($"active order position {index} references free resident slot {residentIndex}");
                continue;
            }
            if (requireActiveBases && slot.BaseId != slot.Code)
                errors.Add($"active resident slot {residentIndex} is not bound to its own persistent stats");
        }
        return errors;
    }
}
