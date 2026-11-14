using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace SuikodenRandomizer.AvatarPlugin;

internal enum JoiningWeaponPolicy { Donor, MatchRole, Level1, TirVariance }

internal sealed class JoiningWeaponBinding(JoiningWeaponPolicy policy, IEnumerable<int> initializedFighters, IEnumerable<int>? initializedRoles = null)
{
    internal JoiningWeaponPolicy Policy { get; } = policy;
    internal IReadOnlyCollection<int> InitializedFighters { get; } = initializedFighters.OrderBy(id => id).ToArray();
    internal IReadOnlyCollection<int> InitializedRoles { get; } = (initializedRoles ?? Array.Empty<int>()).OrderBy(id => id).ToArray();
}

// A separate binding keeps the established avatar mapping and native save
// identities unchanged. A missing binding always means the legacy Donor rule.
internal static class JoiningWeaponBindingStore
{
    private const int Version = 2;
    private const int MaximumPayloadBytes = 4096;
    private static readonly string[] Fields = { "joiningWeaponVersion", "saveIdentity", "gameBuildSha256",
        "campaignFingerprint", "seed", "policy", "initializedFighters" };
    private static string Build => global::SuikodenRandomizer.Plugin.BuildCompatibility.GameAssemblySha256;
    private static string Folder => Path.Combine(AvatarRuntime.Folder, "joining-weapon-save-bindings");

    internal static JoiningWeaponBinding? Read(string identity, string campaignFingerprint, string seed)
    {
        try
        {
            using var file = new FileStream(PathFor(identity), FileMode.Open, FileAccess.Read, FileShare.Read);
            if (file.Length > MaximumPayloadBytes) throw new InvalidDataException("Joining weapon binding exceeds its size bound.");
            using var document = JsonDocument.Parse(file, new JsonDocumentOptions { MaxDepth = 3 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Joining weapon binding object is missing.");
            int version = root.GetProperty("joiningWeaponVersion").GetInt32();
            if (version != 1 && version != Version) throw new InvalidDataException("Joining weapon binding version is unsupported.");
            var fields = new HashSet<string>(Fields, StringComparer.Ordinal);
            if (version >= 2) fields.Add("initializedRoles");
            foreach (var field in root.EnumerateObject())
                if (!fields.Remove(field.Name)) throw new InvalidDataException("Joining weapon binding has a duplicate or unknown field.");
            if (fields.Count != 0) throw new InvalidDataException("Joining weapon binding fields are incomplete.");
            if (root.GetProperty("saveIdentity").GetString() != identity
                || root.GetProperty("gameBuildSha256").GetString() != Build
                || root.GetProperty("campaignFingerprint").GetString() != campaignFingerprint
                || root.GetProperty("seed").GetString() != seed)
                throw new InvalidDataException("Joining weapon binding does not match this save, campaign or game build.");
            string? policyText = root.GetProperty("policy").GetString();
            if (policyText != "MatchRole" && policyText != "Level1" && policyText != "Donor"
                && (policyText != "TirVariance" || version < 2))
                throw new InvalidDataException("Joining weapon policy is unsupported.");
            var policy = (JoiningWeaponPolicy)Enum.Parse(typeof(JoiningWeaponPolicy), policyText);
            var fighters = root.GetProperty("initializedFighters");
            if (fighters.ValueKind != JsonValueKind.Array || fighters.GetArrayLength() > 5)
                throw new InvalidDataException("Joining weapon fighter history is invalid.");
            var ids = new HashSet<int>();
            foreach (var value in fighters.EnumerateArray())
            {
                int id = value.GetInt32();
                if (id < 0 || id > 109 || !ids.Add(id)) throw new InvalidDataException("Joining weapon fighter history has an invalid or duplicate ID.");
            }
            var roles = new HashSet<int>();
            if (version >= 2)
            {
                var savedRoles = root.GetProperty("initializedRoles");
                if (savedRoles.ValueKind != JsonValueKind.Array || savedRoles.GetArrayLength() > 76)
                    throw new InvalidDataException("Joining weapon role history is invalid.");
                foreach (var value in savedRoles.EnumerateArray())
                {
                    int id = value.GetInt32();
                    if (id < 0 || id > 109 || !roles.Add(id)) throw new InvalidDataException("Joining weapon role history has an invalid or duplicate ID.");
                }
            }
            return new JoiningWeaponBinding(policy, ids, roles);
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }

    internal static void CheckWritable()
    {
        Directory.CreateDirectory(Folder);
        string path = Path.Combine(Folder, Guid.NewGuid().ToString("N") + ".probe");
        try
        {
            using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            file.WriteByte(0); file.Flush(true);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    internal static void Write(string identity, string campaignFingerprint, string seed, JoiningWeaponBinding binding)
    {
        if (!Enum.IsDefined(typeof(JoiningWeaponPolicy), binding.Policy)
            || binding.InitializedFighters.Count > 5
            || binding.InitializedFighters.Distinct().Count() != binding.InitializedFighters.Count
            || binding.InitializedFighters.Any(id => id < 0 || id > 109)
            || binding.InitializedRoles.Count > 76
            || binding.InitializedRoles.Distinct().Count() != binding.InitializedRoles.Count
            || binding.InitializedRoles.Any(id => id < 0 || id > 109))
            throw new InvalidDataException("Joining weapon policy or fighter history is invalid.");
        string path = PathFor(identity);
        Directory.CreateDirectory(Folder);
        var existing = Read(identity, campaignFingerprint, seed);
        if (existing is not null) { RequireMatch(existing, binding); return; }
        using var payload = new MemoryStream();
        using (var writer = new Utf8JsonWriter(payload))
        {
            writer.WriteStartObject();
            writer.WriteNumber("joiningWeaponVersion", Version);
            writer.WriteString("saveIdentity", identity);
            writer.WriteString("gameBuildSha256", Build);
            writer.WriteString("campaignFingerprint", campaignFingerprint);
            writer.WriteString("seed", seed);
            writer.WriteString("policy", binding.Policy.ToString());
            writer.WriteStartArray("initializedFighters");
            foreach (int fighter in binding.InitializedFighters) writer.WriteNumberValue(fighter);
            writer.WriteEndArray();
            writer.WriteStartArray("initializedRoles");
            foreach (int role in binding.InitializedRoles) writer.WriteNumberValue(role);
            writer.WriteEndArray(); writer.WriteEndObject();
        }
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { payload.WriteTo(file); file.Flush(true); }
            try { File.Move(temporary, path); }
            catch (IOException) when (File.Exists(path))
            {
                RequireMatch(Read(identity, campaignFingerprint, seed)
                    ?? throw new InvalidDataException("Joining weapon binding disappeared during publication."), binding);
            }
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static void RequireMatch(JoiningWeaponBinding actual, JoiningWeaponBinding expected)
    {
        if (actual.Policy != expected.Policy || !actual.InitializedFighters.SequenceEqual(expected.InitializedFighters)
            || !actual.InitializedRoles.SequenceEqual(expected.InitializedRoles))
            throw new InvalidDataException("Native snapshot already belongs to a different joining weapon policy or fighter history.");
    }

    private static string PathFor(string identity)
    {
        if (identity.Length != 64 || identity.Any(value => !(value >= '0' && value <= '9') && !(value >= 'a' && value <= 'f')))
            throw new InvalidDataException("Invalid native save digest.");
        return Path.Combine(Folder, identity + ".json");
    }
}
