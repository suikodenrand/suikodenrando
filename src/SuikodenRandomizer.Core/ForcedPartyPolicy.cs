using System;
using System.Collections.Generic;

namespace SuikodenRandomizer.Core;

public enum ForcedPartyAction
{
    AlreadyActive,
    ActivateResident,
    ReserveThenActivate
}

public sealed class ForcedPartyPlan
{
    internal ForcedPartyPlan(ForcedPartyAction action, int slot)
    { Action = action; ResidentSlot = slot; }
    public ForcedPartyAction Action { get; }
    public int ResidentSlot { get; }
}

/// <summary>
/// Capacity policy for a mandatory native party activation. This operates on
/// actual character IDs and resident slot indices, never on recruitment flags.
/// It plans without evicting anyone or changing either array.
/// </summary>
public static class ForcedPartyPolicy
{
    public static bool TryPlan(IReadOnlyList<int> residentIds,
        IReadOnlyList<int> activeSlots, int activeCount, int rewardId,
        out ForcedPartyPlan? plan, out string reason)
    {
        plan = null;
        reason = string.Empty;
        if (residentIds is null || activeSlots is null || residentIds.Count != 6
            || activeCount < 0 || activeCount > 6 || activeSlots.Count < activeCount)
        { reason = "invalid native party array shape or active count"; return false; }
        if (rewardId < 0 || rewardId > 109)
        { reason = "invalid actual character ID"; return false; }
        var ids = new HashSet<int>();
        int resident = -1, free = -1;
        for (int index = 0; index < 6; index++)
        {
            int id = residentIds[index];
            if (id == -1) { if (free < 0) free = index; continue; }
            if (id < 0 || id > 109 || !ids.Add(id))
            { reason = "invalid or duplicated occupied resident identity"; return false; }
            if (id == rewardId) resident = index;
        }
        var active = new HashSet<int>();
        for (int index = 0; index < activeCount; index++)
        {
            int slot = activeSlots[index];
            if (slot < 0 || slot >= 6 || residentIds[slot] < 0 || !active.Add(slot))
            { reason = "invalid or duplicated active resident slot"; return false; }
        }
        if (resident >= 0 && active.Contains(resident))
        { plan = new ForcedPartyPlan(ForcedPartyAction.AlreadyActive, resident); return true; }
        if (activeCount == 6)
        { reason = "mandatory activation has no free active position"; return false; }
        if (resident >= 0)
        { plan = new ForcedPartyPlan(ForcedPartyAction.ActivateResident, resident); return true; }
        if (free < 0)
        { reason = "all six resident slots are occupied, including inactive reservations"; return false; }
        plan = new ForcedPartyPlan(ForcedPartyAction.ReserveThenActivate, free);
        return true;
    }
}
