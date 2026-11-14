using System;
using System.IO;

namespace SuikodenRandomizer.AvatarPlugin
{
    internal static class AvatarRuntime
    {
        internal static string Folder { get; } = Path.Combine(Path.GetTempPath(), "s1-treasure-persistence-" + Guid.NewGuid().ToString("N"));
    }
}

namespace SuikodenRandomizer.Plugin
{
    internal static class BuildCompatibility
    {
        internal const string GameAssemblySha256 = "test-game-build";
    }
}
