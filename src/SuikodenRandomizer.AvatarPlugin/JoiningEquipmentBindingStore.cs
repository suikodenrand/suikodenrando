using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace SuikodenRandomizer.AvatarPlugin;

internal sealed class JoiningEquipmentBinding
{
    internal JoiningEquipmentPolicy Policy { get; }
    internal IReadOnlyCollection<int> InitializedFighters { get; }
    internal IReadOnlyCollection<int> InitializedRoles { get; }
    internal IReadOnlyDictionary<int, JoiningEquipmentInventory> CanonicalRoleInventories { get; }
    internal JoiningEquipmentBinding(JoiningEquipmentPolicy policy, IEnumerable<int> fighters, IEnumerable<int> roles,
        IReadOnlyDictionary<int, JoiningEquipmentInventory> canonical)
    {
        Policy = policy;
        InitializedFighters = fighters.OrderBy(id => id).ToArray();
        InitializedRoles = roles.OrderBy(id => id).ToArray();
        CanonicalRoleInventories = canonical.ToDictionary(pair => pair.Key, pair => pair.Value.Clone());
    }
}

// Missing metadata explicitly retains the legacy donor inventory. A mapping
// save from an older release is never retroactively re-equipped on load.
internal static class JoiningEquipmentBindingStore
{
    private const int MaximumBytes = 262144;
    private static string Build => global::SuikodenRandomizer.Plugin.BuildCompatibility.GameAssemblySha256;
    private static string Folder => Path.Combine(AvatarRuntime.Folder, "joining-equipment-save-bindings");

    internal static JoiningEquipmentBinding? Read(string identity, string fingerprint, string seed)
    {
        try
        {
            using var file = new FileStream(PathFor(identity), FileMode.Open, FileAccess.Read, FileShare.Read);
            if (file.Length > MaximumBytes) throw new InvalidDataException("Joining equipment binding exceeds its size bound.");
            using var document = JsonDocument.Parse(file, new JsonDocumentOptions { MaxDepth = 7 });
            var root = document.RootElement;
            Exact(root, "joiningEquipmentVersion", "saveIdentity", "gameBuildSha256", "campaignFingerprint", "seed",
                "policy", "initializedFighters", "initializedRoles", "canonicalRoleInventories");
            if (root.GetProperty("joiningEquipmentVersion").GetInt32() != 1
                || root.GetProperty("saveIdentity").GetString() != identity
                || root.GetProperty("gameBuildSha256").GetString() != Build
                || root.GetProperty("campaignFingerprint").GetString() != fingerprint
                || root.GetProperty("seed").GetString() != seed)
                throw new InvalidDataException("Joining equipment binding does not match this save/campaign/build.");
            string? value = root.GetProperty("policy").GetString();
            if (value != "Donor" && value != "MatchRole") throw new InvalidDataException("Joining equipment policy is invalid.");
            var policy = (JoiningEquipmentPolicy)Enum.Parse(typeof(JoiningEquipmentPolicy), value);
            var fighters = Ids(root.GetProperty("initializedFighters"), 5);
            var roles = Ids(root.GetProperty("initializedRoles"), 76);
            var array = root.GetProperty("canonicalRoleInventories");
            if (array.ValueKind != JsonValueKind.Array || array.GetArrayLength() > 76)
                throw new InvalidDataException("Joining equipment canonical inventories are invalid.");
            var inventories = new Dictionary<int, JoiningEquipmentInventory>();
            foreach (var entry in array.EnumerateArray())
            {
                Exact(entry, "role", "count", "items");
                int role = entry.GetProperty("role").GetInt32();
                if (role < 0 || role > 109 || inventories.ContainsKey(role))
                    throw new InvalidDataException("Joining equipment canonical role ID is invalid or duplicated.");
                var items = entry.GetProperty("items");
                if (items.ValueKind != JsonValueKind.Array || items.GetArrayLength() < 9 || items.GetArrayLength() > byte.MaxValue)
                    throw new InvalidDataException("Joining equipment item array is invalid.");
                var inventory = new JoiningEquipmentInventory
                {
                    Count = entry.GetProperty("count").GetByte(),
                    Items = items.EnumerateArray().Select(item =>
                    {
                        Exact(item, "id", "equipped", "data");
                        return new JoiningEquipmentItem { Id = item.GetProperty("id").GetUInt16(),
                            Equipped = item.GetProperty("equipped").GetByte(), Data = item.GetProperty("data").GetByte() };
                    }).ToArray()
                };
                inventory.Validate(); inventories.Add(role, inventory);
            }
            Validate(policy, fighters, roles, inventories);
            return new JoiningEquipmentBinding(policy, fighters, roles, inventories);
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }

    internal static void CheckWritable()
    {
        Directory.CreateDirectory(Folder);
        string probe = Path.Combine(Folder, Guid.NewGuid().ToString("N") + ".probe");
        try { using var file = new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None); file.WriteByte(0); file.Flush(true); }
        finally { if (File.Exists(probe)) File.Delete(probe); }
    }

