using SuikodenRandomizer.Core.Combat;

int passed = 0;
void Check(bool value, string message)
{
    if (!value) throw new InvalidOperationException(message);
}
void Case(string title, Action test)
{
    test();
    Console.WriteLine("PASS " + title);
    passed++;
}
UniteActor Actor(int role, int donor, int slot, int identity = 0, bool eligible = true)
    => new(role, donor, slot, new IntPtr(identity == 0 ? 1000 + role : identity), eligible);
UniteParticipantSnapshot Snapshot(params UniteActor[] actors)
{
    Check(UniteParticipantSnapshot.TryCreate(actors, out var snapshot, out var reason), reason ?? "Snapshot failed.");
    Check(snapshot is not null && reason is null, "Successful snapshot must be populated without an error.");
    return snapshot!;
}
UniteParticipantGroup Resolve(UniteParticipantSnapshot snapshot, params int[] donors)
{
    Check(snapshot.TryResolve(donors, out var group, out var reason), reason ?? "Resolve failed.");
    Check(group is not null && reason is null, "Successful resolution must be populated without an error.");
    return group!;
}
void RejectSnapshot(params UniteActor[] actors)
{
    Check(!UniteParticipantSnapshot.TryCreate(actors, out var snapshot, out var reason)
        && snapshot is null && !string.IsNullOrWhiteSpace(reason), "Malformed party must fail with a reason.");
}
void RejectRecipe(UniteParticipantSnapshot snapshot, params int[] donors)
{
    Check(!snapshot.TryResolve(donors, out var group, out var reason)
        && group is null && !string.IsNullOrWhiteSpace(reason), "Unavailable recipe must fail with a reason.");
}

Case("Talisman follows Gremio and Pahn on foreign story roles in recipe order", () =>
{
    var snapshot = Snapshot(Actor(91, 6, 1), Actor(8, 8, 2), Actor(39, 2, 5));
    var group = Resolve(snapshot, 2, 6);
    Check(group.Members.Select(x => x.CanonicalRoleId).SequenceEqual(new[] { 39, 91 }), "Story roles were used as unite identity.");
    Check(group.Members.Select(x => x.NativeSlot).SequenceEqual(new[] { 5, 1 }), "Native participants lost recipe order.");
    Check(group.Members[0].Identity == new IntPtr(1039) && group.Members[1].Identity == new IntPtr(1091), "Wrong actor instances.");
    Check(group.IsValidFor(snapshot, out _), "Freshly resolved group should be valid.");
});

Case("Original story roles occupied by other fighters do not grant Talisman", () =>
{
    RejectRecipe(Snapshot(Actor(2, 63, 1), Actor(6, 3, 2)), 2, 6);
});

Case("Original unshuffled partners still resolve", () =>
{
    Check(Resolve(Snapshot(Actor(2, 2, 1), Actor(6, 6, 2)), 2, 6).Members.Count == 2, "Vanilla pair disappeared.");
});

Case("Only active displayed fighters count, not absent campaign assignments", () =>
{
    RejectRecipe(Snapshot(Actor(39, 2, 5), Actor(8, 8, 1)), 2, 6);
    RejectRecipe(Snapshot(), 2, 6);
});

Case("KO or otherwise incapable partner keeps the recipe unavailable", () =>
{
    var snapshot = Snapshot(Actor(39, 2, 1), Actor(91, 6, 2, eligible: false));
    Check(snapshot.Actors.Count == 2, "Ineligible actor should remain represented.");
    RejectRecipe(snapshot, 2, 6);
});

Case("Every arrangement of six foreign actors resolves the same ordered natural pair", () =>
{
    int[] donors = { 6, 8, 91, 2, 39, 76 };
    int[] roles = { 20, 0, 63, 33, 66, 7 };
    for (int rotation = 0; rotation < 6; rotation++)
    {
        var actors = Enumerable.Range(0, 6).Select(i => Actor(roles[i], donors[i], (i + rotation) % 6 + 1)).Reverse().ToArray();
        var group = Resolve(Snapshot(actors), 2, 6);
        Check(group.Members[0].CanonicalRoleId == 33 && group.Members[1].CanonicalRoleId == 20, "Formation changed natural partners.");
    }
});

Case("Canonical role zero and native slot six are valid", () =>
{
    var snapshot = Snapshot(Actor(0, 2, 6), Actor(1, 6, 1));
    Check(snapshot.TryGetBySlot(6, out var actor) && actor.CanonicalRoleId == 0, "Role zero must not mean absent.");
    Check(!snapshot.TryGetBySlot(0, out _), "Native slot zero is not a player.");
    Check(Resolve(snapshot, 2, 6).Members[0].CanonicalRoleId == 0, "Role zero lost during resolution.");
});

Case("Protagonist partner follows the Tir avatar rather than hardcoded role eight", () =>
{
    var group = Resolve(Snapshot(Actor(8, 3, 1), Actor(2, 8, 2), Actor(6, 91, 4)), 8, 91);
    Check(group.Members[0].CanonicalRoleId == 2 && group.Members[1].CanonicalRoleId == 6, "Master Pupil used story Tir.");
});

Case("Fixed roster rebinding requires fresh resolution onto new hosts", () =>
{
    var group = Resolve(Snapshot(Actor(2, 2, 1), Actor(6, 6, 2)), 2, 6);
    var rebound = Snapshot(Actor(39, 2, 1), Actor(91, 6, 2));
    Check(!group.IsValidFor(rebound, out _), "Rebinding must invalidate stale actor handles.");
    Check(Resolve(rebound, 2, 6).Members[1].CanonicalRoleId == 91, "Fresh inverse did not follow fixed roster.");
});

