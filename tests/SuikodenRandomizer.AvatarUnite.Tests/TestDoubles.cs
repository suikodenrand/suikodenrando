using System;
using System.Collections.Generic;
using System.Reflection;

namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class)] public sealed class HarmonyPatch : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] public sealed class HarmonyTargetMethods : Attribute { }
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
    internal sealed class TestProfile
    {
        internal string Fingerprint { get; set; } = "unite-managed-model";
        internal Dictionary<int, int> Assignments { get; } = new Dictionary<int, int>();
        internal int GetDonor(int id) => Assignments.TryGetValue(id, out int donor) ? donor : id;
    }
    internal sealed class AvatarRuntime
    {
        internal static AvatarRuntime? Current { get; set; }
        internal bool Active { get; set; } = true;
        internal bool IsActive => Active && FaultReason is null;
        internal TestProfile Profile { get; } = new TestProfile();
        internal string? FaultReason { get; private set; }
        internal List<string> Logs { get; } = new List<string>();
        internal void Trace(string message) => Logs.Add(message);
        internal void Fail(string reason) { FaultReason = reason; Logs.Add(reason); }
    }
}
