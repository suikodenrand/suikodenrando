using System;
using System.Linq;

namespace SuikodenRandomizer.Core.Avatars;

/// <summary>Admission and atomic initialization for one complete immutable avatar permutation.</summary>
public sealed class AvatarShuffleCampaignState
{
    private enum Phase { Disabled, NewGamePending, Applying, Initialized, Failed }
    private Phase _phase;
    private AvatarShuffleCampaignState(AvatarShuffleProfile? profile, RoleAvatarCampaignOrigin origin, Phase phase)
    { Profile = profile; Origin = origin; _phase = phase; }

    public AvatarShuffleProfile? Profile { get; }
    public RoleAvatarCampaignOrigin Origin { get; }
    public bool IsInitialized => _phase == Phase.Initialized;
    public bool IsApplying => _phase == Phase.Applying;
    public bool IsFailed => _phase == Phase.Failed;
    public string? FailureReason { get; private set; }
    public static AvatarShuffleCampaignState CreateNew(AvatarShuffleProfile profile) =>
        new AvatarShuffleCampaignState(profile ?? throw new ArgumentNullException(nameof(profile)), RoleAvatarCampaignOrigin.NewGame, Phase.NewGamePending);
    public static AvatarShuffleCampaignState CreateUnbound() =>
        new AvatarShuffleCampaignState(null, RoleAvatarCampaignOrigin.UnboundSave, Phase.Disabled);

    /// <summary>The callback must snapshot all original donors before publishing any replacement.</summary>
    public bool ApplyNewGame(Func<bool> initialize)
    {
        if (Profile is null || Origin != RoleAvatarCampaignOrigin.NewGame || _phase != Phase.NewGamePending)
            throw new InvalidOperationException("Avatar shuffle initialization requires a pending new campaign.");
        return Apply(initialize);
    }

    /// <summary>Replay only static templates; never copy the donor's mutable start state again.</summary>
    public bool ReplayTemplates(AvatarShuffleProfile profile, Func<bool> replay)
    {
        if (profile is null) throw new ArgumentNullException(nameof(profile));
        if (_phase != Phase.Initialized || Profile is null || profile.Fingerprint != Profile.Fingerprint)
            throw new InvalidOperationException("Only the same initialized avatar permutation may replay templates.");
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
                _phase = Phase.Failed; FailureReason = "Avatar shuffle transaction did not commit."; return false;
            }
            _phase = Phase.Initialized; FailureReason = null; return true;
        }
        catch (Exception exception)
        {
            _phase = Phase.Failed; FailureReason = exception.GetType().Name + ": " + exception.Message; throw;
        }
    }

    public AvatarShuffleSnapshot Capture(string saveIdentitySha256, string gameBuildSha256)
    {
        if (_phase != Phase.Initialized || Profile is null)
            throw new InvalidOperationException("Only a completed avatar shuffle may be bound to a save.");
        RequireDigest(saveIdentitySha256, nameof(saveIdentitySha256));
        RequireDigest(gameBuildSha256, nameof(gameBuildSha256));
        return new AvatarShuffleSnapshot
        {
            AvatarShuffleVersion = AvatarShuffleSnapshot.CurrentAvatarShuffleVersion,
            ProfileId = Profile.ProfileId, ProfileFingerprint = Profile.Fingerprint, Seed = Profile.Seed,
            ShuffleAlgorithm = Profile.ShuffleAlgorithm, StartPolicy = Profile.StartPolicy,
            ProgressionPolicy = Profile.ProgressionPolicy, SaveIdentity = saveIdentitySha256,
            GameBuildSha256 = gameBuildSha256, TemplateInitializationCompleted = true,
            Assignments = Profile.RoleIds.Select(id => new AvatarShuffleAssignment { RoleId = id, DonorId = Profile.GetDonor(id) }).ToArray()
        };
    }

    public static AvatarShuffleCampaignState Restore(AvatarShuffleSnapshot snapshot,
        string expectedSaveIdentitySha256, string expectedGameBuildSha256)
    {
        if (snapshot is null) throw new ArgumentNullException(nameof(snapshot));
        RequireDigest(expectedSaveIdentitySha256, nameof(expectedSaveIdentitySha256));
        RequireDigest(expectedGameBuildSha256, nameof(expectedGameBuildSha256));
        AvatarShuffleProfile profile = AvatarShuffleProfile.Create(snapshot.Seed,
            includeTir: snapshot.ProfileId == AvatarShuffleProfile.IncludeTirProfileId);
        if (snapshot.AvatarShuffleVersion != AvatarShuffleSnapshot.CurrentAvatarShuffleVersion
            || !snapshot.TemplateInitializationCompleted || snapshot.ProfileId != profile.ProfileId
            || snapshot.ProfileFingerprint != profile.Fingerprint || snapshot.Seed != profile.Seed
            || snapshot.ShuffleAlgorithm != profile.ShuffleAlgorithm || snapshot.StartPolicy != profile.StartPolicy
            || snapshot.ProgressionPolicy != profile.ProgressionPolicy
            || snapshot.SaveIdentity != expectedSaveIdentitySha256 || snapshot.GameBuildSha256 != expectedGameBuildSha256
            || snapshot.Assignments is null || snapshot.Assignments.Length != profile.RoleIds.Count)
            throw new ArgumentException("Avatar shuffle binding does not match its seed, completed profile, save or game build.", nameof(snapshot));
        for (int n = 0; n < profile.RoleIds.Count; n++)
        {
            AvatarShuffleAssignment? pair = snapshot.Assignments[n];
            int role = profile.RoleIds[n];
            if (pair is null || pair.RoleId != role || pair.DonorId != profile.GetDonor(role))
                throw new ArgumentException("Avatar shuffle binding has a missing, reordered, duplicate, protected or changed assignment.", nameof(snapshot));
        }
        return new AvatarShuffleCampaignState(profile, RoleAvatarCampaignOrigin.BoundSave, Phase.Initialized);
    }

    private static void RequireDigest(string digest, string name)
    {
        if (digest is null || digest.Length != 64 || digest.Any(c => !(c >= '0' && c <= '9') && !(c >= 'a' && c <= 'f')))
            throw new ArgumentException("Expected a lowercase SHA-256 digest.", name);
    }
}

/// <summary>Metadata only; persistent stats, items, equipment and runes remain in native save records.</summary>
public sealed class AvatarShuffleSnapshot
{
    public const int CurrentAvatarShuffleVersion = 1;
    public int AvatarShuffleVersion { get; set; }
    public string ProfileId { get; set; } = string.Empty;
    public string ProfileFingerprint { get; set; } = string.Empty;
    public string Seed { get; set; } = string.Empty;
    public string ShuffleAlgorithm { get; set; } = string.Empty;
    public string StartPolicy { get; set; } = string.Empty;
    public string ProgressionPolicy { get; set; } = string.Empty;
    public string SaveIdentity { get; set; } = string.Empty;
    public string GameBuildSha256 { get; set; } = string.Empty;
    public bool TemplateInitializationCompleted { get; set; }
    public AvatarShuffleAssignment[] Assignments { get; set; } = Array.Empty<AvatarShuffleAssignment>();
}

public sealed class AvatarShuffleAssignment
{
    public int RoleId { get; set; } = -1;
    public int DonorId { get; set; } = -1;
}
