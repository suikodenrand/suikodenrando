using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;

if (args.Length is not (2 or 4))
    throw new ArgumentException("Usage: metadata-check <unified.dll> <report.json> [<gsd1.en.json> <gsd2.en.json>]");
using var stream = File.OpenRead(args[0]);
using var pe = new PEReader(stream);
var md = pe.GetMetadataReader();
var checks = new Dictionary<string, bool>();
void Require(string name, bool passed)
{ checks.Add(name, passed); if (!passed) throw new InvalidDataException("FAILED: " + name); }
string TypeName(EntityHandle handle)
{
    if (handle.IsNil) return "";
    if (handle.Kind == HandleKind.TypeDefinition)
    {
        var type = md.GetTypeDefinition((TypeDefinitionHandle)handle);
        string name = md.GetString(type.Name), ns = md.GetString(type.Namespace);
        return !type.GetDeclaringType().IsNil ? TypeName(type.GetDeclaringType()) + "+" + name : ns.Length == 0 ? name : ns + "." + name;
    }
    if (handle.Kind == HandleKind.TypeReference)
    {
        var type = md.GetTypeReference((TypeReferenceHandle)handle);
        string name = md.GetString(type.Name), ns = md.GetString(type.Namespace);
        return type.ResolutionScope.Kind == HandleKind.TypeReference ? TypeName(type.ResolutionScope) + "+" + name : ns.Length == 0 ? name : ns + "." + name;
    }
    return "";
}
string AttributeType(CustomAttributeHandle handle)
{
    var ctor = md.GetCustomAttribute(handle).Constructor;
    return TypeName(ctor.Kind == HandleKind.MemberReference ? md.GetMemberReference((MemberReferenceHandle)ctor).Parent
        : md.GetMethodDefinition((MethodDefinitionHandle)ctor).GetDeclaringType());
}
var types = md.TypeDefinitions.ToDictionary(h => TypeName(h), h => md.GetTypeDefinition(h));
string[] Attributed(string name) => types.Where(p => p.Value.GetCustomAttributes().Any(h => AttributeType(h) == name)).Select(p => p.Key).ToArray();
const string unifiedNamespace = "SuikodenRandomizer.Unified";
const string s1Namespace = "SuikodenRandomizer.AvatarPlugin";
const string s2Namespace = "SuikodenRandomizer.Suikoden2Plugin";
const string bootstrap = unifiedNamespace + ".UnifiedPlugin";
Version assemblyVersion = md.GetAssemblyDefinition().Version;
Require("combined assembly version 0.14.6", assemblyVersion == new Version(0, 14, 6, 0));
var plugins = Attributed("BepInEx.BepInPlugin");
Require("exactly one BepInEx bootstrap", plugins.SequenceEqual(new[] { bootstrap }));
var pluginAttribute = types[bootstrap].GetCustomAttributes().Single(h => AttributeType(h) == "BepInEx.BepInPlugin");
var pluginArguments = md.GetBlobReader(md.GetCustomAttribute(pluginAttribute).Value);
Require("BepInEx bootstrap retains GUID and version 0.14.6", pluginArguments.ReadUInt16() == 1
    && pluginArguments.ReadSerializedString() == "agilen.starsofdestiny"
    && !string.IsNullOrEmpty(pluginArguments.ReadSerializedString())
    && pluginArguments.ReadSerializedString() == "0.14.6");
