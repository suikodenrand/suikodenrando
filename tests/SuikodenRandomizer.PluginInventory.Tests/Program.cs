using System.Security.Cryptography;
using System.Text.Json;
using SuikodenRandomizer.Unified;

if (args.Length != 8) throw new ArgumentException("Usage: <unified.dll> <legacyS1.dll> <legacyS2.dll> <Fix.dll> <native.dll> <workspace-fixture-folder> <receipt.json> <syntheticEmbeddedFix.dll>");
var inputs = args.Take(5).Append(args[7]).Select(Path.GetFullPath).ToArray();
string fixtureBase = Path.GetFullPath(args[5]);
string relative = Path.GetRelativePath(Directory.GetCurrentDirectory(), fixtureBase);
if (Path.IsPathFullyQualified(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
    throw new InvalidOperationException("Fixture folder must stay in the workspace.");
string run = Path.Combine(fixtureBase, "inventory-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(run);
var coreFiles = new[] { "SuikodenRandomizer.Core.dll", "SuikodenRandomizer.Suikoden2.Core.dll", "SuikodenRandomizer.Shared.dll" }
    .Select(name => Path.Combine(Path.GetDirectoryName(inputs[0])!, name)).ToArray();
string Sha(string file) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))).ToLowerInvariant();
var inputHashes = inputs.Concat(coreFiles).ToDictionary(p => p, Sha);
var results = new List<string>();
var loadedTargets = new List<string>();
AppDomain.CurrentDomain.AssemblyLoad += (_, evt) =>
{
    string? name = evt.LoadedAssembly.GetName().Name;
    if (new[] { "SuikodenRandomizer", "SuikodenRandomizer.Suikoden2", "SuikodenRandomizer.Core",
        "SuikodenRandomizer.Suikoden2.Core", "SuikodenRandomizer.Shared", "Suikoden_Fix", "GSD1", "GSD2", "KnownEmbeddedFix.SyntheticFixture" }.Contains(name))
        loadedTargets.Add(name!);
};
void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
void Test(string name, Action test) { test(); results.Add(name); Console.WriteLine("PASS " + name); }
string Copy(string source, string folder, string name)
{
    string path = Path.Combine(folder, name); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.Copy(source, path); return Path.GetFullPath(path);
}
(string Root, string Own) Tree(string name)
{
    string folder = Path.Combine(run, name); Directory.CreateDirectory(folder);
    return (folder, Copy(inputs[0], folder, "SuikodenRandomizer.dll"));
}
void Exact(string root, string own, params string[] expected)
{
    var actual = PluginInventoryAudit.FindConflicts(root, own);
    var sorted = expected.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
    Check(actual.SequenceEqual(sorted, StringComparer.OrdinalIgnoreCase), "Conflict paths differ: " + string.Join(",", actual));
}

