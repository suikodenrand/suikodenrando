using System;
using System.Collections.Generic;

namespace SuikodenRandomizer.Core.Combat;

/// <summary>
/// Encounter-local CTB initiative. Time advances only when an actor is selected;
/// callers own native commands, resource use, effects, and action-completion proof.
/// Slot zero is valid; -1 denotes no selected actor.
/// </summary>
public sealed class CtbTimeline
{
    internal const double DefaultBreakDelayFraction = 0.5;

    internal static (bool SoloBoss, double BreakDelay) ClassifyFormation(int startingEnemyCount, bool targetIsBoss, bool bossMusic = false,
        double soloBossDelay = 0.25, double otherDelay = DefaultBreakDelayFraction)
    {
        bool soloBoss = startingEnemyCount == 1 && (targetIsBoss || bossMusic);
        return (soloBoss, soloBoss ? soloBossDelay : otherDelay);
    }

    internal static int ApplySoloBossSpeedBonus(int nativeSpeed, decimal bonusPercent)
    {
        if (nativeSpeed <= 0 || bonusPercent == 0) return nativeSpeed;
        decimal boosted = decimal.Round(nativeSpeed * (1 + bonusPercent / 100), MidpointRounding.AwayFromZero);
        return (int)Math.Min(boosted, int.MaxValue);
    }

    private readonly Actor[] _actors;
    private readonly double _referenceRecovery;
    private readonly int _referenceSpeed;
    private bool _admitted;

    internal static double OpeningUpkeepInterval(List<double> recoveries)
    {
        if (recoveries.Count == 0) throw new ArgumentException("An opening roster is required.", nameof(recoveries));
        recoveries.Sort();
        return (recoveries[(recoveries.Count - 1) / 2] + recoveries[recoveries.Count / 2]) / 2;
    }

    public CtbTimeline(int slotCount, double referenceRecovery = 1000, int referenceSpeed = 100)
    {
        if (slotCount <= 0) throw new ArgumentOutOfRangeException(nameof(slotCount));
        if (!IsFinite(referenceRecovery) || referenceRecovery <= 0)
            throw new ArgumentOutOfRangeException(nameof(referenceRecovery));
        if (referenceSpeed <= 0) throw new ArgumentOutOfRangeException(nameof(referenceSpeed));
        _actors = new Actor[slotCount];
        _referenceRecovery = referenceRecovery;
        _referenceSpeed = referenceSpeed;
    }

    public int SlotCount => _actors.Length;
    public double Now { get; private set; }
    public int PendingSlot { get; private set; } = -1;
    public bool IsAdmitted => _admitted;
    public double MinimumRecovery => _referenceRecovery * 0.025;

    /// <summary>Every positive SPD remains distinct below the 4x upper rate cap.</summary>
    public double RecoveryForSpeed(int speed, double recoveryMultiplier = 1)
    {
        if (!IsFinite(recoveryMultiplier) || recoveryMultiplier <= 0)
            throw new ArgumentOutOfRangeException(nameof(recoveryMultiplier));
        double rate = Math.Min(4, Math.Max(1d, speed) / _referenceSpeed);
        return Math.Max(MinimumRecovery, _referenceRecovery / rate * Math.Clamp(recoveryMultiplier, 0.025, 4));
    }

    /// <summary>
    /// Absent/ineligible actors lose their initiative and Break state. Reappearing
    /// actors receive a fresh interval. SPD changes preserve remaining charge.
    /// </summary>
    public void Observe(int slot, long identity, int speed, bool eligible, int breakMaximum = 100)
    {
        if (breakMaximum <= 0) throw new ArgumentOutOfRangeException(nameof(breakMaximum));
        ref Actor actor = ref GetActor(slot);
        if (!eligible || identity == 0)
        {
            if (PendingSlot == slot) ClearReservation();
            actor = default;
            return;
        }

        if (actor.Identity != identity)
        {
            if (PendingSlot == slot) ClearReservation();
            actor = new Actor
            {
                Identity = identity,
                Speed = speed,
                NextReady = Now + RecoveryForSpeed(speed),
                Interval = RecoveryForSpeed(speed),
                BreakMaximum = breakMaximum,
                BreakRemaining = breakMaximum
            };
            return;
        }

        if (actor.Speed != speed)
        {
            double ratio = RecoveryForSpeed(speed) / RecoveryForSpeed(actor.Speed);
            actor.NextReady = Now + Math.Max(0, actor.NextReady - Now) * ratio;
            actor.Interval *= ratio;
            actor.DelayUsed *= ratio;
            actor.AdvanceUsed *= ratio;
            actor.Speed = speed;
        }
        ConfigureBreak(slot, breakMaximum);
    }

