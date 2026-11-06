using System;
using System.Collections.Generic;
using System.Linq;

namespace SuikodenRandomizer.Core;

/// <summary>
/// An immutable acquisition detected in one loaded run. These are presentation
/// events, not new ownership records; native canonical flags remain authoritative.
/// </summary>
public sealed class RecruitmentNotice
{
    internal RecruitmentNotice(int sourceId, int rewardId, string seedFingerprint, long runGeneration)
    {
        SourceId = sourceId;
        RewardId = rewardId;
        SeedFingerprint = seedFingerprint;
        RunGeneration = runGeneration;
    }

    public int SourceId { get; }
    public int RewardId { get; }
    public string SeedFingerprint { get; }
    public long RunGeneration { get; }
}

/// <summary>A currently owned randomized recruitment visible in this save.</summary>
public sealed class RecruitmentDiscovery
{
    internal RecruitmentDiscovery(int sourceId, int rewardId, byte sourceFlag)
    {
        SourceId = sourceId;
        RewardId = rewardId;
        SourceFlag = sourceFlag;
    }

    public int SourceId { get; }
    public int RewardId { get; }
    public byte SourceFlag { get; }
    public bool IsSelectable => RosterProjection.IsSelectable(SourceFlag);
}

/// <summary>
/// Pure managed presentation state for the current validated recruitment profile.
/// All methods run on the game's main thread. The initial observation is a
/// baseline, and a load/new-run reset invalidates all old notifications. The
/// discovery view contains only assignments whose source is currently recruited;
/// it is not a persistent chronological history or a complete seed spoiler.
/// </summary>
public sealed class RecruitmentPresentationState
{
    private static readonly IReadOnlyList<RecruitmentNotice> NoNotices =
        Array.AsReadOnly(Array.Empty<RecruitmentNotice>());
    private static readonly IReadOnlyList<RecruitmentDiscovery> NoDiscoveries =
        Array.AsReadOnly(Array.Empty<RecruitmentDiscovery>());

    private KeyValuePair<int, int>[] _assignments = Array.Empty<KeyValuePair<int, int>>();
    private string _seedFingerprint = string.Empty;
    private byte[]? _previousFlags;
    private readonly HashSet<int> _seenSources = new HashSet<int>();
    private readonly HashSet<int> _confirmedHistory = new HashSet<int>();
    private bool _historyChanged;
    private readonly Queue<RecruitmentNotice> _notices = new Queue<RecruitmentNotice>();
    private IReadOnlyList<RecruitmentDiscovery> _discovered = NoDiscoveries;

    public RecruitmentPresentationState(SeedManifest seed) => Reset(seed);

    public long RunGeneration { get; private set; }
    public bool HasBaseline => _previousFlags is not null;
    public string SeedFingerprint => _seedFingerprint;
    public RecruitmentNotice? Current => _notices.Count == 0 ? null : _notices.Peek();
    /// <summary>Includes the currently displayed notice, if any.</summary>
    public int PendingCount => _notices.Count;
    public double CurrentVisibleSeconds { get; private set; }
    public IReadOnlyList<RecruitmentDiscovery> Discovered => _discovered;

    /// <summary>
    /// Starts another observation generation, even when the seed is unchanged.
    /// Use at save-load start, run changes, and disable. No caller-owned manifest
    /// or flags are retained, and an invalid seed does not partially reset state.
    /// </summary>
    public void Reset(SeedManifest seed)
    {
        if (seed is null) throw new ArgumentNullException(nameof(seed));
        SeedValidator.ValidateAndThrow(seed);
        KeyValuePair<int, int>[] assignments = seed.Mapping.OrderBy(pair => pair.Key).ToArray();
        string fingerprint = global::SuikodenRandomizer.Core.SeedFingerprint.Compute(seed);
        long nextGeneration = checked(RunGeneration + 1);

        _assignments = assignments;
        _seedFingerprint = fingerprint;
        RunGeneration = nextGeneration;
        ClearObservation();
    }

    /// <summary>
    /// Restore reviewed history independently of native availability. Historical
    /// grants suppress return notices after load; a newly confirmed live grant
    /// still passes through the ordinary notification observer once.
    /// </summary>
    public void SetConfirmedHistory(IEnumerable<int> canonicalSources, bool suppressHistoricalNotices)
    {
        if (canonicalSources is null) throw new ArgumentNullException(nameof(canonicalSources));
        var sources = new HashSet<int>(canonicalSources);
        var allowed = new HashSet<int>(_assignments.Select(pair => pair.Key));
        if (!sources.IsSubsetOf(allowed)) throw new ArgumentException("History source is outside this seed.", nameof(canonicalSources));
        if (!_confirmedHistory.SetEquals(sources))
        {
            _confirmedHistory.Clear();
            _confirmedHistory.UnionWith(sources);
            _historyChanged = true;
        }
        if (suppressHistoricalNotices) _seenSources.UnionWith(sources);
    }