Test("Own combined plugin plus three libraries and unrelated Fix is clean", () =>
{
    var t = Tree("clean"); foreach (string file in coreFiles) Copy(file, t.Root, Path.GetFileName(file));
    Copy(inputs[3], t.Root, "unrelated-fix.dll"); Exact(t.Root, t.Own);
});
Test("Renamed nested legacy S1 is identified by its actual GUID", () =>
{
    var t = Tree("s1"); string duplicate = Copy(inputs[1], t.Root, "deep/renamed-unrelated.dll"); Exact(t.Root, t.Own, duplicate);
});
Test("Known embedded-Fix type conflicts without a plugin GUID (synthetic compiled fixture)", () =>
{
    var t = Tree("embedded-fix"); string duplicate = Copy(inputs[5], t.Root, "nested/innocent-name.dll"); Exact(t.Root, t.Own, duplicate);
});
Test("Renamed nested legacy S2 is identified by its actual GUID", () =>
{
    var t = Tree("s2"); string duplicate = Copy(inputs[2], t.Root, "deep/two/another-name.dll"); Exact(t.Root, t.Own, duplicate);
});
Test("Both legacy engines are returned once in deterministic path order", () =>
{
    var t = Tree("both"); string s2 = Copy(inputs[2], t.Root, "z/old.dll"), s1 = Copy(inputs[1], t.Root, "a/old.dll");
    Exact(t.Root, t.Own, s1, s2);
});
Test("Renamed second combined plugin is rejected while exact own file is skipped", () =>
{
    var t = Tree("combined-duplicate"); string duplicate = Copy(inputs[0], t.Root, "nested/renamed.dll"); Exact(t.Root, t.Own, duplicate);
});
Test("Same basename as own file is not skipped in another directory", () =>
{
    var t = Tree("same-basename"); string duplicate = Copy(inputs[0], t.Root, "nested/SuikodenRandomizer.dll"); Exact(t.Root, t.Own, duplicate);
});
Test("Own path normalization does not hide unrelated paths", () =>
{
    var t = Tree("canonical-own"); Exact(t.Root, Path.Combine(t.Root, "unused", "..", "SuikodenRandomizer.dll"));
});
Test("Native DLL and malformed non-PE metadata are ignored without code execution", () =>
{
    var t = Tree("bad-pe"); Copy(inputs[4], t.Root, "native.dll");
    File.WriteAllBytes(Path.Combine(t.Root, "bad.dll"), new byte[] { 0x4d, 0x5a, 0, 0, 0, 0 });
    File.WriteAllText(Path.Combine(t.Root, "text.dll"), "agilen.starsofdestiny agilen.suikoden.recruitment_randomizer");
    Exact(t.Root, t.Own);
});
Test("Uppercase extension and hidden legacy DLL still conflict", () =>
{
    var t = Tree("hidden"); string duplicate = Copy(inputs[1], t.Root, "hidden.DLL");
    File.SetAttributes(duplicate, File.GetAttributes(duplicate) | FileAttributes.Hidden); Exact(t.Root, t.Own, duplicate);
});
Test("Core libraries renamed like plugins remain unrelated", () =>
{
    var t = Tree("renamed-libraries"); Copy(coreFiles[0], t.Root, "AvatarPlugin.dll"); Copy(coreFiles[1], t.Root, "Gsd2Plugin.dll");
    Exact(t.Root, t.Own);
});
Test("Absent plugin directory reports no conflicting files", () =>
{
    Check(PluginInventoryAudit.FindConflicts(Path.Combine(run, "missing"), inputs[0]).Length == 0, "Missing folder not empty");
});
Test("Unreadable candidate fails closed instead of reporting a clean inventory", () =>
{
    var t = Tree("locked"); string candidate = Copy(inputs[1], t.Root, "locked.dll");
    using var locked = new FileStream(candidate, FileMode.Open, FileAccess.Read, FileShare.None);
    bool rejected = false; try { PluginInventoryAudit.FindConflicts(t.Root, t.Own); } catch (IOException) { rejected = true; }
    Check(rejected, "Unreadable candidate was silently accepted");
});
Test("Actual candidate assemblies were never loaded", () => Check(loadedTargets.Count == 0, "Loaded target assembly: " + string.Join(",", loadedTargets)));
Test("All original input artifacts are byte-for-byte unchanged", () =>
{
    foreach (var input in inputHashes) Check(Sha(input.Key) == input.Value, "Input modified: " + input.Key);
});
var receipt = new
{
    schemaVersion = 1, passed = results.Count, failed = 0, results,
    scope = "Actual linked production metadata helper; actual compiled combined/legacy/Fix/native PE fixtures copied into workspace. No target assembly loading, detours or installed writes.",
    fixturesRetained = run, inputs = inputHashes.Select(p => new { path = p.Key, sha256 = p.Value }),
    productionHelper = new { path = "src/SuikodenRandomizer.Unified/PluginInventoryAudit.cs" },
    noTargetAssemblyLoads = loadedTargets.Count == 0
};
File.WriteAllText(args[6], JsonSerializer.Serialize(receipt, new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
Console.WriteLine(results.Count + "/" + results.Count + " actual PE plugin inventory checks passed.");
