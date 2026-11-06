using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

/// <summary>
/// Checks the avatar pilot's GSD1 wrapper contracts without loading game code.
/// This verifies metadata, not native detours, resource ownership or gameplay.
/// </summary>
internal static class Program
{
    private const string Arrays = "Il2CppInterop.Runtime.InteropTypes.Arrays.";
    private const string Bytes = Arrays + "Il2CppStructArray`1<Byte>";
    private const string Shorts = Arrays + "Il2CppStructArray`1<Int16>";
    private const string SBytes = Arrays + "Il2CppStructArray`1<SByte>";
    private const string Save = "SaveDataManager+SaveData";

    private sealed record Contract(string Type, string Member, bool Static, string Signature,
        bool UniqueName = false, int NamedParameter = -1, string? ParameterName = null);

    private static readonly List<Contract> Expected = new List<Contract>();

    private static void Main(string[] args)
    {
        if (args.Length != 1 || !File.Exists(args[0]))
            throw new ArgumentException("Supply the installed GSD1.dll path.");
        BuildContracts();
        using var stream = File.OpenRead(args[0]);
        using var pe = new PEReader(stream);
        if (!pe.HasMetadata) throw new InvalidDataException("Not a managed interop assembly.");
        var reader = pe.GetMetadataReader();
        if (reader.GetString(reader.GetAssemblyDefinition().Name) != "GSD1")
            throw new InvalidDataException("Wrong assembly identity.");
        var provider = new Types();
        var definitions = reader.TypeDefinitions.ToDictionary(h => Types.DefinitionName(reader, h), h => h,
            StringComparer.Ordinal);
        int typeCount = CheckSourceTypes(reader, definitions);

        foreach (var contract in Expected)
        {
            var type = RequirePublicType(reader, definitions, contract.Type);
            var overloads = type.GetMethods().Select(reader.GetMethodDefinition)
                .Where(m => reader.GetString(m.Name) == contract.Member).ToArray();
            if (contract.UniqueName && overloads.Length != 1)
                throw new InvalidDataException(contract.Type + "." + contract.Member
                    + ": an untyped Harmony target requires exactly one declared method; found " + overloads.Length);
            string expectedParameters = contract.Signature[contract.Signature.IndexOf('(')..];
            var matches = overloads.Where(m => ParameterSignature(m, provider) == expectedParameters).ToArray();
            // Constructors are selected by their exact parameter list. The normal
            // generated .ctor(IntPtr) does not make the required .ctor() ambiguous.
            if (matches.Length != 1)
                throw new MissingMethodException(contract.Type + "." + contract.Member + expectedParameters
                    + ": expected one exact parameter-list match; found " + matches.Length
                    + "; available: " + string.Join("; ", overloads.Select(m => Signature(m, provider))));
            var method = matches[0];
            bool actualStatic = (method.Attributes & MethodAttributes.Static) != 0;
            string actual = Signature(method, provider);
            if ((method.Attributes & MethodAttributes.MemberAccessMask) != MethodAttributes.Public
                || actualStatic != contract.Static || actual != contract.Signature)
                throw new InvalidDataException(contract.Type + "." + contract.Member + ": expected public "
                    + (contract.Static ? "static " : "instance ") + contract.Signature
                    + "; found " + method.Attributes + " " + actual);
            if (contract.NamedParameter >= 0)
            {
                var parameters = method.GetParameters().Select(reader.GetParameter)
                    .Where(p => p.SequenceNumber == contract.NamedParameter + 1).ToArray();
                if (parameters.Length != 1 || reader.GetString(parameters[0].Name) != contract.ParameterName)
                    throw new InvalidDataException(contract.Type + "." + contract.Member
                        + ": Harmony named parameter " + contract.ParameterName + " is absent at index " + contract.NamedParameter);
            }
            Console.WriteLine("PASS " + contract.Type + "." + contract.Member + " "
                + (contract.Static ? "static " : "instance ") + actual
                + (contract.NamedParameter >= 0 ? " [parameter " + contract.NamedParameter + "=" + contract.ParameterName + "]" : ""));
        }
        CheckNewGameState(reader, definitions, provider);
        using var sha = SHA256.Create();
        Console.WriteLine("Interop SHA256=" + Convert.ToHexString(sha.ComputeHash(File.ReadAllBytes(args[0]))).ToLowerInvariant());
        Console.WriteLine(Expected.Count + "/" + Expected.Count
            + " avatar GSD1 method/property/constructor contracts and 1/1 New Game enum contract verified; "
            + typeCount + " public source type references verified without native execution.");
        Console.WriteLine("Scope: GSD1 metadata only. External SystemSave.Save/cb, Unity Object cloning, SOBase, "
            + "Framework.Chapter.activeChapter, inherited GlobalWork.Instance and OPDSprite.data are not verified here. "
            + "Native detours and gameplay require game-start/runtime checks.");
    }

