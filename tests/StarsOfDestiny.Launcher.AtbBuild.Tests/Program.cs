using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using StarsOfDestiny.Launcher.Core;

bool invalidMetadata = args.Length == 2 && args[0] == "--expect-invalid-metadata";
if (args.Length is < 1 or > 2)
    throw new ArgumentException("Usage: atb-build-tests <matching-atb.dll> [different-build-atb.dll]");
string source = Path.GetFullPath(args[invalidMetadata ? 1 : 0]);
string? other = args.Length == 2 && !invalidMetadata ? Path.GetFullPath(args[1]) : null;
bool needsPortraits = AssemblyName.GetAssemblyName(source).Version >= new Version(0, 12, 0);
const string atbName = "SuikodenRandomizer.S1BattlePreview.dll";
string parent = Path.GetFullPath(Path.GetTempPath());
string fixture = Path.Combine(parent, "stars-atb-build-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(fixture);
File.WriteAllText(Path.Combine(fixture, ".fixture"), "Disposable ATB source-build test only");
int passed = 0;
string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
void Pass(string message) { passed++; Console.WriteLine("PASS " + message); }
Dictionary<string, string> Snapshot(string folder) => Directory.GetFiles(folder, "*", SearchOption.AllDirectories)
    .ToDictionary(p => Path.GetRelativePath(folder, p), p => Hash(p) + ":" + File.GetLastWriteTimeUtc(p).Ticks);
bool Same(Dictionary<string, string> a, Dictionary<string, string> b) => a.Count == b.Count && a.All(e => b.GetValueOrDefault(e.Key) == e.Value);
string CreateGame(string name, string atb, bool includePortraits = true)
{
    string root = Path.Combine(fixture, name);
    Directory.CreateDirectory(Path.Combine(root, "BepInEx", "plugins"));
    Directory.CreateDirectory(Path.Combine(root, "BepInEx", "config"));
    File.WriteAllText(Path.Combine(root, GameInstallation.GameExeName), "INVALID TEST GAME EXE - NEVER EXECUTE");
    File.Copy(atb, Path.Combine(root, "BepInEx", atbName));
    if (needsPortraits && includePortraits)
    {
        string assets = Path.Combine(root, "BepInEx", "CTBAssets", "Enemies");
        Directory.CreateDirectory(assets);
        string portrait = Path.Combine(assets, "enemy_001.png");
        File.WriteAllBytes(portrait, new byte[] { 1, 2, 3 });
        File.WriteAllText(Path.Combine(assets, "index.json"), JsonSerializer.Serialize(new {
            schemaVersion = 1, entries = new[] { new { sourceId = "enemy_001", opdName = "enemy_001_00", file = "enemy_001.png", sha256 = Hash(portrait) } }
        }));
    }
    File.WriteAllText(Path.Combine(root, "BepInEx", "config", "agilen.suikoden1.battle-preview.cfg"), "[Preview]\nEnabled = false\n");
    return root;
}
void Reject(Action run)
{
    try { run(); }
    catch (Exception e) when (e is InvalidDataException or InvalidOperationException) { return; }
    throw new InvalidOperationException("Expected activation rejection.");
}
string Portraits(string game, bool active = false) =>
    Path.Combine(game, "BepInEx", active ? "plugins" : "", "CTBAssets", "Enemies");

void CheckPortraitActivation()
{
    string publicGame = CreateGame("public-ctb", source, includePortraits: false);
    string publicDll = GameInstallation.ActivateAtb(publicGame);
    GameInstallation.RequireCtbSupport(publicGame);
    Check(Hash(publicDll) == Hash(source) && !Directory.Exists(Portraits(publicGame, true)),
        "Public CTB activation requires external game art or created a spurious portrait directory.");
    Pass("Public CTB activates without pre-extracted game art");
    string game = CreateGame("empty-portraits", source);
    string dormant = Portraits(game), active = Portraits(game, true);
    Directory.CreateDirectory(active);
    File.WriteAllText(Path.Combine(dormant, "unreferenced.png"), "should stay in the source directory");
    var index = JsonNode.Parse(File.ReadAllText(Path.Combine(dormant, "index.json")))!.AsObject();
    var entries = index["entries"]!.AsArray();
    var alias = entries[0]!.DeepClone();
    alias["sourceId"] = "enemy_002"; alias["opdName"] = "ENEMY_001_00"; alias["file"] = "enemy_002.png";
    alias["sha256"] = alias["sha256"]!.GetValue<string>().ToLowerInvariant();
    entries.Add(alias);
    File.Copy(Path.Combine(dormant, "enemy_001.png"), Path.Combine(dormant, "enemy_002.png"));
    File.WriteAllText(Path.Combine(dormant, "index.json"), index.ToJsonString());
    Parallel.Invoke(() => GameInstallation.ActivateAtb(game), () => GameInstallation.ActivateAtb(game));
    Check(Directory.GetFiles(active).Length == 3 && !File.Exists(Path.Combine(active, "unreferenced.png")),
        "Concurrent activation copied unrelated files or lost an indexed portrait.");
    var originals = Snapshot(active);
    File.WriteAllText(Path.Combine(dormant, "index.json"), "different dormant build");
    GameInstallation.ActivateAtb(game);
    Check(Same(originals, Snapshot(active)), "An active healthy preview was changed by its dormant copy.");
    Pass("Concurrent activation fills an empty directory with only referenced portraits and accepts identical aliases");

    foreach (bool conflict in new[] { false, true })
    {
        string root = CreateGame("existing-portraits-" + conflict, source);
        string destination = Portraits(root, true);
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.GetFiles(Portraits(root))) File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        string path = Path.Combine(destination, "index.json");
        var existing = JsonNode.Parse(File.ReadAllText(path))!;
        if (conflict) existing["entries"]![0]!["sourceId"] = "another_enemy";
        File.WriteAllText(path, existing.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        File.WriteAllText(Path.Combine(destination, "keep.txt"), "existing local content");
        var before = Snapshot(destination);
        if (conflict)
        {
            Reject(() => GameInstallation.ActivateAtb(root));
            Check(!GameInstallation.Inspect(root).AtbActive, "A conflicting set was activated.");
        }
        else GameInstallation.ActivateAtb(root);
        Check(Same(before, Snapshot(destination)), "Promoting a dormant preview modified an existing portrait set.");
    }
    Pass("Promotion accepts equivalent pre-existing portraits and preserves a conflicting set on rejection");

    foreach (var (name, change) in new (string, Action<JsonArray>)[] {
        ("source", rows => rows[0]!.AsObject().Remove("sourceId")),
        ("alias", rows => rows[0]!["opdName"] = ""),
        ("hash", rows => rows[0]!["sha256"] = "invalid"),
        ("path", rows => rows[0]!["file"] = "../enemy.png"),
        ("missing", rows => rows[0]!["file"] = "absent.png"),
        ("alias-conflict", rows => {
            var entry = rows[0]!.DeepClone();
            entry["sourceId"] = "enemy_002"; entry["opdName"] = "ENEMY_001_00"; entry["file"] = "other.png";
            entry["sha256"] = Convert.ToHexString(SHA256.HashData(new byte[] { 4 })); rows.Add(entry);
        })
    })
    {
        string root = CreateGame("bad-portrait-" + name, source);
        string folder = Portraits(root), path = Path.Combine(folder, "index.json");
        File.WriteAllBytes(Path.Combine(folder, "other.png"), new byte[] { 4 });
        var bad = JsonNode.Parse(File.ReadAllText(path))!;
        change(bad["entries"]!.AsArray());
        File.WriteAllText(path, bad.ToJsonString());
        var before = Snapshot(root);
        Reject(() => GameInstallation.ActivateAtb(root));
        Check(Same(before, Snapshot(root)) && !GameInstallation.Inspect(root).AtbActive,
            "Malformed or incomplete portrait set changed the installation: " + name);
    }
    Pass("Missing portraits and malformed names, hashes, paths and aliases refuse activation without publishing files");
}

try
{
    string game = CreateGame("matching", source);
    var before = Snapshot(game);
    if (invalidMetadata)
    {
        Reject(() => GameInstallation.ActivateAtb(game));
        Check(Same(before, Snapshot(game)), "Malformed build metadata caused fixture writes.");
        Pass("Malformed reviewed hash metadata is refused before any copy");
        return;
    }
    var inspected = GameInstallation.Inspect(game);
    Check(!inspected.AtbActive && inspected.AtbDormantPath is not null && Same(before, Snapshot(game)), "Dormant inspection changed files or found the wrong state.");
    Pass("Dormant inspection is read-only and reports the source build available");
    string activated = GameInstallation.ActivateAtb(game);
    Check(Hash(activated) == Hash(source), "Activation copied different bytes.");
    Check(File.ReadAllText(Path.Combine(game, "BepInEx", "config", "agilen.suikoden1.battle-preview.cfg")).Contains("Enabled = false", StringComparison.Ordinal), "Activation enabled ATB.");
    Check(File.ReadAllText(Path.Combine(game, GameInstallation.GameExeName)).StartsWith("INVALID TEST", StringComparison.Ordinal), "Activation touched the game sentinel.");
    Pass("Matching reviewed ATB activates with exact bytes while config remains disabled");
    if (needsPortraits)
    {
        CheckPortraitActivation();
        string assets = Path.Combine(Path.GetDirectoryName(activated)!, "CTBAssets", "Enemies");
        Check(File.Exists(Path.Combine(assets, "index.json")) && File.Exists(Path.Combine(assets, "enemy_001.png")),
            "Private CTB activation left the portraits behind.");
        string corrupt = CreateGame("corrupt-assets", source);
        File.AppendAllText(Path.Combine(corrupt, "BepInEx", "CTBAssets", "Enemies", "enemy_001.png"), "changed");
        Reject(() => GameInstallation.ActivateAtb(corrupt));
        Check(!GameInstallation.Inspect(corrupt).AtbActive, "An incomplete CTB payload was activated.");
        Pass("CTB activation verifies optional local portraits before publishing the DLL");
    }
    before = Snapshot(game);
    Check(GameInstallation.ActivateAtb(game) == activated && Same(before, Snapshot(game)), "Repeated activation changed files.");
    Pass("Repeated activation preserves bytes and timestamps");
    string changed = CreateGame("changed", source);
    File.AppendAllText(Path.Combine(changed, "BepInEx", atbName), "changed");
    before = Snapshot(changed);
    Reject(() => GameInstallation.ActivateAtb(changed));
    Check(Same(before, Snapshot(changed)), "Rejected source changed files.");
    Pass("Modified ATB bytes are refused without writes");
    string duplicate = CreateGame("duplicate", source);
    string plugins = Path.Combine(duplicate, "BepInEx", "plugins");
    Directory.CreateDirectory(Path.Combine(plugins, "extra"));
    File.Copy(source, Path.Combine(plugins, atbName));
    File.Copy(source, Path.Combine(plugins, "extra", atbName));
    before = Snapshot(duplicate);
    Reject(() => GameInstallation.ActivateAtb(duplicate));
    Check(Same(before, Snapshot(duplicate)), "Duplicate refusal changed files.");
    Pass("Duplicate active ATB copies are refused without writes");
    if (other is not null && Hash(other) != Hash(source))
    {
        string mismatch = CreateGame("different-build", other);
        before = Snapshot(mismatch);
        Reject(() => GameInstallation.ActivateAtb(mismatch));
        Check(Same(before, Snapshot(mismatch)), "Different-build refusal changed files.");
        Pass("A different ATB build is refused without writes");
    }
    Console.WriteLine(passed + " ATB source-build checks passed.");
}
finally
{
    string resolved = Path.GetFullPath(fixture);
    if (Path.GetDirectoryName(resolved)?.TrimEnd(Path.DirectorySeparatorChar) != parent.TrimEnd(Path.DirectorySeparatorChar)
        || !Path.GetFileName(resolved).StartsWith("stars-atb-build-tests-", StringComparison.Ordinal)
        || (File.GetAttributes(resolved) & FileAttributes.ReparsePoint) != 0
        || File.ReadAllText(Path.Combine(resolved, ".fixture")) != "Disposable ATB source-build test only")
        throw new InvalidOperationException("Refusing cleanup of an unverified fixture.");
    Directory.Delete(resolved, true);
}
