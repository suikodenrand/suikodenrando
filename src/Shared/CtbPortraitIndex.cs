using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace SuikodenRandomizer.Shared;

internal sealed class CtbPortraitIndex
{
    internal sealed record Portrait(string File, string Sha256);
    internal readonly Dictionary<string, Portrait> Aliases = new(StringComparer.OrdinalIgnoreCase);
    internal readonly Dictionary<string, string> Files = new(StringComparer.OrdinalIgnoreCase);

    internal static CtbPortraitIndex Read(string directory)
    {
        string path = Path.Combine(directory, "index.json");
        if (!File.Exists(path)) throw new InvalidDataException("The CTB enemy portrait index is missing.");
        using var input = File.OpenRead(path);
        using var reader = new StreamReader(input);
        try
        {
            using var document = JsonDocument.Parse(reader.ReadToEnd());
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("schemaVersion", out var version)
                || version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out int schema) || schema != 1
                || !root.TryGetProperty("entries", out var entries) || entries.ValueKind != JsonValueKind.Array
                || entries.GetArrayLength() == 0)
                throw new InvalidDataException("The CTB enemy portrait index is empty or unsupported.");
            var index = new CtbPortraitIndex();
            foreach (var entry in entries.EnumerateArray())
            {
                string file = Required(entry, "file"), hash = Required(entry, "sha256").ToUpperInvariant();
                if (Path.GetFileName(file) != file || file.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                    || file.Contains('\\') || file.Contains('/') || file.Contains(':')
                    || !string.Equals(Path.GetExtension(file), ".png", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Invalid CTB enemy portrait filename.");
                if (hash.Length != 64 || !hash.All(Uri.IsHexDigit))
                    throw new InvalidDataException("Invalid CTB enemy portrait hash for " + file + ".");
                if (index.Files.TryGetValue(file, out var previous) && hash != previous)
                    throw new InvalidDataException("Conflicting CTB enemy portrait hashes for " + file + ".");
                index.Files[file] = hash;
                var portrait = new Portrait(file, hash);
                index.AddAlias(Required(entry, "sourceId"), portrait);
                index.AddAlias(Required(entry, "opdName"), portrait);
            }
            return index;
        }
        catch (JsonException error) { throw new InvalidDataException("Invalid CTB enemy portrait JSON.", error); }
    }

    private static string Required(JsonElement entry, string field)
    {
        if (entry.ValueKind == JsonValueKind.Object && entry.TryGetProperty(field, out var value)
            && value.ValueKind == JsonValueKind.String && value.GetString() is string text && !string.IsNullOrWhiteSpace(text))
            return text;
        throw new InvalidDataException("Missing CTB enemy portrait field " + field + ".");
    }

    private void AddAlias(string key, Portrait portrait)
    {
        if (Aliases.TryGetValue(key, out var existing) && existing.Sha256 != portrait.Sha256)
            throw new InvalidDataException("Conflicting CTB enemy graphics identity " + key + ".");
        Aliases[key] = portrait;
    }
}
