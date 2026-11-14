using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Linq;

// Read-only integration contract check. Never load or execute the game assembly.
if (args.Length is < 2 or > 3 || (args.Length == 3 && args[2] != "--native-only" && args[2] != "--unified"))
    throw new ArgumentException("Usage: Suikoden2.NativeContracts <GSD2 interop.dll> <built plugin.dll> [--native-only|--unified]");
bool nativeOnly = args.Length == 3 && args[2] == "--native-only";
bool unified = args.Length == 3 && args[2] == "--unified";
using var input = File.OpenRead(args[0]); using var pe = new PEReader(input);
var metadata = pe.GetMetadataReader(); var provider = new NativeTypeNames();
var contracts = new (string Type, string Name, string Returns, string Args)[] {
    ("GRChapterManager", "Update", "Void", ""),
    ("TitleChapter", "TitleMain", "Int32", ""),
    ("TitleChapter+_MapSetup_d__43", "MoveNext", "Boolean", ""),
    ("BootProgram", "BoNewGameSet", "Void", ""),
    ("BootProgram+_gamework_dataset_d__95", "MoveNext", "Boolean", ""),
    ("GSD2SaveData", "SaveFile", "Boolean", "String,Il2CppSystem.Action`2<Boolean,GSD2SaveData+TmpSave>,Boolean"),
    ("GSD2SaveData", "LoadDataCore", "Void", "String,Boolean"),
    ("UISaveLoad2", "ConfirmDecide", "Int32", ""),
    ("PlayerDataLoader", "GetData", "P_GRAPH_DATA", "Int32,Boolean"),
    ("PlayerDataLoader", "LoadPlayerData", "Il2CppSystem.Collections.IEnumerator", "Int32,Boolean,Il2CppSystem.Action`1<P_GRAPH_DATA>"),
    ("PlayerDataLoader", "get_IsLoadEnd", "Boolean", ""),
    ("P_GRAPH_DATA", "DesroySObject", "Void", ""), ("SHU_GRAPH_DATA", "DesroySObject", "Void", ""),
    ("ImageLoader", "LoadImage", "UnityEngine.Sprite", "Int32"),
    ("ImageLoader", "GetCacheSprite", "UnityEngine.Sprite", "Int32"),
    ("ImageLoader", "GetImagePath", "String", "Int32"),
    ("ImageLoader", "LoadAsyncImage", "Il2CppSystem.Collections.IEnumerator", "Int32,Il2CppSystem.Action`1<UnityEngine.Sprite>"),
    ("WindowManager", "GetPartyFaceOffset", "UnityEngine.Vector2", "UInt16"),
    ("Tc_face", "tc_InitFace", "Int32", "Byte,Byte"),
    ("BattleManager", "check_unite", "Byte", "Int32"),
    ("BattleManager", "command_unite", "Int32", ""),
    ("BattleManager", "UniteSelectBeforeFunc", "Int32", ""),
    ("BattleManager", "command_unite01", "Int32", ""),
    ("BattleManager", "command_unite99", "Int32", ""),
    ("BattleManager", "make_unite_window", "Void", "Int32"),
    ("BattleManager", "make_unite_player_window", "Int32", ""),
    ("BattleManager", "make_command_data", "Int32", "Int32")
};
var sharedContracts = new (string Type, string Name, string Returns, string Args)[] {
    ("ReleaseCheck", "Unload", "Void", "UnityEngine.Object,Boolean"),
    ("ResourceLoader", "UnloadAddressSingle", "Void", "UnityEngine.Object,Boolean"),
    ("SystemSave", "Save", "Boolean", "String,String,SystemSave+DataSize,Il2CppSystem.Action`1<Boolean>,Boolean")
};
var campStaticContracts = new (string Type, string Name, string Returns, string Args)[] {
    ("MACHICON", "MachiInit", "MACHICON", "SByte")
};
var campInstanceContracts = new (string Type, string Name, string Returns, string Args)[] {
    ("MACHICON", "MachiMain", "Int32", ""),
    ("SYS_WORK", "get_game_mode", "Int32", ""),
    ("SYS_WORK", "get_game_step", "Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray`1<Int32>", ""),
    ("SYS_WORK", "get_mcon", "MACHICON", ""),
    ("MACHICON", "get_ano", "Int32", ""), ("MACHICON", "get_vno", "Int32", ""),
    ("MACHICON", "get_mno", "Int32", ""), ("MACHICON", "get_memtyp", "Int32", ""),
    ("MACHICON", "get_px", "Int32", ""), ("MACHICON", "get_py", "Int32", ""),
    ("MACHICON", "get_econ", "EVENTCON", ""),
    ("EVENTCON", "get_px", "Int32", ""), ("EVENTCON", "get_py", "Int32", ""),
    ("EVENTCON", "get_mcon", "MACHICON", ""),
    // Native fields become CLR properties in the generated interop assembly.
    // Both accessors establish the types used by the temporary save adjustment.
    ("GSD2SaveData+TmpSave", "get_version", "Int32", ""),
    ("GSD2SaveData+TmpSave", "set_version", "Void", "Int32"),
    ("GSD2SaveData+TmpSave", "get_game_data", "GAME_DATA", ""),
    ("GSD2SaveData+TmpSave", "set_game_data", "Void", "GAME_DATA"),
    ("GSD2SaveData+TmpSave", "get_px", "Int32", ""), ("GSD2SaveData+TmpSave", "set_px", "Void", "Int32"),
    ("GSD2SaveData+TmpSave", "get_py", "Int32", ""), ("GSD2SaveData+TmpSave", "set_py", "Void", "Int32"),
    ("GAME_DATA", "get_area_no", "Int32", ""), ("GAME_DATA", "get_town_no", "Int32", ""),
    ("GAME_DATA", "get_map_no", "Int32", ""), ("GAME_DATA", "get_mem_type", "Byte", ""),
    ("GAME_DATA", "get_area_no2", "Int32", ""), ("GAME_DATA", "get_town_no2", "Int32", "")
};
var jsonContracts = new (string Type, string Name, string Returns, string Args)[] {
    ("LitJson.JsonMapper", "ToJson", "String", "Il2CppSystem.Object")
};
var npcInstanceContracts = new (string Type, string Name, string Returns, string Args)[] {
    ("EVENTCON", "EventHitoAnimeCreate", "Void", ""),
    ("EVENTCON", "get_okaz", "Int32", ""),
    ("EVENTCON", "get_ehuman", "Il2CppSystem.Collections.Generic.List`1<EVENT_HUMAN>", ""),
    ("EVENT_HUMAN", "get_otyp", "Byte", ""),
    ("EVENT_HUMAN", "get_ano", "Int32", ""),
    ("EVENT_HUMAN", "get_opd", "OPD_DATA", ""),
    ("ANIME", "get_data", "OPD_DATA", "")
};
var npcStaticContracts = new (string Type, string Name, string Returns, string Args)[] {
    ("ANIME", "AnimeCreate", "ANIME", "OPD_DATA,Int32,Int32,Boolean,String,Boolean")
};
var opdContracts = new (string Type, string Name, string Returns, string Args)[] {
    ("OPD_DATA", "get_anime_kazu", "Int32", ""),
    ("OPD_DATA", "get_pattern_kazu", "Int32", ""),
    ("OPD_DATA", "get_anime_table", "Il2CppSystem.Collections.Generic.List`1<OPD_DATA+AnimeTable>", ""),
    ("OPD_DATA", "get_pattern_table", "Il2CppSystem.Collections.Generic.List`1<OPD_DATA+PatternTable>", ""),
    ("OPD_DATA", "get_atlas", "UnityEngine.U2D.SpriteAtlas", ""),
    ("OPD_DATA", "get_sprites", "Il2CppSystem.Collections.Generic.Dictionary`2<String,UnityEngine.Sprite>", ""),
    ("OPD_DATA", "get_normal_atlas", "UnityEngine.U2D.SpriteAtlas", ""),
    ("OPD_DATA", "get_normal_sprites", "Il2CppSystem.Collections.Generic.Dictionary`2<String,UnityEngine.Sprite>", ""),
    ("OPD_DATA+AnimeTable", "get_timer_kazu", "Byte", ""),
    ("OPD_DATA+AnimeTable", "get_timer_table", "Il2CppSystem.Collections.Generic.List`1<OPD_DATA+AnimeTable+Timer>", ""),
    ("OPD_DATA+AnimeTable+Timer", "get_pattern_no", "Int32", ""),
    ("OPD_DATA+AnimeTable+Timer", "get_timer", "Byte", ""),
    ("OPD_DATA+PatternTable", "get_obj_table", "Il2CppSystem.Collections.Generic.List`1<OPD_DATA+PatternTable+Obj>", ""),
    ("OPD_DATA+PatternTable+Obj", "get_sprite_name", "String", "")
};
var unityNameContracts = new (string Type, string Name, string Returns, string Args)[] {
    ("UnityEngine.Object", "get_name", "String", "")
};
var dialogueStaticContracts = new (string Type, string Name, string Returns, string Args)[] {
    ("OldSrcBase", "get_game_work", "GAME_WORK", ""),
    ("G2_SYS", "G2_name_get", "String", "Int32"),
    ("G2_SYS", "G2_name_get_org", "Int32", "Int32")
};
var dialogueInstanceContracts = new (string Type, string Name, string Returns, string Args)[] {
    ("EVENTCON+___WindowCreate_d__413", "MoveNext", "Boolean", ""),
    ("WindowManager", "ConvertTagToStringFromLogData", "String", "String"),
    ("GAME_WORK", "get_chara_data", "CHARA_DATA", ""),
    ("GAME_WORK", "get_c_teg_dat", "C_TEG_DAT", ""),
    ("C_TEG_DAT", "get_name", "Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray`1<Int32>", "")
};
var dialogueSharedContracts = new (string Type, string Name, string Returns, string Args)[] {
    ("TextMasterData", "GetSystemText", "String", "String,Int32"),
    ("TextMasterData", "get_CurrentLanguage", "TextMasterData+Lang", "")
};
Verify(metadata, contracts);
Verify(metadata, campStaticContracts, isStatic: true);
Verify(metadata, campInstanceContracts, isStatic: false);
Verify(metadata, npcInstanceContracts, isStatic: false);
Verify(metadata, npcStaticContracts, isStatic: true);
Verify(metadata, dialogueStaticContracts, isStatic: true);
Verify(metadata, dialogueInstanceContracts, isStatic: false);
using var sharedInput = File.OpenRead(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[0]))!, "GSDShare.dll"));
using var sharedPe = new PEReader(sharedInput);
Verify(sharedPe.GetMetadataReader(), sharedContracts);
Verify(sharedPe.GetMetadataReader(), dialogueSharedContracts, isStatic: true);
Verify(sharedPe.GetMetadataReader(), opdContracts, isStatic: false);
var adapterShared = NpcAdapterContracts.SharedInstance.Except(opdContracts).ToArray();
Verify(sharedPe.GetMetadataReader(), adapterShared, allowOverloads: true, isStatic: false);
VerifyBase(sharedPe.GetMetadataReader(), "OPD_DATA", "SOBase");
VerifyBase(sharedPe.GetMetadataReader(), "SOBase", "UnityEngine.ScriptableObject");
using var unityInput = File.OpenRead(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[0]))!, "UnityEngine.CoreModule.dll"));
using var unityPe = new PEReader(unityInput);
VerifyBase(unityPe.GetMetadataReader(), "UnityEngine.ScriptableObject", "UnityEngine.Object");
Verify(unityPe.GetMetadataReader(), unityNameContracts, isStatic: false);
Verify(unityPe.GetMetadataReader(), NpcAdapterContracts.UnityInstance, allowOverloads: true, isStatic: false);
Verify(unityPe.GetMetadataReader(), NpcAdapterContracts.UnityStatic, allowOverloads: true, isStatic: true, genericArity: 0);
Verify(unityPe.GetMetadataReader(), NpcAdapterContracts.UnityGeneric, allowOverloads: true, isStatic: true, genericArity: 1);
using var jsonInput = File.OpenRead(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[0]))!, "LitJson.dll"));
using var jsonPe = new PEReader(jsonInput);
// The Harmony declaration supplies the exact parameter types. Other valid
// ToJson overloads coexist; this precise static overload must remain unique.
Verify(jsonPe.GetMetadataReader(), jsonContracts, allowOverloads: true, isStatic: true);

