using SuikodenRandomizer.Core;
using SuikodenRandomizer.Serialization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace SuikodenRandomizer.Core.SmokeTests;

internal static class Program
{
    private static int Main()
    {
        (string Name, Action Test)[] tests =
        {
            ("Algorithm 2 matches an independently computed golden vector", GoldenVector),
            ("One thousand seeds produce closed derangements of the approved pool", PermutationProperties),
            ("Repeated and Unicode seeds are deterministic", Determinism),
            ("Fingerprint ignores insertion order and diagnostics", FingerprintStability),
            ("Fingerprint changes with the seed contract", FingerprintChanges),
            ("Recruitment grants the mapped reward, not the source identity", Ownership),
            ("Availability follows the source role's bit 4", Availability),
            ("Protected native combat characters remain native", ProtectedRoster),
            ("Changing save flags cannot leak ownership", SaveIsolation),
            ("No starting reward is granted without a recruitment flag", NoStarterGrant),
            ("Duplicate and open mappings are rejected", RejectCollisions),
            ("Unsupported and temporary characters are rejected", RejectUnsupported),
            ("The complete approved profile is required", RejectPartialProfiles),
            ("Null and legacy configurations fail without mutation", RejectMalformed),
            ("Starting-party options cannot bypass the supported scope", RejectStarters),
            ("Seed labels are bounded", SeedBounds),
            ("Roster views do not change after input mutation", SnapshotIsolation),
            ("All 256 source flag values obey the native selectable predicate", EveryFlagCombination),
            ("Plugin and seed-tool JSON contract round-trips the same fingerprint", JsonRoundTrip),
            ("Duplicate JSON properties are rejected at every object level", JsonDuplicateProperties),
            ("Unknown and missing JSON properties are rejected", JsonExactProperties),
            ("Numeric character keys cannot alias each other", JsonKeyAliases),
            ("Malformed and legacy JSON fail explicitly", JsonMalformed),
            ("Source and gameplay IDs stay distinct across permutation cycles", CharacterDomains),
            ("Pinned and temporary IDs pass through both domains unchanged", PinnedCharacterDomains),
            ("Inactive reservations and stale empty pointers are legitimate", ResidentReservations),
            ("Hybrid identities and occupied asset aliases are rejected", ResidentIdentityFailures),
            ("Active ordering rejects duplicates, free slots and invalid indices", ActiveOrderFailures),
            ("Active count and resident array size are bounded", PartyShapeFailures),
            ("Persistent stats are required only for active occupants", ActiveBaseBindings)
        };
        int failed = 0;
        foreach ((string name, Action test) in tests)
        {
            try { test(); Console.WriteLine("PASS: " + name); }
            catch (Exception exception)
            {
                failed++;
                Console.Error.WriteLine("FAIL: " + name + Environment.NewLine + exception);
            }
        }
        Console.WriteLine($"{tests.Length - failed}/{tests.Length} core checks passed.");
        return failed == 0 ? 0 : 1;
    }

    private static SeedManifest Seed(string label = "Agilen-native-v020") => SeedGenerator.Generate(label);
    private static string Mapping(SeedManifest seed) => string.Join(",", seed.Mapping.OrderBy(pair => pair.Key).Select(pair => $"{pair.Key}:{pair.Value}"));

    private static void GoldenVector()
    {
        const string expected = "47:71,51:47,52:92,53:101,54:107,55:73,56:52,59:102,61:95,64:56,65:54,66:76,68:81,71:91,73:59,74:53,76:61,80:51,81:68,91:64,92:80,95:55,101:65,102:66,107:74";
        SeedManifest seed = Seed();
        Equal(expected, Mapping(seed));
        Equal("80fbf1d44dffc7184c17b82acc5189b8914c0eef17fdfb9f5f985564c71d0d59", SeedFingerprint.Compute(seed));
    }

