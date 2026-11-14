using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using SuikodenRandomizer.Core.Suikoden2;

internal static class Program
{
    private static void Check(bool valid, string message) { if (!valid) throw new Exception(message); }
    private static void Reject<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
    private static int Main(string[] args)
    {
        var vectors = new List<object>();
        foreach (string seed in new[] { "first-adventure", "agilen-080", "Å rune 😺" })
        {
            foreach (bool include in new[] { false, true })
            {
                var p = Gsd2AvatarProfile.Create(seed, include);
                vectors.Add(new { seed, mode = p.Mode, includeProtagonist = include, p.ProfileId, p.Fingerprint,
                    assignments = p.RoleIds.Select(id => new[] { id, p.GetDonor(id) }).ToArray(), heroes = p.HeroIds });
            }
            foreach (var p in new[] { Gsd2AvatarProfile.CreateChosen(seed, new[] { 2, 3, 4, 5, 6 }), Gsd2AvatarProfile.CreateRandom(seed) })
                vectors.Add(new { seed, mode = p.Mode, includeProtagonist = false, p.ProfileId, p.Fingerprint,
                    assignments = Array.Empty<int[]>(), heroes = p.HeroIds });
        }
        var profile = Gsd2AvatarProfile.CreateChosen("test", new[] { 2, 3, 4, 5, 6 });
        var empty = Gsd2FixedRosterAssignments.Empty(profile);
        var tests = new (string Name, Action Run)[]
        {
            ("Catalog isolates original IDs and formation classes", () => {
                Check(Gsd2CharacterCatalog.ProtagonistId == 1 && Gsd2CharacterCatalog.NormalCombatIds.Count == 78
                    && Gsd2CharacterCatalog.FixedCompanionIds.Count == 74, "Wrong pool sizes");
                Check(Gsd2CharacterCatalog.LargeCombatIds.SequenceEqual(new[] {49,50,72,74}), "Wrong large units");
                foreach (int id in new[] {0,1,80,81,82,83,84}) Check(!Gsd2CharacterCatalog.IsNormalCombat(id), "Protected ID became eligible");
            }),
            ("Shuffle is deterministic, bijective and formation preserving", () => {
                foreach (int number in Enumerable.Range(0, 100)) foreach (bool include in new[] {false,true}) {
                    var p = Gsd2AvatarProfile.Create("seed-"+number, include);
                    Check(p.RoleIds.Count == (include ? 79 : 78) && p.Assignments.Values.OrderBy(x=>x).SequenceEqual(p.RoleIds), "Not a bijection");
                    Check(p.RoleIds.All(id => Gsd2CharacterCatalog.GetSlotCost(id)==Gsd2CharacterCatalog.GetSlotCost(p.GetDonor(id))), "Footprint changed");
                    Check(p.Fingerprint == Gsd2AvatarProfile.Create("seed-"+number,include).Fingerprint, "Not deterministic");
                }
            }),
            ("Seed normalization rejects malformed or unsafe input", () => {
                Check(Gsd2AvatarProfile.Create("  A\u030A  ").Fingerprint==Gsd2AvatarProfile.Create("Å").Fingerprint, "NFC/trim mismatch");
                foreach (string seed in new[] {"", "\u0000", "a\\b", "x\nq", "\ud800", new string('x',129)}) Reject<ArgumentException>(()=>Gsd2AvatarProfile.Create(seed));
            }),
            ("Fixed rosters reject duplicates, guests, large units and reserved hero", () => {
                foreach (int id in new[] {0,1,49,50,72,74,80,81,82,83,84,-1})
                    Reject<ArgumentException>(()=>Gsd2AvatarProfile.CreateChosen("test",new[] {2,3,4,5,id}));
                Reject<ArgumentException>(()=>Gsd2AvatarProfile.CreateChosen("test",new[] {2,3,4,5,2}));
                Check(profile.HeroIds.SequenceEqual(new[] {1,2,3,4,5,6}), "Ordered roster changed");
                Check(profile.Fingerprint==Gsd2AvatarProfile.CreateChosen("another seed",new[] {2,3,4,5,6}).Fingerprint, "Chosen identity depends on seed");
                Check(profile.Fingerprint!=Gsd2AvatarProfile.CreateChosen("test",new[] {6,5,4,3,2}).Fingerprint, "Order omitted");
            }),
            ("Random fixed rosters contain exactly five distinct single-slot companions", () => {
                foreach (int number in Enumerable.Range(0,100)) {
                    var p=Gsd2AvatarProfile.CreateRandom("fixed-"+number);
                    Check(p.CompanionIds.Count==5 && p.HeroIds.Distinct().Count()==6 && p.CompanionIds.All(Gsd2CharacterCatalog.IsFixedCompanion), "Invalid fixed draw");
                }
            }),
            ("Bindings preserve canonical roles, order independence and survivor ownership", () => {
                var first=empty.Reconcile(new[] {1,80,7,8});
                Check(first.GetDonor(1)==1 && first.GetDonor(80)==2, "Initial assignment wrong");
                Check(ReferenceEquals(first,first.Reconcile(new[] {8,7,80,1})), "Formation reorder changed ownership");
                var next=first.Reconcile(new[] {1,81,7,8});
                Check(next.GetDonor(81)==2 && next.GetDonor(7)==first.GetDonor(7), "Join/departure reset survivors");
                Check(first.Assignments.ContainsKey(80) && empty.Assignments.Count==0, "Prior immutable plan mutated");
            }),
            ("Bindings honor current native actor count and reserve protagonist", () => {
                Check(empty.Reconcile(new[] {80,2,3,4,5}).Assignments.Values.All(id=>id!=1), "Hero leaked into another role");
                var six=empty.Reconcile(new[] {80,2,3,4,5,6});
                Check(six.Assignments.Count==6 && six.GetDonor(6)==1 && !six.Assignments.ContainsKey(1), "Six nonhero actors did not borrow display donor1 without canonical insertion");
                var heroGroup=six.Reconcile(new[] {1,80,2,3,4,6});
                Check(heroGroup.GetDonor(1)==1 && heroGroup.GetDonor(6)!=1 && heroGroup.GetDonor(80)==six.GetDonor(80), "Hero group failed to reclaim its donor or reset a surviving companion");
                var absent=heroGroup.Reconcile(new[] {80,2,3,4,6,7});
                Check(absent.GetDonor(7)==1 && absent.GetDonor(6)==heroGroup.GetDonor(6) && !absent.Assignments.ContainsKey(1), "Absent transition changed survivor ownership or inserted canonical hero");
                var smaller=six.Reconcile(new[] {80,2,3,4,6});
                Check(smaller.Assignments.Values.All(id=>id!=1), "Smaller nonhero party retained borrowed protagonist");
                Reject<InvalidOperationException>(()=>empty.Reconcile(new[] {1,2,3},2));
                Reject<InvalidOperationException>(()=>empty.Reconcile(new[] {1,2,2}));
                Reject<InvalidOperationException>(()=>empty.Reconcile(new[] {1,84}));
                Reject<ArgumentException>(()=>Gsd2FixedRosterAssignments.Empty(Gsd2AvatarProfile.Create("test")));
            }),
            ("Saved fixed bindings validate and roundtrip without reallocation", () => {
                var first=empty.Reconcile(new[] {1,80,7});
                var restored=Gsd2FixedRosterAssignments.Restore(profile,first.Assignments.Reverse());
                Check(first.Assignments.All(p=>restored.GetDonor(p.Key)==p.Value), "Restore changed bindings");
                var borrowed=empty.Reconcile(new[] {80,2,3,4,5,6});
                Check(Gsd2FixedRosterAssignments.Restore(profile,borrowed.Assignments).GetDonor(6)==1, "Six absent-protagonist bindings were rejected");
                Reject<ArgumentException>(()=>Gsd2FixedRosterAssignments.Restore(profile,new[] {new KeyValuePair<int,int>(80,1)}));
                Reject<ArgumentException>(()=>Gsd2FixedRosterAssignments.Restore(profile,new[] {new KeyValuePair<int,int>(80,1),new KeyValuePair<int,int>(1,1)}));
                Reject<ArgumentException>(()=>Gsd2FixedRosterAssignments.Restore(profile,new[] {new KeyValuePair<int,int>(1,2)}));
                Reject<ArgumentException>(()=>Gsd2FixedRosterAssignments.Restore(profile,new[] {new KeyValuePair<int,int>(80,2),new KeyValuePair<int,int>(3,2)}));
            })
        };
        foreach (var test in tests) { test.Run(); Console.WriteLine("PASS " + test.Name); }
        if (args.Length==2 && args[0]=="--vectors") File.WriteAllText(args[1],JsonSerializer.Serialize(vectors,new JsonSerializerOptions {WriteIndented=true}));
        Console.WriteLine($"{tests.Length}/{tests.Length} Suikoden II Core checks passed.");
        return 0;
    }
}
