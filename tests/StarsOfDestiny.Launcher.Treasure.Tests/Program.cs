using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using StarsOfDestiny.Launcher.Core;

string root = Path.Combine(Path.GetTempPath(), "sod-launcher-treasure-" + Guid.NewGuid().ToString("N"));
int passed = 0;

void Check(bool value, string message)
{
    if (!value) throw new InvalidOperationException(message);
}

void Reject(Action action)
{
    try { action(); }
    catch (Exception error) when (error is IOException or InvalidOperationException) { return; }
    throw new InvalidOperationException("Expected the saved treasure operation to be refused.");
}

void Test(string title, Action<string> action)
{
    string game = Path.Combine(root, (++passed).ToString());
    Directory.CreateDirectory(game);
    action(game);
    Console.WriteLine("PASS " + title);
}

string Folder(string game) => Path.Combine(game, "BepInEx", "config", "SuikodenRandomizer", "avatar-shuffle", "treasure-save-bindings");
string Identity(int index) => index.ToString("x64");

string Add(string game, int index, bool enabled)
{
    Directory.CreateDirectory(Folder(game));
    string path = Path.Combine(Folder(game), Identity(index) + ".json");
    File.WriteAllText(path, JsonSerializer.Serialize(new
    {
        treasureBindingVersion = 1,
        saveIdentity = Identity(index),
        campaignFingerprint = new string('a', 64),
        seed = "a saved seed",
        enabled,
        profileFingerprint = enabled ? new string('b', 64) : null
    }));
    return path;
}

try
{
    Test("Missing treasure settings produce an empty saved campaign list", game =>
    {
        var manager = TreasureCampaignManager.Load(game, () => false);
        Check(manager.Campaigns.Count == 0 && manager.Issues.Count == 0, "Missing settings were treated as an error.");
    });

    Test("Enabled and disabled saved policies retain their metadata", game =>
    {
        Add(game, 1, true);
        Add(game, 2, false);
        var manager = TreasureCampaignManager.Load(game, () => false);
        Check(manager.Campaigns.Count == 2 && manager.Campaigns.Count(value => value.Enabled) == 1,
            "Saved policy metadata was lost.");
        Check(manager.Campaigns.All(value => value.Seed == "a saved seed" && value.FileSha256.Length == 64),
            "The seed or source digest was not retained.");
    });

    Test("Reset keeps an exact backup and leaves native saves and avatar bindings intact", game =>
    {
        string path = Add(game, 3, true);
        string original = File.ReadAllText(path);
        string native = Path.Combine(game, "Data18");
        string avatar = Path.Combine(game, "avatar.json");
        File.WriteAllText(native, "native save");
        File.WriteAllText(avatar, "avatar mapping");
        var manager = TreasureCampaignManager.Load(game, () => false);
        string backup = manager.Reset(manager.Campaigns.Single());
        Check(!File.Exists(path) && File.ReadAllText(backup) == original, "Reset did not retain the original treasure bytes.");
        Check(TreasureCampaignManager.Load(game, () => false).Campaigns.Count == 0, "A backup was loaded as an active setting.");
        Check(File.ReadAllText(native) == "native save" && File.ReadAllText(avatar) == "avatar mapping", "Unrelated saves changed.");
    });

    Test("A running game prevents a saved policy reset", game =>
    {
        string path = Add(game, 4, true);
        var manager = TreasureCampaignManager.Load(game, () => true);
        Reject(() => manager.Reset(manager.Campaigns.Single()));
        Check(File.Exists(path) && Directory.GetFiles(Folder(game)).Length == 1, "The source changed while the game was running.");
    });

    Test("A game starting between validation and reset prevents changes", game =>
    {
        string path = Add(game, 5, true);
        int probes = 0;
        var manager = TreasureCampaignManager.Load(game, () => ++probes > 1);
        Reject(() => manager.Reset(manager.Campaigns.Single()));
        Check(File.Exists(path), "A race with game launch removed the original policy.");
    });

    Test("External edits after loading refuse reset and preserve the edited file", game =>
    {
        string path = Add(game, 6, true);
        var manager = TreasureCampaignManager.Load(game, () => false);
        File.AppendAllText(path, " ");
        Reject(() => manager.Reset(manager.Campaigns.Single()));
        Check(File.Exists(path) && File.ReadAllText(path).EndsWith(" ", StringComparison.Ordinal), "The concurrent edit was overwritten.");
    });

    Test("A policy selected from another loaded game cannot be reset", game =>
    {
        Add(game, 7, true);
        var first = TreasureCampaignManager.Load(game, () => false);
        var second = TreasureCampaignManager.Load(game, () => false);
        Reject(() => first.Reset(second.Campaigns.Single()));
    });

    Test("Malformed files are reported without hiding valid campaigns", game =>
    {
        Add(game, 8, true);
        File.WriteAllText(Path.Combine(Folder(game), Identity(9) + ".json"), "{}");
        File.WriteAllText(Path.Combine(Folder(game), "unexpected.json"), "{}");
        var manager = TreasureCampaignManager.Load(game, () => false);
        Check(manager.Campaigns.Count == 1 && manager.Issues.Count == 2, "A malformed file broke enumeration or was silently accepted.");
    });

    Test("Unsupported versions and duplicate fields are excluded from reset", game =>
    {
        string future = Add(game, 10, true);
        File.WriteAllText(future, File.ReadAllText(future).Replace("\"treasureBindingVersion\":1", "\"treasureBindingVersion\":2", StringComparison.Ordinal));
        string duplicate = Add(game, 11, true);
        File.WriteAllText(duplicate, File.ReadAllText(duplicate).Replace("\"enabled\":true", "\"enabled\":true,\"enabled\":false", StringComparison.Ordinal));
        var manager = TreasureCampaignManager.Load(game, () => false);
        Check(manager.Campaigns.Count == 0 && manager.Issues.Count == 2, "An unsupported or ambiguous policy was made resettable.");
    });

    Console.WriteLine(passed + " saved treasure launcher checks passed.");
}
finally
{
    if (Directory.Exists(root)) Directory.Delete(root, true);
}