void Verify(MetadataReader metadata, (string Type, string Name, string Returns, string Args)[] expected,
    bool allowOverloads = false, bool? isStatic = null, int? genericArity = null)
{
 foreach (var contract in expected)
 {
    int exact = 0, named = 0;
    foreach (var handle in metadata.TypeDefinitions)
    {
        if (provider.GetTypeFromDefinition(metadata, handle, 0) != contract.Type) continue;
        foreach (var methodHandle in metadata.GetTypeDefinition(handle).GetMethods())
        {
            var method = metadata.GetMethodDefinition(methodHandle);
            if (metadata.GetString(method.Name) != contract.Name) continue;
            named++;
            var signature = method.DecodeSignature(provider, (object?)null);
            if (signature.ReturnType == contract.Returns && string.Join(",", signature.ParameterTypes) == contract.Args)
            {
                if (isStatic.HasValue && method.Attributes.HasFlag(System.Reflection.MethodAttributes.Static) != isStatic.Value)
                    throw new InvalidDataException("Native integration target changed static/instance kind: " + contract.Type + "." + contract.Name);
                if (genericArity.HasValue && signature.GenericParameterCount != genericArity.Value)
                    throw new InvalidDataException("Native integration target changed generic arity: " + contract.Type + "." + contract.Name);
                exact++;
            }
        }
    }
    if (exact != 1 || (!allowOverloads && named != 1))
        throw new InvalidDataException("Native integration target changed or is ambiguous: " + contract.Type + "." + contract.Name);
 }
}
using var pluginInput = File.OpenRead(args[1]); using var pluginPe = new PEReader(pluginInput);
var plugin = pluginPe.GetMetadataReader();
const string dialoguePrefix = "SuikodenRandomizer.Unified.DialogueS2";
bool hasDialogueS2 = unified && plugin.TypeDefinitions.Any(h => provider.GetTypeFromDefinition(plugin, h, 0) == dialoguePrefix);
string[] nameSuppressionTypes = [dialoguePrefix + "NamePatch", dialoguePrefix + "NameIdPatch"];
var references = plugin.AssemblyReferences.Select(handle => plugin.GetString(plugin.GetAssemblyReference(handle).Name)).ToArray();
if (!references.Contains("SuikodenRandomizer.Suikoden2.Core")
    || (!unified && references.Contains("SuikodenRandomizer.Core"))
    || (unified && (!references.Contains("SuikodenRandomizer.Core") || !references.Contains("SuikodenRandomizer.Shared"))))
    throw new InvalidDataException("Expected isolated game Core identities and, for the combined host, the shared owner/UI library.");