Require("exactly one BasePlugin subclass", types.Count(p => TypeName(p.Value.BaseType) == "BepInEx.Unity.IL2CPP.BasePlugin") == 1);
Require("exactly one Unity frame/UI component", types.Where(p => TypeName(p.Value.BaseType) == "UnityEngine.MonoBehaviour").Select(p => p.Key).SequenceEqual(new[] { "SuikodenRandomizer.Unified.UnifiedOverlay" }));
Require("standalone UI drivers excluded", !types.ContainsKey("SuikodenRandomizer.AvatarPlugin.AvatarOverlay") && !types.ContainsKey("SuikodenRandomizer.Suikoden2Plugin.Gsd2Driver"));
Require("both engine runtimes included", types.ContainsKey("SuikodenRandomizer.AvatarPlugin.AvatarRuntime") && types.ContainsKey("SuikodenRandomizer.Suikoden2Plugin.Gsd2Runtime"));
var refs = md.AssemblyReferences.Select(h => md.GetString(md.GetAssemblyReference(h).Name)).ToArray();
Require("separate core identities and shared router library", new[] { "SuikodenRandomizer.Core", "SuikodenRandomizer.Suikoden2.Core", "SuikodenRandomizer.Shared" }.All(refs.Contains));
Require("both native games use distinct interop references", refs.Contains("GSD1") && refs.Contains("GSD2"));
Require("no standalone S2 plugin dependency", !refs.Contains("SuikodenRandomizer.Suikoden2"));
Require("dialogue gate compiled into the unified assembly", types.ContainsKey("SuikodenRandomizer.Dialogue.DialogueRewriteGate"));
Require("no separate dialogue DLL dependency", !refs.Contains("SuikodenRandomizer.Dialogue", StringComparer.OrdinalIgnoreCase));
var joiningEquipmentTypes = new[] {
    s1Namespace + ".JoiningEquipmentPolicy", s1Namespace + ".JoiningEquipmentInventory",
    s1Namespace + ".JoiningEquipmentCatalogEntry", s1Namespace + ".JoiningEquipmentPlan",
    s1Namespace + ".JoiningEquipmentNative", s1Namespace + ".JoiningEquipmentBinding",
    s1Namespace + ".JoiningEquipmentBindingStore", s2Namespace + ".Gsd2EquipmentBalance",
    s2Namespace + ".Gsd2EquipmentSnapshot", s2Namespace + ".Gsd2PendingEquipment"
};
Require("both joining-inventory planners and saved-history helpers compiled into the host", joiningEquipmentTypes.All(types.ContainsKey));
bool StaticStringProperty(string owner, string name)
{
    if (!types.TryGetValue(owner, out var type)) return false;
    var matches = type.GetProperties().Select(md.GetPropertyDefinition).Where(p => md.GetString(p.Name) == name).ToArray();
    if (matches.Length != 1) return false;
    var property = matches[0];
    var signature = md.GetBlobReader(property.Signature);
    var header = signature.ReadSignatureHeader();
    var getterHandle = property.GetAccessors().Getter;
    return header.Kind == SignatureKind.Property && !header.IsInstance && signature.ReadCompressedInteger() == 0
        && signature.ReadSignatureTypeCode() == SignatureTypeCode.String && signature.RemainingBytes == 0
        && !getterHandle.IsNil && md.GetMethodDefinition(getterHandle).Attributes.HasFlag(MethodAttributes.Static)
        && md.GetString(md.GetMethodDefinition(getterHandle).Name) == "get_" + name;
}
Require("S1 facade exposes static string ConfiguredJoiningEquipment", StaticStringProperty(s1Namespace + ".AvatarPlugin", "ConfiguredJoiningEquipment"));
Require("S2 facade exposes static string JoiningEquipment", StaticStringProperty(s2Namespace + ".Gsd2Plugin", "JoiningEquipment"));

string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
var catalogSources = new[]
{
    (Name: "StarsOfDestiny.Dialogue.GSD1.en.json", Path: args.Length == 4 ? args[2] : null,
        PinnedHash: "f09ecb0938e3ba3bc579f7bef5716caad2f7a8a3b9630af5a2f779002baa5980"),
    (Name: "StarsOfDestiny.Dialogue.GSD2.en.json", Path: args.Length == 4 ? args[3] : null,
        PinnedHash: "21b5248a3d59ba0e21601bdd8773038dfc435572fbc451cb89a93e42d4d9a7df")
};
var embedded = md.ManifestResources.Select(md.GetManifestResource)
    .ToDictionary(resource => md.GetString(resource.Name), StringComparer.Ordinal);
Require("exactly two embedded English dialogue catalog resources", embedded.Count == catalogSources.Length
    && catalogSources.All(source => embedded.TryGetValue(source.Name, out var resource) && resource.Implementation.IsNil));
