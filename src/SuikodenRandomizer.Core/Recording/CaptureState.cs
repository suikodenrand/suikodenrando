using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SuikodenRandomizer.Core.Recording;

/// <summary>Managed copies, never native wrappers. Null arrays mean unreadable, not empty.</summary>
public sealed class CaptureState
{
    public bool Available { get; set; }
    public string Scene { get; set; } = "";
    public string Phase { get; set; } = "";
    public long GameIdentity { get; set; }
    public int Village { get; set; } = -1;
    public int Map { get; set; } = -1;
    public int Area { get; set; } = -1;
    public int ActiveCount { get; set; }
    public int? EventDoingFlag { get; set; }
    public bool ChapterTransitionPending { get; set; }
    public int[] ResidentIds { get; set; } = Array.Empty<int>();
    public int[] ActiveOrder { get; set; } = Array.Empty<int>();
    public int[] RuntimeCharacterIds { get; set; } = Array.Empty<int>();
    public int[] BaseCharacterIds { get; set; } = Array.Empty<int>();
    public int[] Levels { get; set; } = Array.Empty<int>();
    public int[]? MemberFlags { get; set; }
    public int[]? StoryFlags { get; set; }
    public int[]? TemporaryFlags { get; set; }
    public int Money { get; set; }
    public bool NoclipActive { get; set; }
    public bool EventGuardActive { get; set; }
    public bool RandomizationActive { get; set; }
    public List<string> Issues { get; set; } = new List<string>();
}

public sealed class CaptureByteChange
{
    public string Domain { get; set; } = "";
    public int Index { get; set; }
    public int Before { get; set; }
    public int After { get; set; }
    public string Observation { get; set; } = "raw-byte-change";
}

public sealed class CaptureDelta
{
    public bool Baseline { get; set; }
    public bool Changed { get; set; }
    public bool Gap { get; set; }
    public string Reason { get; set; } = "";
    public List<CaptureByteChange> Changes { get; set; } = new List<CaptureByteChange>();
}

/// <summary>Does NOT interpret membership changes as first recruitment, return or death.</summary>
public sealed class CaptureStateTracker
{
    private CaptureState? _previous;
    public void Reset() { _previous = null; }

    public CaptureDelta Observe(CaptureState state)
    {
        if (state is null) throw new ArgumentNullException(nameof(state));
        if (!state.Available || state.MemberFlags is null)
        {
            bool gap = _previous is not null;
            _previous = null;
            return new CaptureDelta { Changed = gap, Gap = gap, Reason = "state-unavailable; next read is a baseline" };
        }
        CaptureState? before = _previous;
        _previous = Clone(state);
        if (before is null) return new CaptureDelta { Baseline = true, Changed = true, Reason = "baseline; existing flags are not recruitment events" };
        if (before.GameIdentity != state.GameIdentity || before.MemberFlags!.Length != state.MemberFlags.Length)
            return new CaptureDelta { Baseline = true, Changed = true, Gap = true, Reason = "game identity or flag-array shape changed; rebaselined" };
        var delta = new CaptureDelta();
        Compare("member_flag", before.MemberFlags, state.MemberFlags, delta);
        Compare("storyFlagS", before.StoryFlags, state.StoryFlags, delta);
        Compare("tmpEventFlagS", before.TemporaryFlags, state.TemporaryFlags, delta);
        delta.Changed = delta.Changes.Count != 0 || delta.Gap
            || before.Scene != state.Scene || before.Phase != state.Phase
            || before.Village != state.Village || before.Map != state.Map || before.Area != state.Area
            || before.EventDoingFlag != state.EventDoingFlag || before.ChapterTransitionPending != state.ChapterTransitionPending
            || before.ActiveCount != state.ActiveCount || before.Money != state.Money
            || before.NoclipActive != state.NoclipActive || before.EventGuardActive != state.EventGuardActive
            || before.RandomizationActive != state.RandomizationActive
            || !before.ResidentIds.SequenceEqual(state.ResidentIds) || !before.ActiveOrder.SequenceEqual(state.ActiveOrder)
            || !before.RuntimeCharacterIds.SequenceEqual(state.RuntimeCharacterIds)
            || !before.BaseCharacterIds.SequenceEqual(state.BaseCharacterIds) || !before.Levels.SequenceEqual(state.Levels)
            || !before.Issues.SequenceEqual(state.Issues);
        if (delta.Changed && delta.Reason.Length == 0) delta.Reason = "observed-state-change; interpretation requires review";
        return delta;
    }

