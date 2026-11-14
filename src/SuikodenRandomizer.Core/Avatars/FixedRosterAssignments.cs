using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace SuikodenRandomizer.Core.Avatars;

/// <summary>Active story roles and their current fighters, independent of formation order.</summary>
public sealed class FixedRosterAssignments
{
    private FixedRosterAssignments(FixedRosterProfile profile, Dictionary<int, int> assignments)
    {
        Profile = profile;
        Assignments = new ReadOnlyDictionary<int, int>(assignments);
    }

    public FixedRosterProfile Profile { get; }
    public IReadOnlyDictionary<int, int> Assignments { get; }

    public static FixedRosterAssignments Empty(FixedRosterProfile profile)
    {
        if (profile is null) throw new ArgumentNullException(nameof(profile));
        return new FixedRosterAssignments(profile, new Dictionary<int, int>());
    }

    public static FixedRosterAssignments Restore(FixedRosterProfile profile, IEnumerable<KeyValuePair<int, int>> assignments)
    {
        if (profile is null) throw new ArgumentNullException(nameof(profile));
        if (assignments is null) throw new ArgumentNullException(nameof(assignments));
        var restored = new Dictionary<int, int>();
        var used = new HashSet<int>();
        foreach (var assignment in assignments)
        {
            int role = assignment.Key;
            int donor = assignment.Value;
            if (!profile.IsStoryRole(role) || !profile.HeroIds.Contains(donor)
                || (role == CombatCatalog.TirId) != (donor == CombatCatalog.TirId)
                || !used.Add(donor) || !restored.TryAdd(role, donor))
                throw new ArgumentException("Saved fixed roster assignments contain an invalid or duplicate role or fighter.", nameof(assignments));
        }
        return new FixedRosterAssignments(profile, restored);
    }

    public int GetDonor(int roleId)
    {
        if (roleId < 0 || roleId > CombatCatalog.OdessaId) throw new ArgumentOutOfRangeException(nameof(roleId));
        return Assignments.TryGetValue(roleId, out int donor) ? donor : roleId;
    }

    /// <summary>Retain roles that are still active, then allocate free fighters to newly active roles.</summary>
    public FixedRosterAssignments Reconcile(IEnumerable<int> activeRoleIds)
    {
        if (activeRoleIds is null) throw new ArgumentNullException(nameof(activeRoleIds));
        var active = activeRoleIds.Take(AvatarPartyLayout.ResidentCapacity + 1).ToArray();
        if (active.Length > AvatarPartyLayout.ResidentCapacity || active.Distinct().Count() != active.Length
            || active.Any(id => !Profile.IsStoryRole(id)))
            throw new InvalidOperationException("Active party contains too many roles or an invalid or duplicate story role.");
        if (active.Count(id => id != CombatCatalog.TirId) > FixedRosterProfile.CompanionCount)
            throw new InvalidOperationException("Fixed roster has five companion slots. Six active companion roles without Tir are unsupported.");
        if (active.Length == Assignments.Count && active.All(Assignments.ContainsKey)) return this;

        var next = new Dictionary<int, int>();
        foreach (int role in active)
            if (Assignments.TryGetValue(role, out int retained)) next.Add(role, retained);

        var used = new HashSet<int>(next.Values);
        foreach (int role in active)
        {
            if (next.ContainsKey(role)) continue;
            int donor = role == CombatCatalog.TirId
                ? CombatCatalog.TirId : Profile.CompanionIds.First(id => !used.Contains(id));
            next.Add(role, donor);
            used.Add(donor);
        }
        return new FixedRosterAssignments(Profile, next);
    }
}
