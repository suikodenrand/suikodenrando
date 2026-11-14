using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BepInEx;

namespace SuikodenRandomizer.Suikoden2Plugin;

// Receives unmodified JSON passed to SystemSave.Save by GSD2SaveData.SaveFile.
// The hook initiates no save; native automatic saves may also reach this entry.
// A capture is evidence to review, never an admitted start.
internal static class Gsd2CheckpointCapture
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly object Sync = new();
    private static BuildReceipt? _build;

    internal static void Export(string json)
    {
        if (!Gsd2Plugin.NativeCheckpointCapture || Gsd2Runtime.Current is not null) return;
        try
        {
            if (json is null) throw new ArgumentNullException(nameof(json));
            string directory = Gsd2Plugin.NativeCheckpointExportDirectory;
            if (!Path.IsPathFullyQualified(directory))
                throw new InvalidOperationException("Checkpoint export requires an absolute folder.");

            // Encode the exact native string without normalizing or deserializing it.
            byte[] bytes = Utf8.GetBytes(json);
            string jsonHash = Hash(bytes);
            lock (Sync)
            {
                BuildReceipt build = _build ??= ReadBuild();
                Directory.CreateDirectory(directory);
                string stem = "gsd2-native-candidate-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffffZ")
                    + "-" + jsonHash[..12] + "-" + Guid.NewGuid().ToString("N")[..8];
                string jsonName = stem + ".json";
                string receiptName = stem + ".receipt.json";
                var receipt = new
                {
                    schemaVersion = 1,
                    status = "candidate-unverified",
                    game = "GSD2",
                    capturedAtUtc = DateTime.UtcNow.ToString("O"),
                    source = "GSD2SaveData.SaveFile -> SystemSave.Save json argument",
                    nativeCheckpointCapture = true,
                    avatarRuntimeActive = false,
                    nativeSaveOutcome = "not-observed-at-pre-storage-capture",
                    json = new { file = jsonName, sha256 = jsonHash, byteLength = bytes.Length, encoding = "utf-8-without-bom" },
                    build,
                    qualification = "Not a verified checkpoint. Review game state, checkpoint location, difficulty, takeover, and complete restoration before enabling an alternate start."
                };
                byte[] receiptBytes = Utf8.GetBytes(JsonSerializer.Serialize(receipt, new JsonSerializerOptions { WriteIndented = true }));
                WriteNew(Path.Combine(directory, jsonName), bytes);
                WriteNew(Path.Combine(directory, receiptName), receiptBytes);
                Gsd2Plugin.PluginLog.LogInfo("Exported unverified native Suikoden II checkpoint candidate: " + Path.Combine(directory, jsonName));
            }
        }
        catch (Exception exception)
        {
            // Diagnostic failure must never cancel or replace the original save.
            Gsd2Plugin.PluginLog.LogWarning("Native Suikoden II checkpoint export failed; the original save continues: " + exception.Message);
        }
    }

    private static BuildReceipt ReadBuild()
    {
        string root = Paths.GameRootPath;
        string assembly = Path.Combine(root, "GameAssembly.dll");
        string metadata = Path.Combine(root, "Suikoden I and II HD Remaster_Data", "il2cpp_data", "Metadata", "global-metadata.dat");
        return new BuildReceipt(HashFile(assembly), HashFile(metadata));
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
    }

    private static string Hash(byte[] bytes)
    {
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(bytes)).ToLowerInvariant();
    }

    private static void WriteNew(string path, byte[] bytes)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        stream.Write(bytes, 0, bytes.Length);
    }

    private sealed record BuildReceipt(string gameAssemblySha256, string globalMetadataSha256);
}