    private static void PermutationProperties()
    {
        var expected = new HashSet<int>(SeedProfiles.OptionalRecruitIds);
        True(expected.Count == 25, "The approved pool must stay versioned.");
        for (int index = 0; index < 1000; index++)
        {
            SeedManifest seed = Seed("property-" + index.ToString(System.Globalization.CultureInfo.InvariantCulture));
            True(expected.SetEquals(seed.Mapping.Keys), "Wrong source set.");
            True(expected.SetEquals(seed.Mapping.Values), "Wrong reward set.");
            True(seed.Mapping.Count == seed.Mapping.Values.Distinct().Count(), "Duplicate reward.");
            True(seed.Mapping.All(pair => pair.Key != pair.Value), "An identity survived repair.");
            True(SeedValidator.Validate(seed).Count == 0, "Generated seed failed validation.");
        }
    }

    private static void Determinism()
    {
        Equal(Mapping(Seed("Ísland 🐉")), Mapping(Seed("Ísland 🐉")));
        True(Mapping(Seed("one")) != Mapping(Seed("two")), "Different labels unexpectedly shared a permutation.");
    }

    private static void FingerprintStability()
    {
        SeedManifest first = Seed();
        SeedManifest reordered = Seed();
        reordered.Mapping = reordered.Mapping.OrderByDescending(pair => pair.Key).ToDictionary(pair => pair.Key, pair => pair.Value);
        reordered.Diagnostics.TracePlayerLoading = !first.Diagnostics.TracePlayerLoading;
        Equal(SeedFingerprint.Compute(first), SeedFingerprint.Compute(reordered));
    }

    private static void FingerprintChanges()
    {
        SeedManifest first = Seed();
        SeedManifest changed = Seed();
        changed.Seed = "same mapping, different campaign label";
        True(SeedFingerprint.Compute(first) != SeedFingerprint.Compute(changed), "Seed label must be part of the contract.");
        True(SeedFingerprint.Compute(first) != SeedFingerprint.Compute(Seed("other")), "Different mapping fingerprint expected.");
    }

    private static void Ownership()
    {
        SeedManifest seed = Seed();
        var flags = new byte[128];
        flags[91] = 9;
        RosterView view = RosterProjection.Build(seed, flags);
        EqualSet(new[] { 91 }, view.CompletedChecks);
        EqualSet(new[] { seed.Mapping[91] }, view.OwnedRewards);
        EqualSet(new[] { seed.Mapping[91] }, view.AvailableRewards);
        True(view.HiddenSources.Contains(91), "Source identity leaked.");
        True(!view.SelectableCharacterIds.Contains(91), "Original recruit must not also be selectable.");
    }

    private static void Availability()
    {
        SeedManifest seed = Seed();
        int reward = seed.Mapping[91];
        var flags = new byte[128];
        flags[91] = 9;
        flags[reward] = 4;
        RosterView available = RosterProjection.Build(seed, flags);
        True(available.AvailableRewards.Contains(reward), "Reward's own original-source bit4 must not veto this completed check.");
        flags[91] = 13;
        flags[reward] = 0;
        RosterView blocked = RosterProjection.Build(seed, flags);
        True(blocked.OwnedRewards.Contains(reward), "Temporary unavailability must not erase ownership.");
        True(!blocked.AvailableRewards.Contains(reward), "Blocked source role leaked to the selectable roster.");
    }

    private static void ProtectedRoster()
    {
        var flags = new byte[128];
        flags[1] = 9;
        flags[8] = 9;
        flags[75] = 9;
        flags[27] = 9;
        flags[108] = 9;
        RosterView view = RosterProjection.Build(Seed(), flags);
        EqualSet(new[] { 1, 8, 75 }, view.SelectableCharacterIds);
    }

    private static void SaveIsolation()
    {
        SeedManifest seed = Seed();
        var firstSave = new byte[128];
        firstSave[91] = 9;
        True(RosterProjection.Build(seed, firstSave).OwnedRewards.Count == 1, "First save should own one reward.");
        RosterView other = RosterProjection.Build(seed, new byte[128]);
        True(other.OwnedRewards.Count == 0 && other.CompletedChecks.Count == 0, "Progress leaked between saves.");
        True(RosterProjection.Build(seed, new byte[4]).OwnedRewards.Count == 0, "Short boot-time flags must not grant rewards.");
    }

    private static void NoStarterGrant()
    {
        SeedManifest seed = Seed();
        seed.StartingParty.Enabled = true;
        seed.StartingParty.SourceIds.Add(91);
        True(RosterProjection.Build(seed, new byte[128]).OwnedRewards.Count == 0, "Invalid starter settings must never create ownership.");
    }

