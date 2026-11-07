extern alias GSD1;
using System;
using System.IO;
using System.Security.Cryptography;

namespace SuikodenRandomizer.Plugin
{
    internal static class BuildCompatibility { internal static string GameAssemblySha256 => new string('b',64); }
}

namespace SuikodenRandomizer.AvatarPlugin
{
    internal static class AvatarRuntime
    {
        internal static string Folder { get; } = Path.Combine(Path.GetTempPath(), "srr-nuzlocke-binding-tests-" + Guid.NewGuid().ToString("N"));
    }
    internal static class FixedRosterBindingStore
    {
        internal static string ComputeIdentity(GSD1.SaveDataManager.SaveData save) => save.Identity;
    }
    internal static class AvatarSaveIdentity
    {
        internal static string Hash(byte[] value) => Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();
    }
    // Persistence tests model the snapshot's validation contract. Production
    // snapshot/native reconstruction is verified by the runtime and metadata suites.
    internal sealed class FixedRosterBaseSnapshot
    {
        public int CharacterId { get; set; }
        public byte Level { get; set; } = 1;
        public ushort Hp { get; set; } = 50;
        public ushort MaxHp { get; set; } = 50;
        public string Payload { get; set; } = "";
        internal void ValidateState()
        {
            if (Level == 0 || MaxHp == 0) throw new InvalidDataException("Invalid model fighter state.");
        }
    }
}
