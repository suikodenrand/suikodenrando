using System;
using System.Collections.Generic;

namespace SuikodenRandomizer.Core.Avatars;

/// <summary>
/// Native resident occupancy and active membership are separate. chara_code
/// describes up to six resident slots; only narabi's player_kazu prefix selects
/// active slots. Loaded or staged residents outside that prefix are not active.
/// </summary>
public static class AvatarPartyLayout
{
    public const int ResidentCapacity = 6;

    /// <summary>
    /// Resolve and validate active resident indices without changing either
    /// input. Unused order entries and inactive resident codes are deliberately
    /// ignored: native scripts may leave reservations or stale data there.
    /// </summary>
    public static IReadOnlyList<int> GetActiveResidentSlots(IReadOnlyList<int> codes,
        IReadOnlyList<int> order, int activeCount)
    {
        if (codes is null) throw new ArgumentNullException(nameof(codes));
        if (order is null) throw new ArgumentNullException(nameof(order));
        if (codes.Count > ResidentCapacity)
            throw new InvalidOperationException("Native resident capacity exceeds six slots.");
        if (activeCount < 0 || activeCount > ResidentCapacity || activeCount > order.Count)
            throw new InvalidOperationException("Native active count is outside its order prefix or capacity.");

        var activeSlots = new int[activeCount];
        var seenSlots = new HashSet<int>();
        var seenCharacters = new HashSet<int>();
        for (int n = 0; n < activeCount; n++)
        {
            int slot = order[n];
            if (slot < 0 || slot >= codes.Count || slot >= ResidentCapacity)
                throw new InvalidOperationException("Native active order refers to a missing resident slot.");
            if (!seenSlots.Add(slot))
                throw new InvalidOperationException("Native active order repeats a resident slot.");
            int characterId = codes[slot];
            if (characterId < 0 || characterId > 109)
                throw new InvalidOperationException("Native active resident has an empty or invalid character code.");
            if (!seenCharacters.Add(characterId))
                throw new InvalidOperationException("Native active residents repeat a canonical character identity.");
            activeSlots[n] = slot;
        }
        return Array.AsReadOnly(activeSlots);
    }
}