    private static void RejectCollisions()
    {
        SeedManifest seed = Seed();
        seed.Mapping[91] = seed.Mapping[47];
        ErrorContains(seed, "one-to-one");
        seed = Seed();
        seed.Mapping[91] = 75;
        ErrorContains(seed, "closed");
        ErrorContains(seed, "protected characters");
    }

    private static void RejectUnsupported()
    {
        foreach (int id in new[] { -1, 8, 27, 58, 108, 109, 127, 1000 })
        {
            SeedManifest seed = Seed();
            seed.Mapping[91] = id;
            True(SeedValidator.Validate(seed).Count > 0, "Unsupported reward accepted: " + id);
        }
    }

    private static void RejectPartialProfiles()
    {
        SeedManifest seed = Seed();
        seed.Mapping.Remove(47);
        ErrorContains(seed, "missing");
        seed = Seed();
        seed.Profile = "all-combat";
        ErrorContains(seed, "profile must");
        seed = Seed();
        seed.Mapping[47] = 47;
        ErrorContains(seed, "maps to itself");
    }

    private static void RejectMalformed()
    {
        True(SeedValidator.Validate(null).Count > 0, "Null seed accepted.");
        SeedManifest seed = Seed();
        seed.SchemaVersion = 1;
        ErrorContains(seed, "Back up");
        seed.Mapping = null!;
        seed.StartingParty = null!;
        seed.Diagnostics = null!;
        True(SeedValidator.Validate(seed).Count >= 4, "Null sections not rejected.");
        True(seed.Mapping is null && seed.StartingParty is null, "Validation mutated input.");
    }

    private static void RejectStarters()
    {
        SeedManifest seed = Seed();
        seed.StartingParty.Enabled = true;
        ErrorContains(seed, "not supported");
        seed.StartingParty.Enabled = false;
        seed.StartingParty.SourceIds.Add(1);
        ErrorContains(seed, "must be empty");
    }

    private static void SeedBounds()
    {
        foreach (string text in new[] { "", "   ", new string('a', 257) })
        {
            try { SeedGenerator.Generate(text); throw new InvalidOperationException("Invalid seed label accepted."); }
            catch (ArgumentException) { }
        }
        True(SeedValidator.Validate(Seed(new string('a', 256))).Count == 0, "Maximum supported label rejected.");
    }

    private static void SnapshotIsolation()
    {
        SeedManifest seed = Seed();
        var flags = new byte[128];
        flags[91] = 9;
        RosterView view = RosterProjection.Build(seed, flags);
        flags[91] = 0;
        seed.Mapping.Clear();
        EqualSet(new[] { 91 }, view.CompletedChecks);
        EqualSet(new[] { 64 }, view.OwnedRewards);
    }

    private static void EveryFlagCombination()
    {
        SeedManifest seed = Seed();
        var flags = new byte[128];
        for (int flag = 0; flag <= byte.MaxValue; flag++)
        {
            flags[91] = (byte)flag;
            RosterView view = RosterProjection.Build(seed, flags);
            True(view.OwnedRewards.Contains(seed.Mapping[91]) == ((flag & 1) != 0), "Ownership mismatch for flag " + flag);
            True(view.AvailableRewards.Contains(seed.Mapping[91]) == ((flag & 5) == 1), "Availability mismatch for flag " + flag);
        }
    }

    private static void JsonRoundTrip()
    {
        SeedManifest original = Seed("Ísland 🐉");
        SeedManifest roundTrip = SeedJson.Deserialize(SeedJson.Serialize(original));
        Equal(Mapping(original), Mapping(roundTrip));
        Equal(SeedFingerprint.Compute(original), SeedFingerprint.Compute(roundTrip));
    }

    private static void JsonDuplicateProperties()
    {
        string json = SeedJson.Serialize(Seed());
        InvalidJson(json.Replace("\"schemaVersion\": 2,", "\"schemaVersion\": 2, \"schemaVersion\": 2,"), "Duplicate");
        InvalidJson(json.Replace("\"enabled\": false,", "\"enabled\": false, \"enabled\": false,"), "Duplicate");
        InvalidJson(json.Replace("\"47\": 71,", "\"47\": 71, \"47\": 71,"), "Duplicate");
    }

