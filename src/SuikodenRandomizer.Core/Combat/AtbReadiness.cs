using System;

namespace SuikodenRandomizer.Core.Combat;

/// <summary>Tracks experimental ATB readiness without issuing any combat commands.</summary>
public sealed class AtbReadiness
{
    private readonly Slot[] _slots;
    private readonly double _secondsAtReferenceSpeed;
    private readonly int _referenceSpeed;

    public AtbReadiness(int slotCount, double secondsAtReferenceSpeed = 6, int referenceSpeed = 50)
    {
        if (slotCount <= 0) throw new ArgumentOutOfRangeException(nameof(slotCount));
        if (!double.IsFinite(secondsAtReferenceSpeed) || secondsAtReferenceSpeed <= 0)
            throw new ArgumentOutOfRangeException(nameof(secondsAtReferenceSpeed));
        if (referenceSpeed <= 0) throw new ArgumentOutOfRangeException(nameof(referenceSpeed));

        _slots = new Slot[slotCount];
        _secondsAtReferenceSpeed = secondsAtReferenceSpeed;
        _referenceSpeed = referenceSpeed;
    }

    public int SlotCount => _slots.Length;

    /// <summary>Returns the time for a fresh meter to fill at the given SPD.</summary>
    public double SecondsToFull(int speed) => SecondsToFull(speed, 1);

    internal double SecondsToFull(int speed, double rateMultiplier)
        => _secondsAtReferenceSpeed / RateForSpeed(speed, rateMultiplier);

    internal double SecondsRemaining(int slot)
    {
        ref var actor = ref GetSlot(slot);
        return actor.Rate > 0 ? (1 - actor.Gauge) * _secondsAtReferenceSpeed / actor.Rate : double.PositiveInfinity;
    }

    /// <summary>Updates a slot's current SPD. Ineligible or absent actors lose their gauge.</summary>
    public void Observe(int slot, int speed, bool eligible) => Observe(slot, speed, eligible, 1);

    internal void Observe(int slot, int speed, bool eligible, double rateMultiplier)
    {
        ref var actor = ref GetSlot(slot);
        if (!eligible)
        {
            actor = default;
            return;
        }

        actor.Rate = RateForSpeed(speed, rateMultiplier);
    }

    public void Advance(double elapsedSeconds, bool advance)
    {
        if (!advance || elapsedSeconds == 0) return;
        if (!double.IsFinite(elapsedSeconds) || elapsedSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));

        double elapsedAtReferenceRate = elapsedSeconds / _secondsAtReferenceSpeed;
        for (int i = 0; i < _slots.Length; i++)
        {
            ref var actor = ref _slots[i];
            if (actor.Rate > 0 && actor.Gauge < 1)
                actor.Gauge = Math.Min(1, actor.Gauge + elapsedAtReferenceRate * actor.Rate);
        }
    }

    public double GetGauge(int slot) => GetSlot(slot).Gauge;
    public bool IsReady(int slot) => GetGauge(slot) >= 1;

    internal void SeedOpeningCharge(int slot, double fraction)
    {
        ref var actor = ref GetSlot(slot);
        if (actor.Rate > 0 && actor.Gauge == 0)
            actor.Gauge = fraction;
    }

    /// <summary>Restarts a meter when the caller observes a native action by its actor.</summary>
    public void ObserveNativeAction(int slot) => GetSlot(slot).Gauge = 0;

    public void Reset() => Array.Clear(_slots, 0, _slots.Length);

    private double RateForSpeed(int speed, double multiplier)
        => Math.Min(2, Math.Clamp((double)speed / _referenceSpeed, 0.5, 2) * multiplier);

    private ref Slot GetSlot(int slot)
    {
        if ((uint)slot >= (uint)_slots.Length) throw new ArgumentOutOfRangeException(nameof(slot));
        return ref _slots[slot];
    }

    private struct Slot
    {
        public double Gauge;
        public double Rate;
    }
}
