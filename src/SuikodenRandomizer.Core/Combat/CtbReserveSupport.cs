using System;
using System.Collections.Generic;
using System.Linq;

namespace SuikodenRandomizer.Core.Combat;

/// <summary>A detached eligibility snapshot. Canonical role and displayed fighter are distinct identities.</summary>
public readonly struct CtbReserveCandidate
{
    public CtbReserveCandidate(int roleId, int displayId, string name, bool recruitedAlive,
        bool battleCapable, bool initialized)
    {
        RoleId = roleId;
        DisplayId = displayId;
        Name = name ?? string.Empty;
        RecruitedAlive = recruitedAlive;
        BattleCapable = battleCapable;
        Initialized = initialized;
    }

    public int RoleId { get; }
    public int DisplayId { get; }
    public string Name { get; }
    public bool RecruitedAlive { get; }
    public bool BattleCapable { get; }
    public bool Initialized { get; }
}

/// <summary>
/// Selects support from validated reserve snapshots and tracks encounter-local assist cadence.
/// This class never changes recruitment, party membership or saved character data.
/// </summary>
public sealed class CtbReserveSupport
{
    private int _actionsSinceAssist;

    public CtbReserveSupport(int actionsPerAssist = 4)
    {
        if (actionsPerAssist <= 0) throw new ArgumentOutOfRangeException(nameof(actionsPerAssist));
        ActionsPerAssist = actionsPerAssist;
    }

    public int ActionsPerAssist { get; }
    public int ActionsSinceAssist => _actionsSinceAssist;

    /// <summary>Call exactly once after a completed hero action; thinking time and enemy actions do not count.</summary>
    public bool RecordHeroAction()
    {
        _actionsSinceAssist++;
        if (_actionsSinceAssist < ActionsPerAssist) return false;
        _actionsSinceAssist = 0;
        return true;
    }

    public void Reset() => _actionsSinceAssist = 0;

    /// <summary>Advance the latest scheduled eligible ally with remaining advance capacity.</summary>
    public static (int Slot, double Advance) Rally(CtbTimeline timeline, IEnumerable<(int Slot, long Identity)> allies)
    {
        foreach (var ally in allies.OrderByDescending(ally => timeline.GetNextReady(ally.Slot)))
        {
            double advance = timeline.Advance(ally.Slot, ally.Identity, 0.15);
            if (advance > 0) return (ally.Slot, advance);
        }
        return (-1, 0);
    }

    /// <summary>
    /// Null preferences select reserves in canonical role order. A provided list is an explicit
    /// roster: missing or ineligible choices are never silently replaced with another character.
    /// </summary>
    public static CtbReserveCandidate[] Select(IEnumerable<CtbReserveCandidate> candidates,
        IEnumerable<int> activeRoleIds, IEnumerable<int> activeDisplayIds,
        IEnumerable<int>? preferredRoleIds = null, int maximum = 2)
    {
        if (candidates is null) throw new ArgumentNullException(nameof(candidates));
        if (activeRoleIds is null) throw new ArgumentNullException(nameof(activeRoleIds));
        if (activeDisplayIds is null) throw new ArgumentNullException(nameof(activeDisplayIds));
        if (maximum < 0) throw new ArgumentOutOfRangeException(nameof(maximum));
        if (maximum == 0) return Array.Empty<CtbReserveCandidate>();

        var activeRoles = new HashSet<int>(activeRoleIds);
        var activeFighters = new HashSet<int>(activeDisplayIds);
        var eligible = candidates.Where(candidate => candidate.RoleId >= 0 && candidate.DisplayId >= 0
            && candidate.RecruitedAlive && candidate.BattleCapable && candidate.Initialized
            && !activeRoles.Contains(candidate.RoleId) && !activeFighters.Contains(candidate.DisplayId))
            .OrderBy(candidate => candidate.RoleId).ThenBy(candidate => candidate.DisplayId)
            .ThenBy(candidate => candidate.Name, StringComparer.Ordinal).ToArray();

        IEnumerable<CtbReserveCandidate> ordered;
        if (preferredRoleIds is null)
        {
            ordered = eligible;
        }
        else
        {
            var preferred = new List<CtbReserveCandidate>();
            var requestedRoles = new HashSet<int>();
            foreach (int role in preferredRoleIds)
            {
                if (role < 0 || !requestedRoles.Add(role)) continue;
                foreach (var candidate in eligible)
                    if (candidate.RoleId == role) preferred.Add(candidate);
            }
            ordered = preferred;
        }

        var selected = new List<CtbReserveCandidate>();
        var selectedRoles = new HashSet<int>();
        var selectedFighters = new HashSet<int>();
        foreach (var candidate in ordered)
        {
            if (selectedRoles.Contains(candidate.RoleId) || selectedFighters.Contains(candidate.DisplayId)) continue;
            selectedRoles.Add(candidate.RoleId);
            selectedFighters.Add(candidate.DisplayId);
            selected.Add(candidate);
            if (selected.Count == maximum) break;
        }
        return selected.ToArray();
    }
}