foreach (var handle in plugin.CustomAttributes)
{
    var attribute = plugin.GetCustomAttribute(handle);
    // Attribute string values use their own UTF8 blob encoding. The S2 name
    // table is already projected at canonical indices; a name-argument Harmony
    // patch would apply a shuffle twice even though it compiles successfully.
    string blob = Encoding.UTF8.GetString(plugin.GetBlobBytes(attribute.Value));
    if (blob.Contains("G2_name_get", StringComparison.Ordinal))
    {
        string owner = attribute.Parent.Kind == HandleKind.TypeDefinition
            ? provider.GetTypeFromDefinition(plugin, (TypeDefinitionHandle)attribute.Parent, 0) : "";
        if (!hasDialogueS2 || !nameSuppressionTypes.Contains(owner, StringComparer.Ordinal))
            throw new InvalidDataException("Unexpected S2 name hook would risk double-projecting canonical name tables.");
    }
}
if (!nativeOnly)
{
    const string prefix = "SuikodenRandomizer.Suikoden2Plugin.";
    Verify(plugin, new (string, string, string, string)[] {
        (prefix + "Gsd2CampNativeGuard", "ValidateFresh", "Void", "GAME_WORK"),
        (prefix + "Gsd2CampNativeGuard", "ValidateInitialMap", "Void", "MACHICON"),
        (prefix + "Gsd2CampNativeGuard", "TryVerifyPlayer", "Boolean", "MACHICON"),
        (prefix + "Gsd2CampMapObservationPatch", "Postfix", "Void", "MACHICON"),
        (prefix + "Gsd2CampPlayerObservationPatch", "Prefix", "Boolean", "MACHICON"),
        (prefix + "Gsd2InitialSnapshotPositionPatch", "Prefix", "Void", "Il2CppSystem.Object")
    }, isStatic: true);
    RequireHook(prefix + "Gsd2CampMapObservationPatch", "MACHICON", "MachiInit", "Postfix", "HarmonyLib.HarmonyPostfix");
    RequireHook(prefix + "Gsd2CampPlayerObservationPatch", "MACHICON", "MachiMain", "Prefix", "HarmonyLib.HarmonyPrefix");
    RequireHook(prefix + "Gsd2InitialSnapshotPositionPatch", "LitJson.JsonMapper", "ToJson", "Prefix", "HarmonyLib.HarmonyPrefix", "Il2CppSystem.Object");
    Verify(plugin, new (string, string, string, string)[] {
        (prefix + "Gsd2NpcSpriteContextPatch", "Prefix", "Void", "EVENTCON," + prefix + "Gsd2NpcSprites+Scope&"),
        (prefix + "Gsd2NpcSpriteContextPatch", "Finalizer", "System.Exception", "System.Exception," + prefix + "Gsd2NpcSprites+Scope"),
        (prefix + "Gsd2NpcSpriteFactoryPatch", "Prefix", "Void", "OPD_DATA&,Int32,Int32,Boolean," + prefix + "Gsd2NpcSprites+FactoryScope&"),
        (prefix + "Gsd2NpcSpriteFactoryPatch", "Postfix", "Void", "ANIME," + prefix + "Gsd2NpcSprites+FactoryScope"),
        (prefix + "Gsd2NpcSpriteFactoryPatch", "Finalizer", "System.Exception", "System.Exception," + prefix + "Gsd2NpcSprites+FactoryScope")
    }, isStatic: true);
    RequireHook(prefix + "Gsd2NpcSpriteContextPatch", "EVENTCON", "EventHitoAnimeCreate", "Prefix", "HarmonyLib.HarmonyPrefix");
    RequireHook(prefix + "Gsd2NpcSpriteContextPatch", "EVENTCON", "EventHitoAnimeCreate", "Finalizer", "HarmonyLib.HarmonyFinalizer");
    RequireHook(prefix + "Gsd2NpcSpriteFactoryPatch", "ANIME", "AnimeCreate", "Prefix", "HarmonyLib.HarmonyPrefix", "OPD_DATA");
    RequireHook(prefix + "Gsd2NpcSpriteFactoryPatch", "ANIME", "AnimeCreate", "Postfix", "HarmonyLib.HarmonyPostfix", "OPD_DATA");
    RequireHook(prefix + "Gsd2NpcSpriteFactoryPatch", "ANIME", "AnimeCreate", "Finalizer", "HarmonyLib.HarmonyFinalizer", "OPD_DATA");
    Verify(plugin, new (string, string, string, string)[] {
        (prefix + "Gsd2NpcDirectionAdapters", "Prepare", "OPD_DATA", "Int32,OPD_DATA"),
        (prefix + "Gsd2NpcDirectionAdapters", "Find", "OPD_DATA", "Int32,OPD_DATA"),
        (prefix + "Gsd2NpcDirectionAdapters", "IsVerified", "Boolean", "OPD_DATA"),
        (prefix + "Gsd2NpcDirectionAdapters", "IsSupportedDonor", "Boolean", "Int32"),
        (prefix + "Gsd2Graphics", "NpcFieldOpd", "OPD_DATA", "Int32")
    }, isStatic: true);
}
if (hasDialogueS2 && !nativeOnly)
{
    var targets = new[] {
        (dialoguePrefix + "WindowStepPatch", "EVENTCON+___WindowCreate_d__413", "MoveNext", (string?)null),
        (dialoguePrefix + "BacklogPatch", "WindowManager", "ConvertTagToStringFromLogData", "String"),
        (nameSuppressionTypes[0], "G2_SYS", "G2_name_get", "Int32"),
        (nameSuppressionTypes[1], "G2_SYS", "G2_name_get_org", "Int32")
    };
    foreach (var (type, nativeType, method, argument) in targets)
    {
        // Scope-only signatures do not admit an ID argument, __result, or
        // replacement native result. The actual-linked adapter tests verify
        // that name scopes only suppress formatting and unwind on exception.
        Verify(plugin, new (string, string, string, string)[] {
            (type, "Prefix", "Void", "System.IDisposable&"),
            (type, "Finalizer", "System.Exception", "System.Exception,System.IDisposable")
        }, isStatic: true);
        RequireHook(type, nativeType, method, "Prefix", "HarmonyLib.HarmonyPrefix", argument, exactTarget: true);
        RequireHook(type, nativeType, method, "Finalizer", "HarmonyLib.HarmonyFinalizer", argument, exactTarget: true);
        var definition = plugin.TypeDefinitions.Single(h => provider.GetTypeFromDefinition(plugin, h, 0) == type);
        if (plugin.GetTypeDefinition(definition).GetMethods().Count != 2)
            throw new InvalidDataException("Unexpected additional dialogue scope method: " + type);
    }
    Verify(plugin, new (string, string, string, string)[] {
        (dialoguePrefix, "Capture", "SuikodenRandomizer.Unified.NativeDialogueContext", ""),
        (dialoguePrefix, "get_PatchTypes", "System.Type[]", "")
    }, isStatic: true);
    Verify(plugin, new (string, string, string, string)[] {
        ("SuikodenRandomizer.Suikoden2Plugin.Gsd2Runtime", "get_CanUseDialogueNames", "Boolean", ""),
        ("SuikodenRandomizer.Suikoden2Plugin.Gsd2Runtime", "get_DialoguePublishedGame", "IntPtr", ""),
        ("SuikodenRandomizer.Suikoden2Plugin.Gsd2Runtime", "get_DialoguePublishedCharacters", "IntPtr", ""),
        ("SuikodenRandomizer.Suikoden2Plugin.Gsd2Runtime", "get_DialoguePublishedNames", "IntPtr", "")
    }, isStatic: false);
}
Console.WriteLine($"PASS: {contracts.Length + sharedContracts.Length + campStaticContracts.Length + campInstanceContracts.Length + jsonContracts.Length + npcInstanceContracts.Length + npcStaticContracts.Length + opdContracts.Length + unityNameContracts.Length + adapterShared.Length + NpcAdapterContracts.UnityInstance.Length + NpcAdapterContracts.UnityStatic.Length + NpcAdapterContracts.UnityGeneric.Length + dialogueStaticContracts.Length + dialogueInstanceContracts.Length + dialogueSharedContracts.Length} exact native signatures, camp/NPC/dialogue static/instance API kinds, 3 OPD Unity-name inheritance links, canonical name-table projection{(nativeOnly ? "; managed hook checks intentionally skipped for previous plugin" : ", 16 camp/NPC/adapter managed signatures and 8 Harmony hook declarations")}{(hasDialogueS2 && !nativeOnly ? ", 14 S2 dialogue managed signatures and 8 exact scope/suppression hook declarations" : "")}; game code was not executed.");