var resourceDirectory = pe.PEHeaders.CorHeader!.ResourcesDirectory;
var resourceSection = pe.GetSectionData(resourceDirectory.RelativeVirtualAddress);
var resourceReport = new List<object>();
foreach (var source in catalogSources)
{
    // The CLR stores the resource's byte count at its offset in the managed
    // resources directory, followed by the exact embedded file bytes.
    int offset = checked((int)embedded[source.Name].Offset);
    if (resourceDirectory.Size < sizeof(int) || offset > resourceDirectory.Size - sizeof(int))
        throw new InvalidDataException("Invalid embedded resource offset for " + source.Name);
    var reader = resourceSection.GetReader(offset, resourceDirectory.Size - offset);
    int byteCount = reader.ReadInt32();
    if (byteCount < 0 || byteCount > reader.RemainingBytes)
        throw new InvalidDataException("Invalid embedded resource length for " + source.Name);
    string hash = Sha256(reader.ReadBytes(byteCount));
    string expected = source.Path is null ? source.PinnedHash : Sha256(File.ReadAllBytes(source.Path));
    Require("embedded catalog matches verified source bytes for " + source.Name, hash == expected);
    resourceReport.Add(new { name = source.Name, bytes = byteCount, sha256 = hash, expectedSha256 = expected,
        sourcePath = source.Path is null ? null : Path.GetFullPath(source.Path) });
}

var opcodes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.FieldType == typeof(OpCode))
    .Select(f => (OpCode)f.GetValue(null)!).ToDictionary(op => unchecked((ushort)op.Value));
var calls = new List<(string Caller, string Owner, string Name)>();
var literals = new HashSet<string>(StringComparer.Ordinal);
var methodLiterals = new List<(string Caller, string Value)>();
(string Owner, string Name) MethodTarget(EntityHandle handle)
{
    if (handle.Kind == HandleKind.MethodSpecification) return MethodTarget(md.GetMethodSpecification((MethodSpecificationHandle)handle).Method);
    if (handle.Kind == HandleKind.MemberReference)
    {
        var target = md.GetMemberReference((MemberReferenceHandle)handle);
        return (TypeName(target.Parent), md.GetString(target.Name));
    }
    if (handle.Kind == HandleKind.MethodDefinition)
    {
        var target = md.GetMethodDefinition((MethodDefinitionHandle)handle);
        return (TypeName(target.GetDeclaringType()), md.GetString(target.Name));
    }
    return ("", "");
}
foreach (var type in types)
foreach (var handle in type.Value.GetMethods())
{
    var method = md.GetMethodDefinition(handle);
    if (method.RelativeVirtualAddress == 0) continue;
    var reader = pe.GetMethodBody(method.RelativeVirtualAddress).GetILReader();
    string caller = type.Key + "." + md.GetString(method.Name);
    while (reader.RemainingBytes > 0)
    {
        ushort code = reader.ReadByte();
        if (code == 0xfe) code = (ushort)(0xfe00 | reader.ReadByte());
        var op = opcodes[code];
        switch (op.OperandType)
        {
            case OperandType.InlineNone: break;
            case OperandType.ShortInlineBrTarget: case OperandType.ShortInlineI: case OperandType.ShortInlineVar: reader.Offset += 1; break;
            case OperandType.InlineVar: reader.Offset += 2; break;
            case OperandType.InlineI8: case OperandType.InlineR: reader.Offset += 8; break;
            case OperandType.InlineSwitch: int count = reader.ReadInt32(); reader.Offset += checked(count * 4); break;
            case OperandType.InlineString:
                string literal = md.GetUserString(MetadataTokens.UserStringHandle(reader.ReadInt32() & 0xffffff));
                literals.Add(literal); methodLiterals.Add((caller, literal)); break;
            case OperandType.InlineMethod:
                var target = MetadataTokens.EntityHandle(reader.ReadInt32());
                if (op == OpCodes.Call || op == OpCodes.Callvirt)
                {
                    var called = MethodTarget(target);
                    calls.Add((caller, called.Owner, called.Name));
                }
                break;
            default: reader.Offset += 4; break;
        }
    }
}
foreach (var facade in new[] {
    (Type: s1Namespace + ".AvatarPlugin", Property: "ConfiguredJoiningEquipment"),
    (Type: s2Namespace + ".Gsd2Plugin", Property: "JoiningEquipment")
})
{
    string configure = facade.Type + ".Configure";
    var configLiterals = methodLiterals.Where(value => value.Caller == configure).Select(value => value.Value).ToHashSet(StringComparer.Ordinal);
    Require("joining-inventory config key and both policies bound by " + facade.Type,
        new[] { "Shuffle", "JoiningEquipment", "MatchRole", "Donor" }.All(configLiterals.Contains)
        && calls.Any(call => call.Caller == configure && call.Owner == facade.Type && call.Name == "set_" + facade.Property)
        && calls.Any(call => call.Caller == configure && call.Owner == "BepInEx.Configuration.ConfigFile" && call.Name == "Bind"));
}
Require("S1 runtime consumes its joining-inventory facade policy", calls.Any(call => call.Caller.StartsWith(s1Namespace + ".AvatarRuntime.", StringComparison.Ordinal)
    && call.Owner == s1Namespace + ".AvatarPlugin" && call.Name == "get_ConfiguredJoiningEquipment"));