    /// <summary>
    /// Returns newly detected acquisitions for logging, whether or not notices
    /// are enabled. Simultaneous transitions are ordered by source ID; their
    /// actual within-frame script chronology cannot be inferred from flags.
    /// Disabling enqueue still marks acquisitions seen and updates discoveries.
    /// </summary>
    public IReadOnlyList<RecruitmentNotice> Observe(IReadOnlyList<byte> flags, bool enqueueNotices = true)
    {
        if (flags is null) throw new ArgumentNullException(nameof(flags));

        var currentFlags = new byte[flags.Count];
        for (int index = 0; index < currentFlags.Length; index++) currentFlags[index] = flags[index];

        // A differently shaped native array is not a valid predecessor. Invalidate
        // pending display work and establish a new baseline instead of inventing
        // recruitments from an incomplete or replaced state object.
        if (_previousFlags is not null && _previousFlags.Length != currentFlags.Length)
        {
            RunGeneration = checked(RunGeneration + 1);
            int[] confirmed = _confirmedHistory.ToArray();
            ClearObservation();
            SetConfirmedHistory(confirmed, true);
        }

        // ChapterManager samples every frame. Preserve the immutable discovery
        // snapshot when no flag changed instead of allocating one row per
        // owned recruit on every unchanged frame.
        if (!_historyChanged && _previousFlags is not null && _previousFlags.SequenceEqual(currentFlags))
            return NoNotices;

        bool baseline = _previousFlags is null;
        var discoveries = new List<RecruitmentDiscovery>();
        var acquisitions = new List<RecruitmentNotice>();
        foreach (KeyValuePair<int, int> assignment in _assignments)
        {
            int sourceId = assignment.Key;
            if (sourceId >= currentFlags.Length) continue;
            byte sourceFlag = currentFlags[sourceId];
            bool currentlyRecruited = RosterProjection.IsRecruited(sourceFlag);
            if (!currentlyRecruited && !_confirmedHistory.Contains(sourceId)) continue;

            discoveries.Add(new RecruitmentDiscovery(sourceId, assignment.Value, sourceFlag));
            if (!currentlyRecruited) continue;
            if (baseline)
            {
                _seenSources.Add(sourceId);
                continue;
            }

            if (RosterProjection.IsRecruited(_previousFlags![sourceId]) || !_seenSources.Add(sourceId))
                continue;

            var notice = new RecruitmentNotice(sourceId, assignment.Value, _seedFingerprint, RunGeneration);
            acquisitions.Add(notice);
            // Every source can enqueue once per generation, so the present
            // validated profile bounds memory to its source count (25 or 78).
            if (enqueueNotices) _notices.Enqueue(notice);
        }

        _previousFlags = currentFlags;
        _historyChanged = false;
        _discovered = discoveries.Count == 0 ? NoDiscoveries : Array.AsReadOnly(discoveries.ToArray());
        return acquisitions.Count == 0 ? NoNotices : Array.AsReadOnly(acquisitions.ToArray());
    }

    /// <summary>
    /// Advances only while the caller confirms the current card is visible.
    /// Excess elapsed time never consumes the next queued card: every card must
    /// receive its own visible duration, including after a slow frame or pause.
    /// Invalid timing input is ignored so a cosmetic timer cannot affect play.
    /// </summary>
    public void AdvanceVisibleTime(double elapsedSeconds, double durationSeconds)
    {
        if (_notices.Count == 0 || !IsFinite(elapsedSeconds) || elapsedSeconds < 0
            || !IsFinite(durationSeconds) || durationSeconds <= 0)
            return;

        CurrentVisibleSeconds = Math.Min(durationSeconds, CurrentVisibleSeconds + elapsedSeconds);
        if (CurrentVisibleSeconds < durationSeconds) return;
        _notices.Dequeue();
        CurrentVisibleSeconds = 0;
    }

    /// <summary>Discards cards without forgetting completed discoveries.</summary>
    public void ClearNotices()
    {
        _notices.Clear();
        CurrentVisibleSeconds = 0;
    }

    /// <summary>Rejects a callback for any expired, cleared, or previous-run card.</summary>
    public bool IsCurrent(RecruitmentNotice? notice) => notice is not null
        && ReferenceEquals(Current, notice)
        && notice.RunGeneration == RunGeneration
        && string.Equals(notice.SeedFingerprint, _seedFingerprint, StringComparison.Ordinal);

    private void ClearObservation()
    {
        _previousFlags = null;
        _seenSources.Clear();
        _confirmedHistory.Clear();
        _historyChanged = false;
        _discovered = NoDiscoveries;
        ClearNotices();
    }

    private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
}
