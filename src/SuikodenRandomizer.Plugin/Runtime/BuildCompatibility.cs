using BepInEx;
using System;
using System.IO;
using System.Security.Cryptography;

namespace SuikodenRandomizer.Plugin;

internal static class BuildCompatibility
{
    public const string GameAssemblySha256 = "e093f175eaf3734d293ca0a236c652dd7fcc72ea5fa10d7b21103edf12f11c3a";

    public static void Verify()
    {
        string path = Path.Combine(Paths.GameRootPath, "GameAssembly.dll");
        using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        string actual = Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
        if (!string.Equals(actual, GameAssemblySha256, StringComparison.Ordinal))
            throw new InvalidOperationException($"Unsupported GameAssembly.dll build. Expected SHA256 {GameAssemblySha256}; found {actual}. The plugin has not applied any patches.");
    }
}