Require("S2 runtime consumes its joining-inventory facade policy", calls.Any(call => call.Caller.StartsWith(s2Namespace + ".Gsd2Runtime.", StringComparison.Ordinal)
    && call.Owner == s2Namespace + ".Gsd2Plugin" && call.Name == "get_JoiningEquipment"));
Require("S1 runtime calls the compiled inventory adapter and saved binding store", calls.Any(call => call.Caller.StartsWith(s1Namespace + ".AvatarRuntime.", StringComparison.Ordinal)
    && call.Owner == s1Namespace + ".JoiningEquipmentNative" && call.Name == "Apply")
    && calls.Any(call => call.Owner == s1Namespace + ".JoiningEquipmentBindingStore" && call.Name == "Write"));
Require("S2 recruitment transaction calls the compiled compatible-equipment planner", calls.Any(call => call.Caller == s2Namespace + ".Gsd2Runtime.ObserveJoinedEquipment"
    && call.Owner == s2Namespace + ".Gsd2EquipmentBalance" && call.Name == "Plan"));
Require("no broad PatchAll call in combined assembly", !calls.Any(c => c.Name == "PatchAll"));
Require("one explicit patch-class dispatcher", calls.Where(c => c.Name == "CreateClassProcessor")
    .SequenceEqual(new[] { (bootstrap + ".ApplyPatches", "HarmonyLib.Harmony", "CreateClassProcessor") }));
Require("engine and dialogue installers share the one dispatcher", calls.Where(c => c.Owner == bootstrap && c.Name == "ApplyPatches")
    .Select(c => c.Caller).OrderBy(name => name, StringComparer.Ordinal)
    .SequenceEqual(new[] { unifiedNamespace + ".DialogueNative.Install", bootstrap + ".LoadEngine" }));
Require("one BepInEx component registration in the unified bootstrap",
    calls.Where(c => c.Owner == "BepInEx.Unity.IL2CPP.BasePlugin" && c.Name == "AddComponent")
        .Select(c => c.Caller).SequenceEqual(new[] { bootstrap + ".Load" }));
Require("one F6 consumer", calls.Where(c => c.Name == "get_f6Key").Select(c => c.Caller).SequenceEqual(new[] { "SuikodenRandomizer.Unified.UnifiedOverlay.Update" }));
Require("one OnGUI method", types.SelectMany(p => p.Value.GetMethods().Select(h => p.Key + "." + md.GetString(md.GetMethodDefinition(h).Name)))
    .Where(name => name.EndsWith(".OnGUI", StringComparison.Ordinal)).SequenceEqual(new[] { "SuikodenRandomizer.Unified.UnifiedOverlay.OnGUI" }));
