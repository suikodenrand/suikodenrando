using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace SuikodenRandomizer.Core.Treasure;

/// <summary>A complete immutable shuffle of reviewed S1 chest item rewards.</summary>
public sealed class TreasureShuffleProfile
{
    public const int CurrentVersion = 1;
    public const string AlgorithmId = "s1-sha256-splitmix64-fisher-yates-v1";
    private readonly IReadOnlyDictionary<string, int> _assignments;

    private TreasureShuffleProfile(string seed, Dictionary<string, int> assignments)
    {
        Seed = seed;
        _assignments = new ReadOnlyDictionary<string, int>(assignments);
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
        {
            writer.Write(CurrentVersion);
            writer.Write(AlgorithmId);
            writer.Write(Seed);
            writer.Write(CatalogDigest);
            writer.Write(S1TreasureCatalog.Chests.Count);
            foreach (var chest in S1TreasureCatalog.Chests)
            {
                writer.Write(chest.LocationId);
                writer.Write(assignments[chest.LocationId]);
            }
        }
        using var sha = SHA256.Create();
        Fingerprint = S1TreasureCatalog.Hex(sha.ComputeHash(stream.ToArray()));
    }

    public string Seed { get; }
    public string Fingerprint { get; }
    public string CatalogDigest => S1TreasureCatalog.CatalogDigest;
    public IReadOnlyDictionary<string, int> Assignments => _assignments;
    public bool TryGetReward(string locationId, out int itemId) => _assignments.TryGetValue(locationId, out itemId);

    public static TreasureShuffleProfile Create(string seed)
    {
        if (seed is null) throw new ArgumentNullException(nameof(seed));
        if (seed.Length > 128) throw new ArgumentException("Seed exceeds 128 characters.", nameof(seed));
        string normalized = seed.Trim().Normalize(NormalizationForm.FormC);
        if (normalized.Length == 0 || normalized.Length > 128 || normalized.Any(char.IsControl))
            throw new ArgumentException("Seed must contain 1 to 128 characters without control characters.", nameof(seed));

        var eligible = S1TreasureCatalog.Chests.Where(chest => chest.IsShuffleEligible).ToArray();
        int[] rewards = eligible.Select(chest => chest.OriginalItemId).ToArray();
        var random = new SeededShuffle(AlgorithmId, normalized);
        random.Shuffle(rewards);

        var assignments = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var chest in S1TreasureCatalog.Chests) assignments.Add(chest.LocationId, chest.OriginalItemId);
        for (int index = 0; index < eligible.Length; index++) assignments[eligible[index].LocationId] = rewards[index];
        return new TreasureShuffleProfile(normalized, assignments);
    }
}
