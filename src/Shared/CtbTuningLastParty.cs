using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace SuikodenRandomizer.Shared;

internal static class CtbTuningLastParty
{
    internal const string FileName = "s1-ctb-last-party.json";

    private static bool Valid(int star, int speed) => CtbTuningCatalog.HasCombatStar(star) && speed is >= 1 and <= ushort.MaxValue;

    internal static IReadOnlyDictionary<int, int>? Read(string path)
    {
        try
        {
            using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (input.Length > 65536) return null;
            using var json = JsonDocument.Parse(input);
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("schemaVersion", out var version)
                || version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out int schema) || schema != 1
                || !root.TryGetProperty("stars", out var stars) || stars.ValueKind != JsonValueKind.Object) return null;
            var result = new Dictionary<int, int>();
            foreach (var entry in stars.EnumerateObject())
            {
                if (!int.TryParse(entry.Name, NumberStyles.None, CultureInfo.InvariantCulture, out int star)
                    || entry.Value.ValueKind != JsonValueKind.Number || !entry.Value.TryGetInt32(out int speed)
                    || !Valid(star, speed) || !result.TryAdd(star, speed)) return null;
            }
            return result;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    internal static void Write(string path, IReadOnlyDictionary<int, int> stars)
    {
        string fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        string temp = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        try
        {
            using (stream)
            using (var json = new Utf8JsonWriter(stream))
            {
                json.WriteStartObject();
                json.WriteNumber("schemaVersion", 1);
                json.WriteStartObject("stars");
                foreach (var entry in stars)
                {
                    if (!Valid(entry.Key, entry.Value)) throw new InvalidDataException("Invalid last-party SPD sample.");
                    json.WriteNumber(entry.Key.ToString(CultureInfo.InvariantCulture), entry.Value);
                }
                json.WriteEndObject();
                json.WriteEndObject();
            }
            File.Move(temp, fullPath, overwrite: true);
        }
        finally { File.Delete(temp); }
    }
}
