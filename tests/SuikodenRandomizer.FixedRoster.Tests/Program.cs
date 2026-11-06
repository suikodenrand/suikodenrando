using System;
using System.Collections.Generic;
using System.Linq;
using SuikodenRandomizer.Core;
using SuikodenRandomizer.Core.Avatars;

internal static class Program
{
    private const int Tir = CombatCatalog.TirId;
    private const int Ted = CombatCatalog.TedId;
    private const int Odessa = CombatCatalog.OdessaId;
    private const int Mathiu = 27;
    private static readonly int[] Companions = { 65, 26, 37, 102, 40 };

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static void Reject<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new Exception("Expected operation to be rejected.");
    }

    private static void Main()
    {
        var profile = FixedRosterProfile.CreateChosen(Companions);
        var empty = FixedRosterAssignments.Empty(profile);

        var tests = new (string Name, Action Run)[]
        {
            ("Chosen companions keep their order and Tir is reserved", () =>
            {
                int[] selection = (int[])Companions.Clone();
                var chosen = FixedRosterProfile.CreateChosen(selection);
                selection[0] = Ted;
                Check(chosen.HeroIds.SequenceEqual(new[] { Tir }.Concat(Companions)), "Wrong roster order or input aliased.");
                Check(chosen.Fingerprint == profile.Fingerprint, "Equivalent roster identity differs.");
                Check(chosen.Fingerprint != FixedRosterProfile.CreateChosen(Companions.Reverse()).Fingerprint,
                    "Order must contribute to identity.");
            }),
            ("Invalid companion lists are rejected", () =>
            {
                Reject<ArgumentException>(() => FixedRosterProfile.CreateChosen(Companions.Take(4)));
                Reject<ArgumentException>(() => FixedRosterProfile.CreateChosen(Companions.Append(5)));
                foreach (int invalid in new[] { Tir, Ted, Odessa, Mathiu, Companions[0], -1, 110 })
                    Reject<ArgumentException>(() => FixedRosterProfile.CreateChosen(Companions.Take(4).Append(invalid)));
            }),
            ("Seeded draw matches the validated 0.8.4 permutation prefix", () =>
            {
                var rolled = FixedRosterProfile.CreateRandom("  Agilen-080  ");
                Check(rolled.HeroIds.SequenceEqual(profile.HeroIds), "Golden roster mismatch.");
                Check(rolled.Fingerprint == profile.Fingerprint, "Equivalent selection has different identity.");
                foreach (int index in Enumerable.Range(0, 100))
                {
                    var sample = FixedRosterProfile.CreateRandom("fixture-" + index);
                    Check(sample.HeroIds.Distinct().Count() == 6 && sample.CompanionIds.All(CombatCatalog.IsNormalCombat),
                        "Roll yielded a duplicate or ineligible companion.");
                }
            }),
            ("Opening party only allocates fighters to active roles", () =>
            {
                var first = empty.Reconcile(new[] { Tir, Ted });
                Check(first.Assignments.Count == 2 && first.GetDonor(Tir) == Tir && first.GetDonor(Ted) == Companions[0],
                    "Tir or Ted was not allocated correctly.");
                Check(first.GetDonor(Odessa) == Odessa && !first.Assignments.ContainsKey(Odessa), "Inactive role was assigned.");
                Check(empty.Assignments.Count == 0, "Reconciliation mutated prior state.");
            }),
            ("Full party reorder preserves every fighter", () =>
            {
                var roles = new[] { Tir, Ted, Odessa, 1, 2, 6 };
                var full = empty.Reconcile(roles);
                Check(full.Assignments.Values.OrderBy(x => x).SequenceEqual(profile.HeroIds.OrderBy(x => x)),
                    "Full party did not use exactly the selected fighters.");
                Check(ReferenceEquals(full, full.Reconcile(roles.Reverse())), "Reorder changed assignments.");
            }),
            ("Departure and same-boundary join reuse only the vacated fighter", () =>
            {
                var original = empty.Reconcile(new[] { Tir, Ted, 1, 2 });
                var next = original.Reconcile(new[] { Odessa, 2, Tir, 1 });
                Check(next.GetDonor(Odessa) == original.GetDonor(Ted)
                    && next.GetDonor(1) == original.GetDonor(1)
                    && next.GetDonor(2) == original.GetDonor(2)
                    && !next.Assignments.ContainsKey(Ted), "Departures and joins failed to preserve stable ownership.");
                Check(next.Reconcile(Array.Empty<int>()).Assignments.Count == 0, "Empty party did not free fighters.");
            }),
            ("Reserved Tir remains unavailable when absent", () =>
            {
                var companionsOnly = empty.Reconcile(new[] { Ted, Odessa, 1, 2, 6 });
                Check(!companionsOnly.Assignments.Values.Contains(Tir), "Tir was assigned to a different role.");
                Reject<InvalidOperationException>(() => companionsOnly.Reconcile(new[] { Ted, Odessa, 1, 2, 6, 9 }));
            }),
            ("Malformed active parties fail without touching the previous plan", () =>
            {
                var active = empty.Reconcile(new[] { Tir, Ted });
                Reject<InvalidOperationException>(() => active.Reconcile(new[] { Tir, Ted, Ted }));
                Reject<InvalidOperationException>(() => active.Reconcile(new[] { Tir, Mathiu }));
                Reject<InvalidOperationException>(() => active.Reconcile(new[] { Tir, 110 }));
                Check(active.Assignments.Count == 2 && active.GetDonor(Ted) == Companions[0], "Failed plan changed state.");
            }),
            ("Save restores exact assignments and rejects malformed bindings", () =>
            {
                var original = empty.Reconcile(new[] { Tir, Ted, Odessa });
                var restored = FixedRosterAssignments.Restore(profile, original.Assignments.Reverse());
                Check(restored.Assignments.All(x => original.Assignments[x.Key] == x.Value), "Restore did not retain original assignments.");
                Reject<ArgumentException>(() => FixedRosterAssignments.Restore(profile,
                    original.Assignments.Append(new KeyValuePair<int, int>(1, Companions[0]))));
                Reject<ArgumentException>(() => FixedRosterAssignments.Restore(profile,
                    original.Assignments.Append(new KeyValuePair<int, int>(Ted, Companions[4]))));
                Reject<ArgumentException>(() => FixedRosterAssignments.Restore(profile,
                    new[] { new KeyValuePair<int, int>(Ted, Tir) }));
                Reject<ArgumentException>(() => FixedRosterAssignments.Restore(profile,
                    new[] { new KeyValuePair<int, int>(Tir, Companions[0]) }));
                Reject<ArgumentException>(() => FixedRosterAssignments.Restore(profile,
                    new[] { new KeyValuePair<int, int>(Ted, 1) }));
            })
        };

        foreach (var test in tests)
        {
            test.Run();
            Console.WriteLine("PASS " + test.Name);
        }
        Console.WriteLine($"{tests.Length}/{tests.Length} fixed-roster checks passed.");
    }
}
