using System;
using SuikodenRandomizer.Core.Combat;

namespace SuikodenRandomizer.S1BattlePreview;

internal sealed class MeterActionScheduler
{
    private readonly AtbReadiness _gauges;
    private readonly IntPtr[] _identities;
    private readonly double[] _readyAt;
    private readonly double[] _chargeSeconds;
    private readonly bool[] _completedThisRound;
    private double _lastTime;
    private bool _charging;
    private bool _admitted;
    private readonly CtbTimeline _ctb;
    private readonly bool[] _ctbEligible;
    private double _ctbRecovery = 1;
    internal bool UseCtb { get; set; }
    internal CtbTimeline Ctb => _ctb;
    // Filled at the native turn-selection boundary. Menu and animation previews
    // reuse these decisions without probing native action readiness mid-command.
    internal Span<bool> CtbEligible => _ctbEligible;
    internal double CtbRecoveryMultiplier => _ctbRecovery;

    internal MeterActionScheduler(int slotCount, double secondsAtReferenceSpeed = 6)
    {
        _gauges = new AtbReadiness(slotCount, secondsAtReferenceSpeed);
        _identities = new IntPtr[slotCount];
        _readyAt = new double[slotCount];
        _chargeSeconds = new double[slotCount];
        _completedThisRound = new bool[slotCount];
        _ctb = new CtbTimeline(slotCount);
        _ctbEligible = new bool[slotCount];
    }

    internal int PendingSlot { get; private set; }
    internal bool IsAdmitted => _admitted;

    internal void Observe(int slot, IntPtr identity, int speed, bool alive, double rateMultiplier = 1)
    {
        bool eligible = alive && identity != IntPtr.Zero;
        if (!eligible || _identities[slot] != identity)
        {
            if (PendingSlot == slot) ClearReservation();
            _gauges.ObserveNativeAction(slot);
            _readyAt[slot] = 0;
            _chargeSeconds[slot] = 0;
            _completedThisRound[slot] = false;
            _ctbEligible[slot] = false;
        }

        _identities[slot] = eligible ? identity : IntPtr.Zero;
        _gauges.Observe(slot, speed, eligible, rateMultiplier);
        if (UseCtb) _ctb.Observe(slot, identity.ToInt64(), speed, eligible);
    }

    internal void Tick(double now, bool charging)
    {
        if (UseCtb) return; // No wall-clock input can advance CTB.
        if (now < _lastTime) return;
        if (_charging && charging && now > _lastTime)
        {
            double elapsed = now - _lastTime;
            for (int slot = 1; slot < _identities.Length; slot++)
            {
                if (_gauges.IsReady(slot)) continue;
                double remaining = _gauges.SecondsRemaining(slot);
                _readyAt[slot] = _lastTime + remaining;
                // Include the first sample at full and stop the measurement on subsequent ticks.
                if (double.IsFinite(remaining)) _chargeSeconds[slot] += elapsed;
            }
            _gauges.Advance(elapsed, advance: true);
        }

        _lastTime = now;
        _charging = charging;
    }

    internal int Select(ReadOnlySpan<bool> eligible)
    {
        if (PendingSlot != 0) return 0;
        if (UseCtb)
        {
            int chosen = _ctb.Select(eligible);
            PendingSlot = chosen < 1 ? 0 : chosen;
            return PendingSlot;
        }
        int selected = 0;
        for (int slot = 1; slot < eligible.Length; slot++)
        {
            if (eligible[slot] && IsReadyBefore(slot, selected))
                selected = slot;
        }
        PendingSlot = selected;
        return selected;
    }

    internal CtbForecastEntry[] ForecastCtb(int count, ReadOnlySpan<bool> currentCandidates, IntPtr pendingActor,
        double recovery)
    {
        // A native menu callback can see a KO or slot replacement before Observe runs.
        if (PendingSlot != 0 && pendingActor != _identities[PendingSlot]) return Array.Empty<CtbForecastEntry>();
        // Live observations may remove an actor between native selection calls.
        // Only a fresh native selection pass can admit a previously excluded slot.
        Span<bool> eligible = stackalloc bool[_ctbEligible.Length];
        for (int slot = 0; slot < _ctbEligible.Length; slot++)
            eligible[slot] = currentCandidates[slot] && _ctbEligible[slot];
        return _ctb.Forecast(count, eligible, recovery);
    }

