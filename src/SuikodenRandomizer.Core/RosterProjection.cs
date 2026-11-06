using System;
using System.Collections.Generic;
using System.Linq;

namespace SuikodenRandomizer.Core;

/// <summary>
/// Projects rewards solely from the currently loaded save's canonical source
/// flags. Ownership and temporary availability are separate decisions.
/// </summary>
public static class RosterProjection
{
    public static RosterView Build(SeedManifest manifest, IReadOnlyList<byte> memberFlags)
    {
        if (manifest is null) throw new ArgumentNullException(nameof(manifest));
        if (memberFlags is null) throw new ArgumentNullException(nameof(memberFlags));
        if (manifest.Mapping is null) throw new ArgumentException("Mapping is required.", nameof(manifest));

        var completed = new HashSet<int>();
        var owned = new HashSet<int>();
        var available = new HashSet<int>();
        foreach (KeyValuePair<int, int> assignment in manifest.Mapping)
        {
            int sourceId = assignment.Key;
            if (sourceId < 0 || sourceId >= memberFlags.Count) continue;
            byte sourceFlag = memberFlags[sourceId];
            if (!IsRecruited(sourceFlag)) continue;
            completed.Add(sourceId);
            owned.Add(assignment.Value);
            if (IsSelectable(sourceFlag)) available.Add(assignment.Value);
        }

        var hidden = new HashSet<int>(manifest.Mapping.Keys);
        hidden.ExceptWith(owned);
        var selectable = new HashSet<int>(available);
        foreach (int characterId in CombatCatalog.NormalCombatIds.Concat(new[] { CombatCatalog.TirId }))
        {
            if (!manifest.Mapping.ContainsKey(characterId)
                && characterId < memberFlags.Count
                && IsSelectable(memberFlags[characterId]))
                selectable.Add(characterId);
        }
        return new RosterView(completed, owned, available, hidden, selectable);
    }

    public static bool IsRecruited(byte flagValue) => (flagValue & 1) != 0;

    /// <summary>The eligibility predicate verified in native setWorkData.</summary>
    public static bool IsSelectable(byte flagValue) => (flagValue & 0x05) == 0x01;
}

public sealed class RosterView
{
    internal RosterView(HashSet<int> completed, HashSet<int> owned,
        HashSet<int> available, HashSet<int> hidden, HashSet<int> selectable)
    {
        CompletedChecks = Array.AsReadOnly(completed.OrderBy(id => id).ToArray());
        OwnedRewards = Array.AsReadOnly(owned.OrderBy(id => id).ToArray());
        AvailableRewards = Array.AsReadOnly(available.OrderBy(id => id).ToArray());
        HiddenSources = Array.AsReadOnly(hidden.OrderBy(id => id).ToArray());
        SelectableCharacterIds = Array.AsReadOnly(selectable.OrderBy(id => id).ToArray());
    }

    public IReadOnlyCollection<int> CompletedChecks { get; }
    public IReadOnlyCollection<int> OwnedRewards { get; }
    public IReadOnlyCollection<int> AvailableRewards { get; }
    public IReadOnlyCollection<int> HiddenSources { get; }
    public IReadOnlyCollection<int> SelectableCharacterIds { get; }
}
