using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using SuikodenRandomizer.Shared;

namespace StarsOfDestiny.Launcher.Core;

internal static class CtbEnemyAssets
{
    internal static void Install(string source, string destination, bool alreadyActive)
    {
        if (alreadyActive && HasContents(destination))
        {
            Verify(destination);
            return;
        }

        // The public preview reads portraits from the running game. Pre-extracted
        // portraits from an earlier local build remain optional; verify any supplied set.
        if (!HasContents(source))
        {
            if (HasContents(destination)) Verify(destination);
            return;
        }
        string indexFile = Path.Combine(source, "index.json");
        if (!File.Exists(indexFile))
            throw new InvalidDataException("The optional CTB enemy portrait folder has no index.json.");
        // Publish only after checking the exact index and files that the renderer will see.
        string parent = Path.GetDirectoryName(destination)!;
        Directory.CreateDirectory(parent);
        string staging = Path.Combine(parent, Guid.NewGuid().ToString("N") + ".pending");
        Directory.CreateDirectory(staging);
        try
        {
            File.Copy(indexFile, Path.Combine(staging, "index.json"));
            var expected = CtbPortraitIndex.Read(staging);
            foreach (string file in expected.Files.Keys)
            {
                string from = Path.Combine(source, file);
                if (!File.Exists(from)) throw new InvalidDataException("Missing CTB enemy portrait " + file + ".");
                File.Copy(from, Path.Combine(staging, file));
            }
            VerifyFiles(staging, expected);
            if (!HasContents(destination))
            {
                // Non-recursive deletion can only remove an empty directory. Concurrent
                // activations may already have removed it or published the complete set.
                try
                {
                    if ((File.GetAttributes(destination) & FileAttributes.ReparsePoint) != 0)
                        throw new InvalidDataException("Cannot replace a linked CTB enemy portrait directory.");
                    Directory.Delete(destination);
                }
                catch (FileNotFoundException) { }
                catch (DirectoryNotFoundException) { }
                catch (IOException) when (HasContents(destination)) { }
                try { Directory.Move(staging, destination); return; }
                catch (IOException) when (HasContents(destination)) { }
            }
            var existing = Verify(destination);
            if (expected.Files.Count != existing.Files.Count || expected.Aliases.Count != existing.Aliases.Count
                || expected.Files.Any(entry => !existing.Files.TryGetValue(entry.Key, out var hash) || entry.Value != hash)
                || expected.Aliases.Any(entry => !existing.Aliases.TryGetValue(entry.Key, out var portrait)
                    || entry.Value.Sha256 != portrait.Sha256))
                throw new InvalidDataException("Existing CTB enemy portraits conflict with the preview being activated.");
        }
        finally
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
        }
    }

    private static bool HasContents(string directory)
    {
        try { return Directory.EnumerateFileSystemEntries(directory).Any(); }
        catch (DirectoryNotFoundException) { return false; }
    }

    private static CtbPortraitIndex Verify(string directory)
    {
        var index = CtbPortraitIndex.Read(directory);
        VerifyFiles(directory, index);
        return index;
    }

    private static void VerifyFiles(string directory, CtbPortraitIndex index)
    {
        foreach (var entry in index.Files)
        {
            string path = Path.Combine(directory, entry.Key);
            if (!File.Exists(path)) throw new InvalidDataException("Missing CTB enemy portrait " + entry.Key + ".");
            using var portrait = File.OpenRead(path);
            if (Convert.ToHexString(SHA256.HashData(portrait)) != entry.Value)
                throw new InvalidDataException("CTB enemy portrait differs from the index: " + entry.Key);
        }
    }
}