    private static void Compare(string domain, int[]? before, int[]? after, CaptureDelta delta)
    {
        if (before is null && after is null) return;
        if (before is null || after is null || before.Length != after.Length)
        { delta.Gap = true; delta.Reason = domain + " coverage changed; no byte transitions inferred for this array"; return; }
        for (int i = 0; i < before.Length; i++)
        {
            if (before[i] == after[i]) continue;
            string observation = "raw-byte-change";
            if (domain == "member_flag")
                observation = (before[i] & 1) == (after[i] & 1) ? "other-membership-bits-changed"
                    : (after[i] & 1) != 0 ? "membership-bit-set-not-classified" : "membership-bit-cleared-not-classified";
            delta.Changes.Add(new CaptureByteChange { Domain = domain, Index = i, Before = before[i], After = after[i], Observation = observation });
        }
    }

    private static CaptureState Clone(CaptureState s) => new CaptureState
    {
        Available = s.Available, Scene = s.Scene, Phase = s.Phase, GameIdentity = s.GameIdentity,
        Village = s.Village, Map = s.Map, Area = s.Area, ActiveCount = s.ActiveCount, Money = s.Money,
        EventDoingFlag = s.EventDoingFlag, ChapterTransitionPending = s.ChapterTransitionPending,
        NoclipActive = s.NoclipActive, EventGuardActive = s.EventGuardActive, RandomizationActive = s.RandomizationActive,
        MemberFlags = s.MemberFlags?.ToArray(), StoryFlags = s.StoryFlags?.ToArray(), TemporaryFlags = s.TemporaryFlags?.ToArray(),
        ResidentIds = s.ResidentIds.ToArray(), ActiveOrder = s.ActiveOrder.ToArray(), RuntimeCharacterIds = s.RuntimeCharacterIds.ToArray(),
        BaseCharacterIds = s.BaseCharacterIds.ToArray(), Levels = s.Levels.ToArray(), Issues = new List<string>(s.Issues)
    };
}

public static class CaptureTargets
{
    /// <summary>Targets label a session; they never filter out other characters' changes.</summary>
    public static bool TryParse(string? value, out int[] ids, out string error)
    {
        ids = Array.Empty<int>(); error = "";
        string input = (value ?? "").Trim();
        if (input.Length == 0 || string.Equals(input, "all", StringComparison.OrdinalIgnoreCase)) return true;
        if (input.Length > 512) { error = "Target list is too long."; return false; }
        var result = new SortedSet<int>();
        foreach (string piece in input.Split(','))
        {
            if (!int.TryParse(piece.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int id) || id < 0 || id > 109)
            { error = "Use IDs 0-109 separated by commas, or all."; return false; }
            result.Add(id);
        }
        ids = result.ToArray(); return true;
    }
}

/// <summary>Counts suppressed identical diagnostic evaluations without interpreting them.</summary>
public sealed class CaptureDeduplicator
{
    private readonly int _capacity;
    private readonly HashSet<string> _seen = new HashSet<string>(StringComparer.Ordinal);
    public long Repeats { get; private set; }
    public long Resets { get; private set; }
    public CaptureDeduplicator(int capacity = 2048) { if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity)); _capacity = capacity; }
    public bool First(string key)
    {
        if (_seen.Contains(key)) { Repeats++; return false; }
        if (_seen.Count >= _capacity) { _seen.Clear(); Resets++; }
        _seen.Add(key); return true;
    }
    public void Reset() { _seen.Clear(); Repeats = 0; Resets = 0; }
}
