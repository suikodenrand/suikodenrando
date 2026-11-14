using System;
using System.Collections.Generic;
using System.IO;

namespace BepInEx { public static class Paths { public static string ConfigPath { get; set; } = Path.Combine(Directory.GetCurrentDirectory(), "work", "suikoden2", "test-state", "runtime-" + Guid.NewGuid().ToString("N")); } }
namespace SuikodenRandomizer.Plugin { internal static class BuildCompatibility { internal const string GameAssemblySha256 = "e093f175eaf3734d293ca0a236c652dd7fcc72ea5fa10d7b21103edf12f11c3a"; } }
namespace SuikodenRandomizer.Suikoden2Plugin
{
    internal sealed class TestLog
    {
        internal List<string> Lines { get; } = new List<string>();
        internal void LogInfo(object message) => Lines.Add(message.ToString()!);
        internal void LogError(object message) => Lines.Add(message.ToString()!);
    }
    internal static class Gsd2Plugin
    {
        internal static TestLog PluginLog { get; } = new TestLog();
        internal static string Seed { get; set; } = "runtime-seed";
        internal static string Mode { get; set; } = "Shuffle";
        internal static bool IncludeProtagonist { get; set; }
        internal static string Companions { get; set; } = "2,3,4,5,6";
        internal static string StartPoint { get; set; } = "Opening";
        internal static string JoiningWeaponLevel { get; set; } = "RiouVariance";
        internal static string JoiningEquipment { get; set; } = "Donor";
    }
    internal static class Gsd2Graphics
    {
        internal static bool IsReady { get; set; }
        internal static void Reset() { IsReady = false; }
        internal static int SuspendCalls;
        internal static void Suspend() { IsReady = false; SuspendCalls++; }
    }
    // The actual transaction and native read guards have separate linked tests.
    // These collaborators test the runtime's admission and projection ordering.
    internal sealed class Gsd2CampStart
    {
        internal const string Point = "ViktorCamp";
        internal static Action<GAME_WORK>? BeforeApply;
        internal static int Applications;
        private bool _armed;
        internal bool IsApplied { get; private set; }
        internal void Arm(bool requested) { _armed = requested; IsApplied = false; }
        internal void Cancel() { _armed = false; IsApplied = false; }
        internal bool ApplyAtNativeBoundary(GAME_WORK game)
        {
            if (!_armed) return false;
            _armed = false; BeforeApply?.Invoke(game); Applications++; IsApplied = true; return true;
        }
    }
    internal static class Gsd2CampNativeGuard
    {
        internal static bool PlayerReady = true, RejectFresh, RejectMap, RejectPlayer;
        internal static int FreshChecks, MapChecks, PlayerChecks;
        internal static void ValidateFresh(GAME_WORK game) { FreshChecks++; if (RejectFresh) throw new InvalidOperationException("injected existing field"); }
        internal static void ValidateInitialMap(MACHICON map) { MapChecks++; if (RejectMap) throw new InvalidOperationException("injected map mismatch"); }
        internal static bool TryVerifyPlayer(MACHICON map) { PlayerChecks++; if (RejectPlayer) throw new InvalidOperationException("injected player mismatch"); return PlayerReady; }
    }
    internal static class Gsd2RejectedSaveMirrorGuard
    {
        internal const string Status = "Managed-test mirror protection collaborator";
        internal static int EnsureCalls;
        internal static bool RejectEnsure;
        internal static void Ensure() { EnsureCalls++; if (RejectEnsure) throw new InvalidOperationException("injected unprotected Fix mirror"); }
    }
}
