using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace SuikodenRandomizer.Core.Avatars;

/// <summary>An ordered roster with Tir reserved for his canonical role.</summary>
public sealed class FixedRosterProfile
{
    public const string CurrentProfileId = "fixed-roster-v1";
    public const int CompanionCount = AvatarPartyLayout.ResidentCapacity - 1;

    private FixedRosterProfile(int[] companions)
    {
        CompanionIds = Array.AsReadOnly(companions);
        HeroIds = Array.AsReadOnly(new[] { CombatCatalog.TirId }.Concat(companions).ToArray());
        using var sha = SHA256.Create();
        string identity = CurrentProfileId + "\n" + string.Join(",", HeroIds.Select(id => id.ToString(CultureInfo.InvariantCulture)));
        Fingerprint = AvatarShuffleProfile.Hex(sha.ComputeHash(Encoding.UTF8.GetBytes(identity)));
    }

    public IReadOnlyList<int> CompanionIds { get; }
    public IReadOnlyList<int> HeroIds { get; }
    public string Fingerprint { get; }

    public bool IsStoryRole(int roleId) => CombatCatalog.IsNormalCombat(roleId)
        || roleId == CombatCatalog.TirId || roleId == CombatCatalog.TedId || roleId == CombatCatalog.OdessaId;

    public static FixedRosterProfile CreateChosen(IEnumerable<int> companionIds)
    {
        if (companionIds is null) throw new ArgumentNullException(nameof(companionIds));
        int[] companions = companionIds.Take(CompanionCount + 1).ToArray();
        if (companions.Length != CompanionCount || companions.Distinct().Count() != CompanionCount
            || companions.Any(id => !CombatCatalog.IsNormalCombat(id)))
            throw new ArgumentException("Choose five distinct eligible combat companions. Tir is reserved as the sixth hero.", nameof(companionIds));
        return new FixedRosterProfile(companions);
    }

    public static FixedRosterProfile CreateRandom(string seed)
    {
        // The prefix of the existing unbiased permutation is an ordered sample
        // without replacement. Read in RoleIds order rather than relying on
        // dictionary iteration order.
        var shuffle = AvatarShuffleProfile.Create(seed);
        return CreateChosen(shuffle.RoleIds.Take(CompanionCount).Select(shuffle.GetDonor));
    }
}