    private static void JsonExactProperties()
    {
        string json = SeedJson.Serialize(Seed());
        InvalidJson(json.Replace("\"schemaVersion\": 2,", "\"schemaVersion\": 2, \"unknown\": 1,"), "Unknown");
        InvalidJson(json.Replace("\"enabled\": false,", "\"enabled\": false, \"typo\": true,"), "Unknown");
        InvalidJson(json.Replace("\"algorithmVersion\": 2,", ""), "Missing");
        InvalidJson(json.Replace("\"enabled\": false,", ""), "Missing");
        InvalidJson(json.Replace("\"tracePlayerLoading\": true,", ""), "Missing");
        InvalidJson(json.Replace("\"seed\":", "\"Seed\":"), "Unknown");
    }

    private static void JsonKeyAliases()
    {
        string json = SeedJson.Serialize(Seed());
        foreach (string alias in new[] { "047", "+47", " 47", "47 ", "0x2f", "-1", "99999999999999999999" })
            InvalidJson(json.Replace("\"47\":", "\"" + alias + "\":"), "Noncanonical");
    }

    private static void JsonMalformed()
    {
        string json = SeedJson.Serialize(Seed());
        InvalidJson("{\"schemaVersion\":1}", "Back up");
        foreach (string invalid in new[]
        {
            "null", "[]", "{}", json + " trailing", json.Replace("\"enabled\": false", "\"enabled\": \"false\""),
            json.Replace("\"sourceIds\": []", "\"sourceIds\": null"),
            json.Replace("\"47\": 71", "\"47\": 99999999999999999999999"),
            new string(' ', 131073)
        }) InvalidJson(invalid);
    }

    private static void InvalidJson(string json, string? fragment = null)
    {
        try { SeedJson.Deserialize(json); }
        catch (Exception exception) when (exception is JsonException or SeedValidationException)
        {
            if (fragment is not null) True(exception.Message.Contains(fragment, StringComparison.Ordinal), "Missing error fragment " + fragment + ": " + exception.Message);
            return;
        }
        throw new InvalidOperationException("Invalid JSON was accepted.");
    }

    private static void CharacterDomains()
    {
        SeedManifest seed = Seed();
        int[] ids = SeedProfiles.OptionalRecruitIds.ToArray();
        seed.Mapping[ids[0]] = ids[1];
        seed.Mapping[ids[1]] = ids[0];
        for (int i = 2; i < ids.Length; i++) seed.Mapping[ids[i]] = ids[i == ids.Length - 1 ? 2 : i + 1];
        SeedValidator.ValidateAndThrow(seed);
        int reward = CharacterResolution.Resolve(seed, ids[0], CharacterIdDomain.RecruitmentSource);
        True(reward == ids[1], "Source recruitment did not resolve its reward.");
        True(CharacterResolution.Resolve(seed, reward, CharacterIdDomain.Gameplay) == reward,
            "Save or menu gameplay ID was remapped a second time.");
        True(CharacterResolution.Resolve(seed, reward, CharacterIdDomain.RecruitmentSource) == ids[0],
            "A character already used as a reward still needs its own independent source mapping.");
    }

    private static void PinnedCharacterDomains()
    {
        foreach (int id in new[] { 1, 2, 6, 8, 17, 35, 75, 108, 109 })
        {
            True(CharacterResolution.Resolve(Seed(), id, CharacterIdDomain.RecruitmentSource) == id, "Protected source was remapped.");
            True(CharacterResolution.Resolve(Seed(), id, CharacterIdDomain.Gameplay) == id, "Protected gameplay ID was remapped.");
        }
        try { CharacterResolution.Resolve(Seed(), 91, (CharacterIdDomain)99); }
        catch (ArgumentOutOfRangeException) { return; }
        throw new InvalidOperationException("Unknown character domain was accepted.");
    }