    internal bool IsReadyBefore(int slot, int other)
        => UseCtb ? _ctb.GetNextReady(slot) <= _ctb.Now
            && (other == 0 || _ctb.GetNextReady(slot) < _ctb.GetNextReady(other))
            : _gauges.IsReady(slot) && (other == 0 || _readyAt[slot] < _readyAt[other]);

    internal bool TryReserveReady(int slot, IntPtr identity)
    {
        if (PendingSlot != 0 || slot <= 0 || slot >= _identities.Length || identity == IntPtr.Zero
            || _identities[slot] != identity || !_gauges.IsReady(slot)) return false;

        PendingSlot = slot;
        return true;
    }

    internal bool Admit(int slot, IntPtr identity)
    {
        if (_admitted || !MatchesReservation(slot, identity)) return false;
        if (UseCtb && !_ctb.Admit(slot, identity.ToInt64())) return false;
        _admitted = true;
        return true;
    }

    internal bool CancelReservation(int slot, IntPtr identity)
    {
        if (_admitted || !MatchesReservation(slot, identity)) return false;
        if (UseCtb && !_ctb.CancelReservation(slot, identity.ToInt64())) return false;
        ClearReservation();
        return true;
    }

    internal bool Finish(int slot, IntPtr identity)
    {
        if (!_admitted || !MatchesReservation(slot, identity)) return false;
        if (UseCtb && !_ctb.Complete(slot, identity.ToInt64(), _ctbRecovery)) return false;
        _ctbRecovery = 1;
        _gauges.ObserveNativeAction(slot);
        _readyAt[slot] = 0;
        _chargeSeconds[slot] = 0;
        _completedThisRound[slot] = true;
        ClearReservation();
        return true;
    }

    internal double GetGauge(int slot) => UseCtb ? _ctb.GetGauge(slot) : _gauges.GetGauge(slot);
    internal void SetCtbRecovery(double multiplier)
    {
        if (!double.IsFinite(multiplier) || multiplier <= 0) throw new ArgumentOutOfRangeException(nameof(multiplier));
        _ctbRecovery = multiplier;
    }
    internal bool SpendCtbMenuTurn(int slot, IntPtr identity, double recoveryMultiplier)
    {
        if (!UseCtb || !MatchesReservation(slot, identity)
            || !_ctb.Wait(slot, identity.ToInt64(), recoveryMultiplier)) return false;
        ClearReservation();
        return true;
    }
    internal double GetChargeSeconds(int slot) => _chargeSeconds[slot];
    internal double SecondsToFull(int speed, double rateMultiplier = 1) => _gauges.SecondsToFull(speed, rateMultiplier);
    internal static double EnemyDexMultiplier(int dex) => 1 + 0.0075 * Math.Clamp(dex, 0, 100);

    internal void SeedOpeningCharge(int slot, double fraction) => _gauges.SeedOpeningCharge(slot, fraction);

    internal static double OpeningCharge(int speed, int dex, double speedRoll, double dexRoll, double bonusRoll)
    {
        double speedCharge = 0.001 * Math.Clamp(speed, 0, 100);
        double variation = 0.04 * (speedRoll - 0.5);
        double dexChance = 0.005 * Math.Clamp(dex, 0, 100);
        double dexBonus = dexRoll < dexChance ? 0.04 + 0.08 * bonusRoll : 0;
        return Math.Max(0, speedCharge + variation) + dexBonus;
    }

    internal bool CanRepeat(int slot, IntPtr identity)
        => slot > 0 && identity != IntPtr.Zero && _identities[slot] == identity && (UseCtb || _completedThisRound[slot]);

    internal void BeginRound() => Array.Clear(_completedThisRound, 0, _completedThisRound.Length);

    internal void Reset()
    {
        _gauges.Reset();
        _ctb.Reset();
        Array.Clear(_ctbEligible, 0, _ctbEligible.Length);
        _ctbRecovery = 1;
        Array.Clear(_identities, 0, _identities.Length);
        Array.Clear(_readyAt, 0, _readyAt.Length);
        Array.Clear(_chargeSeconds, 0, _chargeSeconds.Length);
        BeginRound();
        _lastTime = 0;
        _charging = false;
        ClearReservation();
    }

    private bool MatchesReservation(int slot, IntPtr identity)
        => slot > 0 && slot == PendingSlot && _identities[slot] == identity;

    private void ClearReservation()
    {
        PendingSlot = 0;
        _admitted = false;
        _ctbRecovery = 1;
    }
}
