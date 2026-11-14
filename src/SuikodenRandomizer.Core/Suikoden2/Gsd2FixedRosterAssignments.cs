using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace SuikodenRandomizer.Core.Suikoden2;

/// <summary>Immutable active canonical-role bindings; formation and native records stay outside Core.</summary>
public sealed class Gsd2FixedRosterAssignments
{
    private Gsd2FixedRosterAssignments(Gsd2AvatarProfile profile, Dictionary<int, int> assignments)
    {
        Profile = profile;
        Assignments = new ReadOnlyDictionary<int, int>(assignments);
    }
    public Gsd2AvatarProfile Profile { get; }
    public IReadOnlyDictionary<int, int> Assignments { get; }

    public static Gsd2FixedRosterAssignments Empty(Gsd2AvatarProfile profile)
    {
        ValidateProfile(profile);
        return new Gsd2FixedRosterAssignments(profile, new Dictionary<int, int>());
    }

    public static Gsd2FixedRosterAssignments Restore(Gsd2AvatarProfile profile, IEnumerable<KeyValuePair<int, int>> assignments)
    {
        ValidateProfile(profile);
        if (assignments is null) throw new ArgumentNullException(nameof(assignments));
        var restored = new Dictionary<int, int>();
        var used = new HashSet<int>();
        foreach (var assignment in assignments)
        {
            if (restored.Count == 6 || !Gsd2CharacterCatalog.IsStoryRole(assignment.Key)
                || !profile.HeroIds.Contains(assignment.Value)
                || (assignment.Key == Gsd2CharacterCatalog.ProtagonistId && assignment.Value != Gsd2CharacterCatalog.ProtagonistId)
                || !used.Add(assignment.Value) || !restored.TryAdd(assignment.Key, assignment.Value))
                throw new ArgumentException("Saved Suikoden II fixed roster contains an invalid, duplicate, or reserved binding.", nameof(assignments));
        }
        if (restored.Any(pair => pair.Key != Gsd2CharacterCatalog.ProtagonistId && pair.Value == Gsd2CharacterCatalog.ProtagonistId)
            && (restored.Count != 6 || restored.ContainsKey(Gsd2CharacterCatalog.ProtagonistId)))
            throw new ArgumentException("Borrowing the protagonist display donor requires six active non-protagonist roles.", nameof(assignments));
        return new Gsd2FixedRosterAssignments(profile, restored);
    }

    public int GetDonor(int canonicalRoleId)
    {
        if (canonicalRoleId < 0) throw new ArgumentOutOfRangeException(nameof(canonicalRoleId));
        return Assignments.TryGetValue(canonicalRoleId, out int donor) ? donor : canonicalRoleId;
    }

    /// <summary>Pass distinct actor IDs from the current six combat cells, excluding reserve/extra slots6/7.</summary>
    public Gsd2FixedRosterAssignments Reconcile(IEnumerable<int> activeRoleIds, int maximumActiveActors = 6)
    {
        if (activeRoleIds is null) throw new ArgumentNullException(nameof(activeRoleIds));
        if (maximumActiveActors < 0 || maximumActiveActors > 6) throw new ArgumentOutOfRangeException(nameof(maximumActiveActors));
        int[] active = activeRoleIds.Take(7).ToArray();
        if (active.Length > maximumActiveActors || active.Distinct().Count() != active.Length
            || active.Any(id => !Gsd2CharacterCatalog.IsStoryRole(id)))
            throw new InvalidOperationException("The active Suikoden II context has too many, duplicate, or invalid canonical actor roles.");
        bool hasProtagonist = active.Contains(Gsd2CharacterCatalog.ProtagonistId);
        bool mayBorrowProtagonist = !hasProtagonist && active.Length == 6;
        if (active.Length == Assignments.Count && active.All(Assignments.ContainsKey)
            && (mayBorrowProtagonist || !Assignments.Any(pair => pair.Key != Gsd2CharacterCatalog.ProtagonistId && pair.Value == Gsd2CharacterCatalog.ProtagonistId))) return this;
        var next = new Dictionary<int, int>();
        foreach (int role in active)
            if (Assignments.TryGetValue(role, out int retained)
                && (retained != Gsd2CharacterCatalog.ProtagonistId || role == Gsd2CharacterCatalog.ProtagonistId || mayBorrowProtagonist)) next.Add(role, retained);
        var used = new HashSet<int>(next.Values);
        foreach (int role in active)
        {
            if (next.ContainsKey(role)) continue;
            int donor = role == Gsd2CharacterCatalog.ProtagonistId ? role
                : Profile.CompanionIds.Concat(mayBorrowProtagonist ? new[] { Gsd2CharacterCatalog.ProtagonistId } : Array.Empty<int>()).First(id => !used.Contains(id));
            next.Add(role, donor); used.Add(donor);
        }
        return new Gsd2FixedRosterAssignments(Profile, next);
    }

    private static void ValidateProfile(Gsd2AvatarProfile profile)
    {
        if (profile is null) throw new ArgumentNullException(nameof(profile));
        if (profile.Mode == "Shuffle") throw new ArgumentException("A fixed roster profile is required.", nameof(profile));
    }
}
