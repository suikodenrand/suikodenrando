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
        var random = new StableRandom(normalized);
        for (int index = rewards.Length - 1; index > 0; index--)
        {
            int other = random.Next(index + 1);
            int value = rewards[index];
            rewards[index] = rewards[other];
            rewards[other] = value;
        }

        var assignments = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var chest in S1TreasureCatalog.Chests) assignments.Add(chest.LocationId, chest.OriginalItemId);
        for (int index = 0; index < eligible.Length; index++) assignments[eligible[index].LocationId] = rewards[index];
        return new TreasureShuffleProfile(normalized, assignments);
    }

    // A separate seed domain makes treasure independent of character-shuffle draws.
    private sealed class StableRandom
    {
        private ulong _state;
        internal StableRandom(string seed)
        {
            using var sha = SHA256.Create();
            byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(AlgorithmId + "\n" + seed));
            for (int index = 0; index < 8; index++) _state |= (ulong)hash[index] << (8 * index);
        }

        private ulong NextUInt64()
        {
            unchecked
            {
                ulong value = (_state += 0x9e3779b97f4a7c15UL);
                value = (value ^ (value >> 30)) * 0xbf58476d1ce4e5b9UL;
                value = (value ^ (value >> 27)) * 0x94d049bb133111ebUL;
                return value ^ (value >> 31);
            }
        }

        internal int Next(int exclusiveMaximum)
        {
            ulong bound = (ulong)exclusiveMaximum;
            ulong threshold = unchecked(0UL - bound) % bound;
            ulong value;
            do { value = NextUInt64(); } while (value < threshold);
            return (int)(value % bound);
        }
    }
}
