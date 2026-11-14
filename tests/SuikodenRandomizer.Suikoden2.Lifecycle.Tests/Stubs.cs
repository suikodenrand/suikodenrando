using System.Reflection;

namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.All)] internal sealed class HarmonyPatch : Attribute
    {
        public HarmonyPatch() { }
        public HarmonyPatch(Type type, string name) { }
        public HarmonyPatch(Type type, string name, Type[] args) { }
    }
    [AttributeUsage(AttributeTargets.All)] internal sealed class HarmonyPrefix : Attribute { }
    [AttributeUsage(AttributeTargets.All)] internal sealed class HarmonyPostfix : Attribute { }
    [AttributeUsage(AttributeTargets.All)] internal sealed class HarmonyFinalizer : Attribute { }
    [AttributeUsage(AttributeTargets.All)] internal sealed class HarmonyTargetMethod : Attribute { }
    internal static class AccessTools
    {
        internal static MethodInfo? Method(Type type, string name) => type.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
    }
    internal sealed class Harmony
    {
        internal Harmony(string id) => throw new NotSupportedException("Tests never install runtime patches.");
        internal void Patch(MethodBase target, HarmonyMethod? prefix = null) => throw new NotSupportedException();
        internal static Patches? GetPatchInfo(MethodBase target) => throw new NotSupportedException();
    }
    internal sealed class HarmonyMethod
    {
        internal HarmonyMethod(MethodInfo method) { }
        internal int priority;
    }
    internal static class Priority { internal const int First = 800; }
    internal sealed class Patches { internal List<Patch> Prefixes = new(); }
    internal sealed class Patch { internal string owner = ""; internal MethodInfo PatchMethod = null!; }
}
namespace BepInEx { internal static class Paths { internal static string GameRootPath = ""; } }
namespace SuikodenRandomizer.Suikoden2Plugin
{
    internal sealed class Gsd2Runtime
    {
        internal static Gsd2Runtime? Current;
        internal static Func<bool> ExecutionAllowed = () => true;
        internal static Func<bool> MayOwnSaveContext = () => ExecutionAllowed();
        internal static Func<long> OwnershipEpoch = () => 0;
        internal long ExecutionEpoch = OwnershipEpoch();
        internal bool OwnsExecution => ExecutionAllowed() && ExecutionEpoch == OwnershipEpoch();
        internal bool MatchesExecution(long epoch) => OwnsExecution && ExecutionEpoch == epoch;
        internal bool ObserveOwnedBoundary() { if (!ExecutionAllowed()) return false; ExecutionEpoch = OwnershipEpoch(); return true; }
        internal bool HoldNativeUpdate = false;
        internal bool IsAdmitted = true;
        internal bool HasAdmittedCampaignBeforeSuspend;
        internal bool HasCampaignSaveProtection => IsAdmitted || HasAdmittedCampaignBeforeSuspend;
        internal string? FaultReason;
        internal int BeginSaveCalls, EndSaveCalls, BindCalls;
        internal int CompletionCalls, Initializations;
        internal bool PendingNewGame, ThrowCompletion;
        internal bool ThrowBind;
        internal bool BindReturnsFalse;
        internal Action? DuringBind;
        internal string? LastBoundJson;
        internal readonly List<string> CallOrder = new();
        internal readonly List<bool> CompletionBoundaryModes = new();
        internal bool ThrowEndSave;
        internal Action? DuringBeginSave;
        internal bool NeedsCampInitialSavePosition;
        internal int CampBindings, MapObservations, PlayerObservations;
        internal int PrepareLoadCalls, CompleteLoadCalls;
        internal bool ThrowPrepareLoad;
        internal void BeginNewGame() { }
        internal void CancelPendingNewGame() { }
        internal void CompleteNativeNewGame(bool finalNativeBoundary = false)
        {
            CompletionCalls++; CallOrder.Add("complete-new-game");
            CompletionBoundaryModes.Add(finalNativeBoundary);
            if (ThrowCompletion) throw new InvalidOperationException("injected new-game completion failure");
            if (PendingNewGame) { PendingNewGame = false; Initializations++; }
        }
        internal void ObserveNativeDataset() { }
        internal void Fail(string message) => FaultReason = message;
        internal void BeginSave()
        {
            CallOrder.Add("begin-save");
            if (PendingNewGame) throw new InvalidOperationException("campaign is not initialized");
            BeginSaveCalls++;
            DuringBeginSave?.Invoke();
        }
        internal void EndSave() { EndSaveCalls++; if (ThrowEndSave) throw new InvalidOperationException("injected EndSave failure"); }
        internal void Trace(string message) { }
        internal bool BindSnapshot(string json)
        {
            BindCalls++; LastBoundJson = json; CallOrder.Add("bind-snapshot");
            if (ThrowBind) throw new IOException("injected binding failure");
            DuringBind?.Invoke();
            return !BindReturnsFalse;
        }
        internal void PrepareLoad(string json) { PrepareLoadCalls++; if (ThrowPrepareLoad) throw new InvalidOperationException("injected load rejection"); }
        internal void CompleteLoad(bool success) { CompleteLoadCalls++; }
        internal void MarkCampInitialSnapshotBound()
        {
            if (!NeedsCampInitialSavePosition) throw new InvalidOperationException("Camp initial snapshot already consumed");
            CampBindings++; NeedsCampInitialSavePosition = false;
        }
        internal void ObserveCampMap(MACHICON map) => MapObservations++;
        internal void VerifyCampPlayer(MACHICON map) => PlayerObservations++;
    }
    internal static class Gsd2CampStart { internal const int SavePx = 167, SavePy = 206; }
    internal static class Gsd2Plugin
    {
        internal static bool NativeCheckpointCapture;
        internal static string NativeCheckpointExportDirectory = "";
        internal static readonly TestLog PluginLog = new();
    }
    internal sealed class TestLog
    {
        internal readonly List<string> Warnings = new();
        internal void LogWarning(string message) => Warnings.Add(message);
        internal void LogInfo(string message) { }
    }
}
