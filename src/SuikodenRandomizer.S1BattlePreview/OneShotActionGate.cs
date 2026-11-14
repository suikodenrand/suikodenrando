using System;

namespace SuikodenRandomizer.S1BattlePreview;

internal enum ActionGateDecision { PassThrough, Armed, Wait, Release, TimedOut, ActorChanged, Ineligible }

internal struct OneShotActionGate
{
    private const double TimeoutSeconds = 15;
    private int _slot;
    private double _armedAt;
    private double _secondsToFull;

    internal bool Consumed { get; private set; }
    internal int WaitingSlot => Consumed ? 0 : _slot;
    internal double Gauge { get; private set; }
    internal double ElapsedSeconds { get; private set; }
    internal bool IsWaiting(double now) => WaitingSlot > 0 && now - _armedAt < TimeoutSeconds;

    internal ActionGateDecision Evaluate(int slot, bool eligible, double secondsToFull, double now)
    {
        if (Consumed) return ActionGateDecision.PassThrough;
        if (_slot == 0)
        {
            if (slot <= 0 || !eligible || !double.IsFinite(secondsToFull) || secondsToFull <= 0)
                return ActionGateDecision.PassThrough;
            _slot = slot;
            _armedAt = now;
            _secondsToFull = secondsToFull;
            return ActionGateDecision.Armed;
        }

        ElapsedSeconds = now - _armedAt;
        if (slot != _slot) return Consume(ActionGateDecision.ActorChanged);
        if (!eligible) return Consume(ActionGateDecision.Ineligible);
        if (ElapsedSeconds >= TimeoutSeconds) return Consume(ActionGateDecision.TimedOut);

        // The native handoff keeps the charge current independently of the HUD samples.
        Gauge = Math.Clamp(ElapsedSeconds / _secondsToFull, 0, 1);
        return Gauge >= 1 ? Consume(ActionGateDecision.Release) : ActionGateDecision.Wait;
    }

    private ActionGateDecision Consume(ActionGateDecision decision)
    {
        Consumed = true;
        return decision;
    }

    internal void Reset() => this = default;
}
