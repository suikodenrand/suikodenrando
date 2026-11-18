extern alias GSD1;

using Il2CppInterop.Runtime.InteropTypes.Arrays;
using SuikodenRandomizer.Core;
using SuikodenRandomizer.Core.Avatars;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace SuikodenRandomizer.AvatarPlugin;

internal sealed class FixedRosterBinding
{
    public string Seed { get; set; } = string.Empty;
    public int[] Companions { get; set; } = Array.Empty<int>();
    public Dictionary<int, int> Assignments { get; set; } = new();
    public Dictionary<int, FixedRosterBaseSnapshot> CompanionStates { get; set; } = new();
    public Dictionary<int, FixedRosterBaseSnapshot> RoleBases { get; set; } = new();

    public FixedRosterBinding() { }
}

internal static class FixedRosterBindingStore
{
    private const int Version = 1;
    private const int MaximumPayloadBytes = 65536;
    private static readonly JsonSerializerOptions JsonOptions = new() { MaxDepth = 10 };
    private static string Build => global::SuikodenRandomizer.Plugin.BuildCompatibility.GameAssemblySha256;
    private static string Folder => Path.Combine(AvatarRuntime.Folder, "fixed-roster-save-bindings");
    internal static bool HasBindings => Directory.Exists(Folder);

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

