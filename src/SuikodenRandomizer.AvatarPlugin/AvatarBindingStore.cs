using SuikodenRandomizer.Core.Avatars;
using SuikodenRandomizer.Serialization;
using System;
using System.IO;

namespace SuikodenRandomizer.AvatarPlugin;

internal static class AvatarBindingStore
{
    private static string Build => global::SuikodenRandomizer.Plugin.BuildCompatibility.GameAssemblySha256;
    private static string Folder => Path.Combine(AvatarRuntime.Folder, "save-bindings");
    internal static AvatarShuffleCampaignState? Read(string identity)
    {
        string path = PathFor(identity);
        if (!File.Exists(path)) return null;
        if (new FileInfo(path).Length > AvatarShuffleBindingJson.MaximumPayloadBytes) throw new InvalidDataException("Shuffle binding exceeds its size bound.");
        return AvatarShuffleBindingJson.Deserialize(File.ReadAllText(path), identity, Build);
    }

    internal static void CheckWritable()
    {
        Directory.CreateDirectory(Folder);
        string path = Path.Combine(Folder, Guid.NewGuid().ToString("N") + ".probe");
        using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        { file.WriteByte(0); file.Flush(true); }
        File.Delete(path);
    }

    internal static void Write(AvatarShuffleProfile profile, AvatarShuffleCampaignState state, string identity)
    {
        Directory.CreateDirectory(Folder);
        var existing = Read(identity);
        if (existing is not null)
        {
            if (existing.Profile?.Fingerprint != profile.Fingerprint)
                throw new InvalidDataException("Native snapshot already belongs to a different shuffle mapping.");
            return;
        }
        string json = AvatarShuffleBindingJson.Serialize(profile, state, identity, Build);
        string path = PathFor(identity), temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream))
            { writer.Write(json); writer.Flush(); stream.Flush(true); }
            File.Move(temporary, path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static string PathFor(string identity)
    {
        if (identity.Length != 64) throw new InvalidDataException("Invalid native save digest.");
        foreach (char value in identity)
            if (!(value >= '0' && value <= '9') && !(value >= 'a' && value <= 'f'))
                throw new InvalidDataException("Invalid native save digest.");
        return Path.Combine(Folder, identity + ".json");
    }
}