    private static void BuildContracts()
    {
        Method("TitleChapter", "TitleMain", false, "Int32(Int32)", true);
        Method("ChapterManager", "GetGameStepSeq", true, "Int32(Int32)");
        Property("ChapterManager", "GR1Instance", "ChapterManager", isStatic: true);
        Method("GameInit", "LoadPlayerData", false, "Int32(Int32)", true);
        Method("PlayerManager", "GetPlayerData", false, "PLAYER_DATA(CHARACTER_ID)", true);
        Property("PlayerManager", "player_cache", "Il2CppSystem.Collections.Generic.Dictionary`2<Int32,PLAYER_DATA>");
        Property("GlobalWork", "player_manager", "PlayerManager");
        Property("OldSrcBase", "game_work", "GAME_WORK", isStatic: true);
        Method("P_player_c", "LoadPlayer", true, "Int32(Int32)", true);
        Method("P_player_c", "AddPlayer", true, "Int32(Int32)", true);
        Method("BattleBase", "initialize_battle", true, "Int32(Int32)", true);
        Method("BattleBase", "player_initialize", true, "Void()", true);
        Method("BattleBase", "calc_exp", true, "Void()", true);
        Method("BattleBase", "battle_end_free", true, "Int32(Int32)", true);
        Property("BattleBase", "battle_work", "BATTLE_WORK", isStatic: true);
        Property("BATTLE_WORK", "p_battle_data", References("P_BATTLE_DATA"));
        Property("P_BATTLE_DATA", "player_data", "PLAYER_DATA");

        // Keep Unite candidate construction inside the same temporary native
        // participant mask. The native routines retain their own result values
        // and command state; these contracts do not assume an empty-result code.
        foreach (string name in new[] { "check_kyoryoku", "make_command_data", "make_kyoryoku_window" })
            Method("BattleBase", name, true, "Int32(Int32)", true, 0, "player_no");
        foreach (string name in new[] { "command_kyoryoku", "command_kyoryoku01", "command_kyoryoku02",
            "command_kyoryoku10" })
            Method("BattleBase", name, true, "Int32(Int32)", true, 0, "step_level");
        Property("BattleBase", "kyoryoku_data_table", References("KYORYOKU_DATA"), isStatic: true);
        Property("KYORYOKU_DATA", "player_kazu", "Byte");
        Property("KYORYOKU_DATA", "player_id", Bytes);
        Method("Event_c", "GoEventCommand", false, "Byte()", true);
        Method("Event_c", "ClrEventRenzoku", false, "Void()");

        Method("SaveDataManager", "SaveGameWork", true,
            "Boolean(String,Il2CppSystem.Action`2<Boolean," + Save + ">,Boolean)", true, 1, "callback");
        Method("SaveDataManager", "CopyGameWorkToSaveData", true,
            "Boolean(GAME_WORK," + Save + ")", true, 1, "save_data");
        Method("SaveDataManager", "CopySaveDataToGameWork", true,
            "Boolean(" + Save + ",GAME_WORK)", true, 0, "save_data");
        foreach (string name in new[] { "playerName", "playerCName", "date_time_now" }) Property(Save, name, "String");
        Property(Save, "playTime", "UInt32");
        foreach (string name in new[] { "member_flag", "storyFlagS", "tmpEventFlagS" }) Property(Save, name, Bytes);
        Property(Save, "party_data", "PARTY_DATA");
        Property(Save, "player_base", References("PLAYER_BASE"));
        foreach (string name in new[] { "vil_no", "map_no", "area_no", "player_kazu" }) Property("PARTY_DATA", name, "Byte");
        foreach (string name in new[] { "x", "y", "mochi_kin" }) Property("PARTY_DATA", name, "Int32");
        Property("PARTY_DATA", "chara_code", Shorts);
        Property("PARTY_DATA", "narabi", SBytes);
        Property("PARTY_DATA", "b_chara", SBytes);
        Property("GAME_WORK", "party_data", "PARTY_DATA");
        Property("GAME_WORK", "player_base", References("PLAYER_BASE"));
        Property("GAME_WORK", "player_data", References("PLAYER_DATA"));
        Property("GAME_WORK", "member_flag", Bytes);

        foreach (string type in new[] { "PLAYER_DATA", "PLAYER_BASE", "PLAYER_ITEM", "PLAYER_BUKI",
            "PLAYER_MONSYO", "PLAYER_FIELD", "PLAYER_BATTLE_SO", "PLAYER_BATTLE_SO+ActionCommand", "PLAYER_FACE" })
            Method(type, ".ctor", false, "Void()");
        foreach (string name in new[] { "chara_id", "n_2", "sex", "soubi_type" }) Property("PLAYER_DATA", name, "Byte", true);
        Property("PLAYER_DATA", "chara_name", "UInt32", true);
        foreach (string name in new[] { "n_1", "monsyu_type", "seicho_type" }) Property("PLAYER_DATA", name, Bytes, true);
        Property("PLAYER_DATA", "player_base", "PLAYER_BASE", true);
        Property("PLAYER_DATA", "battle_data", "PLAYER_BATTLE_SO", true);
        Property("PLAYER_DATA", "field_data", "PLAYER_FIELD", true);
        Property("PLAYER_DATA", "face_data", "PLAYER_FACE", true);
        Property("PLAYER_BASE", "chara_no", "Int32", true);
        foreach (string name in new[] { "max_hp", "hp", "exp", "status" }) Property("PLAYER_BASE", name, "UInt16", true);
        foreach (string name in new[] { "level", "item_kazu" }) Property("PLAYER_BASE", name, "Byte", true);
        foreach (string name in new[] { "magic_point", "noryoku", "seicho_type" }) Property("PLAYER_BASE", name, Bytes, true);
        Property("PLAYER_BASE", "item", References("PLAYER_ITEM"), true);
        Property("PLAYER_BASE", "buki_data", "PLAYER_BUKI", true);
        Property("PLAYER_BASE", "monsyo_data", "PLAYER_MONSYO", true);
        Property("PLAYER_ITEM", "item_id", "UInt16", true);
        foreach (string name in new[] { "soubi", "data" }) Property("PLAYER_ITEM", name, "Byte", true);
        foreach (string name in new[] { "buki_id", "level" }) Property("PLAYER_BUKI", name, "Byte", true);
        Property("PLAYER_BUKI", "monsyo", Bytes, true);
        foreach (string name in new[] { "monsyo_id", "monsyo_level" }) Property("PLAYER_MONSYO", name, "Byte", true);
        Property("PLAYER_MONSYO", "monsyo_exp", "UInt16", true);
        Property("PLAYER_FIELD", "opd_data", "OPD_DATA", true);
        Property("PLAYER_BATTLE_SO", "action_table", References("PLAYER_BATTLE_SO+ActionCommand"), true);
        Property("PLAYER_BATTLE_SO", "action_function_index", Shorts, true);
        Property("PLAYER_BATTLE_SO", "opd_data", "OPD_DATA", true);
        Property("PLAYER_BATTLE_SO+ActionCommand", "action_cmd", Shorts, true);

        // All eight presentation native methods, with their actual staticness.
        Method("Fhuman_c", "GetCharacterName", true, "String(Int32,Boolean)");
        Method("Fhuman_c", "GetCharacterNameTextID", true, "Int32(Int32,Boolean)");
        Method("Ws_face_c", "CharacterIDConvertToFaceIDStatic", true, "Int16(UInt16)");
        Method("Ws_face_c", "CharacterIDConvertToFaceID", false, "Int16(UInt16)");
        Method("Ws_face_c", "GetFaceSprite", false,
            "Il2CppSystem.Collections.IEnumerator(Int16,Il2CppSystem.Action`1<UnityEngine.Sprite>)", true);
        Method("Village_c", "h2DACreate", false, "AnimeSPRITE(Int32,Int32)");
        Method("Village_c", "Create2DAnimeHuman", false, "AnimeSPRITE(OPD_DATA,Int32)");
        Method("AnimeSpriteManager", "Destroy2DAnime", true, "Void(AnimeSPRITE&)");
        Property("AnimeSPRITE", "opd_data", "OPD_DATA");
        Property("AnimeSPRITE", "opd_sprite", "OPDSprite");
        foreach (string name in new[] { "anime_no", "pattern_kazu", "pattern_no" }) Property("AnimeSPRITE", name, "Int32");
        Property("AnimeSPRITE", "pattern_table", "OPD_DATA+PatternTable");
    }

