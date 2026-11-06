using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace SuikodenRandomizer.Core;

/// <summary>
/// Confirmed acquisition history for one loaded campaign. Native membership
/// flags are read only for current availability; they never create a receipt.
/// This class does not detect a recruitment scene or modify native party state.
/// </summary>
public sealed class CampaignAcquisitionState
{
    public const string NewGameOrigin = "new-game-v1";
    public const string LegacyUnknownOrigin = "legacy-unknown-v1";

    private readonly IReadOnlyDictionary<int, int> _mapping;
    private readonly HashSet<int> _starterRoles;
    private readonly Dictionary<int, string> _receipts = new Dictionary<int, string>();
    private readonly HashSet<int> _startupActivations = new HashSet<int>();

    private CampaignAcquisitionState(SeedManifest manifest, string origin)
    {
        if (manifest is null) throw new ArgumentNullException(nameof(manifest));
        SeedValidator.ValidateAndThrow(manifest);
        SeedFingerprint = global::SuikodenRandomizer.Core.SeedFingerprint.Compute(manifest);
        _mapping = new ReadOnlyDictionary<int, int>(new Dictionary<int, int>(manifest.Mapping));
        _starterRoles = manifest.StartingParty.Enabled
            ? new HashSet<int>(manifest.StartingParty.SourceIds)
            : new HashSet<int>();
        HistoryOrigin = origin;
        IsStartupClosed = origin == LegacyUnknownOrigin || _starterRoles.Count == 0;
    }

    /// <summary>A fresh run starts with no historical grants, including starters.</summary>
    public static CampaignAcquisitionState CreateNew(SeedManifest manifest) =>
        new CampaignAcquisitionState(manifest, NewGameOrigin);

    /// <summary>
    /// Existing seed-only saves have unknown history. Current flags must not be
    /// relabelled as historical first acquisitions during migration.
    /// </summary>
    public static CampaignAcquisitionState CreateLegacy(SeedManifest manifest) =>
        new CampaignAcquisitionState(manifest, LegacyUnknownOrigin);

    public string SeedFingerprint { get; }
    public string HistoryOrigin { get; }
    public bool HasUnknownPriorHistory => HistoryOrigin == LegacyUnknownOrigin;
    public bool IsStartupClosed { get; private set; }
    public IReadOnlyCollection<int> ConfirmedSourceIds =>
        Array.AsReadOnly(_receipts.Keys.OrderBy(id => id).ToArray());
    public IReadOnlyCollection<int> OwnedRewardIds =>
        Array.AsReadOnly(_receipts.Keys.Select(source => _mapping[source]).OrderBy(id => id).ToArray());
    public IReadOnlyCollection<int> StartupSourceIds =>
        Array.AsReadOnly(_startupActivations.OrderBy(id => id).ToArray());

    /// <summary>
    /// Call only AFTER a reviewed acquisition site has matched its complete
    /// signature/context and its native grant succeeded. A digest is a locator,
    /// not authentication or evidence of runtime safety. This API deliberately
    /// has no membership-bit observer that can create a historical grant.
    /// Returns true only when the source receives its first receipt.
    /// </summary>
    public bool RecordReviewedAcquisition(int canonicalSourceId, string sceneSignatureSha256)
    {
        RequireSource(canonicalSourceId);
        RequireDigest(sceneSignatureSha256, nameof(sceneSignatureSha256));
        if (_receipts.ContainsKey(canonicalSourceId)) return false;
        _receipts.Add(canonicalSourceId, sceneSignatureSha256);
        return true;
    }

    public bool HasConfirmedAcquisition(int canonicalSourceId) => _receipts.ContainsKey(canonicalSourceId);

    /// <summary>Resolve a canonical role once. Saved/menu gameplay IDs bypass this method.</summary>
    public int ResolveSource(int canonicalSourceId) => _mapping.TryGetValue(canonicalSourceId, out int reward)
        ? reward : canonicalSourceId;

    /// <summary>
    /// Native startup integration calls this only at its reviewed New Game
    /// boundary. Recording an activation never grants permanent ownership.
    /// </summary>
    public bool RecordStartupActivation(int canonicalSourceId, int actualGameplayId)
    {
        if (IsStartupClosed) throw new InvalidOperationException("Startup acquisition admission has closed.");
        if (!_starterRoles.Contains(canonicalSourceId))
            throw new ArgumentOutOfRangeException(nameof(canonicalSourceId), "The source is not a declared starter role.");
        RequireSource(canonicalSourceId);
        if (_mapping[canonicalSourceId] != actualGameplayId)
            throw new ArgumentException("The startup gameplay identity does not match its role assignment.", nameof(actualGameplayId));
        return _startupActivations.Add(canonicalSourceId);
    }

    public void CompleteStartup() => IsStartupClosed = true;

    /// <summary>
    /// Current selectability follows canonical source flags exactly as the
    /// existing roster projection does. Absence/death/return never erase a
    /// receipt. This method neither rerolls nor infers any acquisition.
    /// </summary>
    public IReadOnlyCollection<int> GetAvailableRewardIds(IReadOnlyList<byte> canonicalFlags)
    {
        if (canonicalFlags is null) throw new ArgumentNullException(nameof(canonicalFlags));
        return Array.AsReadOnly(_receipts.Keys
            .Where(source => source >= 0 && source < canonicalFlags.Count
                && RosterProjection.IsSelectable(canonicalFlags[source]))
            .Select(source => _mapping[source]).OrderBy(id => id).ToArray());
    }

