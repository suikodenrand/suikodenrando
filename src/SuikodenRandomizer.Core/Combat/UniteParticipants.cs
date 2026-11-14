using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace SuikodenRandomizer.Core.Combat;

/// <summary>An active S1 battle actor, with story identity kept separate from its displayed fighter.</summary>
public readonly struct UniteActor
{
    public UniteActor(int canonicalRoleId, int displayedDonorId, int nativeSlot, IntPtr identity, bool eligible)
    {
        CanonicalRoleId = canonicalRoleId;
        DisplayedDonorId = displayedDonorId;
        NativeSlot = nativeSlot;
        Identity = identity;
        Eligible = eligible;
    }

    public int CanonicalRoleId { get; }
    public int DisplayedDonorId { get; }
    public int NativeSlot { get; }
    public IntPtr Identity { get; }
    public bool Eligible { get; }
}

/// <summary>
/// Immutable inversion of the actual active S1 party. It never infers a participant
/// from a campaign mapping or an inactive story role.
/// </summary>
public sealed class UniteParticipantSnapshot
{
    private readonly Dictionary<int, UniteActor> _byDonor;
    private readonly Dictionary<int, UniteActor> _bySlot;

    private UniteParticipantSnapshot(List<UniteActor> actors, Dictionary<int, UniteActor> byDonor,
        Dictionary<int, UniteActor> bySlot)
    {
        Actors = new ReadOnlyCollection<UniteActor>(actors);
        _byDonor = byDonor;
        _bySlot = bySlot;
    }

    public IReadOnlyList<UniteActor> Actors { get; }

    public static bool TryCreate(IEnumerable<UniteActor> actors, out UniteParticipantSnapshot? snapshot,
        out string? reason)
    {
        if (actors is null) throw new ArgumentNullException(nameof(actors));
        snapshot = null;
        var copied = new List<UniteActor>();
        var byDonor = new Dictionary<int, UniteActor>();
        var bySlot = new Dictionary<int, UniteActor>();
        var roles = new HashSet<int>();
        var identities = new HashSet<IntPtr>();
        foreach (var actor in actors)
        {
            if (copied.Count >= 6)
            {
                reason = "The active S1 party has more than six actors.";
                return false;
            }
            if (actor.CanonicalRoleId < 0 || actor.CanonicalRoleId > 109
                || actor.DisplayedDonorId < 0 || actor.DisplayedDonorId > 109
                || actor.NativeSlot < 1 || actor.NativeSlot > 6 || actor.Identity == IntPtr.Zero)
            {
                reason = "An active S1 actor has an invalid role, fighter, battle slot or identity.";
                return false;
            }
            if (!roles.Add(actor.CanonicalRoleId) || !identities.Add(actor.Identity)
                || byDonor.ContainsKey(actor.DisplayedDonorId) || bySlot.ContainsKey(actor.NativeSlot))
            {
                reason = "The active S1 party contains an ambiguous role, fighter, battle slot or identity.";
                return false;
            }
            copied.Add(actor);
            byDonor.Add(actor.DisplayedDonorId, actor);
            bySlot.Add(actor.NativeSlot, actor);
        }
        snapshot = new UniteParticipantSnapshot(copied, byDonor, bySlot);
        reason = null;
        return true;
    }

    public bool TryGetBySlot(int nativeSlot, out UniteActor actor) => _bySlot.TryGetValue(nativeSlot, out actor);

    public bool TryResolve(IReadOnlyList<int> orderedDonorIds, out UniteParticipantGroup? group,
        out string? reason)
    {
        if (orderedDonorIds is null) throw new ArgumentNullException(nameof(orderedDonorIds));
        group = null;
        if (orderedDonorIds.Count < 2 || orderedDonorIds.Count > 6)
        {
            reason = "An S1 unite requires two to six distinct fighters.";
            return false;
        }
        var members = new List<UniteActor>(orderedDonorIds.Count);
        var used = new HashSet<int>();
        foreach (int donor in orderedDonorIds)
        {
            if (donor < 0 || donor > 109 || !used.Add(donor))
            {
                reason = "The unite recipe contains an invalid or repeated fighter.";
                return false;
            }
            if (!_byDonor.TryGetValue(donor, out var actor))
            {
                reason = "Required fighter " + donor + " is absent from the active battle party.";
                return false;
            }
            if (!actor.Eligible)
            {
                reason = "Required fighter " + donor + " cannot currently act.";
                return false;
            }
            members.Add(actor);
        }
        group = new UniteParticipantGroup(members);
        reason = null;
        return true;
    }
}

/// <summary>Recipe-ordered real participants, valid only while their battle identities and slots remain current.</summary>
public sealed class UniteParticipantGroup
{
    internal UniteParticipantGroup(List<UniteActor> members)
    {
        Members = new ReadOnlyCollection<UniteActor>(members);
    }

    public IReadOnlyList<UniteActor> Members { get; }

    /// <summary>Rechecks only this group's members; unrelated party changes do not invalidate the action.</summary>
    public bool IsValidFor(UniteParticipantSnapshot current, out string? reason)
    {
        if (current is null) throw new ArgumentNullException(nameof(current));
        foreach (var member in Members)
        {
            if (!current.TryGetBySlot(member.NativeSlot, out var actor)
                || actor.CanonicalRoleId != member.CanonicalRoleId
                || actor.DisplayedDonorId != member.DisplayedDonorId || actor.Identity != member.Identity)
            {
                reason = "A unite participant left, moved, changed fighter or was replaced.";
                return false;
            }
            if (!actor.Eligible)
            {
                reason = "A unite participant can no longer act.";
                return false;
            }
        }
        reason = null;
        return true;
    }
}