    internal static void Write(string identity, string fingerprint, string seed, JoiningEquipmentBinding binding)
    {
        Validate(binding.Policy, binding.InitializedFighters, binding.InitializedRoles, binding.CanonicalRoleInventories);
        string path = PathFor(identity); Directory.CreateDirectory(Folder);
        var previous = Read(identity, fingerprint, seed);
        if (previous is not null) { RequireMatch(previous, binding); return; }
        using var payload = new MemoryStream();
        using (var writer = new Utf8JsonWriter(payload))
        {
            writer.WriteStartObject(); writer.WriteNumber("joiningEquipmentVersion", 1);
            writer.WriteString("saveIdentity", identity); writer.WriteString("gameBuildSha256", Build);
            writer.WriteString("campaignFingerprint", fingerprint); writer.WriteString("seed", seed);
            writer.WriteString("policy", binding.Policy.ToString());
            writer.WriteStartArray("initializedFighters"); foreach (int id in binding.InitializedFighters) writer.WriteNumberValue(id); writer.WriteEndArray();
            writer.WriteStartArray("initializedRoles"); foreach (int id in binding.InitializedRoles) writer.WriteNumberValue(id); writer.WriteEndArray();
            writer.WriteStartArray("canonicalRoleInventories");
            foreach (var pair in binding.CanonicalRoleInventories.OrderBy(pair => pair.Key))
            {
                writer.WriteStartObject(); writer.WriteNumber("role", pair.Key); writer.WriteNumber("count", pair.Value.Count);
                writer.WriteStartArray("items");
                foreach (var item in pair.Value.Items)
                {
                    writer.WriteStartObject(); writer.WriteNumber("id", item.Id); writer.WriteNumber("equipped", item.Equipped);
                    writer.WriteNumber("data", item.Data); writer.WriteEndObject();
                }
                writer.WriteEndArray(); writer.WriteEndObject();
            }
            writer.WriteEndArray(); writer.WriteEndObject();
        }
        if (payload.Length > MaximumBytes) throw new InvalidDataException("Joining equipment binding exceeds its size bound.");
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { payload.WriteTo(file); file.Flush(true); }
            try { File.Move(temporary, path); }
            catch (IOException) when (File.Exists(path))
            { RequireMatch(Read(identity, fingerprint, seed) ?? throw new InvalidDataException("Equipment binding disappeared."), binding); }
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static void Validate(JoiningEquipmentPolicy policy, IEnumerable<int> fighters, IEnumerable<int> roles,
        IReadOnlyDictionary<int, JoiningEquipmentInventory> inventories)
    {
        var fighterIds = fighters.ToArray(); var roleIds = roles.ToArray();
        if (!Enum.IsDefined(typeof(JoiningEquipmentPolicy), policy) || fighterIds.Length > 5 || roleIds.Length > 76
            || fighterIds.Distinct().Count() != fighterIds.Length || roleIds.Distinct().Count() != roleIds.Length
            || fighterIds.Concat(roleIds).Concat(inventories.Keys).Any(id => id < 0 || id > 109) || inventories.Count > 76
            || (policy == JoiningEquipmentPolicy.Donor && (fighterIds.Length != 0 || roleIds.Length != 0 || inventories.Count != 0)))
            throw new InvalidDataException("Joining equipment policy/history is invalid.");
        foreach (var inventory in inventories.Values) inventory.Validate();
    }
    private static int[] Ids(JsonElement array, int maximum)
    {
        if (array.ValueKind != JsonValueKind.Array || array.GetArrayLength() > maximum)
            throw new InvalidDataException("Joining equipment initialization history is invalid.");
        var ids = array.EnumerateArray().Select(value => value.GetInt32()).ToArray();
        if (ids.Any(id => id < 0 || id > 109) || ids.Distinct().Count() != ids.Length)
            throw new InvalidDataException("Joining equipment initialization IDs are invalid or duplicated.");
        return ids;
    }
    private static void Exact(JsonElement value, params string[] names)
    {
        if (value.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Equipment binding object is missing.");
        var remaining = new HashSet<string>(names, StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
            if (!remaining.Remove(property.Name)) throw new InvalidDataException("Equipment binding has duplicate/unknown fields.");
        if (remaining.Count != 0) throw new InvalidDataException("Equipment binding fields are missing.");
    }
    private static void RequireMatch(JoiningEquipmentBinding actual, JoiningEquipmentBinding expected)
    {
        if (actual.Policy != expected.Policy || !actual.InitializedFighters.SequenceEqual(expected.InitializedFighters)
            || !actual.InitializedRoles.SequenceEqual(expected.InitializedRoles)
            || !actual.CanonicalRoleInventories.Keys.OrderBy(id => id).SequenceEqual(expected.CanonicalRoleInventories.Keys.OrderBy(id => id))
            || actual.CanonicalRoleInventories.Any(pair => !pair.Value.Same(expected.CanonicalRoleInventories[pair.Key])))
            throw new InvalidDataException("Native snapshot already has different joining equipment policy/history.");
    }
    private static string PathFor(string identity)
    {
        if (identity.Length != 64 || identity.Any(value => !(value >= '0' && value <= '9') && !(value >= 'a' && value <= 'f')))
            throw new InvalidDataException("Invalid native save digest.");
        return Path.Combine(Folder, identity + ".json");
    }
}