    /// <summary>
    /// Deep-copy into the SAME native save's binding document before native
    /// storage. The caller owns the native snapshot digest/build check and
    /// atomic binding publication. No canonical flags or party IDs are copied.
    /// </summary>
    public AcquisitionSnapshot Capture(string saveIdentitySha256, string gameBuildSha256)
    {
        RequireDigest(saveIdentitySha256, nameof(saveIdentitySha256));
        RequireDigest(gameBuildSha256, nameof(gameBuildSha256));
        return new AcquisitionSnapshot
        {
            HistoryVersion = AcquisitionSnapshot.CurrentHistoryVersion,
            SeedFingerprint = SeedFingerprint,
            SaveIdentity = saveIdentitySha256,
            GameBuildSha256 = gameBuildSha256,
            HistoryOrigin = HistoryOrigin,
            IsStartupClosed = IsStartupClosed,
            StartupSourceIds = _startupActivations.OrderBy(id => id).ToList(),
            Receipts = _receipts.OrderBy(pair => pair.Key).Select(pair => new AcquisitionReceipt
            {
                SourceId = pair.Key,
                SceneSignatureSha256 = pair.Value
            }).ToList()
        };
    }

    public static CampaignAcquisitionState Restore(SeedManifest manifest, AcquisitionSnapshot snapshot,
        string expectedSaveIdentitySha256, string expectedGameBuildSha256)
    {
        if (snapshot is null) throw new ArgumentNullException(nameof(snapshot));
        RequireDigest(expectedSaveIdentitySha256, nameof(expectedSaveIdentitySha256));
        RequireDigest(expectedGameBuildSha256, nameof(expectedGameBuildSha256));
        if (snapshot.HistoryVersion != AcquisitionSnapshot.CurrentHistoryVersion)
            throw new ArgumentException("Unsupported acquisition history version.", nameof(snapshot));
        if (snapshot.HistoryOrigin != NewGameOrigin && snapshot.HistoryOrigin != LegacyUnknownOrigin)
            throw new ArgumentException("Unknown acquisition history origin.", nameof(snapshot));
        var restored = new CampaignAcquisitionState(manifest, snapshot.HistoryOrigin);
        if (snapshot.SeedFingerprint != restored.SeedFingerprint
            || snapshot.SaveIdentity != expectedSaveIdentitySha256
            || snapshot.GameBuildSha256 != expectedGameBuildSha256)
            throw new ArgumentException("Acquisition history belongs to a different seed, native snapshot or game build.", nameof(snapshot));
        if (snapshot.Receipts is null || snapshot.StartupSourceIds is null
            || snapshot.Receipts.Count > restored._mapping.Count
            || snapshot.StartupSourceIds.Count > restored._starterRoles.Count)
            throw new ArgumentException("Invalid acquisition history collections.", nameof(snapshot));
        foreach (AcquisitionReceipt receipt in snapshot.Receipts)
        {
            if (receipt is null)
                throw new ArgumentException("An acquisition receipt is null.", nameof(snapshot));
            restored.RequireSource(receipt.SourceId);
            RequireDigest(receipt.SceneSignatureSha256, nameof(snapshot));
            if (restored._receipts.ContainsKey(receipt.SourceId))
                throw new ArgumentException("Duplicate acquisition source.", nameof(snapshot));
            restored._receipts.Add(receipt.SourceId, receipt.SceneSignatureSha256);
        }
        foreach (int source in snapshot.StartupSourceIds)
        {
            if (!restored._starterRoles.Contains(source) || !restored._startupActivations.Add(source))
                throw new ArgumentException("Unknown or duplicate startup activation source.", nameof(snapshot));
        }
        if ((snapshot.HistoryOrigin == LegacyUnknownOrigin || restored._starterRoles.Count == 0)
            && (!snapshot.IsStartupClosed || snapshot.StartupSourceIds.Count != 0))
            throw new ArgumentException("Legacy/ordinary histories cannot reopen or invent startup activation.", nameof(snapshot));
        // A loaded snapshot is never a New Game boundary, even if it was
        // captured before native initialization completed. Admission itself
        // is a run-phase permission, not a privilege restored from a save.
        restored.IsStartupClosed = true;
        return restored;
    }

    private void RequireSource(int source)
    {
        if (!_mapping.ContainsKey(source))
            throw new ArgumentOutOfRangeException(nameof(source), "The source is outside the campaign's immutable assignment.");
    }

    private static void RequireDigest(string digest, string parameterName)
    {
        if (digest is null || digest.Length != 64
            || digest.Any(character => !(character >= '0' && character <= '9')
                && !(character >= 'a' && character <= 'f')))
            throw new ArgumentException("Expected a lowercase SHA-256 digest.", parameterName);
    }
}

/// <summary>Serialization DTO; validated and deep-copied on restore.</summary>
public sealed class AcquisitionSnapshot
{
    public const int CurrentHistoryVersion = 1;
    public int HistoryVersion { get; set; }
    public string SeedFingerprint { get; set; } = string.Empty;
    public string SaveIdentity { get; set; } = string.Empty;
    public string GameBuildSha256 { get; set; } = string.Empty;
    public string HistoryOrigin { get; set; } = string.Empty;
    public bool IsStartupClosed { get; set; }
    public List<int> StartupSourceIds { get; set; } = new List<int>();
    public List<AcquisitionReceipt> Receipts { get; set; } = new List<AcquisitionReceipt>();
}

public sealed class AcquisitionReceipt
{
    public int SourceId { get; set; }
    public string SceneSignatureSha256 { get; set; } = string.Empty;
}
