using System;
using System.Collections.Generic;

namespace SuikodenRandomizer.Core;

/// <summary>Pure rules for the opt-in all-playable experiment. Never guesses ID domains.</summary>
public static class ExperimentalRoleRules
{
    public static int ResolveStoryRole(SeedManifest seed, int id) => SeedProfiles.IsExperimental(seed)
        ? CharacterResolution.Resolve(seed, id, CharacterIdDomain.RecruitmentSource) : id;

    public static short[] ProjectNewGameSlots(SeedManifest seed, IReadOnlyList<short> canonical)
    {
        if (seed is null || canonical is null) throw new ArgumentNullException();
        var result = new short[canonical.Count];
        for (int i = 0; i < result.Length; i++)
            result[i] = checked((short)ResolveStoryRole(seed, canonical[i]));
        return result;
    }

    // Native kill_member: low-bit toggle, dead bit, optional absence bit(s).
    public static byte AfterNativeLoss(byte sourceFlag, int absence) =>
        unchecked((byte)((sourceFlag ^ 1) | 2 | (absence << 4)));

    public static bool MayOfferGuest(SeedManifest seed, int id) =>
        (id != CombatCatalog.TedId && id != CombatCatalog.OdessaId) || SeedProfiles.IsExperimental(seed);
}
