using System;

namespace SuikodenRandomizer.S1BattlePreview;

internal enum BattleSampleKind { NewBattle, Continued, Duplicate }

internal struct BattleSampleClock
{
    private const double FreshnessSeconds = 0.5;
    private int _frame;
    private double _sampleTime;
    private bool _running;

    internal IntPtr Battle { get; private set; }

    internal bool IsFresh(double now) => Battle != IntPtr.Zero
        && now >= _sampleTime && now - _sampleTime <= FreshnessSeconds;

    internal BattleSampleKind Sample(IntPtr battle, int frame, double now, bool running, out double elapsed)
    {
        elapsed = 0;
        bool sameBattle = Battle == battle && battle != IntPtr.Zero;
        if (sameBattle && _frame == frame) return BattleSampleKind.Duplicate;
        if (sameBattle && _running && running && IsFresh(now))
            elapsed = now - _sampleTime;
        Battle = battle;
        _frame = frame;
        _sampleTime = now;
        _running = running;
        return sameBattle ? BattleSampleKind.Continued : BattleSampleKind.NewBattle;
    }

    internal void Reset() => this = default;
}