void VerifyBase(MetadataReader reader, string typeName, string expectedBase)
{
    var matches = reader.TypeDefinitions.Where(h => provider.GetTypeFromDefinition(reader, h, 0) == typeName).ToArray();
    if (matches.Length != 1) throw new InvalidDataException("Missing or ambiguous native type: " + typeName);
    var parent = reader.GetTypeDefinition(matches[0]).BaseType;
    string actual = parent.Kind switch {
        HandleKind.TypeDefinition => provider.GetTypeFromDefinition(reader, (TypeDefinitionHandle)parent, 0),
        HandleKind.TypeReference => provider.GetTypeFromReference(reader, (TypeReferenceHandle)parent, 0),
        _ => throw new InvalidDataException("Unexpected native base type: " + typeName)
    };
    if (actual != expectedBase) throw new InvalidDataException("Native type inheritance changed: " + typeName + " -> " + actual);
}

string AttributeType(MetadataReader reader, CustomAttribute attribute)
{
    EntityHandle owner = attribute.Constructor.Kind == HandleKind.MemberReference
        ? reader.GetMemberReference((MemberReferenceHandle)attribute.Constructor).Parent
        : reader.GetMethodDefinition((MethodDefinitionHandle)attribute.Constructor).GetDeclaringType();
    return owner.Kind switch {
        HandleKind.TypeDefinition => provider.GetTypeFromDefinition(reader, (TypeDefinitionHandle)owner, 0),
        HandleKind.TypeReference => provider.GetTypeFromReference(reader, (TypeReferenceHandle)owner, 0),
        _ => throw new InvalidDataException("Unexpected integration attribute owner.")
    };
}
void RequireHook(string typeName, string targetType, string targetMethod, string callback, string callbackAttribute, string? argumentType = null,
    bool exactTarget = false)
{
    var types = plugin.TypeDefinitions.Where(h => provider.GetTypeFromDefinition(plugin, h, 0) == typeName).ToArray();
    if (types.Length != 1) throw new InvalidDataException("Missing or ambiguous camp hook type: " + typeName);
    var type = plugin.GetTypeDefinition(types[0]);
    var patches = type.GetCustomAttributes().Select(plugin.GetCustomAttribute)
        .Where(a => AttributeType(plugin, a) == "HarmonyLib.HarmonyPatch").ToArray();
    if (patches.Length != 1) throw new InvalidDataException("Camp hook requires one explicit HarmonyPatch declaration: " + typeName);
    string blob = Encoding.UTF8.GetString(plugin.GetBlobBytes(patches[0].Value));
    if (exactTarget)
    {
        var reader = plugin.GetBlobReader(patches[0].Value);
        if (reader.ReadUInt16() != 1 || reader.ReadSerializedString()?.Split(',')[0] != targetType
            || reader.ReadSerializedString() != targetMethod)
            throw new InvalidDataException("Exact dialogue scope target changed: " + typeName);
    }
    if (!blob.Contains(targetType, StringComparison.Ordinal) || !blob.Contains(targetMethod, StringComparison.Ordinal)
        || (argumentType is not null && !blob.Contains(argumentType, StringComparison.Ordinal)))
        throw new InvalidDataException("Camp hook target declaration changed: " + typeName);
    var callbacks = type.GetMethods().Select(plugin.GetMethodDefinition)
        .Where(m => plugin.GetString(m.Name) == callback).ToArray();
    if (callbacks.Length != 1 || callbacks[0].GetCustomAttributes().Select(plugin.GetCustomAttribute)
        .Count(a => AttributeType(plugin, a) == callbackAttribute) != 1)
        throw new InvalidDataException("Camp hook callback attribute changed: " + typeName + "." + callback);
}