    private static PartySlotState[] SampleSlots() => new[]
    {
        new PartySlotState(8, 8, 0x1000, 8),
        new PartySlotState(47, 47, 0x2000, 47),
        new PartySlotState(91, 91, 0x3000, null),
        new PartySlotState(-1, 47, 0x2000, 47), // legitimate stale free slot
        new PartySlotState(-1, null, 0, null),
        new PartySlotState(-1, null, 0, null)
    };

    private static void ResidentReservations()
    {
        True(PartyInvariants.Validate(SampleSlots(), new[] { 0, 1, -2, -2, -2, -2 }, 2).Count == 0,
            "A valid inactive resident or stale free cell was rejected.");
    }

    private static void ResidentIdentityFailures()
    {
        PartySlotState[] slots = SampleSlots();
        slots[1] = new PartySlotState(47, 91, 0x2000, 47);
        True(PartyInvariants.Validate(slots, new[] { 0, 1 }, 2).Any(error => error.Contains("disagrees", StringComparison.Ordinal)), "Hybrid identity accepted.");
        slots = SampleSlots();
        slots[2] = new PartySlotState(91, 91, 0x2000, null);
        True(PartyInvariants.Validate(slots, new[] { 0, 1 }, 2).Any(error => error.Contains("share", StringComparison.Ordinal)), "Duplicate occupied asset accepted.");
        slots[2] = new PartySlotState(47, 47, 0x3000, null);
        True(PartyInvariants.Validate(slots, new[] { 0, 1 }, 2).Any(error => error.Contains("more than one", StringComparison.Ordinal)), "Duplicate occupied identity accepted.");
        slots[2] = new PartySlotState(91, 91, 0, null);
        True(PartyInvariants.Validate(slots, new[] { 0, 1 }, 2).Any(error => error.Contains("no character asset", StringComparison.Ordinal)), "Null occupied pointer accepted.");
    }

    private static void ActiveOrderFailures()
    {
        foreach (int[] order in new[] { new[] { 0, 0 }, new[] { 0, 3 }, new[] { 0, 6 }, new[] { 0, -2 }, new[] { 0 } })
            True(PartyInvariants.Validate(SampleSlots(), order, 2).Count > 0, "Invalid active order accepted: " + string.Join(",", order));
    }

    private static void PartyShapeFailures()
    {
        foreach (int count in new[] { -1, 7, int.MaxValue })
            True(PartyInvariants.Validate(SampleSlots(), new[] { 0, 1 }, count).Count > 0, "Invalid active count accepted.");
        True(PartyInvariants.Validate(SampleSlots().Take(5).ToArray(), new[] { 0, 1 }, 2).Count > 0, "Short resident array accepted.");
        PartySlotState[] slots = SampleSlots();
        slots[5] = new PartySlotState(-2, null, 0, null);
        True(PartyInvariants.Validate(slots, new[] { 0, 1 }, 2).Count > 0, "Invalid empty-slot sentinel accepted.");
    }

    private static void ActiveBaseBindings()
    {
        PartySlotState[] slots = SampleSlots();
        True(PartyInvariants.Validate(slots, new[] { 0, 1, 2 }, 3).Any(error => error.Contains("persistent stats", StringComparison.Ordinal)), "Unbound active occupant accepted.");
        True(PartyInvariants.Validate(slots, new[] { 0, 1, 2 }, 3, requireActiveBases: false).Count == 0, "Explicit pre-binding inspection failed.");
        slots[2] = new PartySlotState(91, 91, 0x3000, 91);
        True(PartyInvariants.Validate(slots, new[] { 0, 1, 2 }, 3).Count == 0, "Correct binding rejected.");
        slots[1] = new PartySlotState(47, 47, 0x2000, 91);
        True(PartyInvariants.Validate(slots, new[] { 0, 1, 2 }, 3).Count > 0, "Cross-character stats binding accepted.");
    }

    private static void ErrorContains(SeedManifest seed, string fragment)
    {
        True(SeedValidator.Validate(seed).Any(error => error.Contains(fragment, StringComparison.Ordinal)), "Expected validation error containing: " + fragment);
    }
    private static void True(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Equal(string expected, string actual) => True(expected == actual, $"Expected {expected}; actual {actual}");
    private static void EqualSet(IEnumerable<int> expected, IEnumerable<int> actual) => True(new HashSet<int>(expected).SetEquals(actual), "Set mismatch.");
}
