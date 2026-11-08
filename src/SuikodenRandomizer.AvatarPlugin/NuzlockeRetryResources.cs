extern alias GSD1;

using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace SuikodenRandomizer.AvatarPlugin;

/// <summary>Restore the shared effect OPD released by defeat cleanup before retrying.</summary>
internal static class NuzlockeRetryResources
{
    private const int MaximumRetainedLoads = 8;
    private static readonly object Gate = new();
    private static readonly List<Job> RetainedCallbacks = new();
    private static readonly Il2CppSystem.Func<int, int> WaitStep =
        DelegateSupport.ConvertDelegate<Il2CppSystem.Func<int, int>>(new Func<int, int>(WaitForResources))
            ?? throw new InvalidOperationException("The native resource-wait delegate could not be created.");
    private static readonly Il2CppSystem.Func<int, int> BattleStep =
        DelegateSupport.ConvertDelegate<Il2CppSystem.Func<int, int>>(new Func<int, int>(GSD1.BattleBase.battle))
            ?? throw new InvalidOperationException("The native battle delegate could not be created.");
    private static long _generation;
    private static Job? _current;

    internal static void Begin(int step)
    {
        if (step != 0)
            throw new InvalidOperationException("The Nuzlocke resource retry supports only the top-level battle step (0).");
        var runtime = AvatarRuntime.Current ?? throw new InvalidOperationException("No active randomizer runtime.");
        if (!runtime.IsNuzlocke || !runtime.IsAdmitted || runtime.FaultReason is not null)
            throw new InvalidOperationException("No admitted Nuzlocke campaign can retry this battle.");
        var game = GSD1.OldSrcBase.game_work ?? throw new InvalidOperationException("No native campaign for battle retry.");
        var manager = GSD1.GlobalWork.Instance?.battle_manager
            ?? throw new InvalidOperationException("No native battle resource manager.");
        PruneCompletedCallbacks();
        lock (Gate)
        {
            if (_current is not null)
                throw new InvalidOperationException("A Nuzlocke battle resource retry is already active.");
            if (RetainedCallbacks.Count >= MaximumRetainedLoads)
                throw new InvalidOperationException("Previous native resource loads are still pending. Restart the game before another retry.");
        }

        var job = new Job(++_generation, runtime, game.Pointer, manager);
        job.State = new NuzlockeRetryResourceGate(
            complete => StartLoad(job, complete),
            () => ResourcesReady(job),
            () => ResumeBattle(job),
            error => Fault(job, error),
            () => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency);
        _current = job;
        CrashDiagnostics.Breadcrumb("nuz.retry.resources.wait.scheduled", "generation=" + job.Generation);
        GSD1.BattleBase.NextGameStep(WaitStep, 0, "Nuzlocke shared battle resource reload");
    }

    internal static void Reset()
    {
        ++_generation;
        var job = _current;
        _current = null;
        if (job is not null)
        {
            job.State.Cancel();
            CrashDiagnostics.Breadcrumb("nuz.retry.resources.cancelled", "generation=" + job.Generation);
        }
        // A cancelled native coroutine may still invoke its Action. Keep its
        // delegates rooted until it has returned a completion notification.
        PruneCompletedCallbacks();
    }

    private static int WaitForResources(int step)
    {
        var job = _current;
        if (job is null) return -1;
        try
        {
            PruneCompletedCallbacks();
            if (!IsCurrent(job)) { Reset(); return -1; }
            if (step != 0)
                throw new InvalidOperationException("The native resource-wait callback moved outside battle step 0.");
            if (GSD1.OldSrcBase.game_work?.Pointer != job.Game
                || GSD1.GlobalWork.Instance?.battle_manager?.Pointer != job.Manager.Pointer)
                throw new InvalidOperationException("The native campaign or battle resource manager changed during retry.");
            job.State.Poll();
        }
        catch (Exception error) { Fault(job, error); }
        return -1;
    }

    private static void StartLoad(Job job, Action complete)
    {
        if (!IsCurrent(job)) throw new InvalidOperationException("The Nuzlocke retry was cancelled before loading resources.");
        job.ManagedCallback = () =>
        {
            try
            {
                CrashDiagnostics.Breadcrumb("nuz.retry.resources.callback", "generation=" + job.Generation
                    + "; current=" + IsCurrent(job));
                complete();
            }
            finally { Volatile.Write(ref job.CallbackArrived, 1); }
        };
        job.NativeCallback = DelegateSupport.ConvertDelegate<Il2CppSystem.Action>(job.ManagedCallback)
            ?? throw new InvalidOperationException("The native resource completion delegate could not be created.");
        lock (Gate) RetainedCallbacks.Add(job);
        CrashDiagnostics.Breadcrumb("nuz.retry.resources.load.before", "generation=" + job.Generation + "; sharedOPD=0");
        // This API starts the native coroutine on the game's persistent
        // SystemObject host and calls back after assigning requested OPD slots.
        job.Manager.LoadBattleDatas(new Il2CppStructArray<int>(new[] { 0 }), job.NativeCallback);
        CrashDiagnostics.Breadcrumb("nuz.retry.resources.load.started", "generation=" + job.Generation);
    }

    private static bool ResourcesReady(Job job)
    {
        if (!IsCurrent(job)) return false;
        var shared = job.Manager.GetBattleData(0);
        bool ready = shared is not null && shared.Pointer != IntPtr.Zero && shared.anime_table is not null
            && shared.anime_table.Count > 0;
        CrashDiagnostics.Breadcrumb("nuz.retry.resources.verified", "generation=" + job.Generation + "; ready=" + ready
            + "; sharedOPD=0x" + (shared?.Pointer.ToString("X") ?? "0"));
        return ready;
    }

    private static void ResumeBattle(Job job)
    {
        if (!IsCurrent(job)) throw new InvalidOperationException("The Nuzlocke retry became stale before resuming.");
        CrashDiagnostics.Breadcrumb("nuz.retry.resources.resume.before", "generation=" + job.Generation);
        GSD1.BattleBase.NextGameStep(BattleStep, 0, "Nuzlocke reserve team retry after resource reload");
        _current = null;
        CrashDiagnostics.Breadcrumb("nuz.retry.resources.resume.after", "generation=" + job.Generation);
        PruneCompletedCallbacks();
    }

    private static bool IsCurrent(Job job) => ReferenceEquals(_current, job) && job.Generation == _generation
        && ReferenceEquals(AvatarRuntime.Current, job.Runtime) && job.Runtime.IsNuzlocke
        && job.Runtime.IsAdmitted && job.Runtime.FaultReason is null;

    private static void Fault(Job job, Exception error)
    {
        CrashDiagnostics.Exception("Nuzlocke shared battle resource reload", error);
        job.Runtime.Fail("Nuzlocke battle resources could not be restored: " + error.Message);
    }

    private static void PruneCompletedCallbacks()
    {
        lock (Gate) RetainedCallbacks.RemoveAll(job => Volatile.Read(ref job.CallbackArrived) != 0);
    }

    private sealed class Job
    {
        internal readonly long Generation;
        internal readonly AvatarRuntime Runtime;
        internal readonly IntPtr Game;
        internal readonly GSD1.BattleManager Manager;
        internal NuzlockeRetryResourceGate State = null!;
        internal Action? ManagedCallback;
        internal Il2CppSystem.Action? NativeCallback;
        internal int CallbackArrived;
        internal Job(long generation, AvatarRuntime runtime, IntPtr game, GSD1.BattleManager manager)
        { Generation = generation; Runtime = runtime; Game = game; Manager = manager; }
    }
}