    /// <summary>Allows an adapter to derive a threshold from native HP without leaking game types.</summary>
    public void ConfigureBreak(int slot, int maximum)
    {
        if (maximum <= 0) throw new ArgumentOutOfRangeException(nameof(maximum));
        ref Actor actor = ref GetActor(slot);
        if (actor.Identity != 0 && actor.BreakMaximum != maximum)
        {
            int damage = actor.BreakMaximum - actor.BreakRemaining;
            actor.BreakMaximum = maximum;
            actor.BreakRemaining = actor.Broken ? 0 : Math.Max(1, maximum - damage);
        }
    }

    /// <summary>Reads the next logical event without advancing time or reserving an actor.</summary>
    public double PeekNextTime(ReadOnlySpan<bool> eligible)
    {
        RequireMask(eligible);
        if (PendingSlot >= 0) return Now;
        return PeekOpportunity(eligible)?.ReadyAt ?? double.PositiveInfinity;
    }

    /// <summary>
    /// Advances an explicit logical maintenance event without changing any actor's
    /// scheduled turn. Menus and native actions must finish before maintenance.
    /// </summary>
    public void AdvanceTo(double logicalTime)
    {
        if (!IsFinite(logicalTime) || logicalTime < Now)
            throw new ArgumentOutOfRangeException(nameof(logicalTime));
        if (PendingSlot >= 0)
            throw new InvalidOperationException("A selected actor owns the logical clock until canceled or completed.");
        Now = logicalTime;
    }

    /// <summary>Reserves the earliest eligible actor, with stable slot-order ties.</summary>
    public int Select(ReadOnlySpan<bool> eligible)
    {
        RequireMask(eligible);
        if (PendingSlot >= 0) return -1;
        if (PeekOpportunity(eligible) is not { } next) return -1;
        Now = next.ReadyAt;
        PendingSlot = next.Slot;
        return next.Slot;
    }

    // Project exactly one opportunity, including the current actor's return if
    // it wins again. Input during playback must never reserve a second actor.
    internal CtbForecastEntry? PeekOpportunity(ReadOnlySpan<bool> eligible, double pendingRecovery = 1)
    {
        RequireMask(eligible);
        int selected = -1;
        double earliest = double.PositiveInfinity;
        for (int slot = 0; slot < _actors.Length; slot++)
        {
            ref Actor actor = ref _actors[slot];
            if (!eligible[slot] || actor.Identity == 0) continue;
            double due = slot == PendingSlot ? Now + RecoveryForSpeed(actor.Speed, pendingRecovery) : actor.NextReady;
            if (selected >= 0 && due >= earliest) continue;
            selected = slot;
            earliest = due;
        }
        return selected < 0 ? null : new CtbForecastEntry(selected, _actors[selected].Identity, Math.Max(Now, earliest), false);
    }

    public bool Admit(int slot, long identity)
    {
        if (_admitted || !MatchesReservation(slot, identity)) return false;
        _admitted = true;
        return true;
    }

    public bool CancelReservation(int slot, long identity)
    {
        if (_admitted || !MatchesReservation(slot, identity)) return false;
        ClearReservation();
        return true;
    }

    /// <summary>Call once after native completion, never when animation merely begins.</summary>
    public bool Complete(int slot, long identity, double recoveryMultiplier = 1)
    {
        if (!_admitted || !MatchesReservation(slot, identity)) return false;
        ref Actor actor = ref GetActor(slot);
        double recovery = RecoveryForSpeed(actor.Speed, recoveryMultiplier);
        actor.NextReady = Now + recovery;
        actor.Interval = recovery;
        actor.DelayUsed = 0;
        actor.AdvanceUsed = 0;
        if (actor.Broken)
        {
            actor.Broken = false;
            actor.BreakRemaining = actor.BreakMaximum;
        }
        ClearReservation();
        return true;
    }

