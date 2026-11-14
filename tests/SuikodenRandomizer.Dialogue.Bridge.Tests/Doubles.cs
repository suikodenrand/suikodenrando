using HarmonyLib;
using SuikodenRandomizer.Shared;

internal static class TextMasterData
{
    internal enum Lang { English, Japanese }
    internal static Lang CurrentLanguage { get; set; }
}

namespace BepInEx.Configuration
{
    internal sealed class ConfigEntry<T> { internal T Value { get; set; } public ConfigEntry(T value) => Value = value; }
    internal sealed class ConfigFile
    {
        private readonly Dictionary<(string, string), object> _entries = new();
        internal ConfigEntry<T> Bind<T>(string section, string key, T value, string description)
        {
            if (_entries.TryGetValue((section, key), out var existing)) return (ConfigEntry<T>)existing;
            var entry = new ConfigEntry<T>(value); _entries.Add((section, key), entry); return entry;
        }
    }
}

namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class)]
    internal sealed class HarmonyPatch : Attribute { public HarmonyPatch(Type type, string name, Type[] parameters) { } }
    [AttributeUsage(AttributeTargets.Method)] internal sealed class HarmonyPostfix : Attribute { }
    internal sealed class Harmony
    {
        internal static readonly List<string> Unpatched = new();
        internal string Id { get; }
        internal Harmony(string id) => Id = id;
        internal void UnpatchSelf() => Unpatched.Add(Id);
    }
}

namespace SuikodenRandomizer.Unified
{
    internal static class UnifiedPlugin
    {
        internal const string PluginGuid = "agilen.starsofdestiny";
        internal static readonly ActiveGameRouter Router = new();
        internal static readonly TestLog HostLog = new();
        internal static readonly List<Type> Registered = new();
        internal static bool FailPatch;
        internal static GameOwnershipToken Observe() => Router.Capture();
        internal static void ApplyPatches(Harmony harmony, IEnumerable<Type> patches)
        {
            foreach (var type in patches)
            {
                Registered.Add(type);
                if (FailPatch) throw new InvalidOperationException("test detour rejection");
            }
        }
    }
    internal sealed class TestLog
    {
        internal readonly List<string> Messages = new();
        internal void LogInfo(string text) => Messages.Add(text);
        internal void LogWarning(string text) => Messages.Add(text);
        internal void LogError(string text) => Messages.Add(text);
    }
    internal static class DialogueS1 { internal static Type[] PatchTypes { get; } = { typeof(DialogueS1) }; }
    internal static class DialogueS2 { internal static Type[] PatchTypes { get; } = { typeof(DialogueS2) }; }
}
