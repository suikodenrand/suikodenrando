using SuikodenRandomizer.Core.Avatars;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;

namespace SuikodenRandomizer.Serialization;

/// <summary>
/// Exact, flat net6-compatible codec for the separate avatar pilot binding.
/// This format cannot adopt a recruitment seed binding or initialize an old
/// native save. Native file publication is the caller's responsibility.
/// </summary>
public static class RoleAvatarBindingJson
{
    public const int MaximumPayloadBytes = 32768;
    public const int MaximumDepth = 4;

    public static string Serialize(RoleAvatarProfile profile, RoleAvatarCampaignState state,
        string saveId, string build)
    {
        if (profile is null) throw new ArgumentNullException(nameof(profile));
        if (state is null) throw new ArgumentNullException(nameof(state));
        RoleAvatarSnapshot snapshot = state.Capture(saveId, build);
        // Check the selected profile too; the caller must not serialize one
        // campaign while labelling it with another configured donor/profile.
        ValidateSnapshot(profile, snapshot, saveId, build);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("avatarVersion", snapshot.AvatarVersion);
            writer.WriteString("profileId", snapshot.ProfileId);
            writer.WriteString("profileFingerprint", snapshot.ProfileFingerprint);
            writer.WriteNumber("canonicalRoleId", snapshot.CanonicalRoleId);
            writer.WriteNumber("donorCharacterId", snapshot.DonorCharacterId);
            writer.WriteString("progressionPolicy", snapshot.ProgressionPolicy);
            writer.WriteString("startPolicy", snapshot.StartPolicy);
            writer.WriteString("saveIdentity", snapshot.SaveIdentity);
            writer.WriteString("gameBuildSha256", snapshot.GameBuildSha256);
            writer.WriteBoolean("templateInitializationCompleted", snapshot.TemplateInitializationCompleted);
            writer.WriteEndObject();
        }
        string json = Encoding.UTF8.GetString(stream.ToArray()) + Environment.NewLine;
        RequirePayloadBound(json);
        return json;
    }

    public static RoleAvatarCampaignState Deserialize(RoleAvatarProfile profile, string json,
        string saveId, string build)
    {
        if (profile is null) throw new ArgumentNullException(nameof(profile));
        if (json is null) throw new ArgumentNullException(nameof(json));
        RequirePayloadBound(json);
        using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = MaximumDepth
        });
        JsonElement root = document.RootElement;
        ExactFields(root);
        var snapshot = new RoleAvatarSnapshot
        {
            AvatarVersion = Integer(root.GetProperty("avatarVersion"), "avatarVersion"),
            ProfileId = String(root.GetProperty("profileId"), "profileId"),
            ProfileFingerprint = Digest(root.GetProperty("profileFingerprint"), "profileFingerprint"),
            CanonicalRoleId = Integer(root.GetProperty("canonicalRoleId"), "canonicalRoleId"),
            DonorCharacterId = Integer(root.GetProperty("donorCharacterId"), "donorCharacterId"),
            ProgressionPolicy = String(root.GetProperty("progressionPolicy"), "progressionPolicy"),
            StartPolicy = String(root.GetProperty("startPolicy"), "startPolicy"),
            SaveIdentity = Digest(root.GetProperty("saveIdentity"), "saveIdentity"),
            GameBuildSha256 = Digest(root.GetProperty("gameBuildSha256"), "gameBuildSha256"),
            TemplateInitializationCompleted = Boolean(root.GetProperty("templateInitializationCompleted"),
                "templateInitializationCompleted")
        };
        return ValidateSnapshot(profile, snapshot, saveId, build);
    }

    private static RoleAvatarCampaignState ValidateSnapshot(RoleAvatarProfile profile,
        RoleAvatarSnapshot snapshot, string saveId, string build)
    {
        try { return RoleAvatarCampaignState.Restore(profile, snapshot, saveId, build); }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException("Avatar binding does not match its initialized profile, native snapshot or build.", exception);
        }
    }

    private static void RequirePayloadBound(string json)
    {
        // Bound both the input string and its actual UTF-8 payload; multibyte
        // padding must not evade a nominal 32 KiB document limit.
        if (json.Length > MaximumPayloadBytes || Encoding.UTF8.GetByteCount(json) > MaximumPayloadBytes)
            throw new JsonException("Avatar binding exceeds its 32 KiB payload bound.");
    }

    private static void ExactFields(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) throw new JsonException("Avatar binding must be an object.");
        var remaining = new HashSet<string>(new[]
        {
            "avatarVersion", "profileId", "profileFingerprint", "canonicalRoleId", "donorCharacterId",
            "progressionPolicy", "startPolicy", "saveIdentity", "gameBuildSha256", "templateInitializationCompleted"
        }, StringComparer.Ordinal);
        foreach (JsonProperty property in root.EnumerateObject())
            if (!remaining.Remove(property.Name))
                throw new JsonException("Unknown, aliased or duplicate avatar binding property: " + property.Name);
        if (remaining.Count != 0) throw new JsonException("Avatar binding is missing required fields: " + string.Join(", ", remaining));
    }

    private static int Integer(JsonElement value, string name)
    {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out int result)
            || value.GetRawText() != result.ToString(CultureInfo.InvariantCulture))
            throw new JsonException(name + " must be a canonical 32-bit JSON integer.");
        return result;
    }

    private static bool Boolean(JsonElement value, string name)
    {
        if (value.ValueKind != JsonValueKind.True && value.ValueKind != JsonValueKind.False)
            throw new JsonException(name + " must be a JSON Boolean.");
        return value.GetBoolean();
    }

    private static string String(JsonElement value, string name)
    {
        if (value.ValueKind != JsonValueKind.String) throw new JsonException(name + " must be a JSON string.");
        return value.GetString()!;
    }

    private static string Digest(JsonElement value, string name)
    {
        string result = String(value, name);
        if (result.Length != 64) throw new JsonException(name + " must be a lowercase SHA-256 digest.");
        foreach (char character in result)
            if (!(character >= '0' && character <= '9') && !(character >= 'a' && character <= 'f'))
                throw new JsonException(name + " must be a lowercase SHA-256 digest.");
        return result;
    }
}
