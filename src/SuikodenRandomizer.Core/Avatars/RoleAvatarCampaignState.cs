using System;
using System.Linq;

namespace SuikodenRandomizer.Core.Avatars;

public enum RoleAvatarCampaignOrigin
{
    UnboundSave,
    NewGame,
    BoundSave
}

/// <summary>
/// Campaign admission and save identity for the avatar pilot. Native integration
/// creates NewGame state only at the reviewed New Game boundary. Neither loading
/// an unbound save nor restoring a bound snapshot grants initialization permission.
/// </summary>
public sealed class RoleAvatarCampaignState
{
    private enum Phase { Disabled, NewGamePending, Applying, Initialized, Failed }
    private readonly RoleAvatarProfile? _profile;
    private Phase _phase;

    private RoleAvatarCampaignState(RoleAvatarProfile? profile, RoleAvatarCampaignOrigin origin, Phase phase)
    {
        _profile = profile;
        Origin = origin;
        _phase = phase;
    }

    public static RoleAvatarCampaignState CreateNew(RoleAvatarProfile profile) =>
        new RoleAvatarCampaignState(profile ?? throw new ArgumentNullException(nameof(profile)),
            RoleAvatarCampaignOrigin.NewGame, Phase.NewGamePending);

    public static RoleAvatarCampaignState CreateUnbound() =>
        new RoleAvatarCampaignState(null, RoleAvatarCampaignOrigin.UnboundSave, Phase.Disabled);

    public RoleAvatarCampaignOrigin Origin { get; }
    public RoleAvatarProfile? Profile => _profile;
    public bool IsInitialized => _phase == Phase.Initialized;
    public bool IsApplying => _phase == Phase.Applying;
    public bool IsFailed => _phase == Phase.Failed;
    public string? FailureReason { get; private set; }

    /// <summary>
    /// One initialization attempt after canonical asset/base preflight. The
    /// callback must perform and verify its native copy transaction. False or
    /// an exception marks the campaign failed, without admitting a retry on a
    /// possibly partial native state. Return to title/load a valid snapshot.
    /// </summary>
    public bool ApplyNewGame(AvatarIdentity identity, Func<bool> initialize)
    {
        if (_profile is null || Origin != RoleAvatarCampaignOrigin.NewGame || _phase != Phase.NewGamePending)
            throw new InvalidOperationException("Avatar initialization requires a fresh pending New Game.");
        identity.RequireCanonicalRole(_profile);
        return Apply(initialize);
    }

    /// <summary>
    /// Reapply the same immutable templates after cache reconstruction. This
    /// never imports donor progression or initializes a different profile.
    /// </summary>
    public bool ReplayTemplate(RoleAvatarProfile profile, AvatarIdentity identity, Func<bool> replay)
    {
        RequireSameProfile(profile);
        if (_phase != Phase.Initialized)
            throw new InvalidOperationException("Only an initialized bound campaign can replay avatar templates.");
        identity.RequireCanonicalRole(profile);
        return Apply(replay);
    }

    private bool Apply(Func<bool> operation)
    {
        if (operation is null) throw new ArgumentNullException(nameof(operation));
        _phase = Phase.Applying;
        try
        {
            if (!operation())
            {
                _phase = Phase.Failed;
                FailureReason = "Avatar template transaction did not commit.";
                return false;
            }
            _phase = Phase.Initialized;
            FailureReason = null;
            return true;
        }
        catch (Exception exception)
        {
            _phase = Phase.Failed;
            FailureReason = exception.GetType().Name + ": " + exception.Message;
            throw;
        }
    }

    public RoleAvatarSnapshot Capture(string saveIdentitySha256, string gameBuildSha256)
    {
        if (_phase != Phase.Initialized || _profile is null)
            throw new InvalidOperationException("Only a completed avatar transaction can be saved.");
        RequireDigest(saveIdentitySha256, nameof(saveIdentitySha256));
        RequireDigest(gameBuildSha256, nameof(gameBuildSha256));
        return new RoleAvatarSnapshot
        {
            AvatarVersion = RoleAvatarSnapshot.CurrentAvatarVersion,
            ProfileId = _profile.ProfileId,
            ProfileFingerprint = _profile.Fingerprint,
            CanonicalRoleId = _profile.CanonicalRoleId,
            DonorCharacterId = _profile.DonorCharacterId,
            StartPolicy = _profile.StartPolicy,
            ProgressionPolicy = _profile.ProgressionPolicy,
            SaveIdentity = saveIdentitySha256,
            GameBuildSha256 = gameBuildSha256,
            TemplateInitializationCompleted = true
        };
    }

    public static RoleAvatarCampaignState Restore(RoleAvatarProfile profile, RoleAvatarSnapshot snapshot,
        string expectedSaveIdentitySha256, string expectedGameBuildSha256)
    {
        if (profile is null) throw new ArgumentNullException(nameof(profile));
        if (snapshot is null) throw new ArgumentNullException(nameof(snapshot));
        RequireDigest(expectedSaveIdentitySha256, nameof(expectedSaveIdentitySha256));
        RequireDigest(expectedGameBuildSha256, nameof(expectedGameBuildSha256));
        if (snapshot.AvatarVersion != RoleAvatarSnapshot.CurrentAvatarVersion
            || !snapshot.TemplateInitializationCompleted
            || snapshot.ProfileId != profile.ProfileId
            || snapshot.ProfileFingerprint != profile.Fingerprint
            || snapshot.CanonicalRoleId != profile.CanonicalRoleId
            || snapshot.DonorCharacterId != profile.DonorCharacterId
            || snapshot.StartPolicy != profile.StartPolicy
            || snapshot.ProgressionPolicy != profile.ProgressionPolicy
            || snapshot.SaveIdentity != expectedSaveIdentitySha256
            || snapshot.GameBuildSha256 != expectedGameBuildSha256)
            throw new ArgumentException("Avatar binding does not match this profile, initialized save snapshot or game build.", nameof(snapshot));
        return new RoleAvatarCampaignState(profile, RoleAvatarCampaignOrigin.BoundSave, Phase.Initialized);
    }

    private void RequireSameProfile(RoleAvatarProfile profile)
    {
        if (profile is null) throw new ArgumentNullException(nameof(profile));
        if (_profile is null || profile.Fingerprint != _profile.Fingerprint)
            throw new InvalidOperationException("A loaded campaign cannot switch avatar profiles.");
    }

    private static void RequireDigest(string digest, string argumentName)
    {
        if (digest is null || digest.Length != 64
            || digest.Any(character => !(character >= '0' && character <= '9')
                && !(character >= 'a' && character <= 'f')))
            throw new ArgumentException("Expected a lowercase SHA-256 digest.", argumentName);
    }
}

/// <summary>
/// Only binding metadata is duplicated. After the one-time donor start, HP/MP,
/// level/EXP, equipment/runes and items stay in the canonical native save and
/// are preserved on replay. Story/plot overrides always remain canonical.
/// </summary>
public sealed class RoleAvatarSnapshot
{
    public const int CurrentAvatarVersion = 1;
    public int AvatarVersion { get; set; }
    public string ProfileId { get; set; } = string.Empty;
    public string ProfileFingerprint { get; set; } = string.Empty;
    public int CanonicalRoleId { get; set; } = -1;
    public int DonorCharacterId { get; set; } = -1;
    public string StartPolicy { get; set; } = string.Empty;
    public string ProgressionPolicy { get; set; } = string.Empty;
    public string SaveIdentity { get; set; } = string.Empty;
    public string GameBuildSha256 { get; set; } = string.Empty;
    public bool TemplateInitializationCompleted { get; set; }
}
