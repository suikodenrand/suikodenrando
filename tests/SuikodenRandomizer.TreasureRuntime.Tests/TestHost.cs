using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using SuikodenRandomizer.Core.Treasure;

namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class HarmonyPatch : Attribute
    {
        public HarmonyPatch(Type type, string name) { }
    }
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class HarmonyPrefix : Attribute { }
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class HarmonyFinalizer : Attribute { }
}

namespace SuikodenRandomizer.AvatarPlugin
{
    internal sealed class AvatarRuntime
    {
        internal static AvatarRuntime? Current;
        internal bool Enabled = true;
        internal TreasureShuffleProfile? TreasureProfile;
        internal bool TreasureActive => Enabled && FaultReason is null && TreasureProfile is not null;
        internal readonly List<string> Messages = new();
        internal string? FaultReason;
        internal void Trace(string message) => Messages.Add(message);
        internal void Fail(string reason) => FaultReason ??= reason;
    }

    internal static class AvatarNativeData
    {
        internal static Il2CppStructArray<byte> CloneBytes(Il2CppStructArray<byte> source)
        {
            var copy = new Il2CppStructArray<byte>(source.Count);
            for (int index = 0; index < source.Count; index++) copy[index] = source[index];
            return copy;
        }
    }
}