sealed class NativeTypeNames : ISignatureTypeProvider<string, object?>
{
    public string GetTypeFromDefinition(MetadataReader r, TypeDefinitionHandle h, byte raw)
    { var t=r.GetTypeDefinition(h); var p=t.GetDeclaringType(); string n=r.GetString(t.Name), s=r.GetString(t.Namespace); return !p.IsNil ? GetTypeFromDefinition(r,p,raw)+"+"+n : s.Length>0?s+"."+n:n; }
    public string GetTypeFromReference(MetadataReader r, TypeReferenceHandle h, byte raw)
    { var t=r.GetTypeReference(h); string n=r.GetString(t.Name),s=r.GetString(t.Namespace); return t.ResolutionScope.Kind==HandleKind.TypeReference?GetTypeFromReference(r,(TypeReferenceHandle)t.ResolutionScope,raw)+"+"+n:s.Length>0?s+"."+n:n; }
    public string GetTypeFromSpecification(MetadataReader r, object? c, TypeSpecificationHandle h, byte raw)=>r.GetTypeSpecification(h).DecodeSignature(this,c);
    public string GetArrayType(string t,ArrayShape s)=>t+"["+new string(',',s.Rank-1)+"]";
    public string GetByReferenceType(string t)=>t+"&";
    public string GetFunctionPointerType(MethodSignature<string> s)=>"fnptr";
    public string GetGenericInstantiation(string t,System.Collections.Immutable.ImmutableArray<string> a)=>t+"<"+string.Join(",",a)+">";
    public string GetGenericMethodParameter(object? c,int i)=>"!!"+i;
    public string GetGenericTypeParameter(object? c,int i)=>"!"+i;
    public string GetModifiedType(string m,string u,bool required)=>u;
    public string GetPinnedType(string t)=>t+" pinned";
    public string GetPointerType(string t)=>t+"*";
    public string GetPrimitiveType(PrimitiveTypeCode c)=>c.ToString();
    public string GetSZArrayType(string t)=>t+"[]";
}
