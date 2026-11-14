using SuikodenRandomizer.Core.Treasure;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace SuikodenRandomizer.AvatarPlugin;

internal sealed class TreasureBinding(TreasureShuffleProfile? profile)
{
    internal TreasureShuffleProfile? Profile { get; } = profile;
}

internal static class TreasureBindingStore
{
    private const int BindingVersion = 1;
    private const int MaximumPayloadBytes = 65536;
    private static readonly string[] RootFields =
    {
        "treasureBindingVersion", "saveIdentity", "gameBuildSha256", "campaignFingerprint", "seed", "enabled",
        "treasureVersion", "algorithm", "catalogDigest", "profileFingerprint", "assignments"
    };
    private static string Build => global::SuikodenRandomizer.Plugin.BuildCompatibility.GameAssemblySha256;
    private static string Folder => Path.Combine(AvatarRuntime.Folder, "treasure-save-bindings");

    internal static TreasureBinding? Read(string identity, string campaignFingerprint, string seed)
    {
        string path = PathFor(identity);
        try
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (file.Length > MaximumPayloadBytes) throw new InvalidDataException("Treasure binding exceeds its size bound.");
            using var document = JsonDocument.Parse(file, new JsonDocumentOptions { MaxDepth = 5 });
            var root = document.RootElement;
            RequireFields(root, RootFields);
            if (root.GetProperty("treasureBindingVersion").GetInt32() != BindingVersion
                || root.GetProperty("saveIdentity").GetString() != identity
                || root.GetProperty("gameBuildSha256").GetString() != Build
                || root.GetProperty("campaignFingerprint").GetString() != campaignFingerprint
                || root.GetProperty("seed").GetString() != seed)
                throw new InvalidDataException("Treasure binding does not match this save, campaign or game build.");

            if (!root.GetProperty("enabled").GetBoolean())
            {
                foreach (string field in new[] { "treasureVersion", "algorithm", "catalogDigest", "profileFingerprint" })
                    if (root.GetProperty(field).ValueKind != JsonValueKind.Null)
                        throw new InvalidDataException("Disabled treasure binding contains an active profile.");
                if (root.GetProperty("assignments").ValueKind != JsonValueKind.Array
                    || root.GetProperty("assignments").GetArrayLength() != 0)
                    throw new InvalidDataException("Disabled treasure binding contains reward assignments.");
                return new TreasureBinding(null);
            }

            var profile = TreasureShuffleProfile.Create(seed);
            if (root.GetProperty("treasureVersion").GetInt32() != TreasureShuffleProfile.CurrentVersion
                || root.GetProperty("algorithm").GetString() != TreasureShuffleProfile.AlgorithmId
                || root.GetProperty("catalogDigest").GetString() != profile.CatalogDigest
                || root.GetProperty("profileFingerprint").GetString() != profile.Fingerprint)
                throw new InvalidDataException("Treasure profile version, catalog or fingerprint differs from the supported mapping.");
            var assignments = root.GetProperty("assignments");
            if (assignments.ValueKind != JsonValueKind.Array || assignments.GetArrayLength() != profile.Assignments.Count)
                throw new InvalidDataException("Treasure binding does not contain the complete reward mapping.");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var assignment in assignments.EnumerateArray())
            {
                RequireFields(assignment, new[] { "locationId", "itemId" });
                string location = assignment.GetProperty("locationId").GetString()
                    ?? throw new InvalidDataException("Treasure location is missing.");
                if (!seen.Add(location) || !profile.TryGetReward(location, out int expected)
                    || assignment.GetProperty("itemId").GetInt32() != expected)
                    throw new InvalidDataException("Treasure binding contains a duplicate, unknown or changed reward assignment.");
            }
            return new TreasureBinding(profile);
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
            file.WriteByte(0);
            file.Flush(true);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    internal static void Write(string identity, string campaignFingerprint, string seed, TreasureShuffleProfile? profile)
    {
        if (profile is not null && profile.Seed != seed)
            throw new InvalidDataException("Treasure mapping seed differs from the active campaign.");
        string path = PathFor(identity);
        Directory.CreateDirectory(Folder);
        var existing = Read(identity, campaignFingerprint, seed);
        if (existing is not null)
        {
            RequireMatchingProfile(existing, profile);
            return;
        }
        using var payload = new MemoryStream();
        using (var writer = new Utf8JsonWriter(payload))
        {
            writer.WriteStartObject();
            writer.WriteNumber("treasureBindingVersion", BindingVersion);
            writer.WriteString("saveIdentity", identity);
            writer.WriteString("gameBuildSha256", Build);
            writer.WriteString("campaignFingerprint", campaignFingerprint);
            writer.WriteString("seed", seed);
            writer.WriteBoolean("enabled", profile is not null);
            if (profile is null)
            {
                foreach (string field in new[] { "treasureVersion", "algorithm", "catalogDigest", "profileFingerprint" })
                    writer.WriteNull(field);
            }
            else
            {
                writer.WriteNumber("treasureVersion", TreasureShuffleProfile.CurrentVersion);
                writer.WriteString("algorithm", TreasureShuffleProfile.AlgorithmId);
                writer.WriteString("catalogDigest", profile.CatalogDigest);
                writer.WriteString("profileFingerprint", profile.Fingerprint);
            }
            writer.WriteStartArray("assignments");
            if (profile is not null)
                foreach (var assignment in profile.Assignments.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                {
                    writer.WriteStartObject();
                    writer.WriteString("locationId", assignment.Key);
                    writer.WriteNumber("itemId", assignment.Value);
                    writer.WriteEndObject();
                }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        if (payload.Length > MaximumPayloadBytes) throw new InvalidDataException("Treasure binding exceeds its size bound.");
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                payload.WriteTo(file);
                file.Flush(true);
            }
            try { File.Move(temporary, path); }
            catch (IOException) when (File.Exists(path))
            {
                var published = Read(identity, campaignFingerprint, seed)
                    ?? throw new InvalidDataException("Treasure binding disappeared during publication.");
                RequireMatchingProfile(published, profile);
            }
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static void RequireMatchingProfile(TreasureBinding binding, TreasureShuffleProfile? expected)
    {
        if (binding.Profile?.Fingerprint != expected?.Fingerprint)
            throw new InvalidDataException("Native snapshot already belongs to a different treasure policy.");
    }

    private static void RequireFields(JsonElement element, string[] expected)
    {
        if (element.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Treasure binding object is missing.");
        var fields = new HashSet<string>(expected, StringComparer.Ordinal);
        foreach (var field in element.EnumerateObject())
            if (!fields.Remove(field.Name)) throw new InvalidDataException("Treasure binding has a duplicate or unknown field.");
        if (fields.Count != 0) throw new InvalidDataException("Treasure binding fields are incomplete.");
    }

    private static string PathFor(string identity)
    {
        if (identity.Length != 64 || identity.Any(value => !(value >= '0' && value <= '9') && !(value >= 'a' && value <= 'f')))
            throw new InvalidDataException("Invalid native save digest.");
        return Path.Combine(Folder, identity + ".json");
    }
}
