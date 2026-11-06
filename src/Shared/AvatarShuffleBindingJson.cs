using SuikodenRandomizer.Core.Avatars;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;

namespace SuikodenRandomizer.Serialization;

/// <summary>Strict net6-compatible metadata codec for a complete seeded combat-avatar campaign.</summary>
public static class AvatarShuffleBindingJson
{
    public const int MaximumPayloadBytes = 65536;
    public const int MaximumDepth = 6;
    private static readonly string[] RootFields = { "avatarShuffleVersion", "profileId", "profileFingerprint", "seed", "shuffleAlgorithm", "startPolicy", "progressionPolicy", "saveIdentity", "gameBuildSha256", "templateInitializationCompleted", "assignments" };

    public static string Serialize(AvatarShuffleProfile profile, AvatarShuffleCampaignState state, string saveId, string build)
    {
        if (profile is null) throw new ArgumentNullException(nameof(profile));
        if (state is null) throw new ArgumentNullException(nameof(state));
        AvatarShuffleSnapshot snapshot = state.Capture(saveId, build);
        if (snapshot.ProfileFingerprint != profile.Fingerprint) throw new InvalidDataException("Selected shuffle profile differs from campaign.");
        Validate(snapshot, saveId, build);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("avatarShuffleVersion", snapshot.AvatarShuffleVersion);
            writer.WriteString("profileId", snapshot.ProfileId);
            writer.WriteString("profileFingerprint", snapshot.ProfileFingerprint);
            writer.WriteString("seed", snapshot.Seed);
            writer.WriteString("shuffleAlgorithm", snapshot.ShuffleAlgorithm);
            writer.WriteString("startPolicy", snapshot.StartPolicy);
            writer.WriteString("progressionPolicy", snapshot.ProgressionPolicy);
            writer.WriteString("saveIdentity", snapshot.SaveIdentity);
            writer.WriteString("gameBuildSha256", snapshot.GameBuildSha256);
            writer.WriteBoolean("templateInitializationCompleted", snapshot.TemplateInitializationCompleted);
            writer.WriteStartArray("assignments");
            foreach (AvatarShuffleAssignment assignment in snapshot.Assignments)
            {
                writer.WriteStartObject(); writer.WriteNumber("roleId", assignment.RoleId);
                writer.WriteNumber("donorId", assignment.DonorId); writer.WriteEndObject();
            }
            writer.WriteEndArray(); writer.WriteEndObject();
        }
        string json = Encoding.UTF8.GetString(stream.ToArray()) + Environment.NewLine;
        RequirePayloadBound(json); return json;
    }

    public static AvatarShuffleCampaignState Deserialize(string json, string saveId, string build)
    {
        if (json is null) throw new ArgumentNullException(nameof(json));
        RequirePayloadBound(json);
        using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions
        { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow, MaxDepth = MaximumDepth });
        JsonElement root = document.RootElement;
        ExactFields(root, RootFields);
        JsonElement array = root.GetProperty("assignments");
        if (array.ValueKind != JsonValueKind.Array || array.GetArrayLength() != 75)
            throw new JsonException("Avatar shuffle requires exactly 75 assignment objects.");
        var assignments = new AvatarShuffleAssignment[75];
        int n = 0;
        foreach (JsonElement item in array.EnumerateArray())
        {
            ExactFields(item, new[] { "roleId", "donorId" });
            assignments[n++] = new AvatarShuffleAssignment
            { RoleId = Integer(item.GetProperty("roleId"), "roleId"), DonorId = Integer(item.GetProperty("donorId"), "donorId") };
        }
        var snapshot = new AvatarShuffleSnapshot
        {
            AvatarShuffleVersion = Integer(root.GetProperty("avatarShuffleVersion"), "avatarShuffleVersion"),
            ProfileId = String(root.GetProperty("profileId"), "profileId"),
            ProfileFingerprint = Digest(root.GetProperty("profileFingerprint"), "profileFingerprint"),
            Seed = String(root.GetProperty("seed"), "seed"),
            ShuffleAlgorithm = String(root.GetProperty("shuffleAlgorithm"), "shuffleAlgorithm"),
            StartPolicy = String(root.GetProperty("startPolicy"), "startPolicy"),
            ProgressionPolicy = String(root.GetProperty("progressionPolicy"), "progressionPolicy"),
            SaveIdentity = Digest(root.GetProperty("saveIdentity"), "saveIdentity"),
            GameBuildSha256 = Digest(root.GetProperty("gameBuildSha256"), "gameBuildSha256"),
            TemplateInitializationCompleted = Boolean(root.GetProperty("templateInitializationCompleted"), "templateInitializationCompleted"),
            Assignments = assignments
        };
        return Validate(snapshot, saveId, build);
    }

    private static AvatarShuffleCampaignState Validate(AvatarShuffleSnapshot snapshot, string saveId, string build)
    {
        try { return AvatarShuffleCampaignState.Restore(snapshot, saveId, build); }
        catch (ArgumentException exception)
        { throw new InvalidDataException("Avatar shuffle binding does not match its seed, mapping, native snapshot or build.", exception); }
    }

    private static void RequirePayloadBound(string json)
    {
        if (json.Length > MaximumPayloadBytes || Encoding.UTF8.GetByteCount(json) > MaximumPayloadBytes)
            throw new JsonException("Avatar shuffle binding exceeds its 64 KiB payload bound.");
    }

    private static void ExactFields(JsonElement item, IEnumerable<string> fields)
    {
        if (item.ValueKind != JsonValueKind.Object) throw new JsonException("Avatar shuffle metadata must be an object.");
        var remaining = new HashSet<string>(fields, StringComparer.Ordinal);
        foreach (JsonProperty property in item.EnumerateObject())
            if (!remaining.Remove(property.Name)) throw new JsonException("Unknown, duplicate or aliased avatar shuffle property: " + property.Name);
        if (remaining.Count != 0) throw new JsonException("Missing avatar shuffle properties: " + string.Join(", ", remaining));
    }
    private static int Integer(JsonElement value, string name)
    {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out int result)
            || value.GetRawText() != result.ToString(CultureInfo.InvariantCulture))
            throw new JsonException(name + " must be a canonical 32-bit JSON integer.");
        return result;
    }
    private static string String(JsonElement value, string name)
    {
        if (value.ValueKind != JsonValueKind.String) throw new JsonException(name + " must be a JSON string.");
        return value.GetString()!;
    }
    private static bool Boolean(JsonElement value, string name)
    {
        if (value.ValueKind != JsonValueKind.True && value.ValueKind != JsonValueKind.False)
            throw new JsonException(name + " must be a JSON Boolean.");
        return value.GetBoolean();
    }
    private static string Digest(JsonElement value, string name)
    {
        string result = String(value, name);
        if (result.Length != 64) throw new JsonException(name + " must be a lowercase SHA-256 digest.");
        foreach (char c in result)
            if (!(c >= '0' && c <= '9') && !(c >= 'a' && c <= 'f')) throw new JsonException(name + " must be a lowercase SHA-256 digest.");
        return result;
    }
}
