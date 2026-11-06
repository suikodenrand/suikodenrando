using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace SuikodenRandomizer.Core;

public static class SeedGenerator
{
    /// <summary>
    /// Stable algorithm 2: ascending profile IDs, SHA-256 counter-stream random
    /// bytes, unbiased Fisher-Yates shuffle, deterministic fixed-point repair.
    /// The repair ensures a derangement; output is not claimed to be a uniform
    /// sample of every mathematical derangement.
    /// </summary>
    public static SeedManifest Generate(string seed) => Generate(seed, SeedProfiles.OptionalRecruitsV1);

    public static SeedManifest Generate(string seed, string profile)
    {
        if (string.IsNullOrWhiteSpace(seed) || seed.Length > 256)
            throw new ArgumentException("Seed must contain 1–256 UTF-16 characters and must not be whitespace.", nameof(seed));
        int[] sources = SeedProfiles.GetIds(profile).OrderBy(id => id).ToArray();
        int[] rewards = (int[])sources.Clone();
        using var random = new StableRandom(seed, profile);
        for (int index = rewards.Length - 1; index > 0; index--)
        {
            int selected = random.Next(index + 1);
            Swap(rewards, index, selected);
        }

        int[] fixedPoints = Enumerable.Range(0, sources.Length)
            .Where(index => sources[index] == rewards[index]).ToArray();
        if (fixedPoints.Length == 1)
        {
            int fixedIndex = fixedPoints[0];
            int otherIndex = random.Next(rewards.Length - 1);
            if (otherIndex >= fixedIndex) otherIndex++;
            Swap(rewards, fixedIndex, otherIndex);
        }
        else if (fixedPoints.Length > 1)
        {
            int firstReward = rewards[fixedPoints[0]];
            for (int index = 0; index < fixedPoints.Length - 1; index++)
                rewards[fixedPoints[index]] = rewards[fixedPoints[index + 1]];
            rewards[fixedPoints[fixedPoints.Length - 1]] = firstReward;
        }

        var manifest = new SeedManifest { Seed = seed, Profile = profile };
        if (SeedProfiles.IsExperimental(manifest))
        {
            manifest.StartingParty.Enabled = true;
            manifest.StartingParty.SourceIds = SeedProfiles.ExperimentalStarterIds.ToList();
        }
        for (int index = 0; index < sources.Length; index++)
            manifest.Mapping.Add(sources[index], rewards[index]);
        SeedValidator.ValidateAndThrow(manifest);
        return manifest;
    }

    private static void Swap(int[] values, int left, int right)
    {
        int temporary = values[left];
        values[left] = values[right];
        values[right] = temporary;
    }

    private sealed class StableRandom : IDisposable
    {
        private readonly SHA256 _hash = SHA256.Create();
        private readonly byte[] _input;
        private byte[] _block = Array.Empty<byte>();
        private int _position;
        private ulong _counter;

        public StableRandom(string seed, string profile)
        {
            // This domain separator, encoding and byte order are versioned.
            byte[] prefix = Encoding.UTF8.GetBytes(
                "SuikodenRandomizer\0algorithm=2\0profile=" + profile + "\0seed=" + seed);
            _input = new byte[prefix.Length + sizeof(ulong)];
            Buffer.BlockCopy(prefix, 0, _input, 0, prefix.Length);
        }

        public int Next(int exclusiveMaximum)
        {
            if (exclusiveMaximum <= 0) throw new ArgumentOutOfRangeException(nameof(exclusiveMaximum));
            uint bound = (uint)exclusiveMaximum;
            uint threshold = unchecked(0U - bound) % bound;
            uint value;
            do { value = NextUInt32(); } while (value < threshold);
            return (int)(value % bound);
        }

        private uint NextUInt32()
        {
            if (_position >= _block.Length)
            {
                for (int index = 0; index < sizeof(ulong); index++)
                    _input[_input.Length - sizeof(ulong) + index] = (byte)(_counter >> (8 * index));
                _counter++;
                _block = _hash.ComputeHash(_input);
                _position = 0;
            }
            uint result = (uint)(_block[_position]
                | (_block[_position + 1] << 8)
                | (_block[_position + 2] << 16)
                | (_block[_position + 3] << 24));
            _position += 4;
            return result;
        }

        public void Dispose() => _hash.Dispose();
    }
}