    /// <summary>Wait consumes a selected, unadmitted turn and always incurs positive recovery.</summary>
    public bool Wait(int slot, long identity, double recoveryMultiplier = 0.5)
    {
        if (_admitted || !MatchesReservation(slot, identity)) return false;
        // Validate before changing admission state.
        RecoveryForSpeed(GetActor(slot).Speed, recoveryMultiplier);
        return Admit(slot, identity) && Complete(slot, identity, recoveryMultiplier);
    }

    /// <summary>
    /// Delays are capped cumulatively at 75% of a base SPD interval until the
    /// recipient completes a turn. Repeated attackers cannot postpone it forever.
    /// A currently selected actor cannot be displaced by a later command.
    /// </summary>
    public double Delay(int slot, long identity, double intervalFraction = 0.25)
    {
        ValidateFraction(intervalFraction);
        ref Actor actor = ref GetActor(slot);
        if (!MatchesActor(actor, identity) || PendingSlot == slot) return 0;
        double interval = RecoveryForSpeed(actor.Speed);
        double added = Math.Min(interval * intervalFraction, Math.Max(0, interval * 0.75 - actor.DelayUsed));
        if (added > 0) actor.NextReady = Math.Max(Now, actor.NextReady) + added;
        actor.DelayUsed += added;
        return added;
    }

    /// <summary>Advance is capped at 50% per turn and cannot grant an immediate extra action.</summary>
    public double Advance(int slot, long identity, double intervalFraction = 0.25)
    {
        ValidateFraction(intervalFraction);
        ref Actor actor = ref GetActor(slot);
        if (!MatchesActor(actor, identity) || PendingSlot == slot) return 0;
        double interval = RecoveryForSpeed(actor.Speed);
        double available = Math.Max(0, actor.NextReady - Now - MinimumRecovery);
        double removed = Math.Min(available,
            Math.Min(interval * intervalFraction, Math.Max(0, interval * 0.5 - actor.AdvanceUsed)));
        actor.NextReady -= removed;
        actor.AdvanceUsed += removed;
        return removed;
    }

    /// <summary>
    /// Records confirmed native hits only. Break lasts until the target completes
    /// its next action, does not stack, and shares the ordinary capped delay budget.
    /// This model does not alter native damage or imply a native vulnerability effect.
    /// </summary>
    public CtbBreakResult ApplyBreak(int slot, long identity, int points, double delayFraction = DefaultBreakDelayFraction)
    {
        if (points < 0) throw new ArgumentOutOfRangeException(nameof(points));
        ValidateFraction(delayFraction);
        ref Actor actor = ref GetActor(slot);
        if (!MatchesActor(actor, identity) || actor.Broken || points == 0)
            return new CtbBreakResult(false, false, 0, actor.BreakRemaining, 0);
        int applied = Math.Min(points, actor.BreakRemaining);
        actor.BreakRemaining -= applied;
        bool triggered = actor.BreakRemaining == 0;
        double delay = 0;
        if (triggered)
        {
            actor.Broken = true;
            delay = Delay(slot, identity, delayFraction);
        }
        return new CtbBreakResult(true, triggered, applied, actor.BreakRemaining, delay);
    }

    public double GetNextReady(int slot)
    {
        ref Actor actor = ref GetActor(slot);
        return actor.Identity == 0 ? double.PositiveInfinity : actor.NextReady;
    }

    public long GetIdentity(int slot) => GetActor(slot).Identity;
    public int GetSpeed(int slot) => GetActor(slot).Speed;
    public int GetBreakRemaining(int slot) => GetActor(slot).BreakRemaining;
    public int GetBreakMaximum(int slot) => GetActor(slot).BreakMaximum;
    public bool IsBroken(int slot) => GetActor(slot).Broken;

    public double GetGauge(int slot)
    {
        ref Actor actor = ref GetActor(slot);
        if (actor.Identity == 0 || actor.Interval <= 0) return 0;
        return Math.Clamp(1 - Math.Max(0, actor.NextReady - Now) / actor.Interval, 0, 1);
    }

    /// <summary>
    /// Projects ordinary future turns on an isolated copy. Later choices, native
    /// AI, KO, status and speed changes can invalidate this projection.
    /// </summary>
    public CtbForecastEntry[] Forecast(int count, ReadOnlySpan<bool> eligible)
        => Forecast(count, eligible, 1);

