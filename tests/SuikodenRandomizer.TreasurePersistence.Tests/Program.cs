using SuikodenRandomizer.AvatarPlugin;
using SuikodenRandomizer.Core.Treasure;
using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

internal static class Program
{
    private const string Campaign = "test-avatar-campaign";
    private const string Seed = "treasure-persistence-test";
    private static readonly TreasureShuffleProfile Profile = TreasureShuffleProfile.Create(Seed);
    private static int _checks;

    private static string Identity(int number) => number.ToString("x64");
    private static string BindingPath(int number) => Path.Combine(AvatarRuntime.Folder, "treasure-save-bindings", Identity(number) + ".json");

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is InvalidDataException or InvalidOperationException) { return; }
        throw new InvalidOperationException("Expected invalid treasure binding to be rejected.");
    }

    private static void Test(string name, Action action)
    {
        action();
        _checks++;
        Console.WriteLine("PASS " + name);
    }

    private static void Tamper(int number, Action<JsonObject> modify)
    {
        TreasureBindingStore.Write(Identity(number), Campaign, Seed, Profile);
        var json = JsonNode.Parse(File.ReadAllText(BindingPath(number)))!.AsObject();
        modify(json);
        File.WriteAllText(BindingPath(number), json.ToJsonString());
        Reject(() => TreasureBindingStore.Read(Identity(number), Campaign, Seed));
    }

    private static void Main()
    {
        try
        {
            Test("Enabled policy round-trips its complete seed-bound mapping", () =>
            {
                TreasureBindingStore.CheckWritable();
                TreasureBindingStore.Write(Identity(1), Campaign, Seed, Profile);
                var restored = TreasureBindingStore.Read(Identity(1), Campaign, Seed)?.Profile;
                Check(restored?.Fingerprint == Profile.Fingerprint, "Restored profile changed.");
                Check(restored!.Assignments.Count == Profile.Assignments.Count
                    && restored.Assignments.All(pair => Profile.Assignments[pair.Key] == pair.Value), "Mapping is incomplete.");
            });

            Test("Disabled policy is explicitly saved and read", () =>
            {
                TreasureBindingStore.Write(Identity(2), Campaign, Seed, null);
                var restored = TreasureBindingStore.Read(Identity(2), Campaign, Seed);
                Check(restored is not null && restored.Profile is null, "Disabled policy disappeared.");
            });

            Test("The same snapshot cannot change from disabled to enabled or back", () =>
            {
                Reject(() => TreasureBindingStore.Write(Identity(1), Campaign, Seed, null));
                Reject(() => TreasureBindingStore.Write(Identity(2), Campaign, Seed, Profile));
                Check(TreasureBindingStore.Read(Identity(1), Campaign, Seed)!.Profile is not null, "Enabled binding was overwritten.");
                Check(TreasureBindingStore.Read(Identity(2), Campaign, Seed)!.Profile is null, "Disabled binding was overwritten.");
            });

            Test("Foreign campaign and seed cannot reuse an existing snapshot", () =>
            {
                Reject(() => TreasureBindingStore.Read(Identity(1), "different-campaign", Seed));
                Reject(() => TreasureBindingStore.Read(Identity(1), Campaign, "different-seed"));
                Reject(() => TreasureBindingStore.Write(Identity(1), "different-campaign", Seed, Profile));
            });

            Test("Version, build, algorithm, catalog and fingerprint changes are rejected", () =>
            {
                Tamper(10, json => json["treasureBindingVersion"] = 2);
                Tamper(11, json => json["gameBuildSha256"] = "other-game-build");
                Tamper(12, json => json["treasureVersion"] = 2);
                Tamper(13, json => json["algorithm"] = "future-algorithm");
                Tamper(14, json => json["catalogDigest"] = "different-catalog");
                Tamper(15, json => json["profileFingerprint"] = "different-profile");
                Tamper(16, json => json["saveIdentity"] = Identity(17));
            });

            Test("Changed, duplicate and missing reward assignments are rejected", () =>
            {
                Tamper(20, json => json["assignments"]![0]!["itemId"] = 255);
                Tamper(21, json => json["assignments"]![1]!["locationId"] = json["assignments"]![0]!["locationId"]!.GetValue<string>());
                Tamper(22, json => json["assignments"]!.AsArray().RemoveAt(0));
                Tamper(23, json => json["assignments"]![0]!["locationId"] = "unknown-location");
            });

            Test("Duplicate and unknown JSON fields are rejected", () =>
            {
                Tamper(30, json => json["unrecognized"] = true);
                Tamper(31, json => json["assignments"]![0]!["extra"] = true);
                TreasureBindingStore.Write(Identity(32), Campaign, Seed, Profile);
                string original = File.ReadAllText(BindingPath(32));
                File.WriteAllText(BindingPath(32), "{\"enabled\":true," + original.Substring(1));
                Reject(() => TreasureBindingStore.Read(Identity(32), Campaign, Seed));
            });

            Test("Disabled policy cannot smuggle an active profile", () =>
            {
                Tamper(40, json => json["enabled"] = false);
            });

            Test("Oversized binding files are rejected before parsing", () =>
            {
                File.WriteAllText(BindingPath(50), new string(' ', 65537));
                Reject(() => TreasureBindingStore.Read(Identity(50), Campaign, Seed));
            });

            Test("Concurrent identical publishers leave one complete mapping and no temporary files", () =>
            {
                Parallel.For(0, 8, _ => TreasureBindingStore.Write(Identity(60), Campaign, Seed, Profile));
                Check(TreasureBindingStore.Read(Identity(60), Campaign, Seed)!.Profile?.Fingerprint == Profile.Fingerprint,
                    "Concurrent publishers changed the profile.");
                Check(!Directory.EnumerateFiles(Path.GetDirectoryName(BindingPath(60))!, "*.tmp").Any(), "Temporary publication leaked.");
            });

            Console.WriteLine(_checks + " treasure persistence checks passed.");
        }
        finally
        {
            if (Directory.Exists(AvatarRuntime.Folder)) Directory.Delete(AvatarRuntime.Folder, recursive: true);
        }
    }
}
