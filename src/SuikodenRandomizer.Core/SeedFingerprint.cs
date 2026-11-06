using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace SuikodenRandomizer.Core;

public static class SeedFingerprint
{
    /// <summary>
    /// SHA-256 of the canonical seed contract, excluding diagnostic verbosity.
    /// BinaryWriter's little-endian integers and UTF-8 length-prefixed strings
    /// make dictionary insertion order and JSON formatting irrelevant.
    /// </summary>
    public static string Compute(SeedManifest manifest)
    {
        if (manifest is null) throw new ArgumentNullException(nameof(manifest));
        SeedValidator.ValidateAndThrow(manifest);
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write("SuikodenRandomizer.SeedFingerprint.v1");
            writer.Write(manifest.SchemaVersion);
            writer.Write(manifest.AlgorithmVersion);
            writer.Write(manifest.Game);
            writer.Write(manifest.Profile);
            writer.Write(manifest.Seed);
            writer.Write(manifest.Mapping.Count);
            foreach (var pair in manifest.Mapping.OrderBy(pair => pair.Key))
            {
                writer.Write(pair.Key);
                writer.Write(pair.Value);
            }
        }
        using var sha = SHA256.Create();
        return string.Concat(sha.ComputeHash(stream.ToArray()).Select(value => value.ToString("x2")));
    }
}
