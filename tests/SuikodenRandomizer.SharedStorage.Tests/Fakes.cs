using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Reflection;

namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.All)] internal sealed class HarmonyPatch : Attribute
    { internal HarmonyPatch(Type target, string name, Type[] args) { } }
    [AttributeUsage(AttributeTargets.All)] internal sealed class HarmonyPrefix : Attribute { }
    [AttributeUsage(AttributeTargets.All)] internal sealed class HarmonyPostfix : Attribute { }
    internal sealed class Harmony
    {
        internal Harmony(string id) => throw new NotSupportedException("Tests never install runtime patches.");
        internal void Patch(MethodBase target, HarmonyMethod? prefix=null) => throw new NotSupportedException();
        internal static Patches? GetPatchInfo(MethodBase target) => throw new NotSupportedException();
    }
    internal sealed class HarmonyMethod { internal HarmonyMethod(MethodInfo method) { } internal int priority; }
    internal static class Priority { internal const int First=800; }
    internal sealed class Patches { internal List<Patch> Prefixes=new(); }
    internal sealed class Patch { internal string owner=""; internal MethodInfo PatchMethod=null!; }
}
namespace Il2CppSystem { internal delegate void Action<T>(T value); }
internal static class SystemSave
{
    internal enum DataSize { Default }
    internal static bool Save(string path, string json, DataSize size, Il2CppSystem.Action<bool> callback, bool flag)
        => throw new NotSupportedException("Tests never execute native storage.");
}
internal static class Owner
{
    [ThreadStatic] internal static string? Game;
    [ThreadStatic] internal static long Epoch;
}
namespace SuikodenRandomizer.AvatarPlugin
{
    internal sealed class AvatarRuntime
    {
        internal readonly long Epoch = Owner.Epoch;
        internal readonly ConcurrentQueue<string> Traces = new();
        internal bool MatchesExecution(long epoch) => Owner.Game == "GSD1" && Epoch == epoch && epoch == Owner.Epoch;
        internal void Trace(string text) => Traces.Enqueue(text);
    }
}
namespace SuikodenRandomizer.Suikoden2Plugin
{
    internal sealed class Gsd2Runtime
    {
        internal static Gsd2Runtime? Current;
        internal static Func<bool> ExecutionAllowed = () => Owner.Game == "GSD2";
        internal static Func<bool> MayOwnSaveContext = () => Owner.Game == "GSD2";
        internal static Func<long> OwnershipEpoch = () => Owner.Epoch;
        internal readonly long Epoch = Owner.Epoch;
        internal readonly ConcurrentQueue<string> Bindings = new(), Traces = new(), Faults = new();
        internal bool ThrowBind;
        internal bool BindReturnsFalse;
        internal Action? DuringBind;
        internal int CampBindings;
        internal bool MatchesExecution(long epoch) => Owner.Game == "GSD2" && Epoch == epoch && epoch == Owner.Epoch;
        internal bool BindSnapshot(string json)
        {
            if (ThrowBind || !Faults.IsEmpty) throw new InvalidOperationException("injected S2 binding refusal");
            DuringBind?.Invoke();
            if (BindReturnsFalse) return false;
            Bindings.Enqueue(json);
            return true;
        }
        internal void MarkCampInitialSnapshotBound() => Interlocked.Increment(ref CampBindings);
        internal void Trace(string text) => Traces.Enqueue(text);
        internal void Fail(string text) => Faults.Enqueue(text);
    }
    internal static class Gsd2InitialSavePosition
    {
        internal static bool Reject;
        internal static void ValidateStorage(string json, Gsd2SaveScope scope)
        { if (Reject) throw new InvalidOperationException("injected initial-position validation refusal"); }
    }
    internal static class Gsd2CheckpointCapture
    {
        internal static readonly ConcurrentQueue<string> Exports = new();
        internal static bool Reject;
        internal static void Export(string json)
        { if (Reject) throw new InvalidOperationException("injected capture error"); Exports.Enqueue(json); }
    }
    internal static class Gsd2Plugin { internal static readonly TestLog PluginLog = new(); }
    internal sealed class TestLog
    {
        internal readonly ConcurrentQueue<string> Warnings = new();
        internal void LogWarning(string text) => Warnings.Enqueue(text);
    }
}
