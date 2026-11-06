using SuikodenRandomizer.Core;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;

namespace SuikodenRandomizer.Serialization;

/// <summary>
/// Shared source compiled by both the net6 game plugin and the standalone seed
/// tool. This uses only net6 BCL APIs and deliberately rejects unknown, missing,
/// duplicate and aliased properties before System.Text.Json can apply defaults.
/// </summary>
public static class SeedJson
{
    private const int MaximumDocumentCharacters = 131072;
    private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        WriteIndented = true,
        AllowTrailingCommas = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        MaxDepth = 16
    };

    public static SeedManifest Deserialize(string json)
    {
        if (json is null) throw new ArgumentNullException(nameof(json));
        if (json.Length > MaximumDocumentCharacters)
            throw new JsonException("Seed document exceeds the 128 KiB character limit.");
        using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = 16
        });
        RejectDuplicateProperties(document.RootElement, "$", 0);
        if (document.RootElement.ValueKind == JsonValueKind.Object
            && document.RootElement.TryGetProperty("schemaVersion", out JsonElement schema)
            && schema.ValueKind == JsonValueKind.Number && schema.TryGetInt32(out int version)
            && version != SeedManifest.CurrentSchemaVersion)
        {
            throw new SeedValidationException(new[]
            {
                $"schemaVersion {version} is unsupported; expected {SeedManifest.CurrentSchemaVersion}. Back up the old active-seed.json and generate a version-2 seed; do not reuse an old randomized save with a different mapping"
            });
        }
        RequireExactProperties(document.RootElement, "$", new[]
        {
            "schemaVersion", "algorithmVersion", "game", "seed", "profile",
            "mapping", "startingParty", "diagnostics"
        });
        RequireExactProperties(document.RootElement.GetProperty("startingParty"), "$.startingParty",
            new[] { "enabled", "sourceIds" });
        RequireExactProperties(document.RootElement.GetProperty("diagnostics"), "$.diagnostics",
            new[] { "tracePlayerLoading", "tracePartyInitialization" });

        JsonElement mapping = document.RootElement.GetProperty("mapping");
        if (mapping.ValueKind != JsonValueKind.Object)
            throw new JsonException("mapping must be an object with canonical decimal character ID keys.");
        foreach (JsonProperty property in mapping.EnumerateObject())
        {
            if (!int.TryParse(property.Name, NumberStyles.None, CultureInfo.InvariantCulture, out int id)
                || id.ToString(CultureInfo.InvariantCulture) != property.Name)
                throw new JsonException($"Noncanonical character ID key '{property.Name}'. Use decimal IDs without signs or leading zeroes.");
        }

        SeedManifest? manifest = JsonSerializer.Deserialize<SeedManifest>(json, Options);
        IReadOnlyList<string> errors = SeedValidator.Validate(manifest);
        if (errors.Count > 0) throw new SeedValidationException(errors);
        return manifest!;
    }

    public static string Serialize(SeedManifest manifest)
    {
        if (manifest is null) throw new ArgumentNullException(nameof(manifest));
        SeedValidator.ValidateAndThrow(manifest);
        return JsonSerializer.Serialize(manifest, Options) + Environment.NewLine;
    }

    private static void RequireExactProperties(JsonElement value, string path, string[] allowed)
    {
        if (value.ValueKind != JsonValueKind.Object) throw new JsonException(path + " must be an object.");
        var remaining = new HashSet<string>(allowed, StringComparer.Ordinal);
        foreach (JsonProperty property in value.EnumerateObject())
        {
            if (!remaining.Remove(property.Name))
                throw new JsonException($"Unknown property '{path}.{property.Name}'. Property names are case-sensitive.");
        }
        if (remaining.Count != 0)
            throw new JsonException($"Missing required properties in '{path}': " + string.Join(", ", remaining));
    }

    private static void RejectDuplicateProperties(JsonElement value, string path, int depth)
    {
        if (depth > 16) throw new JsonException("Seed document is too deeply nested.");
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonProperty property in value.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new JsonException($"Duplicate property '{path}.{property.Name}'.");
                RejectDuplicateProperties(property.Value, path + "." + property.Name, depth + 1);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement element in value.EnumerateArray())
                RejectDuplicateProperties(element, path + "[]", depth + 1);
        }
    }
}
