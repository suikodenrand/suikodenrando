using System;
using System.Collections.Generic;
using System.Reflection;

namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class)] public sealed class HarmonyPatch : Attribute
    {
        public HarmonyPatch() { }
        public HarmonyPatch(Type type, string method) { Type = type; Method = method; }
        public Type? Type { get; }
        public string? Method { get; }
    }
    [AttributeUsage(AttributeTargets.Method)] public sealed class HarmonyTargetMethods : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] public sealed class HarmonyPrepare : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] public sealed class HarmonyPrefix : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] public sealed class HarmonyPostfix : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] public sealed class HarmonyFinalizer : Attribute { }
    public static class AccessTools
    {
        public static MethodInfo? Method(Type type, string name) => type.GetMethod(name);
    }
}

namespace SuikodenRandomizer.AvatarPlugin
{
    internal sealed class AvatarRuntime
    {
        internal static AvatarRuntime? Current { get; set; }
        internal bool Active { get; set; } = true;
        internal bool Owns { get; set; } = true;
        internal long ExecutionEpoch { get; set; } = 1;
        internal bool MatchesExecution(long epoch) => Owns && epoch == ExecutionEpoch;
        internal bool IsActive => Active && FaultReason is null;
        internal string CampaignFingerprint => "all-shuffled-unites-linked-managed-fixture";
        internal Dictionary<int, int> Mapping { get; } = new Dictionary<int, int>();
        internal string? FaultReason { get; private set; }
        internal List<string> Logs { get; } = new List<string>();
        internal int DisplayReads { get; set; }
        internal int MapDisplayId(int id) { DisplayReads++; return Mapping.TryGetValue(id, out int donor) ? donor : id; }
        internal void Trace(string message) => Logs.Add(message);
        internal void Fail(string reason) { FaultReason = reason; Logs.Add(reason); }
    }
}

namespace SuikodenRandomizer.S1BattlePreview
{
    // The production adapter finds this optional type by its actual full name.
    internal static class BattlePreviewPlugin
    {
        internal static bool ActiveAtbEnabled { get; set; }
        internal static bool OneShotAttackEnabled { get; set; }
    }
}

namespace SuikodenRandomizer.AvatarPlugin
{
    internal static class AvatarTalismanPreview
    {
        internal static void Configure(bool enabled) { }
    }
}
