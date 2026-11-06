using System;
using System.Collections.Generic;
using System.Linq;
using SuikodenRandomizer.Core.Avatars;

internal static class Program
{
    private static int _passed;
    private static void Check(bool condition, string message)
    { if (!condition) throw new Exception(message); }
    private static void Case(string name, Action action)
    { action(); _passed++; Console.WriteLine("PASS " + name); }
    private static IReadOnlyList<int> Active(int[] codes, int[] order, int count) =>
        AvatarPartyLayout.GetActiveResidentSlots(codes, order, count);
    private static void Reject(Action action)
    {
        try { action(); }
        catch (InvalidOperationException) { return; }
        throw new Exception("Invalid native active layout was admitted.");
    }
    private static void RejectNull(Action action)
    {
        try { action(); }
        catch (ArgumentNullException) { return; }
        throw new Exception("Null native layout was admitted.");
    }

    private static void Main()
    {
        Case("Five loaded residents with only Tir active are a valid opening layout", () =>
            Check(Active(new[] { 1, 2, 6, 8, 108, -1 }, new[] { 3, 0, 1, 2, 4, -1 }, 1)
                .SequenceEqual(new[] { 3 }), "Resident count was confused with active membership."));
        Case("Permuted order selects resident slots rather than character IDs", () =>
            Check(Active(new[] { 1, 2, 6, 8, 108, -1 }, new[] { 4, 1, 0, 3, 2, -1 }, 3)
                .SequenceEqual(new[] { 4, 1, 0 }), "Active order permutation was lost."));
        Case("Staged residents can exist with no active party", () =>
            Check(Active(new[] { 1, 2, 6, 8, 108, 109 }, new[] { -1, 99, 99, -1 }, 0).Count == 0,
                "Zero active count required staged residents to disappear."));
        Case("Empty native layout supports zero active members", () =>
            Check(Active(Array.Empty<int>(), Array.Empty<int>(), 0).Count == 0, "Empty party was rejected."));
        Case("Full six-member party preserves its active order", () =>
            Check(Active(new[] { 8, 1, 2, 6, 108, 109 }, new[] { 5, 3, 0, 2, 4, 1 }, 6)
                .SequenceEqual(new[] { 5, 3, 0, 2, 4, 1 }), "Full party order changed."));
        Case("Unused order entries do not contribute membership", () =>
            Check(Active(new[] { 8, 1, -1, -1, -1, -1 }, new[] { 0, -1, 99, 0, 0, -7, 999 }, 1)
                .SequenceEqual(new[] { 0 }), "Unused native order entries were validated as active."));
        Case("Inactive duplicate resident codes are ignored", () =>
            Check(Active(new[] { 8, 1, 1, 1, -1, -1 }, new[] { 0, -1 }, 1).SequenceEqual(new[] { 0 }),
                "Inactive duplicate codes blocked an active resident."));
        Case("Inactive empty and stale invalid codes are ignored", () =>
            Check(Active(new[] { 8, -1, 65535, 110, -999, 999 }, new[] { 0 }, 1).SequenceEqual(new[] { 0 }),
                "Inactive stale codes were treated as active members."));
        Case("A short resident array is valid when all active slots exist", () =>
            Check(Active(new[] { 8, 1 }, new[] { 1 }, 1).SequenceEqual(new[] { 1 }), "Resident capacity was treated as mandatory occupancy."));
        Case("All canonical character classes are valid active identities", () =>
            Check(Active(new[] { 8, 108, 109, 27, 1, 0 }, new[] { 0, 1, 2, 3, 4, 5 }, 6).Count == 6,
                "Shuffle exclusions incorrectly excluded native active identities."));
        Case("Negative active count is rejected", () => Reject(() => Active(new[] { 8 }, new[] { 0 }, -1)));
        Case("Active count above six is rejected", () => Reject(() => Active(new[] { 8 }, new[] { 0, 0, 0, 0, 0, 0, 0 }, 7)));
        Case("Active count longer than its order prefix is rejected", () => Reject(() => Active(new[] { 8, 1 }, new[] { 0 }, 2)));
        Case("Resident capacity above six is rejected", () => Reject(() => Active(new[] { 8, 1, 2, 3, 4, 5, 6 }, Array.Empty<int>(), 0)));
        Case("Negative active slot is rejected", () => Reject(() => Active(new[] { 8 }, new[] { -1 }, 1)));
        Case("Active slot outside native capacity is rejected", () => Reject(() => Active(new[] { 8, 1, 2, 3, 4, 5 }, new[] { 6 }, 1)));
        Case("Active slot beyond the available resident array is rejected", () => Reject(() => Active(new[] { 8 }, new[] { 1 }, 1)));
        Case("Empty active resident code is rejected", () => Reject(() => Active(new[] { -1 }, new[] { 0 }, 1)));
        Case("Out-of-range active character code is rejected", () => Reject(() => Active(new[] { 110 }, new[] { 0 }, 1)));
        Case("Duplicate active resident slot is rejected", () => Reject(() => Active(new[] { 8, 1 }, new[] { 0, 0 }, 2)));
        Case("Duplicate active canonical identity is rejected", () => Reject(() => Active(new[] { 1, 1 }, new[] { 0, 1 }, 2)));
        Case("Null code and order inputs are rejected explicitly", () =>
        {
            RejectNull(() => AvatarPartyLayout.GetActiveResidentSlots(null!, Array.Empty<int>(), 0));
            RejectNull(() => AvatarPartyLayout.GetActiveResidentSlots(Array.Empty<int>(), null!, 0));
        });
        Case("Resolving membership never mutates native layout snapshots", () =>
        {
            var codes = new[] { 1, 2, 6, 8, 108, -1 }; var order = new[] { 3, 2, -1, -1, -1, -1 };
            var beforeCodes = (int[])codes.Clone(); var beforeOrder = (int[])order.Clone();
            var result = Active(codes, order, 2);
            Check(codes.SequenceEqual(beforeCodes) && order.SequenceEqual(beforeOrder) && result.SequenceEqual(new[] { 3, 2 }),
                "Membership resolution mutated its inputs.");
            Check(result is not int[], "Mutable active-slot array escaped the helper.");
        });
        Console.WriteLine(_passed + "/" + _passed + " native-party layout checks passed.");
        Console.WriteLine("Scope: pure native-layout semantics; native resident/base bindings are checked separately by the adapter.");
    }
}