    private static string References(string type) => Arrays + "Il2CppReferenceArray`1<" + type + ">";
    private static void Method(string type, string name, bool isStatic, string signature,
        bool uniqueName = false, int namedParameter = -1, string? parameterName = null) =>
        Expected.Add(new Contract(type, name, isStatic, signature, uniqueName, namedParameter, parameterName));
    private static void Property(string type, string name, string valueType, bool writable = false, bool isStatic = false)
    {
        Method(type, "get_" + name, isStatic, valueType + "()");
        if (writable) Method(type, "set_" + name, isStatic, "Void(" + valueType + ")");
    }
    private static string Signature(MethodDefinition method, Types provider)
    {
        var signature = method.DecodeSignature(provider, (object?)null);
        return signature.ReturnType + "(" + string.Join(",", signature.ParameterTypes) + ")";
    }
    private static string ParameterSignature(MethodDefinition method, Types provider) =>
        "(" + string.Join(",", method.DecodeSignature(provider, (object?)null).ParameterTypes) + ")";

    private static TypeDefinition RequirePublicType(MetadataReader reader,
        Dictionary<string, TypeDefinitionHandle> definitions, string name)
    {
        if (!definitions.TryGetValue(name, out var handle)) throw new TypeLoadException("Missing GSD1 type " + name);
        var type = reader.GetTypeDefinition(handle);
        var visibility = type.Attributes & TypeAttributes.VisibilityMask;
        if (type.IsNested)
        {
            if (visibility != TypeAttributes.NestedPublic) throw new TypeLoadException("Nonpublic GSD1 type " + name);
            RequirePublicType(reader, definitions, Types.DefinitionName(reader, type.GetDeclaringType()));
        }
        else if (visibility != TypeAttributes.Public) throw new TypeLoadException("Nonpublic GSD1 type " + name);
        return type;
    }

