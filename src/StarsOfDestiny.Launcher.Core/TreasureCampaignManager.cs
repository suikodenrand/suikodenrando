using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

namespace StarsOfDestiny.Launcher.Core;

public sealed class TreasureCampaignInfo
{
    internal TreasureCampaignInfo(string fileName, string saveIdentity, string campaignFingerprint, string seed,
        bool enabled, string? profileFingerprint, DateTime lastWriteTimeUtc, string fileSha256)
    {
        FileName = fileName;
        SaveIdentity = saveIdentity;
        CampaignFingerprint = campaignFingerprint;
        Seed = seed;
        Enabled = enabled;
        ProfileFingerprint = profileFingerprint;
        LastWriteTimeUtc = lastWriteTimeUtc;
        FileSha256 = fileSha256;
    }

    public string FileName { get; }
    public string SaveIdentity { get; }
    public string CampaignFingerprint { get; }
    public string Seed { get; }
    public bool Enabled { get; }
    public string? ProfileFingerprint { get; }
    public DateTime LastWriteTimeUtc { get; }
    public string FileSha256 { get; }
}

/// <summary>Reads and backs up only S1 treasure settings. Native saves and avatar bindings are never opened for writing.</summary>
public sealed class TreasureCampaignManager
{
    private const int MaximumBindingBytes = 65536;
    private readonly string _folder;
    private readonly Func<bool> _isGameRunning;
    public IReadOnlyList<TreasureCampaignInfo> Campaigns { get; }
    public IReadOnlyList<string> Issues { get; }

    private TreasureCampaignManager(string gameFolder, Func<bool>? isGameRunning)
    {
        _folder = Path.GetFullPath(Path.Combine(gameFolder, "BepInEx", "config", "SuikodenRandomizer",
            "avatar-shuffle", "treasure-save-bindings"));
        _isGameRunning = isGameRunning ?? GameIsRunning;
        var campaigns = new List<TreasureCampaignInfo>();
        var issues = new List<string>();
        if (Directory.Exists(_folder))
        {
            RequireOrdinaryDirectory(_folder);
            foreach (string path in Directory.EnumerateFiles(_folder, "*.json", SearchOption.TopDirectoryOnly))
            {
                try { campaigns.Add(Read(path)); }
                catch (Exception error) when (error is IOException or InvalidDataException or JsonException or InvalidOperationException
                    or FormatException or UnauthorizedAccessException or KeyNotFoundException)
                {
                    issues.Add(Path.GetFileName(path) + " — " + error.Message);
                }
            }
        }
        Campaigns = campaigns.OrderByDescending(value => value.LastWriteTimeUtc).ToList().AsReadOnly();
        Issues = issues.AsReadOnly();
    }

    public static TreasureCampaignManager Load(string gameFolder, Func<bool>? isGameRunning = null)
    {
        if (string.IsNullOrWhiteSpace(gameFolder)) throw new ArgumentException("Choose a game folder.", nameof(gameFolder));
        return new TreasureCampaignManager(Path.GetFullPath(gameFolder), isGameRunning);
    }

    /// <summary>Keep a backup and let this save adopt the current chest policy next time it loads.</summary>
    public string Reset(TreasureCampaignInfo campaign)
    {
        if (campaign is null) throw new ArgumentNullException(nameof(campaign));
        if (!Campaigns.Any(value => ReferenceEquals(value, campaign)))
            throw new InvalidOperationException("Refresh the saved treasure list before selecting a campaign.");
        if (_isGameRunning()) throw new InvalidOperationException("Close Suikoden before changing saved treasure settings.");
        RequireOrdinaryDirectory(_folder);
        string path = Path.GetFullPath(Path.Combine(_folder, campaign.FileName));
        if (!string.Equals(Path.GetDirectoryName(path), _folder, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The selected treasure setting is outside the game folder.");
        var current = Read(path);
        if (current.FileSha256 != campaign.FileSha256)
            throw new IOException("This saved treasure setting changed. Refresh the list before trying again.");

        string suffix = DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff", CultureInfo.InvariantCulture)
            + "-" + Guid.NewGuid().ToString("N");
        string backup = path + ".disabled-" + suffix;
        if (_isGameRunning()) throw new InvalidOperationException("Suikoden started. Close it before changing saved treasure settings.");
        File.Move(path, backup);
        if (Hash(File.ReadAllBytes(backup)) != campaign.FileSha256)
        {
            // A concurrent writer changed the source after its check. Preserve both versions if it also recreated the source.
            if (!File.Exists(path)) File.Move(backup, path);
            throw new IOException("The treasure setting changed during backup. Refresh the list before trying again.");
        }
        return backup;
    }

    private static TreasureCampaignInfo Read(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Linked treasure setting files cannot be reset.");
        string identity = Path.GetFileNameWithoutExtension(path);
        if (!IsDigest(identity)) throw new InvalidDataException("The treasure filename is not a saved snapshot identity.");
        var info = new FileInfo(path);
        if (info.Length > MaximumBindingBytes) throw new InvalidDataException("Treasure settings exceed the supported size.");
        byte[] bytes = File.ReadAllBytes(path);
        if (bytes.Length > MaximumBindingBytes) throw new InvalidDataException("Treasure settings exceed the supported size.");
        using var json = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 5 });
        JsonElement root = json.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("The treasure setting must contain an object.");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in root.EnumerateObject())
            if (!names.Add(property.Name)) throw new InvalidDataException("The treasure setting contains duplicate fields.");
        if (root.GetProperty("treasureBindingVersion").GetInt32() != 1
            || root.GetProperty("saveIdentity").GetString() != identity)
            throw new InvalidDataException("The treasure setting has an unsupported version or snapshot identity.");
        string campaign = root.GetProperty("campaignFingerprint").GetString() ?? "";
        string seed = root.GetProperty("seed").GetString() ?? "";
        bool enabled = root.GetProperty("enabled").GetBoolean();
        string? profile = root.GetProperty("profileFingerprint").GetString();
        if (!IsDigest(campaign) || string.IsNullOrWhiteSpace(seed) || seed.Length > 128 || seed.Any(char.IsControl)
            || (enabled ? !IsDigest(profile) : profile is not null))
            throw new InvalidDataException("The saved treasure campaign metadata is invalid.");
        return new TreasureCampaignInfo(Path.GetFileName(path), identity, campaign, seed, enabled, profile,
            info.LastWriteTimeUtc, Hash(bytes));
    }

    private static bool IsDigest(string? value) => value is { Length: 64 }
        && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static void RequireOrdinaryDirectory(string folder)
    {
        for (DirectoryInfo? current = new DirectoryInfo(folder); current is not null; current = current.Parent)
            if ((current.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Linked game folders cannot be used to reset saved treasure settings.");
    }

    private static bool GameIsRunning()
    {
        Process[] processes = Process.GetProcessesByName("Suikoden I and II HD Remaster");
        try { return processes.Length != 0; }
        finally { foreach (var process in processes) process.Dispose(); }
    }
}
