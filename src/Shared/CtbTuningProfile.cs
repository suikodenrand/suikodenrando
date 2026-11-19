using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;

namespace SuikodenRandomizer.Shared;

internal sealed class CtbTuningProfile
{
    internal const string FileName = "s1-ctb-tuning.json";
    internal const int MaxEnemySpd = 400;
    internal const int MaxStarSpd = 255;
    private const int MaximumBytes = 65536;
    private const string BossBonusField = "soloBossSpdBonusPercent";
    private const string BlendEnabledField = "soloBossBlendEnabled";
    private const string NormalScalingField = "normalEnemyScalingEnabled";
    private const string PartyWeightField = "soloBossPartyWeightPercent";
    private const string VariationField = "soloBossVariationPercent";
    internal const int DefaultPartyWeightPercent = 75;
    internal const int MaxVariationPercent = 15;
    private static readonly string[] Fields =
        { "schemaVersion", "soloBossBreakPercent", "otherBreakPercent", "starSpd", "enemySpd" };
    private static readonly string[] OptionalFields = { BossBonusField, BlendEnabledField, PartyWeightField, VariationField, NormalScalingField };

    private readonly Dictionary<int, int> _stars = new();
    private readonly Dictionary<string, int> _enemies = new(StringComparer.Ordinal);
    private int _soloBossBreakPercent = 25;
    private int _otherBreakPercent = 50;
    private decimal _soloBossSpdBonusPercent;
    private int _soloBossPartyWeightPercent = DefaultPartyWeightPercent;
    private int _soloBossVariationPercent;

    internal bool HasStarOverrides => _stars.Count != 0;
    internal bool HasEnemyOverrides => _enemies.Count != 0;
    internal bool SoloBossBlendEnabled { get; set; }
    internal bool NormalEnemyScalingEnabled { get; set; }

    internal int SoloBossPartyWeightPercent
    {
        get => _soloBossPartyWeightPercent;
        set => _soloBossPartyWeightPercent = InRange(value, 0, 100, "Solo boss party weight");
    }

    internal int SoloBossVariationPercent
    {
        get => _soloBossVariationPercent;
        set => _soloBossVariationPercent = InRange(value, 0, MaxVariationPercent, "Solo boss SPD variation");
    }

    internal decimal SoloBossSpdBonusPercent
    {
        get => _soloBossSpdBonusPercent;
        set => _soloBossSpdBonusPercent = value is >= 0m and <= 300m ? value
            : throw new InvalidDataException("Solo boss SPD bonus must be between 0 and 300 percent.");
    }

    internal int SoloBossBreakPercent
    {
        get => _soloBossBreakPercent;
        set => _soloBossBreakPercent = InRange(value, 0, 75, "Solo boss Break percentage");
    }

    internal int OtherBreakPercent
    {
        get => _otherBreakPercent;
        set => _otherBreakPercent = InRange(value, 0, 75, "Other Break percentage");
    }

    internal int? GetStarSpd(int id) => _stars.TryGetValue(id, out int spd) ? spd : null;
    internal int? GetEnemySpd(string? id) =>
        id is not null && _enemies.TryGetValue(id, out int spd) ? spd : null;

    internal void SetStarSpd(int id, int? spd)
    {
        if (!CtbTuningCatalog.HasCombatStar(id))
            throw new InvalidDataException($"Star ID {id} has no playable battle record.");
        if (spd.HasValue) _stars[id] = InRange(spd.Value, 1, MaxStarSpd, $"Star {id} SPD");
        else _stars.Remove(id);
    }

    internal void SetEnemySpd(string id, int? spd)
    {
        if (!CtbTuningCatalog.HasEnemy(id))
            throw new InvalidDataException($"Unknown enemy source ID '{id}'.");
        if (spd.HasValue) _enemies[id] = InRange(spd.Value, 1, MaxEnemySpd, $"Enemy {id} SPD");
        else _enemies.Remove(id);
    }