    private static int CheckSourceTypes(MetadataReader reader, Dictionary<string, TypeDefinitionHandle> definitions)
    {
        var used = new HashSet<string>(StringComparer.Ordinal);
        var assembly = Assembly.GetExecutingAssembly();
        string[] resources = assembly.GetManifestResourceNames().Where(n => n.StartsWith("AvatarSource.", StringComparison.Ordinal)).ToArray();
        if (resources.Length == 0) throw new InvalidDataException("Avatar source was not embedded for native type verification.");
        foreach (string resource in resources)
        {
            using var stream = assembly.GetManifestResourceStream(resource) ?? throw new InvalidDataException(resource);
            using var text = new StreamReader(stream);
            string source = Regex.Replace(text.ReadToEnd(), "@?\"(?:\"\"|\\\\.|[^\"])*\"|//[^\\r\\n]*|/\\*[\\s\\S]*?\\*/", " ");
            foreach (Match match in Regex.Matches(source, @"\bGSD1\.([A-Za-z_]\w*(?:\.[A-Za-z_]\w*)*)"))
            {
                string[] path = match.Groups[1].Value.Split('.');
                string typeName = path[0];
                var type = RequirePublicType(reader, definitions, typeName);
                used.Add(typeName);
                int next = 1;
                while (next < path.Length && definitions.ContainsKey(typeName + "+" + path[next]))
                {
                    typeName += "+" + path[next++];
                    type = RequirePublicType(reader, definitions, typeName);
                    used.Add(typeName);
                }
                if (next < path.Length)
                {
                    string member = path[next];
                    bool declared = type.GetMethods().Any(h => reader.GetString(reader.GetMethodDefinition(h).Name) == member)
                        || type.GetProperties().Any(h => reader.GetString(reader.GetPropertyDefinition(h).Name) == member)
                        || type.GetFields().Any(h => reader.GetString(reader.GetFieldDefinition(h).Name) == member);
                    // This inherited singleton property is outside GSD1; it is
                    // explicitly excluded from the compatibility claim below.
                    if (!declared && !(typeName == "GlobalWork" && member == "Instance"))
                        throw new TypeLoadException(resource + ": unresolved GSD1 type/member path " + match.Value);
                }
            }
            // In a type context, the entire path must resolve as a type. This
            // catches a misspelled nested type even if its outer type exists.
            string[] typeContexts = {
                @"(?:typeof\s*\(\s*|new\s+|<\s*)GSD1\.(?<type>[A-Za-z_]\w*(?:\.[A-Za-z_]\w*)*)",
                @"\bGSD1\.(?<type>[A-Za-z_]\w*(?:\.[A-Za-z_]\w*)*)\s*(?:\?|\[\])*\s+\w+\s*[,;)=\{]"
            };
            foreach (string pattern in typeContexts)
                foreach (Match match in Regex.Matches(source, pattern))
                {
                    string typeName = match.Groups["type"].Value.Replace('.', '+');
                    RequirePublicType(reader, definitions, typeName);
                    used.Add(typeName);
                }
        }
        foreach (string name in used.OrderBy(n => n, StringComparer.Ordinal)) Console.WriteLine("PASS public source type " + name);
        return used.Count;
    }