    /// <summary>
    /// Applies a previewed or committed recovery cost to the currently reserved
    /// actor only. An admitted action retains that cost during playback. Later
    /// hypothetical opportunities assume ordinary Attack recovery, and no real
    /// reservation, resource, Break state, or random state is consumed.
    /// </summary>
    public CtbForecastEntry[] Forecast(int count, ReadOnlySpan<bool> eligible, double pendingRecoveryMultiplier)
    {
        if (count < 0 || count > 256) throw new ArgumentOutOfRangeException(nameof(count));
        if (!IsFinite(pendingRecoveryMultiplier) || pendingRecoveryMultiplier <= 0)
            throw new ArgumentOutOfRangeException(nameof(pendingRecoveryMultiplier));
        RequireMask(eligible);
        CtbTimeline copy = Clone();
        var result = new CtbForecastEntry[count];
        int written = 0;
        if (count > 0 && copy.PendingSlot >= 0)
        {
            int slot = copy.PendingSlot;
            long identity = copy.GetIdentity(slot);
            result[written++] = new CtbForecastEntry(slot, identity, copy.Now, true);
            if (!copy.IsAdmitted) copy.Admit(slot, identity);
            copy.Complete(slot, identity, pendingRecoveryMultiplier);
        }
        while (written < count)
        {
            int slot = copy.Select(eligible);
            if (slot < 0) break;
            long identity = copy.GetIdentity(slot);
            result[written++] = new CtbForecastEntry(slot, identity, copy.Now, false);
            copy.Admit(slot, identity);
            copy.Complete(slot, identity);
        }
        if (written != count) Array.Resize(ref result, written);
        return result;
    }

    public CtbTimeline Clone()
    {
        var copy = new CtbTimeline(_actors.Length, _referenceRecovery, _referenceSpeed)
        {
            Now = Now,
            PendingSlot = PendingSlot,
            _admitted = _admitted
        };
        Array.Copy(_actors, copy._actors, _actors.Length);
        return copy;
    }

    public void Reset()
    {
        Array.Clear(_actors, 0, _actors.Length);
        Now = 0;
        ClearReservation();
    }

    private bool MatchesReservation(int slot, long identity)
        => slot >= 0 && slot < _actors.Length && slot == PendingSlot && MatchesActor(_actors[slot], identity);

    private static bool MatchesActor(Actor actor, long identity) => identity != 0 && actor.Identity == identity;

    private void ClearReservation()
    {
        PendingSlot = -1;
        _admitted = false;
    }

    private ref Actor GetActor(int slot)
    {
        if ((uint)slot >= (uint)_actors.Length) throw new ArgumentOutOfRangeException(nameof(slot));
        return ref _actors[slot];
    }

    private void RequireMask(ReadOnlySpan<bool> eligible)
    {
        if (eligible.Length != _actors.Length) throw new ArgumentException("Eligibility must cover every actor slot.", nameof(eligible));
    }

    private static void ValidateFraction(double fraction)
    {
        if (!IsFinite(fraction) || fraction < 0 || fraction > 1)
            throw new ArgumentOutOfRangeException(nameof(fraction));
    }

    private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

    private struct Actor
    {
        public long Identity;
        public int Speed;
        public double NextReady;
        public double Interval;
        public double DelayUsed;
        public double AdvanceUsed;
        public int BreakMaximum;
        public int BreakRemaining;
        public bool Broken;
    }
}

public readonly struct CtbForecastEntry
{
    public CtbForecastEntry(int slot, long identity, double readyAt, bool current)
    {
        Slot = slot;
        Identity = identity;
        ReadyAt = readyAt;
        Current = current;
    }
    public int Slot { get; }
    public long Identity { get; }
    public double ReadyAt { get; }
    public bool Current { get; }
}

public readonly struct CtbBreakResult
{
    public CtbBreakResult(bool accepted, bool triggered, int applied, int remaining, double delay)
    {
        Accepted = accepted;
        Triggered = triggered;
        Applied = applied;
        Remaining = remaining;
        Delay = delay;
    }
    public bool Accepted { get; }
    public bool Triggered { get; }
    public int Applied { get; }
    public int Remaining { get; }
    public double Delay { get; }
}
