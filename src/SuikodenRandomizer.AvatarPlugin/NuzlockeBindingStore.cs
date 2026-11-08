extern alias GSD1;

using SuikodenRandomizer.Core;
using SuikodenRandomizer.Core.Avatars;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace SuikodenRandomizer.AvatarPlugin;

internal sealed class NuzlockeBinding
{
    public NuzlockeSnapshot Campaign { get; set; } = new();
    public Dictionary<int, FixedRosterBaseSnapshot> FighterStates { get; set; } = new();
    public Dictionary<int, FixedRosterBaseSnapshot> RoleBases { get; set; } = new();
    public Dictionary<int, int> RoleRules { get; set; } = new();
    // Missing in older bindings: retain their original future starting kits.
    public bool BalancedStartingGear { get; set; }
}

/// <summary>A native save snapshot and its Nuzlocke campaign state are published together.</summary>
internal static class NuzlockeBindingStore
{
    private const int Version = 2;
    private const int MaximumPayloadBytes = 524288;
    private static readonly JsonSerializerOptions JsonOptions = new() { MaxDepth = 16 };
    private static string Build => global::SuikodenRandomizer.Plugin.BuildCompatibility.GameAssemblySha256;
    private static string Folder => Path.Combine(AvatarRuntime.Folder, "nuzlocke-save-bindings");
    internal static bool HasBindings => Directory.Exists(Folder);

    internal static string ComputeIdentity(GSD1.SaveDataManager.SaveData save) =>
        FixedRosterBindingStore.ComputeIdentity(save);

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

    internal static NuzlockeBinding? Read(string identity)
    {
        string path = PathFor(identity);
        try
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (file.Length > MaximumPayloadBytes)
                throw new InvalidDataException("Nuzlocke binding exceeds its size bound.");
            using var document = JsonDocument.Parse(file, new JsonDocumentOptions { MaxDepth = JsonOptions.MaxDepth });
            var root = document.RootElement;
            RequireUniqueProperties(root);
            if (root.GetProperty("Version").GetInt32() != Version)
                throw new InvalidDataException("This 0.8.8 Nuzlocke save uses the earlier Tir replacement rules. Start a new Nuzlocke game with 0.8.9 or later.");
            if (root.GetProperty("SaveIdentity").GetString() != identity
                || root.GetProperty("GameBuildSha256").GetString() != Build)
                throw new InvalidDataException("Nuzlocke binding does not match this save or game build.");
            var state = root.GetProperty("State");
            if (AvatarSaveIdentity.Hash(Encoding.UTF8.GetBytes(state.GetRawText())) != root.GetProperty("StateHash").GetString())
                throw new InvalidDataException("Nuzlocke binding checksum does not match its state.");
            var binding = state.Deserialize<NuzlockeBinding>(JsonOptions)
                ?? throw new InvalidDataException("Nuzlocke binding is empty.");
            Validate(binding);
            return binding;
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }

    internal static void Write(string identity, NuzlockeBinding binding)
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
            throw new InvalidDataException("Nuzlocke binding exceeds its size bound.");

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
                existing = Read(identity) ?? throw new InvalidDataException("Nuzlocke binding disappeared during publication.");
                RequireMatchingState(state, existing);
            }
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static void RequireMatchingState(byte[] state, NuzlockeBinding existing)
    {
        if (!state.AsSpan().SequenceEqual(SerializeState(existing)))
            throw new InvalidDataException("This native save already has different Nuzlocke state. Existing binding preserved.");
    }

    private static byte[] SerializeState(NuzlockeBinding binding)
    {
        var campaign = NuzlockeCampaign.Restore(binding.Campaign).Capture();
        campaign.Assignments = Ordered(campaign.Assignments);
        return JsonSerializer.SerializeToUtf8Bytes(new NuzlockeBinding
        {
            Campaign = campaign,
            FighterStates = Ordered(binding.FighterStates),
            RoleBases = Ordered(binding.RoleBases),
            RoleRules = Ordered(binding.RoleRules),
            BalancedStartingGear = binding.BalancedStartingGear
        }, JsonOptions);
    }

    private static Dictionary<int, T> Ordered<T>(Dictionary<int, T> values) =>
        values.OrderBy(entry => entry.Key).ToDictionary(entry => entry.Key, entry => entry.Value);

    private static void Validate(NuzlockeBinding binding)
    {
        if (binding is null || binding.Campaign is null || binding.FighterStates is null
            || binding.RoleBases is null || binding.RoleRules is null)
            throw new InvalidDataException("Nuzlocke binding is incomplete.");
        var campaign = NuzlockeCampaign.Restore(binding.Campaign);
        foreach (var entry in binding.FighterStates)
        {
            if (!CombatCatalog.IsNormalCombat(entry.Key)
                || entry.Value is null || entry.Value.CharacterId != entry.Key)
                throw new InvalidDataException("Nuzlocke binding contains an invalid fighter snapshot.");
            entry.Value.ValidateState();
        }
        foreach (var entry in binding.RoleBases)
        {
            if (!IsRole(entry.Key) || entry.Key == CombatCatalog.TirId || entry.Value is null || entry.Value.CharacterId != entry.Key)
                throw new InvalidDataException("Nuzlocke binding contains an invalid canonical role snapshot.");
            entry.Value.ValidateState();
        }
        if (!binding.RoleRules.Keys.ToHashSet().SetEquals(binding.RoleBases.Keys)
            || binding.RoleRules.Any(entry => !IsRole(entry.Key) || !IsRole(entry.Value)
                || entry.Key == CombatCatalog.TirId || entry.Value == CombatCatalog.TirId))
            throw new InvalidDataException("Nuzlocke binding contains inconsistent canonical role rules.");
        foreach (int role in campaign.ActiveRoleIds)
        {
            int fighter = campaign.GetDonor(role);
            if (fighter >= 0 && fighter != CombatCatalog.TirId && !binding.FighterStates.ContainsKey(fighter))
                throw new InvalidDataException("Nuzlocke binding is missing an active fighter snapshot.");
        }
    }

    private static bool IsRole(int id) => CombatCatalog.IsNormalCombat(id)
        || id == CombatCatalog.TirId || id == CombatCatalog.TedId || id == CombatCatalog.OdessaId;

    private static string PathFor(string identity)
    {
        if (identity is null || identity.Length != 64
            || identity.Any(c => !(c >= '0' && c <= '9') && !(c >= 'a' && c <= 'f')))
            throw new InvalidDataException("Invalid Nuzlocke native save digest.");
        return Path.Combine(Folder, identity + ".json");
    }

    private static void RequireUniqueProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new InvalidDataException("Nuzlocke binding contains duplicate JSON properties.");
                RequireUniqueProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) RequireUniqueProperties(item);
    }
}
