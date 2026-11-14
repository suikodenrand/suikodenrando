using System;
using System.Collections.Generic;
using System.Linq;
using SuikodenRandomizer.Core.Avatars;
using SuikodenRandomizer.Core.Treasure;

internal static class Program
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Reject(Action action)
    {
        try { action(); }
        catch (ArgumentException) { return; }
        catch (NotSupportedException) { return; }
        throw new InvalidOperationException("Invalid operation was accepted.");
    }

    private static int Main()
    {
        var tests = new (string Name, Action Run)[]
        {
            ("Catalog contains exactly 136 ordinary chest flags and 132 shuffle locations", () =>
            {
                Check(S1TreasureCatalog.Chests.Count == 136, "Wrong ordinary chest count.");
                Check(S1TreasureCatalog.Chests.Count(chest => chest.IsShuffleEligible) == 132, "Wrong shuffle pool.");
                Check(S1TreasureCatalog.Chests.Select(chest => chest.LocationId).Distinct().Count() == 136, "Alias duplicated a location.");
                Check(S1TreasureCatalog.Chests.All(chest => S1TreasureCatalog.TryGetChest(chest.FlagIndex, chest.FlagMask, out var found)
                    && ReferenceEquals(found, chest)), "Flag lookup lost a chest.");
            }),
            ("Catalog digest matches the independently serialized original corpus", () =>
            {
                Check(S1TreasureCatalog.CatalogDigest == "a28907f68d22a9419c2240f5fcb6a5e4d7873e5a792f5c472121019bc64488e2", "Catalog reward or script drifted.");
            }),
            ("Rockland map aliases share one location and money and special flags stay outside the catalog", () =>
            {
                Check(S1TreasureCatalog.TryGetChest(172, 0x10, out var rockland)
                    && rockland.LocationId == "s1:tmp-event:172:10" && rockland.OriginalItemId == 25, "Rockland alias flag changed.");
                Check(!S1TreasureCatalog.TryGetChest(16, 0x20, out _) && !S1TreasureCatalog.TryGetChest(170, 0x10, out _)
                    && !S1TreasureCatalog.TryGetChest(184, 0x04, out _), "Money or custom reward entered normal pool.");
            }),
            ("Whole-script guard requires the original bytes and exact item operand cursor", () =>
            {
                foreach (var chest in S1TreasureCatalog.Chests)
                {
                    byte[] bytes = chest.Script.ToArray();
                    Check(chest.MatchesOriginalScript(bytes, chest.ItemOperandOffset), "Original script rejected.");
                    Check(!chest.MatchesOriginalScript(bytes, chest.ItemOperandOffset - 1), "Opcode cursor accepted as operand.");
                    Check(!chest.MatchesOriginalScript(bytes.AsSpan(1), chest.ItemOperandOffset), "Truncated script accepted.");
                    bytes[^1] ^= 1;
                    Check(!chest.MatchesOriginalScript(bytes, chest.ItemOperandOffset), "Changed suffix accepted.");
                    bytes = chest.Script.ToArray();
                    bytes[chest.ItemOperandOffset] ^= 1;
                    Check(!chest.MatchesOriginalScript(bytes, chest.ItemOperandOffset), "Already edited item accepted.");
                }
            }),
            ("Seed shuffle matches an independent Python golden mapping", () =>
            {
                var profile = TreasureShuffleProfile.Create("Agilen-080");
                Check(profile.Fingerprint == "aaed73dbc36b595ab52097608fe564fdd0b6d7902b192e21ff6ef961ebec6d40", "Algorithm or profile serialization changed.");
                int[] first = S1TreasureCatalog.Chests.Take(12).Select(chest => profile.Assignments[chest.LocationId]).ToArray();
                Check(first.SequenceEqual(new[] {73,91,57,17,144,37,146,46,104,105,25,110}), "Cross-language reward fixture differs.");
            }),
            ("A thousand seeds preserve reward multiplicity and protected recruitment and travel items", () =>
            {
                int[] pool = S1TreasureCatalog.Chests.Select(chest => chest.OriginalItemId).OrderBy(id => id).ToArray();
                foreach (int index in Enumerable.Range(0, 1000))
                {
                    var profile = TreasureShuffleProfile.Create("treasure-" + index);
                    Check(profile.Assignments.Count == 136 && profile.Assignments.Values.OrderBy(id => id).SequenceEqual(pool), "Reward pool changed.");
                    foreach (var chest in S1TreasureCatalog.Chests.Where(chest => !chest.IsShuffleEligible))
                        Check(profile.Assignments[chest.LocationId] == chest.OriginalItemId, "Protected reward moved.");
                }
            }),
            ("Same seed reproduces a mapping and different seeds vary eligible rewards", () =>
            {
                var a = TreasureShuffleProfile.Create("repeat");
                var b = TreasureShuffleProfile.Create("repeat");
                var c = TreasureShuffleProfile.Create("different");
                Check(a.Fingerprint == b.Fingerprint && a.Assignments.SequenceEqual(b.Assignments), "Seed changed on replay.");
                Check(a.Fingerprint != c.Fingerprint && a.Assignments.Any(pair => c.Assignments[pair.Key] != pair.Value), "Different seeds collapsed.");
                Check(!a.TryGetReward("unknown", out _), "Unknown chest resolved.");
            }),
            ("Seed whitespace and Unicode normalize and malformed seeds are rejected", () =>
            {
                Check(TreasureShuffleProfile.Create(" seed ").Fingerprint == TreasureShuffleProfile.Create("seed").Fingerprint, "Whitespace differs.");
                Check(TreasureShuffleProfile.Create("e\u0301").Fingerprint == TreasureShuffleProfile.Create("é").Fingerprint, "Unicode differs.");
                foreach (string seed in new[] {"", " ", new string('a',129), "bad\nseed", "bad\0seed"})
                    Reject(() => TreasureShuffleProfile.Create(seed));
                Reject(() => TreasureShuffleProfile.Create(null!));
            }),
            ("Catalog scripts and reward assignments are immutable", () =>
            {
                var chest = S1TreasureCatalog.Chests[0];
                var profile = TreasureShuffleProfile.Create("immutable");
                Reject(() => ((IList<TreasureChest>)S1TreasureCatalog.Chests)[0] = S1TreasureCatalog.Chests[1]);
                Reject(() => ((IList<byte>)chest.Script)[0] = 0);
                Reject(() => ((IDictionary<string, int>)profile.Assignments)[chest.LocationId] = 0);
            }),
            ("Invalid flag indices cannot collide with a real collection bit", () =>
            {
                foreach (int index in new[] {-1,240,int.MinValue,int.MaxValue,170 + (1 << 24)})
                    Check(!S1TreasureCatalog.TryGetChest(index, 1, out _), "Invalid flag index aliased a chest.");
                Check(!S1TreasureCatalog.TryGetChest(170,0,out _) && !S1TreasureCatalog.TryGetChest(170,3,out _), "Non-single bit resolved.");
            }),
            ("Treasure creation cannot change character-shuffle mapping", () =>
            {
                var before = AvatarShuffleProfile.Create("Agilen-080");
                TreasureShuffleProfile.Create("Agilen-080");
                var after = AvatarShuffleProfile.Create("Agilen-080");
                Check(before.Fingerprint == after.Fingerprint
                    && before.Fingerprint == "27d283e70e6bfb8ec2143e68d79e56fe19f5f7b8178c3ff1ee1356ca744acac4", "Character shuffle changed.");
            })
        };

        foreach (var test in tests)
        {
            try { test.Run(); Console.WriteLine("PASS " + test.Name); }
            catch (Exception error) { Console.Error.WriteLine("FAIL " + test.Name + " " + error); return 1; }
        }
        Console.WriteLine(tests.Length + " treasure checks passed.");
        return 0;
    }
}
