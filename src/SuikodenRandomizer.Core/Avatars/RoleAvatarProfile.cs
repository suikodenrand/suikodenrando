using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace SuikodenRandomizer.Core.Avatars;

/// <summary>
/// A single canonical role receiving donor templates. This is a separate pilot
/// profile, not a source/reward permutation and not an ordinary seed manifest.
/// </summary>
public sealed class RoleAvatarProfile
{
    public const int CurrentVersion = 1;
    public static RoleAvatarProfile CleoEileenPilot { get; } =
        new RoleAvatarProfile("cleo-eileen-avatar-pilot-v1", 1, 0);

    private RoleAvatarProfile(string profileId, int canonicalRoleId, int donorCharacterId)
    {
        ProfileId = profileId;
        CanonicalRoleId = canonicalRoleId;
        DonorCharacterId = donorCharacterId;
        StartPolicy = RoleAvatarProgressionPolicy.StartPolicyId;
        ProgressionPolicy = RoleAvatarProgressionPolicy.PolicyId;
        using var bytes = new MemoryStream();
        using (var writer = new BinaryWriter(bytes, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write("SuikodenRandomizer.RoleAvatarProfile.v1");
            writer.Write(CurrentVersion);
            writer.Write(ProfileId);
            writer.Write(CanonicalRoleId);
            writer.Write(DonorCharacterId);
            writer.Write(StartPolicy);
            writer.Write(ProgressionPolicy);
        }
        using var hash = SHA256.Create();
        Fingerprint = string.Concat(hash.ComputeHash(bytes.ToArray()).Select(value => value.ToString("x2")));
    }

    public string ProfileId { get; }
    public int CanonicalRoleId { get; }
    public int DonorCharacterId { get; }
    public string StartPolicy { get; }
    public string ProgressionPolicy { get; }
    public string Fingerprint { get; }

    /// <summary>
    /// Explicit test/integration factory for one role only. Creating a profile
    /// does not certify the pair's native template compatibility.
    /// Protagonist/guests and support-only identities remain excluded.
    /// </summary>
    public static RoleAvatarProfile CreatePilot(int canonicalRoleId, int donorCharacterId)
    {
        if (!CombatCatalog.IsNormalCombat(canonicalRoleId))
            throw new ArgumentOutOfRangeException(nameof(canonicalRoleId), "Pilot role must be a normal combat identity.");
        if (!CombatCatalog.IsNormalCombat(donorCharacterId))
            throw new ArgumentOutOfRangeException(nameof(donorCharacterId), "Pilot donor must be a normal combat identity.");
        if (canonicalRoleId == donorCharacterId)
            throw new ArgumentException("The pilot requires a distinct donor.", nameof(donorCharacterId));
        return canonicalRoleId == 1 && donorCharacterId == 0 ? CleoEileenPilot
            : new RoleAvatarProfile("role-avatar-pilot-v1:" + canonicalRoleId + ":" + donorCharacterId,
                canonicalRoleId, donorCharacterId);
    }
}

public enum RoleAvatarFieldDomain
{
    AppearanceTemplate,
    GrowthTemplate,
    EquipmentTemplate,
    ActionTemplate,
    CanonicalIdentity,
    PersistentHealthAndMagic,
    PersistentLevelAndExperience,
    PersistentInventory,
    PersistentEquipmentAndRunes,
    PlotUniqueOverrides
}

public enum RoleAvatarCopyPhase
{
    NewGameInitialization,
    BoundReplay
}

/// <summary>
/// Fresh New Game initialization copies donor starting progression once. After
/// that transaction the canonical role's native save owns HP/MP, level/EXP,
/// equipment/runes and items. Replay copies independent templates only and must
/// preserve saved progression. Canonical IDs/plot overrides never come from donor.
/// </summary>
public static class RoleAvatarProgressionPolicy
{
    public const string StartPolicyId = "donor-live-start-once";
    public const string PolicyId = "donor-live-start-once-canonical-save-replay-v1";

    /// <summary>Unqualified calls use the conservative loaded-save replay policy.</summary>
    public static bool MayCopyFromDonor(RoleAvatarFieldDomain field) =>
        MayCopyFromDonor(field, RoleAvatarCopyPhase.BoundReplay);

    public static bool MayCopyFromDonor(RoleAvatarFieldDomain field, RoleAvatarCopyPhase phase)
    {
        if (phase != RoleAvatarCopyPhase.NewGameInitialization && phase != RoleAvatarCopyPhase.BoundReplay)
            throw new ArgumentOutOfRangeException(nameof(phase));
        switch (field)
        {
            case RoleAvatarFieldDomain.AppearanceTemplate:
            case RoleAvatarFieldDomain.GrowthTemplate:
            case RoleAvatarFieldDomain.EquipmentTemplate:
            case RoleAvatarFieldDomain.ActionTemplate:
                return true;
            case RoleAvatarFieldDomain.PersistentHealthAndMagic:
            case RoleAvatarFieldDomain.PersistentLevelAndExperience:
            case RoleAvatarFieldDomain.PersistentInventory:
            case RoleAvatarFieldDomain.PersistentEquipmentAndRunes:
                return phase == RoleAvatarCopyPhase.NewGameInitialization;
            case RoleAvatarFieldDomain.CanonicalIdentity:
            case RoleAvatarFieldDomain.PlotUniqueOverrides:
                return false;
            default:
                throw new ArgumentOutOfRangeException(nameof(field));
        }
    }
}

/// <summary>The final initialized native identity triplet; never donor IDs.</summary>
public readonly struct AvatarIdentity
{
    public AvatarIdentity(int codeId, int dataId, int baseId)
    {
        CodeId = codeId;
        DataId = dataId;
        BaseId = baseId;
    }

    public int CodeId { get; }
    public int DataId { get; }
    public int BaseId { get; }

    public void RequireCanonicalRole(RoleAvatarProfile profile)
    {
        if (profile is null) throw new ArgumentNullException(nameof(profile));
        if (CodeId != profile.CanonicalRoleId || DataId != profile.CanonicalRoleId
            || BaseId != profile.CanonicalRoleId)
            throw new InvalidOperationException("Avatar role code, data and base identities must remain canonical.");
    }
}

public static class RoleAvatarIdentityRules
{
    /// <summary>
    /// Validate occupied final resident slots without changing or requiring any
    /// party membership. Empty/full parties are both valid. Free stale slots
    /// must be omitted by the native adapter before using this final-state gate.
    /// </summary>
    public static void ValidateOccupiedResidents(RoleAvatarProfile profile, IReadOnlyList<AvatarIdentity> residents)
    {
        if (profile is null) throw new ArgumentNullException(nameof(profile));
        if (residents is null) throw new ArgumentNullException(nameof(residents));
        if (residents.Count > 6) throw new InvalidOperationException("Native resident capacity is six.");
        var seen = new HashSet<int>();
        foreach (AvatarIdentity resident in residents)
        {
            if (resident.CodeId < 0 || resident.CodeId > 109
                || resident.CodeId != resident.DataId || resident.CodeId != resident.BaseId
                || !seen.Add(resident.CodeId))
                throw new InvalidOperationException("Resident identities must be consistent, unique canonical codes.");
            if (resident.CodeId == profile.CanonicalRoleId) resident.RequireCanonicalRole(profile);
        }
    }
}