    private static void CheckNewGameState(MetadataReader reader,
        Dictionary<string, TypeDefinitionHandle> definitions, Types provider)
    {
        var type = RequirePublicType(reader, definitions, "TitleChapter+State");
        var fields = type.GetFields().Select(reader.GetFieldDefinition)
            .Where(f => reader.GetString(f.Name) == "DifficultyInit").ToArray();
        if (fields.Length != 1 || fields[0].DecodeSignature(provider, (object?)null) != "TitleChapter+State"
            || (fields[0].Attributes & (FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.Literal))
                != (FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.Literal))
            throw new InvalidDataException("Missing public literal TitleChapter+State.DifficultyInit.");
        var constant = reader.GetConstant(fields[0].GetDefaultValue());
        if (constant.TypeCode != ConstantTypeCode.Int32 || reader.GetBlobReader(constant.Value).ReadInt32() != 19)
            throw new InvalidDataException("TitleChapter+State.DifficultyInit changed from verified state 19.");
        Console.WriteLine("PASS TitleChapter+State.DifficultyInit public static literal Int32=19");
    }

    private sealed class Types : ISignatureTypeProvider<string, object?>
    {
        public string GetArrayType(string elementType, ArrayShape shape) => elementType + "[" + new string(',', shape.Rank - 1) + "]";
        public string GetByReferenceType(string elementType) => elementType + "&";
        public string GetFunctionPointerType(MethodSignature<string> signature) => "fnptr";
        public string GetGenericInstantiation(string genericType, ImmutableArray<string> arguments) => genericType + "<" + string.Join(",", arguments) + ">";
        public string GetGenericMethodParameter(object? context, int index) => "!!" + index;
        public string GetGenericTypeParameter(object? context, int index) => "!" + index;
        public string GetModifiedType(string modifier, string type, bool required) => type;
        public string GetPinnedType(string elementType) => elementType;
        public string GetPointerType(string elementType) => elementType + "*";
        public string GetPrimitiveType(PrimitiveTypeCode code) => code.ToString();
        public string GetSZArrayType(string elementType) => elementType + "[]";
        internal static string DefinitionName(MetadataReader reader, TypeDefinitionHandle handle)
        {
            var type = reader.GetTypeDefinition(handle);
            string name = reader.GetString(type.Name);
            if (type.IsNested) return DefinitionName(reader, type.GetDeclaringType()) + "+" + name;
            string ns = reader.GetString(type.Namespace);
            return ns.Length == 0 ? name : ns + "." + name;
        }
        private static string ReferenceName(MetadataReader reader, TypeReferenceHandle handle)
        {
            var type = reader.GetTypeReference(handle);
            string name = reader.GetString(type.Name);
            if (type.ResolutionScope.Kind == HandleKind.TypeReference)
                return ReferenceName(reader, (TypeReferenceHandle)type.ResolutionScope) + "+" + name;
            string ns = reader.GetString(type.Namespace);
            return ns.Length == 0 ? name : ns + "." + name;
        }
        public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte kind) => DefinitionName(reader, handle);
        public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte kind) => ReferenceName(reader, handle);
        public string GetTypeFromSpecification(MetadataReader reader, object? context, TypeSpecificationHandle handle, byte kind) => reader.GetTypeSpecification(handle).DecodeSignature(this, context);
    }
}