Case("Reordering participants invalidates old slots and fresh resolution restores correct order", () =>
{
    var group = Resolve(Snapshot(Actor(39, 2, 1), Actor(91, 6, 2)), 2, 6);
    var reordered = Snapshot(Actor(39, 2, 2), Actor(91, 6, 1));
    Check(!group.IsValidFor(reordered, out _), "Stale group could target a new occupant of its old slot.");
    Check(Resolve(reordered, 2, 6).Members[0].NativeSlot == 2, "Re-resolved group kept an old slot.");
});

Case("A participant replaced with the same role and fighter still invalidates its old pointer", () =>
{
    var group = Resolve(Snapshot(Actor(39, 2, 1), Actor(91, 6, 2)), 2, 6);
    Check(!group.IsValidFor(Snapshot(Actor(39, 2, 1, 9000), Actor(91, 6, 2)), out _), "Reused story role hid native actor replacement.");
});

Case("KO after confirmation invalidates the whole group and revival permits a fresh group", () =>
{
    var original = Snapshot(Actor(39, 2, 1), Actor(91, 6, 2));
    var group = Resolve(original, 2, 6);
    Check(!group.IsValidFor(Snapshot(Actor(39, 2, 1), Actor(91, 6, 2, eligible: false)), out var reason)
        && !string.IsNullOrWhiteSpace(reason), "Partner KO was not detected.");
    Check(Resolve(original, 2, 6).IsValidFor(original, out _), "Revived unchanged actors should resolve again.");
});

Case("Unrelated actor replacement or enumeration reordering does not invalidate participants", () =>
{
    var group = Resolve(Snapshot(Actor(39, 2, 1), Actor(91, 6, 2), Actor(8, 8, 3)), 2, 6);
    Check(group.IsValidFor(Snapshot(Actor(20, 63, 3, eligible: false), Actor(91, 6, 2), Actor(39, 2, 1)), out _), "Unrelated actor invalidated the group.");
});

Case("Snapshot and group are detached from mutable caller collections", () =>
{
    var actors = new[] { Actor(39, 2, 1), Actor(91, 6, 2) };
    var snapshot = Snapshot(actors);
    int[] donors = { 2, 6 };
    var group = Resolve(snapshot, donors);
    actors[0] = Actor(20, 63, 1);
    donors[0] = 63;
    Check(snapshot.Actors[0].DisplayedDonorId == 2 && group.Members[0].DisplayedDonorId == 2, "Caller mutation changed the snapshot/group.");
    bool rejected = false;
    try { ((IList<UniteActor>)group.Members)[0] = Actor(20, 63, 1); }
    catch (NotSupportedException) { rejected = true; }
    Check(rejected, "A caller could mutate resolved participants.");
});

Case("Ambiguous duplicate fighters, roles, slots and actor instances are rejected", () =>
{
    RejectSnapshot(Actor(39, 2, 1), Actor(91, 2, 2));
    RejectSnapshot(Actor(39, 2, 1), Actor(39, 6, 2, 9091));
    RejectSnapshot(Actor(39, 2, 1), Actor(91, 6, 1));
    RejectSnapshot(Actor(39, 2, 1, 500), Actor(91, 6, 2, 500));
});

Case("Invalid native identity ranges and party capacity are rejected", () =>
{
    RejectSnapshot(Actor(-1, 2, 1));
    RejectSnapshot(Actor(110, 2, 1));
    RejectSnapshot(Actor(39, -1, 1));
    RejectSnapshot(Actor(39, 110, 1));
    RejectSnapshot(Actor(39, 2, 0));
    RejectSnapshot(Actor(39, 2, 7));
    RejectSnapshot(new UniteActor(39, 2, 1, IntPtr.Zero, true));
    RejectSnapshot(Enumerable.Range(1, 7).Select(i => Actor(i, i, i)).ToArray());
});

Case("A single fighter cannot fill two recipe requirements", () =>
{
    var snapshot = Snapshot(Actor(39, 2, 1), Actor(91, 6, 2));
    RejectRecipe(snapshot, 2, 2);
    RejectRecipe(snapshot, 2, 6, 2);
});

Case("Malformed recipe sizes and identities fail without a partial result", () =>
{
    var snapshot = Snapshot(Actor(39, 2, 1), Actor(91, 6, 2));
    RejectRecipe(snapshot);
    RejectRecipe(snapshot, 2);
    RejectRecipe(snapshot, 2, 6, 8, 91, 39, 76, 63);
    RejectRecipe(snapshot, 2, -1);
    RejectRecipe(snapshot, 2, 110);
});

Case("Five-member recipes preserve each distinct real participant and recipe order", () =>
{
    var group = Resolve(Snapshot(Actor(39, 69, 1), Actor(91, 70, 2), Actor(8, 72, 3), Actor(2, 5, 4), Actor(6, 71, 5)),
        69, 70, 72, 5, 71);
    Check(group.Members.Select(x => x.CanonicalRoleId).SequenceEqual(new[] { 39, 91, 8, 2, 6 }), "Large recipe changed the actor order.");
});

Case("All six participants can resolve without consuming one actor twice", () =>
{
    var group = Resolve(Snapshot(Enumerable.Range(0, 6).Select(i => Actor(i + 20, i, 6 - i)).ToArray()), 0, 1, 2, 3, 4, 5);
    Check(group.Members.Count == 6 && group.Members.Select(x => x.Identity).Distinct().Count() == 6, "Six-member recipe is not distinct.");
});

Console.WriteLine($"PASS {passed} unite participant checks; native matching, animation and damage were not executed.");