    internal static CtbTuningProfile Load(string path)
    {
        FileStream input;
        try { input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete); }
        catch (FileNotFoundException) { return new CtbTuningProfile(); }
        catch (DirectoryNotFoundException) { return new CtbTuningProfile(); }
        using var snapshot = new MemoryStream();
        using (input)
        {
            if (input.Length > MaximumBytes) throw new InvalidDataException("The CTB tuning file exceeds 64 KiB.");
            input.CopyTo(snapshot);
        }
        snapshot.Position = 0;
        try
        {
            using var document = JsonDocument.Parse(snapshot, new JsonDocumentOptions { MaxDepth = 4 });
            return Read(document.RootElement);
        }
        catch (JsonException ex) { throw new InvalidDataException("The CTB tuning file contains invalid JSON.", ex); }
    }

    private static CtbTuningProfile Read(JsonElement root)
    {
        var remaining = new HashSet<string>(Fields, StringComparer.Ordinal);
        remaining.UnionWith(OptionalFields);
        foreach (var entry in Properties(root, "tuning profile"))
            if (!remaining.Remove(entry.Name)) throw new InvalidDataException($"Unknown or repeated field '{entry.Name}'.");
        remaining.ExceptWith(OptionalFields);
        if (remaining.Count != 0) throw new InvalidDataException("Missing CTB tuning fields: " + string.Join(", ", remaining));
        if (Integer(root.GetProperty("schemaVersion"), "schemaVersion") != 1)
            throw new InvalidDataException("Unsupported CTB tuning file version.");
        var result = new CtbTuningProfile
        {
            SoloBossBreakPercent = Integer(root.GetProperty("soloBossBreakPercent"), "soloBossBreakPercent"),
            OtherBreakPercent = Integer(root.GetProperty("otherBreakPercent"), "otherBreakPercent")
        };
        if (root.TryGetProperty(BossBonusField, out var bonus))
        {
            if (bonus.ValueKind != JsonValueKind.Number || !bonus.TryGetDecimal(out decimal percent))
                throw new InvalidDataException($"'{BossBonusField}' must be a number.");
            result.SoloBossSpdBonusPercent = percent;
        }
        result.SoloBossBlendEnabled = OptionalFlag(root, BlendEnabledField);
        result.NormalEnemyScalingEnabled = OptionalFlag(root, NormalScalingField);
        if (root.TryGetProperty(PartyWeightField, out var weight))
            result.SoloBossPartyWeightPercent = Integer(weight, PartyWeightField);
        if (root.TryGetProperty(VariationField, out var variation))
            result.SoloBossVariationPercent = Integer(variation, VariationField);
        foreach (var entry in Properties(root.GetProperty("starSpd"), "starSpd"))
        {
            if (!int.TryParse(entry.Name, NumberStyles.None, CultureInfo.InvariantCulture, out int id)
                || id.ToString(CultureInfo.InvariantCulture) != entry.Name || result._stars.ContainsKey(id))
                throw new InvalidDataException($"Invalid or repeated Star ID '{entry.Name}'.");
            result.SetStarSpd(id, Integer(entry.Value, entry.Name));
        }
        foreach (var entry in Properties(root.GetProperty("enemySpd"), "enemySpd"))
        {
            if (result._enemies.ContainsKey(entry.Name))
                throw new InvalidDataException($"Repeated enemy source ID '{entry.Name}'.");
            result.SetEnemySpd(entry.Name, Integer(entry.Value, entry.Name));
        }
        return result;
    }

    private static bool OptionalFlag(JsonElement root, string field)
    {
        if (!root.TryGetProperty(field, out var value)) return false;
        if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new InvalidDataException($"'{field}' must be true or false.");
        return value.GetBoolean();
    }

    private static JsonElement.ObjectEnumerator Properties(JsonElement element, string field)
    {
        if (element.ValueKind != JsonValueKind.Object) throw new InvalidDataException($"'{field}' must be an object.");
        return element.EnumerateObject();
    }

    private static int Integer(JsonElement item, string field)
    {
        if (item.ValueKind != JsonValueKind.Number || !item.TryGetInt32(out int value))
            throw new InvalidDataException($"'{field}' must be an integer.");
        return value;
    }

    private static int InRange(int value, int low, int high, string field)
    {
        if (value < low || value > high) throw new InvalidDataException($"{field} must be between {low} and {high}.");
        return value;
    }

    internal string Save(string path)
    {
        string target = Path.GetFullPath(path);
        string directory = Path.GetDirectoryName(target) ?? throw new IOException("Missing CTB configuration directory.");
        Directory.CreateDirectory(directory);
        string temp = Path.Combine(directory, "." + Path.GetFileName(target) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        var output = new FileStream(temp, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        try
        {
            string version;
            using (output)
            {
                using var writer = new Utf8JsonWriter(output, new JsonWriterOptions { Indented = true });
                writer.WriteStartObject();
                writer.WriteNumber("schemaVersion", 1);
                writer.WriteNumber("soloBossBreakPercent", SoloBossBreakPercent);
                writer.WriteNumber("otherBreakPercent", OtherBreakPercent);
                if (SoloBossSpdBonusPercent != 0m) writer.WriteNumber(BossBonusField, SoloBossSpdBonusPercent);
                if (SoloBossBlendEnabled) writer.WriteBoolean(BlendEnabledField, true);
                if (NormalEnemyScalingEnabled) writer.WriteBoolean(NormalScalingField, true);
                if (SoloBossPartyWeightPercent != DefaultPartyWeightPercent)
                    writer.WriteNumber(PartyWeightField, SoloBossPartyWeightPercent);
                if (SoloBossVariationPercent != 0) writer.WriteNumber(VariationField, SoloBossVariationPercent);
                writer.WriteStartObject("starSpd");
                foreach (var entry in _stars.OrderBy(entry => entry.Key))
                    writer.WriteNumber(entry.Key.ToString(CultureInfo.InvariantCulture), entry.Value);
                writer.WriteEndObject();
                writer.WriteStartObject("enemySpd");
                foreach (var entry in _enemies.OrderBy(entry => entry.Key, StringComparer.Ordinal))
                    writer.WriteNumber(entry.Key, entry.Value);
                writer.WriteEndObject();
                writer.WriteEndObject();
                writer.Flush();
                output.Flush(flushToDisk: true);
                output.Position = 0;
                using var sha = SHA256.Create();
                version = Convert.ToHexString(sha.ComputeHash(output));
            }
            // Windows can briefly refuse to replace a recently opened destination.
            // Retry the rename only. The complete staged file stays intact until it succeeds.
            for (int attempt = 0; ; attempt++)
            {
                try { File.Move(temp, target, overwrite: true); return version; }
                catch (Exception ex) when (attempt < 5 && OperatingSystem.IsWindows() &&
                    ex.HResult is unchecked((int)0x80070005) or unchecked((int)0x80070020) or unchecked((int)0x80070021))
                {
                    Thread.Sleep(10 << attempt);
                }
            }
        }
        catch (Exception saveError)
        {
            try { File.Delete(temp); }
            catch (Exception cleanupError) when (cleanupError is IOException or UnauthorizedAccessException)
            {
                throw new IOException($"Could not save CTB tuning or remove the staged file '{temp}'.",
                    new AggregateException(saveError, cleanupError));
            }
            throw;
        }
    }
}