    /// <summary>
    /// Extend the legacy identity with every declared native base field. Gear or rune changes
    /// must distinguish snapshots even when the legacy snapshot digest stays the same.
    /// Empty records are included as-is because the native save has uninitialized members.
    /// </summary>
    internal static string ComputeIdentity(GSD1.SaveDataManager.SaveData save, string baseIdentity)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
        {
            writer.Write("srr-fixed-roster-save-v1");
            writer.Write(baseIdentity);
            var bases = save.player_base ?? throw new InvalidDataException("Save has no player bases.");
            writer.Write(bases.Count);
            for (int i = 0; i < bases.Count; i++)
                WriteBase(writer, bases[i]);
        }
        return AvatarSaveIdentity.Hash(stream.ToArray());
    }

    internal static FixedRosterBinding? Read(string identity)
    {
        string path = PathFor(identity);
        try
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (file.Length > MaximumPayloadBytes) throw new InvalidDataException("Fixed roster binding exceeds its size bound.");
            using var document = JsonDocument.Parse(file, new JsonDocumentOptions { MaxDepth = JsonOptions.MaxDepth });
            var root = document.RootElement;
            if (root.GetProperty("Version").GetInt32() != Version
                || root.GetProperty("SaveIdentity").GetString() != identity
                || root.GetProperty("GameBuildSha256").GetString() != Build)
                throw new InvalidDataException("Fixed roster binding does not match this save or game build.");
            var state = root.GetProperty("State");
            if (AvatarSaveIdentity.Hash(Encoding.UTF8.GetBytes(state.GetRawText())) != root.GetProperty("StateHash").GetString())
                throw new InvalidDataException("Fixed roster binding checksum does not match its state.");
            var binding = state.Deserialize<FixedRosterBinding>(JsonOptions)
                ?? throw new InvalidDataException("Fixed roster binding is empty.");
            Validate(binding);
            return binding;
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }

    internal static void Write(string identity, FixedRosterBinding binding)
    {
        Validate(binding);
        string path = PathFor(identity);
        byte[] state = SerializeState(binding);
        Directory.CreateDirectory(Folder);
        var existing = Read(identity);
        if (existing is not null)
        {
            RequireMatchingState(state, existing);
            return;
        }

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("Version", Version);
            writer.WriteString("SaveIdentity", identity);
            writer.WriteString("GameBuildSha256", Build);
            writer.WriteString("StateHash", AvatarSaveIdentity.Hash(state));
            writer.WritePropertyName("State");
            writer.WriteRawValue(state);
            writer.WriteEndObject();
        }
        if (stream.Length > MaximumPayloadBytes)
            throw new InvalidDataException("Fixed roster binding exceeds its size bound.");

        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.WriteTo(file);
                file.Flush(true);
            }
            try { File.Move(temporary, path); }
            catch (IOException) when (File.Exists(path))
            {
                // A concurrent publisher may have committed this identity while we wrote.
                existing = Read(identity) ?? throw new InvalidDataException("Fixed roster binding disappeared during publication.");
                RequireMatchingState(state, existing);
            }
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static void RequireMatchingState(byte[] state, FixedRosterBinding existing)
    {
        if (!state.AsSpan().SequenceEqual(SerializeState(existing)))
            throw new InvalidDataException("This native save already has different fixed roster state. Existing binding preserved.");
    }

    private static byte[] SerializeState(FixedRosterBinding binding) =>
        JsonSerializer.SerializeToUtf8Bytes(new FixedRosterBinding
        {
            Seed = binding.Seed,
            Companions = binding.Companions,
            Assignments = Ordered(binding.Assignments),
            CompanionStates = Ordered(binding.CompanionStates),
            RoleBases = Ordered(binding.RoleBases)
        }, JsonOptions);

    private static Dictionary<int, T> Ordered<T>(Dictionary<int, T> values) =>
        values.OrderBy(entry => entry.Key).ToDictionary(entry => entry.Key, entry => entry.Value);

    private static void Validate(FixedRosterBinding binding)
    {
        if (binding is null || binding.Seed is null || binding.Seed.Length is < 1 or > 128
            || binding.Seed.Any(char.IsControl) || string.IsNullOrWhiteSpace(binding.Seed)
            || binding.Companions is null || binding.Assignments is null
            || binding.CompanionStates is null || binding.RoleBases is null)
            throw new InvalidDataException("Fixed roster binding is incomplete.");

        FixedRosterProfile profile = FixedRosterProfile.CreateChosen(binding.Companions);
        var companions = new HashSet<int>(profile.CompanionIds);
        var assigned = new HashSet<int>();
        foreach (var assignment in binding.Assignments)
        {
            if (!profile.IsStoryRole(assignment.Key) || assignment.Key == CombatCatalog.TirId
                || !companions.Contains(assignment.Value) || !assigned.Add(assignment.Value)
                || !binding.CompanionStates.ContainsKey(assignment.Value)
                || !binding.RoleBases.ContainsKey(assignment.Key))
                throw new InvalidDataException("Fixed roster binding contains an invalid party assignment.");
        }

        if (binding.RoleBases.Count != binding.Assignments.Count)
            throw new InvalidDataException("Fixed roster role snapshots differ from its active assignments.");
        foreach (var entry in binding.CompanionStates)
        {
            if (!companions.Contains(entry.Key) || entry.Value is null || entry.Value.CharacterId != entry.Key)
                throw new InvalidDataException("Fixed roster contains an unknown or mismatched companion snapshot.");
            entry.Value.ValidateState();
        }
        foreach (var entry in binding.RoleBases)
        {
            if (entry.Value is null || entry.Value.CharacterId != entry.Key)
                throw new InvalidDataException("Fixed roster contains a mismatched canonical role snapshot.");
            entry.Value.ValidateState();
        }
    }

    private static string PathFor(string identity)
    {
        if (identity is null || identity.Length != 64
            || identity.Any(c => !(c >= '0' && c <= '9') && !(c >= 'a' && c <= 'f')))
            throw new InvalidDataException("Invalid fixed roster native save digest.");
        return Path.Combine(Folder, identity + ".json");
    }

    private static void WriteBase(BinaryWriter writer, GSD1.PLAYER_BASE? source)
    {
        writer.Write(source is not null);
        if (source is null) return;
        writer.Write(source.chara_no);
        writer.Write(source.max_hp);
        writer.Write(source.hp);
        writer.Write(source.exp);
        writer.Write(source.status);
        writer.Write(source.level);
        writer.Write(source.item_kazu);
        WriteBytes(writer, source.magic_point);
        WriteBytes(writer, source.noryoku);
        WriteBytes(writer, source.seicho_type);

        var items = source.item;
        writer.Write(items?.Count ?? -1);
        if (items is not null)
        {
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                writer.Write(item is not null);
                if (item is null) continue;
                writer.Write(item.item_id);
                writer.Write(item.soubi);
                writer.Write(item.data);
            }
        }

        var weapon = source.buki_data;
        writer.Write(weapon is not null);
        if (weapon is not null)
        {
            writer.Write(weapon.buki_id);
            writer.Write(weapon.level);
            WriteBytes(writer, weapon.monsyo);
        }

        var rune = source.monsyo_data;
        writer.Write(rune is not null);
        if (rune is not null)
        {
            writer.Write(rune.monsyo_id);
            writer.Write(rune.monsyo_level);
            writer.Write(rune.monsyo_exp);
        }
    }

    private static void WriteBytes(BinaryWriter writer, Il2CppStructArray<byte>? values)
    {
        writer.Write(values?.Count ?? -1);
        if (values is null) return;
        for (int i = 0; i < values.Count; i++) writer.Write(values[i]);
    }
}
