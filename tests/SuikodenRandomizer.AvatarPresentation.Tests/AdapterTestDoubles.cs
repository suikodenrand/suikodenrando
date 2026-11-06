extern alias GSD1;

using SuikodenRandomizer.Core.Avatars;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
    internal sealed class HarmonyPatch : Attribute
    {
        public string TargetName { get; }
        public HarmonyPatch(Type type, string name) { TargetName = name; }
        public HarmonyPatch(Type type, string name, Type[] parameters) { TargetName = name; }
    }
    [AttributeUsage(AttributeTargets.Method)] internal sealed class HarmonyPrefix : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] internal sealed class HarmonyPostfix : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] internal sealed class HarmonyFinalizer : Attribute { }
}

namespace SuikodenRandomizer.AvatarPlugin
{
    // Test runtime only: native cache publication and state admission are outside
    // this suite. The linked production presentation adapter is exercised directly.
    internal sealed class AvatarRuntime
    {
        internal static AvatarRuntime? Current { get; set; }
        internal bool IsActive { get; set; } = true;
        internal AvatarShuffleProfile Profile { get; }
        internal Dictionary<int, GSD1.PLAYER_DATA> Originals { get; } = new Dictionary<int, GSD1.PLAYER_DATA>();
        internal Dictionary<int, GSD1.PLAYER_DATA> ShuffledCache { get; } = new Dictionary<int, GSD1.PLAYER_DATA>();
        internal List<string> Traces { get; } = new List<string>();
        internal bool ObservedNameScope { get; private set; }
        internal bool ObservedFaceScope { get; private set; }
        internal int OriginalReads { get; private set; }

        internal AvatarRuntime(AvatarShuffleProfile profile) { Profile = profile; }
        internal int MapDisplayId(int canonicalId)
        {
            ObservedNameScope = AvatarPresentation.ResolvingNames;
            ObservedFaceScope = AvatarPresentation.ResolvingFaces;
            return IsActive && canonicalId >= 0 && canonicalId <= 109
                ? Profile.GetDonor(canonicalId) : canonicalId;
        }
        internal bool TryGetOriginal(int id, [NotNullWhen(true)] out GSD1.PLAYER_DATA? original)
        {
            OriginalReads++;
            if (Originals.TryGetValue(id, out var value)) { original = value; return true; }
            original = null; return false;
        }
        internal void Trace(string message) => Traces.Add(message);
    }
}
