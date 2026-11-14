using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using StarsOfDestiny.Launcher.Core;

bool invalidMetadata = args.Length == 2 && args[0] == "--expect-invalid-metadata";
if (args.Length is < 1 or > 2)
    throw new ArgumentException("Usage: atb-build-tests <matching-atb.dll> [different-build-atb.dll]");
string source = Path.GetFullPath(args[invalidMetadata ? 1 : 0]);
string? other = args.Length == 2 && !invalidMetadata ? Path.GetFullPath(args[1]) : null;
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
string CreateGame(string name, string atb)
{
    string root = Path.Combine(fixture, name);
    Directory.CreateDirectory(Path.Combine(root, "BepInEx", "plugins"));
    Directory.CreateDirectory(Path.Combine(root, "BepInEx", "config"));
    File.WriteAllText(Path.Combine(root, GameInstallation.GameExeName), "INVALID TEST GAME EXE - NEVER EXECUTE");
    File.Copy(atb, Path.Combine(root, "BepInEx", atbName));
    File.WriteAllText(Path.Combine(root, "BepInEx", "config", "agilen.suikoden1.battle-preview.cfg"), "[Preview]\nEnabled = false\n");
    return root;
}
void Reject(Action run)
{
    try { run(); }
    catch (Exception e) when (e is InvalidDataException or InvalidOperationException) { return; }
    throw new InvalidOperationException("Expected activation rejection.");
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