Require("both explicit patch namespaces present", literals.Contains(s1Namespace) && literals.Contains(s2Namespace));
var patches = Attributed("HarmonyLib.HarmonyPatch");
var dialogueGroups = new Dictionary<string, string[]>
{
    ["GSD1"] = new[] { "DialogueS1EventMessagePatch", "DialogueS1EventMessage2Patch", "DialogueS1BacklogPatch",
        "DialogueS1NamePatch", "DialogueS1NameTextIdPatch", "DialogueS1LoadBarrierPatch", "DialogueS1SaveBarrierPatch" },
    ["GSD2"] = new[] { "DialogueS2WindowStepPatch", "DialogueS2BacklogPatch", "DialogueS2NamePatch", "DialogueS2NameIdPatch" },
    ["Shared"] = new[] { "DialogueRawTextPatch" }
};
var expectedDialoguePatches = dialogueGroups.Values.SelectMany(names => names)
    .Select(name => unifiedNamespace + "." + name).ToHashSet(StringComparer.Ordinal);
string PatchNamespace(string name) => md.GetString(types[name].Namespace);
Require("exactly twelve allowed declarative dialogue patch classes", expectedDialoguePatches.SetEquals(patches.Where(name => PatchNamespace(name) == unifiedNamespace)));
Require("declarative patches stay in exact engine namespaces or the dialogue allowlist", patches.All(name =>
    PatchNamespace(name) == s1Namespace || PatchNamespace(name) == s2Namespace || expectedDialoguePatches.Contains(name)));
Require("both engine patch sets included", patches.Any(name => PatchNamespace(name) == s1Namespace) && patches.Any(name => PatchNamespace(name) == s2Namespace));
Require("ownership checks compiled into both engines", calls.Count(c => c.Name == "get_OwnsExecution") >= 2);
Require("suspension calls compiled into host", calls.Count(c => c.Caller == "SuikodenRandomizer.Unified.UnifiedPlugin.Observe" && c.Name == "SuspendExecution") == 2);
Require("ownership does not depend on Unity's active battle scene", !calls.Any(c => c.Caller == "SuikodenRandomizer.Unified.UnifiedPlugin.Observe" && c.Name == "GetActiveScene"));
Require("host uses native game selection and both loaded roots", calls.Count(c => c.Caller == "SuikodenRandomizer.Unified.UnifiedPlugin.Observe" && c.Name == "get_mode") == 1
    && calls.Count(c => c.Caller == "SuikodenRandomizer.Unified.UnifiedPlugin.Observe" && c.Name == "GetSceneByName") == 2
    && calls.Count(c => c.Caller == "SuikodenRandomizer.Unified.UnifiedPlugin.Observe" && c.Name == "IsValid") == 2
    && calls.Count(c => c.Caller == "SuikodenRandomizer.Unified.UnifiedPlugin.Observe" && c.Name == "get_isLoaded") == 2
    && calls.Any(c => c.Caller == "SuikodenRandomizer.Unified.UnifiedPlugin.Observe" && c.Name == "ObserveGameContext"));

var report = new { schemaVersion = 2, status = "passed", checks, checksPassed = checks.Count,
    assemblySha256 = Sha256(File.ReadAllBytes(args[0])), assemblyVersion = assemblyVersion.ToString(),
    pluginClasses = plugins, patchClassCounts = patches.GroupBy(name => PatchNamespace(name) == s1Namespace ? "GSD1"
        : PatchNamespace(name) == s2Namespace ? "GSD2" : "Dialogue").ToDictionary(g => g.Key, g => g.Count()),
    dialoguePatchClassCounts = dialogueGroups.ToDictionary(group => group.Key, group => group.Value.Length),
    embeddedDialogueCatalogs = resourceReport,
    targetCodeExecuted = false, scope = "Read-only PE metadata/CIL audit; no game/native assemblies loaded as executable code." };
File.WriteAllText(args[1], JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) + "\n");
Console.WriteLine(checks.Count + "/" + checks.Count + " combined-host PE checks passed; target code was not executed.");
