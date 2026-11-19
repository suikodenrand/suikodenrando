using SuikodenRandomizer.Core.Combat;
using SuikodenRandomizer.S1BattlePreview;
using SuikodenRandomizer.Shared;

int passed = 0;
double waitCost = CtbActionTiming.Describe(CtbActionKind.Wait).Multiplier;
void Check(bool passed, string failure)
{
    if (!passed) throw new InvalidOperationException(failure);
}

void Near(double expected, double actual)
    => Check(Math.Abs(expected - actual) <= 1e-9, $"Expected {expected}, got {actual}.");

void Case(string title, Action test)
{
    test();
    Console.WriteLine($"PASS {title}");
    passed++;
}

Case("Shared items preserve each actor's nine native pockets and identify every ally pocket uniquely", () =>
{
    for (int actor = 1; actor <= 6; actor++)
    {
        var tokens = new HashSet<int>();
        for (int owner = 1; owner <= 6; owner++)
        for (int pocket = 0; pocket < 9; pocket++)
        {
            int token = S1BattleItemAddress.Encode(actor, owner, pocket);
            Check(token <= sbyte.MaxValue && tokens.Add(token), "Duplicate or incompatible native item operand.");
            Check(owner == actor ? token == pocket : token == owner * 9 + pocket, "Wrong pocket encoding.");
            Check(S1BattleItemAddress.TryDecode(actor, token, out int actualOwner, out int actualPocket)
                && owner == actualOwner && pocket == actualPocket, "Owner or pocket lost during dispatch.");
        }
        for (int token = sbyte.MinValue; token <= sbyte.MaxValue; token++)
            Check(S1BattleItemAddress.TryDecode(actor, token, out _, out _) == tokens.Contains(token),
                "Accepted a noncanonical item operand.");
    }
    Check(!S1BattleItemAddress.TryDecode(0, 0, out _, out _) && !S1BattleItemAddress.TryDecode(7, 0, out _, out _),
        "Nonparty actor decoded a pocket.");
});

Case("SPD produces independent readiness with bounded rates", () =>
{
    var scheduler = new AtbReadiness(6);
    scheduler.Observe(0, int.MinValue, eligible: true);
    scheduler.Observe(1, 50, eligible: true);
    scheduler.Observe(2, int.MaxValue, eligible: true);
    scheduler.Observe(4, 44, eligible: true);
    scheduler.Observe(5, 27, eligible: true);

    scheduler.Advance(3, advance: true);
    Near(0.25, scheduler.GetGauge(0));
    Near(0.5, scheduler.GetGauge(1));
    Near(1, scheduler.GetGauge(2));
    Near(0, scheduler.GetGauge(3));
    Near(0.44, scheduler.GetGauge(4));
    Near(0.27, scheduler.GetGauge(5));
    Check(scheduler.IsReady(2) && !scheduler.IsReady(1), "Actors must reach readiness independently.");
});

Case("Wait freezes every gauge and a full gauge holds", () =>
{
    var scheduler = new AtbReadiness(2);
    scheduler.Observe(0, 100, eligible: true);
    scheduler.Observe(1, 50, eligible: true);

    scheduler.Advance(3, advance: true);
    scheduler.Advance(60, advance: false);
    Near(1, scheduler.GetGauge(0));
    Near(0.5, scheduler.GetGauge(1));

    scheduler.Advance(1.5, advance: true);
    Near(1, scheduler.GetGauge(0));
    Near(0.75, scheduler.GetGauge(1));
});

Case("A native action resets only its actor's meter", () =>
{
    var scheduler = new AtbReadiness(2);
    scheduler.Observe(0, 100, eligible: true);
    scheduler.Observe(1, 50, eligible: true);

    scheduler.Advance(3, advance: true);
    scheduler.ObserveNativeAction(0);
    Near(0, scheduler.GetGauge(0));
    Near(0.5, scheduler.GetGauge(1));

    scheduler.Advance(1.5, advance: true);
    Near(0.5, scheduler.GetGauge(0));
    Near(0.75, scheduler.GetGauge(1));
});

Case("Refreshing SPD preserves the gauge and KO or an empty slot clears it", () =>
{
    var scheduler = new AtbReadiness(1);
    scheduler.Observe(0, 50, eligible: true);
    scheduler.Advance(3, advance: true);
    scheduler.Observe(0, 100, eligible: true);
    Near(0.5, scheduler.GetGauge(0));
    scheduler.Advance(1.5, advance: true);
    Check(scheduler.IsReady(0), "New SPD should affect the remaining charge time.");

    scheduler.Observe(0, 0, eligible: false);
    scheduler.Advance(60, advance: true);
    Near(0, scheduler.GetGauge(0));
    scheduler.Observe(0, 50, eligible: true);
    scheduler.Advance(3, advance: true);
    Near(0.5, scheduler.GetGauge(0));
});

Case("Battle reset clears both gauge and prior slot eligibility", () =>
{
    var scheduler = new AtbReadiness(2);
    scheduler.Observe(1, 50, eligible: true);
    scheduler.Advance(6, advance: true);
    scheduler.Reset();
    scheduler.Advance(6, advance: true);
    Near(0, scheduler.GetGauge(1));
    Check(!scheduler.IsReady(1), "A new battle must not inherit old readiness.");
});

Case("Live native gaps of 5, 7 and 13 frames preserve the battle and charge", () =>
{
    var clock = new BattleSampleClock();
    var scheduler = new AtbReadiness(1);
    scheduler.Observe(0, 50, eligible: true);
    var battle = new IntPtr(1);
    Check(clock.Sample(battle, 100, 10, true, out double elapsed) == BattleSampleKind.NewBattle, "First sample.");
    Near(0, elapsed);
    foreach (int frame in new[] { 105, 112, 125 })
    {
        Check(clock.Sample(battle, frame, 10 + (frame - 100) / 60d, true, out elapsed)
            == BattleSampleKind.Continued, "Skipped render frames must not restart the battle.");
        scheduler.Advance(elapsed, true);
    }
    Near(25d / 60d / 6d, scheduler.GetGauge(0));
    Check(clock.Sample(battle, 125, 10.42, true, out elapsed) == BattleSampleKind.Duplicate, "Duplicate render-frame sample.");
    Near(0, elapsed);
});

Case("Stale and unfocused samples pause elapsed time without restarting the battle", () =>
{
    var clock = new BattleSampleClock();
    var battle = new IntPtr(1);
    clock.Sample(battle, 100, 10, true, out _);
    Check(!clock.IsFresh(12), "Hide stale overlay.");
    Check(clock.Sample(battle, 200, 12, true, out double elapsed) == BattleSampleKind.Continued, "A pause must not restart the battle.");
    Near(0, elapsed);
    clock.Sample(battle, 205, 12.1, false, out elapsed);
    Near(0, elapsed);
    clock.Sample(battle, 210, 12.2, true, out elapsed);
    Near(0, elapsed);
    clock.Reset();
    Check(clock.Sample(battle, 215, 12.3, true, out elapsed) == BattleSampleKind.NewBattle, "Explicit teardown also handles reused pointers.");
    Near(0, elapsed);
});

Case("A selected attack waits for a fresh full SPD meter and releases once per battle", () =>
{
    var gate = new OneShotActionGate();
    Check(gate.Evaluate(3, eligible: true, secondsToFull: 6, now: 10) == ActionGateDecision.Armed,
        "Start the selected attack at zero charge.");
    Check(gate.WaitingSlot == 3 && !gate.Consumed, "Expose the queued actor until the attack is released.");
    Near(0, gate.Gauge);
    Check(gate.Evaluate(3, eligible: true, secondsToFull: 3, now: 12) == ActionGateDecision.Wait,
        "Keep the selected attack waiting below full readiness.");
    Near(1d / 3d, gate.Gauge);
    Check(gate.Evaluate(3, eligible: true, secondsToFull: 3, now: 16) == ActionGateDecision.Release,
        "Release the selected attack once its meter fills.");
    Near(1, gate.Gauge);
    Check(gate.WaitingSlot == 0 && gate.Consumed, "Clear the waiting indicator and spend the one-shot.");
    Check(gate.Evaluate(3, eligible: true, secondsToFull: 6, now: 16.1) == ActionGateDecision.PassThrough,
        "A repeated native callback cannot release the same action twice.");
    Check(gate.Evaluate(4, eligible: true, secondsToFull: 6, now: 17) == ActionGateDecision.PassThrough,
        "Other actors continue normally after the one-shot.");
    gate.Reset();
    Check(gate.WaitingSlot == 0 && !gate.Consumed, "Reset the queued actor and consumed state.");
    Check(gate.Evaluate(4, eligible: true, secondsToFull: 6, now: 20) == ActionGateDecision.Armed,
        "A fresh battle gets its own one-shot.");
});

Case("The attack reaches 100% when the HUD goes stale at 73%", () =>
{
    const int slot = 1, speed = 44, start = 10;
    var clock = new BattleSampleClock();
    var hudGauges = new AtbReadiness(2);
    var gate = new OneShotActionGate();
    var battle = new IntPtr(1);
    double secondsToFull = hudGauges.SecondsToFull(speed);
    Near(6d * 50d / speed, secondsToFull);
    hudGauges.Observe(slot, speed, eligible: true);
    clock.Sample(battle, 100, start, running: true, out _);
    Check(gate.Evaluate(slot, true, secondsToFull, start) == ActionGateDecision.Armed,
        "Arm independently of the HUD meter.");

    for (int sample = 1; sample <= 10; sample++)
    {
        clock.Sample(battle, 100 + sample * 30, start + sample * 0.5, running: true, out double elapsed);
        hudGauges.Advance(elapsed, advance: true);
    }

    const double lastHudSample = start + 5;
    double staleHudGauge = hudGauges.GetGauge(slot);
    Near(0.7333333333333333, staleHudGauge);
    Check(gate.Evaluate(slot, true, secondsToFull, lastHudSample) == ActionGateDecision.Wait,
        "The native handoff waits at 73%.");
    Near(staleHudGauge, gate.Gauge);
    Check(gate.Evaluate(slot, true, secondsToFull, lastHudSample) == ActionGateDecision.Wait,
        "A duplicate handoff timestamp must not advance the charge.");
    Near(staleHudGauge, gate.Gauge);

    Check(!clock.IsFresh(start + 6.8), "The overlay samples remain stale.");
    Check(gate.Evaluate(slot, true, secondsToFull, start + 6.8) == ActionGateDecision.Wait,
        "The queued attack still waits until the full SPD duration has passed.");
    Check(gate.Evaluate(slot, true, secondsToFull, start + 6.82) == ActionGateDecision.Release,
        "The native handoff reaches 100% without another HUD sample.");
    Near(1, gate.Gauge);
    Near(6.82, gate.ElapsedSeconds);
    Near(staleHudGauge, hudGauges.GetGauge(slot));
});

Case("An absent or ineligible actor or invalid duration leaves the one-shot available", () =>
{
    var gate = new OneShotActionGate();
    Check(gate.Evaluate(0, eligible: true, secondsToFull: 6, now: 10) == ActionGateDecision.PassThrough,
        "Slot zero is the native no-actor sentinel.");
    Check(gate.Evaluate(-1, eligible: true, secondsToFull: 6, now: 11) == ActionGateDecision.PassThrough,
        "Negative actor indices cannot arm the gate.");
    Check(gate.Evaluate(3, eligible: false, secondsToFull: 6, now: 12) == ActionGateDecision.PassThrough,
        "An ineligible actor cannot arm the gate.");
    foreach (double invalidDuration in new[] { 0, -1, double.NaN, double.PositiveInfinity })
        Check(gate.Evaluate(3, eligible: true, secondsToFull: invalidDuration, now: 12) == ActionGateDecision.PassThrough,
            "An invalid charge duration cannot arm the gate.");
    Check(gate.Evaluate(4, eligible: true, secondsToFull: 6, now: 13) == ActionGateDecision.Armed,
        "Declined actors do not spend the one-shot.");
});

Case("Actor changes and lost eligibility report distinct fallback reasons", () =>
{
    foreach (var (slot, eligible, reason) in new[]
    {
        (4, true, ActionGateDecision.ActorChanged),
        (0, true, ActionGateDecision.ActorChanged),
        (3, false, ActionGateDecision.Ineligible)
    })
    {
        var gate = new OneShotActionGate();
        Check(gate.Evaluate(3, eligible: true, secondsToFull: 6, now: 10) == ActionGateDecision.Armed,
            "Start waiting for the selected actor.");
        Check(gate.Evaluate(slot, eligible, secondsToFull: 6, now: 11) == reason,
            $"Resume native control and report {reason}.");
        Check(gate.WaitingSlot == 0 && gate.Consumed, "Clear the waiting indicator after bypassing.");
        Check(gate.Evaluate(3, eligible: true, secondsToFull: 6, now: 12) == ActionGateDecision.PassThrough,
            "Bypassing prevents another attempt in the same battle.");
    }
});

Case("An armed one-shot falls back to native battle at its 15-second deadline", () =>
{
    var gate = new OneShotActionGate();
    Check(gate.Evaluate(3, eligible: true, secondsToFull: 20, now: 10) == ActionGateDecision.Armed,
        "Start the fallback clock when the selected attack arms.");
    Check(gate.Evaluate(3, eligible: true, secondsToFull: 20, now: 24.99) == ActionGateDecision.Wait,
        "A partial meter keeps waiting before the deadline.");
    Check(gate.Evaluate(3, eligible: true, secondsToFull: 20, now: 25) == ActionGateDecision.TimedOut,
        "The deadline lets native battle progress when the meter stalls.");
    Check(gate.Evaluate(3, eligible: true, secondsToFull: 20, now: 26) == ActionGateDecision.PassThrough,
        "A full meter after the fallback cannot re-trigger the action.");

    gate.Reset();
    Check(gate.Evaluate(3, eligible: true, secondsToFull: 6, now: 10) == ActionGateDecision.Armed,
        "Start a charge with an ordinary SPD duration.");
    Check(gate.Evaluate(3, eligible: true, secondsToFull: 6, now: 25) == ActionGateDecision.TimedOut,
        "A handoff that resumes at the deadline must time out even when enough time has passed to charge.");
});

Case("Boosted SPD meters fill in two seconds at 50 SPD", () =>
{
    var scheduler = new MeterActionScheduler(4, secondsAtReferenceSpeed: 2);
    bool[] eligible = { false, true, true, true };
    scheduler.Observe(1, new IntPtr(11), 50, alive: true);
    scheduler.Observe(2, new IntPtr(12), 44, alive: true);
    scheduler.Observe(3, new IntPtr(13), 27, alive: true);

    scheduler.Tick(10, charging: true);
    scheduler.Tick(11.99, charging: true);
    Check(scheduler.Select(eligible) == 0, "The first meter remains below full just before two seconds.");
    scheduler.Tick(12, charging: true);
    Near(1, scheduler.GetGauge(1));
    Near(0.88, scheduler.GetGauge(2));
    Near(0.54, scheduler.GetGauge(3));
    Check(scheduler.Select(eligible) == 1, "The 50-SPD actor can act after two seconds.");
});

Case("Charge timing counts active time through the first full sample", () =>
{
    var scheduler = new MeterActionScheduler(3, secondsAtReferenceSpeed: 3);
    var hero = new IntPtr(11);
    var enemy = new IntPtr(12);
    bool[] eligible = { false, true, true };
    scheduler.Observe(1, hero, 50, alive: true);
    scheduler.Observe(2, enemy, 25, alive: true);
    scheduler.Tick(10, charging: true);
    scheduler.Tick(11, charging: true);
    Near(1, scheduler.GetChargeSeconds(1));
    Near(1, scheduler.GetChargeSeconds(2));

    scheduler.Tick(11, charging: false);
    scheduler.Tick(100, charging: false);
    scheduler.Tick(100, charging: true);
    Near(1, scheduler.GetChargeSeconds(1));
    Near(1, scheduler.GetChargeSeconds(2));

    scheduler.Tick(104, charging: true);
    Near(1, scheduler.GetGauge(1));
    Near(5, scheduler.GetChargeSeconds(2));
    Near(5, scheduler.GetChargeSeconds(1));
    scheduler.Tick(105.5, charging: true);
    Near(1, scheduler.GetGauge(2));
    Near(5, scheduler.GetChargeSeconds(1));
    Near(6.5, scheduler.GetChargeSeconds(2));

    Check(scheduler.Select(eligible) == 1 && scheduler.Admit(1, hero), "Admit the first full meter.");
    Near(5, scheduler.GetChargeSeconds(1));
    Check(scheduler.Finish(1, hero), "Confirm the action before clearing its charge time.");
    Near(0, scheduler.GetChargeSeconds(1));
    Near(6.5, scheduler.GetChargeSeconds(2));
    scheduler.Tick(106.5, charging: true);
    Near(1, scheduler.GetChargeSeconds(1));
    scheduler.Observe(1, new IntPtr(13), 50, alive: true);
    scheduler.Observe(2, enemy, 25, alive: false);
    Near(0, scheduler.GetChargeSeconds(1));
    Near(0, scheduler.GetChargeSeconds(2));
});

Case("Hero and enemy refill independently after their native actions", () =>
{
    var scheduler = new MeterActionScheduler(3);
    var hero = new IntPtr(11);
    var enemy = new IntPtr(12);
    bool[] eligible = { false, true, true };
    scheduler.Observe(1, hero, 100, alive: true);
    scheduler.Observe(2, enemy, 50, alive: true);
    scheduler.Tick(10, charging: true);
    scheduler.Tick(12, charging: true);
    Check(scheduler.Select(eligible) == 0, "Partial meters cannot dispatch an action.");
    scheduler.Tick(13, charging: true);
    Check(scheduler.Select(eligible) == 1 && scheduler.Admit(1, hero),
        "The faster hero reaches full first.");
    scheduler.Tick(16, charging: true);
    Near(1, scheduler.GetGauge(1));
    Near(1, scheduler.GetGauge(2));
    Check(scheduler.Select(eligible) == 0 && scheduler.PendingSlot == 1,
        "An enemy that fills during an action must wait for the native handoff.");
    Check(scheduler.Finish(1, hero), "Confirm the hero's native action.");
    Near(0, scheduler.GetGauge(1));
    Near(1, scheduler.GetGauge(2));
    eligible[1] = false;
    Check(scheduler.Select(eligible) == 2 && scheduler.Admit(2, enemy),
        "The full enemy can now act.");
    Check(scheduler.Finish(2, enemy), "Confirm the enemy's native action.");
    Near(0, scheduler.GetGauge(2));

    scheduler.Tick(19, charging: true);
    Near(1, scheduler.GetGauge(1));
    Check(scheduler.Select(eligible) == 0, "A full meter still waits for a native-eligible slot.");
    eligible[1] = true;
    Check(scheduler.Select(eligible) == 1 && scheduler.Admit(1, hero) && scheduler.Finish(1, hero),
        "The hero can act again after refilling.");
    Near(0.5, scheduler.GetGauge(2));
    eligible[1] = false;
    scheduler.Tick(22, charging: true);
    Check(scheduler.Select(eligible) == 2 && scheduler.Admit(2, enemy) && scheduler.Finish(2, enemy),
        "The enemy can also act again after refilling.");
});

Case("Only the matching admitted actor can consume a full meter", () =>
{
    var scheduler = new MeterActionScheduler(3);
    var actor = new IntPtr(11);
    var other = new IntPtr(12);
    bool[] eligible = { false, true, true };
    scheduler.Observe(1, actor, 50, alive: true);
    scheduler.Observe(2, other, 50, alive: true);
    scheduler.Tick(10, charging: true);
    scheduler.Tick(16, charging: true);
    Check(scheduler.Select(eligible) == 1, "Reserve the first ready actor.");
    Check(!scheduler.Finish(1, actor), "Selection alone is insufficient to consume a meter.");
    Check(!scheduler.Admit(2, other) && !scheduler.Admit(1, other),
        "A wrong native slot or identity must not admit the selected action.");
    Check(scheduler.Admit(1, actor) && !scheduler.Admit(1, actor),
        "Admit the matching actor once.");
    Check(!scheduler.Finish(2, other) && !scheduler.Finish(1, other),
        "A wrong native slot or identity must not finish the action.");
    Near(1, scheduler.GetGauge(1));
    Check(scheduler.Finish(1, actor) && scheduler.PendingSlot == 0, "Finish the matching native action.");
    scheduler.Tick(19, charging: true);
    Check(!scheduler.Finish(1, actor), "A duplicate completion must not consume the next charge.");
    Near(0.5, scheduler.GetGauge(1));
    Near(1, scheduler.GetGauge(2));
});

Case("Meters that filled during the same sample are ordered by readiness time", () =>
{
    var scheduler = new MeterActionScheduler(5);
    bool[] eligible = { false, true, true, true, true };
    scheduler.Observe(1, new IntPtr(11), 50, alive: true);
    scheduler.Observe(2, new IntPtr(12), 75, alive: true);
    scheduler.Observe(3, new IntPtr(13), 100, alive: true);
    scheduler.Observe(4, new IntPtr(14), 100, alive: true);
    scheduler.Tick(10, charging: true);
    scheduler.Tick(17, charging: true);
    foreach (int expected in new[] { 3, 4, 2, 1 })
    {
        int slot = scheduler.Select(eligible);
        Check(slot == expected, "Earliest full meter wins and equal times are ordered by slot.");
        var identity = new IntPtr(slot + 10);
        Check(scheduler.Admit(slot, identity) && scheduler.Finish(slot, identity),
            "Finish the selected actor before admitting the next.");
    }
});

Case("KO and slot reuse clear charge and pending action identity", () =>
{
    var scheduler = new MeterActionScheduler(2);
    var original = new IntPtr(11);
    var replacement = new IntPtr(12);
    bool[] eligible = { false, true };
    scheduler.Observe(1, original, 100, alive: true);
    scheduler.Tick(10, charging: true);
    scheduler.Tick(13, charging: true);
    Check(scheduler.Select(eligible) == 1 && scheduler.Admit(1, original), "Reserve the original actor.");
    scheduler.Observe(1, original, 100, alive: false);
    Check(scheduler.PendingSlot == 0 && !scheduler.Finish(1, original),
        "KO clears the reservation before a late completion.");
    scheduler.Tick(20, charging: true);
    Near(0, scheduler.GetGauge(1));
    Check(scheduler.Select(eligible) == 0, "A KO actor cannot dispatch even with an eligible native slot.");
    scheduler.Observe(1, original, 100, alive: true);
    scheduler.Tick(23, charging: true);
    Check(scheduler.Select(eligible) == 1, "The revived actor gets a fresh charge.");
    scheduler.Observe(1, replacement, 100, alive: true);
    Check(scheduler.PendingSlot == 0 && !scheduler.Admit(1, original) && scheduler.Select(eligible) == 0,
        "A new actor occupying the slot cannot inherit its predecessor's charge or reservation.");
    Near(0, scheduler.GetGauge(1));
    scheduler.Tick(26, charging: true);
    Check(scheduler.Select(eligible) == 1 && scheduler.Admit(1, replacement),
        "The replacement can dispatch after its own charge.");
    scheduler.Reset();
    Check(scheduler.PendingSlot == 0 && !scheduler.Finish(1, replacement),
        "Encounter reset invalidates the prior reservation.");
    Near(0, scheduler.GetGauge(1));
});

Case("Duplicate ticks and intervals across a pause do not credit charge", () =>
{
    var scheduler = new MeterActionScheduler(2);
    scheduler.Observe(1, new IntPtr(11), 50, alive: true);
    scheduler.Tick(10, charging: true);
    scheduler.Tick(11, charging: true);
    scheduler.Tick(11, charging: true);
    Near(1d / 6d, scheduler.GetGauge(1));
    scheduler.Tick(11.5, charging: false);
    scheduler.Tick(100, charging: false);
    scheduler.Tick(101, charging: true);
    Near(1d / 6d, scheduler.GetGauge(1));
    scheduler.Tick(102, charging: true);
    Near(2d / 6d, scheduler.GetGauge(1));
    scheduler.Tick(101, charging: true);
    scheduler.Tick(103, charging: true);
    Near(3d / 6d, scheduler.GetGauge(1));
});

Case("Full and partial SPD meters carry into the next native round", () =>
{
    var scheduler = new MeterActionScheduler(3);
    var hero = new IntPtr(11);
    var enemy = new IntPtr(12);
    bool[] eligible = { false, true, true };
    scheduler.Observe(1, hero, 100, alive: true);
    scheduler.Observe(2, enemy, 50, alive: true);

    scheduler.Tick(10, charging: true);
    scheduler.Tick(13, charging: true);
    Check(scheduler.Select(eligible) == 1 && scheduler.Admit(1, hero), "Admit the first-round hero.");
    scheduler.Tick(14, charging: true);
    Check(scheduler.Finish(1, hero), "Reset the hero after its first action.");
    eligible[1] = false;

    scheduler.Tick(16, charging: true);
    Check(scheduler.Select(eligible) == 2 && scheduler.Admit(2, enemy) && scheduler.Finish(2, enemy),
        "Admit and complete the first-round enemy.");
    eligible[2] = false;
    scheduler.Tick(19, charging: true);
    Near(1, scheduler.GetGauge(1));
    Near(0.5, scheduler.GetGauge(2));

    scheduler.Tick(19, charging: false); // Phase 5, round closing.
    scheduler.Tick(20, charging: false); // Phase 2, command menu.
    scheduler.Tick(180, charging: false); // Phase 3, next-round setup.
    scheduler.Observe(1, hero, 100, alive: true);
    scheduler.Observe(2, enemy, 50, alive: true);
    Near(1, scheduler.GetGauge(1));
    Near(0.5, scheduler.GetGauge(2));

    eligible[1] = eligible[2] = true;
    scheduler.Tick(181, charging: false);
    scheduler.Tick(181, charging: true); // Phase 4, next-round selection.
    Check(scheduler.Select(eligible) == 1 && scheduler.Admit(1, hero) && scheduler.Finish(1, hero),
        "The carried full meter can dispatch immediately at the next round.");
    eligible[1] = false;
    Near(0.5, scheduler.GetGauge(2));
    Check(scheduler.Select(eligible) == 0, "The carried partial meter must still reach full.");
    scheduler.Tick(184, charging: true);
    Check(scheduler.Select(eligible) == 2 && scheduler.Admit(2, enemy) && scheduler.Finish(2, enemy),
        "The enemy reaches full after the remaining three seconds of active charge.");
    Near(0, scheduler.GetGauge(2));
});

Case("A faster hero or enemy can act again before the slower side acts once", () =>
{
    foreach (int fast in new[] { 1, 2 })
    {
        var scheduler = new MeterActionScheduler(3);
        var identities = new[] { IntPtr.Zero, new IntPtr(11), new IntPtr(12) };
        var nativeFinished = new bool[3];
        int slow = 3 - fast;

        int SelectNative() => scheduler.Select(new[]
        {
            false,
            !nativeFinished[1] || scheduler.CanRepeat(1, identities[1]),
            !nativeFinished[2] || scheduler.CanRepeat(2, identities[2])
        });

        void Complete(int slot)
        {
            Check(scheduler.Admit(slot, identities[slot]) && scheduler.Finish(slot, identities[slot]),
                "Confirm the matching native action.");
            nativeFinished[slot] = true;
            Near(0, scheduler.GetGauge(slot));
        }

        scheduler.BeginRound();
        scheduler.Observe(fast, identities[fast], 100, alive: true);
        scheduler.Observe(slow, identities[slow], 25, alive: true);
        scheduler.Tick(10, charging: true);
        scheduler.Tick(13, charging: true);
        Check(SelectNative() == fast, "The faster side gets the first action.");
        scheduler.Tick(14, charging: true);
        Complete(fast);
        Near(1d / 3d, scheduler.GetGauge(slow));
        Check(SelectNative() == 0, "The spent actor must fully recharge before repeating.");

        scheduler.Tick(17, charging: true);
        Near(7d / 12d, scheduler.GetGauge(slow));
        Check(nativeFinished[fast] && !nativeFinished[slow] && SelectNative() == fast,
            "The faster side can act a second time in the same native round.");
        Complete(fast);
        Near(7d / 12d, scheduler.GetGauge(slow));
        Check(SelectNative() == 0, "The repeat spends the second full meter.");
    }
});

Case("Repeat eligibility belongs to a confirmed actor in the current round", () =>
{
    var scheduler = new MeterActionScheduler(3);
    var actor = new IntPtr(11);
    var other = new IntPtr(12);
    bool[] eligible = { false, true, false };
    scheduler.BeginRound();
    scheduler.Observe(1, actor, 100, alive: true);
    scheduler.Observe(2, other, 50, alive: true);
    scheduler.Tick(10, charging: true);

    void CompleteAt(double time)
    {
        scheduler.Tick(time, charging: true);
        Check(scheduler.Select(eligible) == 1 && scheduler.Admit(1, actor),
            "Reserve and admit the actor with a full meter.");
        Check(!scheduler.CanRepeat(1, actor), "Admission alone is not completion evidence.");
        Check(scheduler.Finish(1, actor) && scheduler.CanRepeat(1, actor),
            "A confirmed completion qualifies its actor for a later repeat.");
    }

    CompleteAt(13);
    Check(!scheduler.CanRepeat(1, other) && !scheduler.CanRepeat(2, other),
        "Another identity or slot cannot inherit repeat eligibility.");
    scheduler.Tick(14, charging: true);
    scheduler.BeginRound();
    Check(!scheduler.CanRepeat(1, actor), "A new round clears prior completion evidence.");
    Near(1d / 3d, scheduler.GetGauge(1));

    CompleteAt(16);
    scheduler.Observe(1, actor, 100, alive: false);
    scheduler.Observe(1, actor, 100, alive: true);
    Check(!scheduler.CanRepeat(1, actor), "Revival does not restore earlier completion evidence.");
    CompleteAt(19);
    scheduler.Observe(1, other, 100, alive: true);
    Check(!scheduler.CanRepeat(1, actor) && !scheduler.CanRepeat(1, other),
        "Slot replacement clears evidence for both the old and new identities.");
    actor = other;
    CompleteAt(22);
    scheduler.Reset();
    Check(!scheduler.CanRepeat(1, actor), "A new encounter cannot inherit repeat eligibility.");
});

Case("The closing round drains only repeats whose meters are already full", () =>
{
    var scheduler = new MeterActionScheduler(3);
    var hero = new IntPtr(11);
    var enemy = new IntPtr(12);
    bool[] eligible = { false, true, true };
    scheduler.BeginRound();
    scheduler.Observe(1, hero, 100, alive: true);
    scheduler.Observe(2, enemy, 50, alive: true);
    scheduler.Tick(10, charging: true);
    scheduler.Tick(13, charging: true);
    Check(scheduler.Select(eligible) == 1 && scheduler.Admit(1, hero), "Admit the hero's first action.");
    scheduler.Tick(13.5, charging: true);
    Check(scheduler.Finish(1, hero), "Confirm the hero's first action.");
    scheduler.Tick(16, charging: true);
    Check(scheduler.Select(eligible) == 2 && scheduler.Admit(2, enemy),
        "The enemy's earlier full meter has priority over the hero's repeat.");
    scheduler.Tick(17, charging: true);
    Check(scheduler.Finish(2, enemy), "The enemy's first action finishes the native round's first turns.");
    Check(scheduler.CanRepeat(1, hero) && scheduler.CanRepeat(2, enemy),
        "Both actors are now eligible to repeat if they already have full charge.");

    scheduler.Tick(17, charging: false);
    Check(scheduler.Select(eligible) == 1 && scheduler.Admit(1, hero), "Drain the hero's already-full repeat.");
    scheduler.Tick(25, charging: false);
    Check(scheduler.Finish(1, hero) && scheduler.Select(eligible) == 0,
        "No additional charge is generated while draining; native round closure can proceed.");
    Near(0, scheduler.GetGauge(1));
    Near(0, scheduler.GetGauge(2));
});

Case("A full hero meter stays reserved while the command menu pauses charge", () =>
{
    var scheduler = new MeterActionScheduler(3, secondsAtReferenceSpeed: 2);
    var gate = new HeroCommandGate();
    var hero = new IntPtr(11);
    var enemy = new IntPtr(12);
    bool[] eligible = { false, true, true };
    scheduler.Observe(1, hero, 50, alive: true);
    scheduler.Observe(2, enemy, 25, alive: true);
    scheduler.Tick(10, charging: true);
    scheduler.Tick(11.99, charging: true);
    Check(scheduler.Select(eligible) == 0, "The hero cannot choose a command before reaching full charge.");
    scheduler.Tick(12.01, charging: true);
    Check(scheduler.Select(eligible) == 1 && gate.Open(1, hero), "Reserve the ready hero and open their command menu.");
    Check(gate.Slot == 1 && gate.Identity == hero && gate.Choice.Kind == HeroCommandKind.None,
        "A fresh prompt belongs to the reserved actor and starts without a choice.");

    scheduler.Tick(12.01, charging: false);
    scheduler.Tick(90, charging: false);
    Check(scheduler.Select(eligible) == 0 && scheduler.PendingSlot == 1, "A second actor cannot overtake the reserved hero.");
    Check(!scheduler.Finish(1, hero), "Opening a prompt cannot consume the full meter.");
    Near(1, scheduler.GetGauge(1));
    Near(2.01 / 4, scheduler.GetGauge(2));

    var attack = new HeroCommand(HeroCommandKind.Attack, 2, enemy);
    Check(gate.Choose(1, hero, attack) && gate.Choice == attack, "Commit Attack with the selected target.");
    Near(1, scheduler.GetGauge(1));
    Check(!scheduler.Finish(1, hero), "Choosing a command still needs matching native admission and completion.");
    gate.Reset();
    scheduler.Tick(90, charging: true);
    Check(scheduler.Admit(1, hero) && scheduler.Finish(1, hero), "Native completion consumes the hero's meter.");
    Near(0, scheduler.GetGauge(1));
    Near(2.01 / 4, scheduler.GetGauge(2));
    scheduler.Tick(92, charging: true);
    Check(scheduler.Select(eligible) == 2, "Enemy scheduling resumes from the charge held during the prompt.");
});

Case("Only the reserved hero can confirm one structurally valid command", () =>
{
    var gate = new HeroCommandGate();
    var hero = new IntPtr(11);
    var other = new IntPtr(12);
    var defend = new HeroCommand(HeroCommandKind.Defend, 0, IntPtr.Zero);
    Check(!gate.Open(0, hero) && !gate.Open(1, IntPtr.Zero), "Reject an invalid prompt actor.");
    Check(gate.Open(1, hero) && !gate.Open(2, other), "Only one actor can have a prompt.");
    Check(!gate.Choose(2, hero, defend) && !gate.Choose(1, other, defend), "Reject a mismatched slot or actor.");
    Check(!gate.Choose(1, hero, default), "Reject an empty command.");
    Check(!gate.Choose(1, hero, new HeroCommand(HeroCommandKind.Attack, 0, other))
        && !gate.Choose(1, hero, new HeroCommand(HeroCommandKind.Attack, 2, IntPtr.Zero)),
        "Attack needs both a target slot and a target identity.");
    Check(!gate.Choose(1, hero, new HeroCommand(HeroCommandKind.Defend, 2, other))
        && !gate.Choose(1, hero, new HeroCommand(HeroCommandKind.Defend, 0, other))
        && !gate.Choose(1, hero, new HeroCommand(HeroCommandKind.Defend, 2, IntPtr.Zero)),
        "Defend carries no target.");
    Check(gate.Choice.Kind == HeroCommandKind.None, "Rejected commands leave the prompt open.");
    Check(gate.Choose(1, hero, defend) && gate.Choice == defend, "Accept the reserved hero's Defend command.");
    Check(!gate.Choose(1, hero, new HeroCommand(HeroCommandKind.Attack, 2, other)) && gate.Choice == defend,
        "A second input cannot replace the committed command.");

    gate.Reset();
    Check(gate.Slot == 0 && gate.Identity == IntPtr.Zero && gate.Choice.Kind == HeroCommandKind.None,
        "Clearing the prompt drops its actor and choice.");
    Check(!gate.Choose(1, hero, defend) && gate.Open(1, other) && !gate.Choose(1, hero, defend),
        "A late input cannot commit to a cleared prompt or a replacement in the same slot.");
});

Case("Canceling an unadmitted prompt preserves its charge and permits native closure", () =>
{
    var scheduler = new MeterActionScheduler(2, secondsAtReferenceSpeed: 2);
    var gate = new HeroCommandGate();
    var hero = new IntPtr(11);
    var replacement = new IntPtr(12);
    bool[] eligible = { false, true };
    scheduler.Observe(1, hero, 50, alive: true);
    scheduler.Tick(10, charging: true);
    scheduler.Tick(12, charging: true);
    Check(scheduler.Select(eligible) == 1 && gate.Open(1, hero), "Reserve a full actor for the prompt.");
    Check(!scheduler.CancelReservation(0, hero) && !scheduler.CancelReservation(1, replacement)
        && scheduler.PendingSlot == 1, "Only the reserved actor can release the pending selection.");
    Check(scheduler.CancelReservation(1, hero) && !scheduler.CancelReservation(1, hero),
        "An unadmitted reservation can be canceled once.");
    gate.Reset();
    Near(1, scheduler.GetGauge(1));
    eligible[1] = false;
    Check(scheduler.Select(eligible) == 0 && scheduler.PendingSlot == 0,
        "With no eligible actor, the selector is free to close the round.");
    eligible[1] = true;
    Check(scheduler.Select(eligible) == 1 && scheduler.Admit(1, hero),
        "A new selection can use the preserved charge.");
    Check(!scheduler.CancelReservation(1, hero), "Cancel cannot release a native action after admission.");
    Check(scheduler.Finish(1, hero), "The admitted actor can still complete normally.");
});

Case("Rune and Item choices require the correct resource and coherent target", () =>
{
    var gate = new HeroCommandGate();
    var hero = new IntPtr(11);
    var enemy = new IntPtr(22);
    var spell = new HeroCommandResource(0, 0, 5);
    var item = new HeroCommandResource(127, 34);
    Check(gate.Open(1, hero), "Open the hero's ready prompt.");
    Check(!gate.Choose(1, hero, new HeroCommand(HeroCommandKind.Attack, 2, enemy, item))
        && !gate.Choose(1, hero, new HeroCommand(HeroCommandKind.Defend, 0, IntPtr.Zero, spell)),
        "Attack and Defend do not use a resource.");

    foreach (var kind in new[] { HeroCommandKind.Rune, HeroCommandKind.Item })
    {
        var resource = kind == HeroCommandKind.Rune ? spell : item;
        Check(!gate.Choose(1, hero, new HeroCommand(kind, 2, enemy)),
            "Rune and Item need an identified resource.");
        Check(!gate.Choose(1, hero, new HeroCommand(kind, 2, enemy, resource with { Index = -1 }))
            && !gate.Choose(1, hero, new HeroCommand(kind, 2, enemy, resource with { Index = 128 })),
            "The native resource index must fit the nonnegative signed-byte range.");
        Check(!gate.Choose(1, hero, new HeroCommand(kind, 0, enemy, resource))
            && !gate.Choose(1, hero, new HeroCommand(kind, 2, IntPtr.Zero, resource)),
            "A targeted choice must include both target slot and identity.");
    }

    Check(!gate.Choose(1, hero, new HeroCommand(HeroCommandKind.Rune, 0, IntPtr.Zero, spell with { Id = -1 }))
        && !gate.Choose(1, hero, new HeroCommand(HeroCommandKind.Rune, 0, IntPtr.Zero, item))
        && !gate.Choose(1, hero, new HeroCommand(HeroCommandKind.Item, 0, IntPtr.Zero, item with { Id = 0 }))
        && !gate.Choose(1, hero, new HeroCommand(HeroCommandKind.Item, 0, IntPtr.Zero, spell with { Id = 34 })),
        "A spell identifies an equipped rune and an item identifies a positive item ID.");
    Check(gate.Choice.Kind == HeroCommandKind.None, "Invalid choices leave the prompt open.");

    var runeChoice = new HeroCommand(HeroCommandKind.Rune, 2, enemy, spell);
    var itemChoice = new HeroCommand(HeroCommandKind.Item, 0, IntPtr.Zero, item);
    Check(gate.Choose(1, hero, runeChoice) && gate.Choice == runeChoice, "Accept a targeted Rune choice.");
    Check(!gate.Choose(1, hero, itemChoice) && gate.Choice == runeChoice,
        "A second input cannot replace the reserved hero's committed choice.");
    gate.Reset();
    Check(gate.Open(1, hero) && gate.Choose(1, hero, itemChoice) && gate.Choice == itemChoice,
        "Accept an untargeted Item choice on a fresh prompt.");
});

Case("Refreshing a resource prompt preserves the reserved meter until native completion", () =>
{
    var scheduler = new MeterActionScheduler(3, secondsAtReferenceSpeed: 2);
    var gate = new HeroCommandGate();
    var hero = new IntPtr(11);
    var target = new IntPtr(22);
    bool[] eligible = { false, true, true };
    scheduler.Observe(1, hero, 50, alive: true);
    scheduler.Observe(2, target, 25, alive: true);
    scheduler.Tick(10, charging: true);
    scheduler.Tick(12, charging: true);
    Check(scheduler.Select(eligible) == 1 && gate.Open(1, hero), "Reserve the full meter for the hero's prompt.");
    var itemChoice = new HeroCommand(HeroCommandKind.Item, 1, hero, new HeroCommandResource(2, 34));
    Check(gate.Choose(1, hero, itemChoice), "Record the selected item for live validation.");
    scheduler.Tick(12, charging: false);

    // Reopen the prompt when the live inventory no longer contains the selected item.
    gate.Reset();
    Check(gate.Open(1, hero) && gate.Choice.Kind == HeroCommandKind.None,
        "A refreshed prompt drops the stale item choice.");
    scheduler.Tick(30, charging: false);
    Check(scheduler.PendingSlot == 1 && scheduler.Select(eligible) == 0 && !scheduler.Finish(1, hero),
        "Refreshing keeps the same reservation without admitting or completing the stale choice.");
    Near(1, scheduler.GetGauge(1));
    Near(0.5, scheduler.GetGauge(2));

    var runeChoice = new HeroCommand(HeroCommandKind.Rune, 0, IntPtr.Zero, new HeroCommandResource(1, 10, 7));
    Check(gate.Choose(1, hero, runeChoice) && !gate.Choose(1, hero, itemChoice),
        "A replacement choice can be committed once.");
    Check(scheduler.Admit(1, hero) && scheduler.Finish(1, hero) && !scheduler.Finish(1, hero),
        "The matching native action consumes the meter exactly once.");
    Near(0, scheduler.GetGauge(1));
    Near(0.5, scheduler.GetGauge(2));
});

Case("Ready heroes can confirm during an action and the finished actor refills immediately", () =>
{
    var scheduler = new MeterActionScheduler(4, secondsAtReferenceSpeed: 2);
    var first = new IntPtr(11);
    var second = new IntPtr(22);
    var third = new IntPtr(33);
    var input = new HeroCommandGate();
    scheduler.Observe(1, first, 50, alive: true);
    scheduler.Observe(2, second, 50, alive: true);
    scheduler.Observe(3, third, 50, alive: true);
    scheduler.Tick(10, charging: true);
    scheduler.Tick(12, charging: true);
    Check(scheduler.TryReserveReady(1, first) && scheduler.Admit(1, first), "Start the first hero's native action.");
    var defend = new HeroCommand(HeroCommandKind.Defend, 0, IntPtr.Zero);
    Check(input.Open(2, second) && input.Choose(2, second, defend), "Accept input during the first action.");
    input.Reset();
    Check(input.Open(3, third) && input.Choose(3, third, defend), "Accept another ready hero's input immediately.");
    Check(!scheduler.TryReserveReady(2, second) && scheduler.PendingSlot == 1, "Only one native action can run.");
    scheduler.Tick(13, charging: true);
    Check(scheduler.Finish(1, first), "Finish the first native action.");
    Near(0, scheduler.GetGauge(1));
    Check(scheduler.TryReserveReady(2, second) && scheduler.Admit(2, second), "Start the next confirmed action.");
    scheduler.Tick(13.5, charging: true);
    Near(0.25, scheduler.GetGauge(1));
    Near(1, scheduler.GetGauge(3));
    Check(scheduler.Finish(2, second) && scheduler.TryReserveReady(3, third),
        "The third hero keeps their full meter until their action starts.");
});

Case("A queued hero cannot dispatch after a KO or slot replacement", () =>
{
    var scheduler = new MeterActionScheduler(3, secondsAtReferenceSpeed: 2);
    var hero = new IntPtr(11);
    var enemy = new IntPtr(22);
    var replacement = new IntPtr(33);
    scheduler.Observe(1, hero, 50, alive: true);
    scheduler.Observe(2, enemy, 25, alive: true);
    scheduler.Tick(10, charging: true);
    scheduler.Tick(14, charging: true);
    Check(scheduler.TryReserveReady(2, enemy) && scheduler.Admit(2, enemy), "Start the enemy action.");
    scheduler.Observe(1, hero, 50, alive: false);
    Check(scheduler.Finish(2, enemy) && !scheduler.TryReserveReady(1, hero),
        "The hero loses their ready meter when the enemy knocks them out.");
    scheduler.Observe(1, replacement, 50, alive: true);
    scheduler.Tick(16, charging: true);
    Check(!scheduler.TryReserveReady(1, hero) && scheduler.TryReserveReady(1, replacement),
        "Only the replacement owns the new full meter.");
});

Case("Enemy DEX accelerates charge after the SPD floor and respects the rate cap", () =>
{
    var scheduler = new MeterActionScheduler(3, secondsAtReferenceSpeed: 2);
    Near(1, MeterActionScheduler.EnemyDexMultiplier(-10));
    Near(1.75, MeterActionScheduler.EnemyDexMultiplier(150));
    double boost = MeterActionScheduler.EnemyDexMultiplier(70);
    Near(1.525, boost);
    Near(100 / (27 * boost), scheduler.SecondsToFull(27, boost));
    Near(1, scheduler.SecondsToFull(100, boost));
    scheduler.Observe(1, new IntPtr(11), 10, alive: true);
    scheduler.Observe(2, new IntPtr(22), 10, alive: true, rateMultiplier: boost);
    scheduler.Tick(10, charging: true);
    scheduler.Tick(12, charging: true);
    Near(0.5, scheduler.GetGauge(1));
    Near(0.5 * boost, scheduler.GetGauge(2));
});

Case("A queued Attack keeps its reachable target or uses the first remaining reachable enemy", () =>
{
    var first = new HeroCommandTarget(6, new IntPtr(61), "First enemy");
    var chosen = new HeroCommandTarget(7, new IntPtr(71), "Chosen enemy");
    Check(HeroCommandTarget.SelectAttackTarget(new[] { first, chosen }, chosen.Slot, chosen.Identity) == chosen,
        "The original target takes priority when it is still in range.");
    Check(HeroCommandTarget.SelectAttackTarget(new[] { first }, chosen.Slot, chosen.Identity) == first,
        "A defeated or out-of-range target is replaced without new input.");
    var reusedSlot = chosen with { Identity = new IntPtr(72) };
    Check(HeroCommandTarget.SelectAttackTarget(new[] { first, reusedSlot }, chosen.Slot, chosen.Identity) == first,
        "A slot reused by a different enemy does not inherit target priority.");
    Check(HeroCommandTarget.SelectAttackTarget(Array.Empty<HeroCommandTarget>(), chosen.Slot, chosen.Identity).Slot == 0,
        "No reachable enemy leaves the attack waiting.");
});

Case("Opening charge scales with SPD and higher DEX can win a bounded bonus", () =>
{
    Near(0.03, MeterActionScheduler.OpeningCharge(30, 0, 0.5, 0, 0.5));
    Near(0.06, MeterActionScheduler.OpeningCharge(60, 0, 0.5, 0, 0.5));
    Near(0.01, MeterActionScheduler.OpeningCharge(30, 0, 0, 0, 0.5));
    Near(0.05, MeterActionScheduler.OpeningCharge(30, 0, 1, 0, 0.5));
    Near(0.03, MeterActionScheduler.OpeningCharge(30, 40, 0.5, 0.25, 0.5));
    Near(0.11, MeterActionScheduler.OpeningCharge(30, 60, 0.5, 0.25, 0.5));
    Near(0.12, MeterActionScheduler.OpeningCharge(150, 150, 1, 0.5, 1));
    Near(0.24, MeterActionScheduler.OpeningCharge(150, 150, 1, 0, 1));
    Near(0, MeterActionScheduler.OpeningCharge(-10, -10, 0, 0, 1));
});

Case("Opening charge breaks equal-SPD ties and refills start from empty", () =>
{
    var scheduler = new MeterActionScheduler(4, secondsAtReferenceSpeed: 2);
    var first = new IntPtr(11);
    var second = new IntPtr(22);
    scheduler.Observe(1, first, 50, alive: true);
    scheduler.Observe(2, second, 50, alive: true);
    scheduler.SeedOpeningCharge(1, 0.05);
    scheduler.SeedOpeningCharge(2, 0.15);
    scheduler.SeedOpeningCharge(3, 0.24);
    scheduler.Observe(2, second, 50, alive: true);
    Near(0.15, scheduler.GetGauge(2));
    Near(0, scheduler.GetGauge(3));

    scheduler.Tick(10, charging: true);
    scheduler.Tick(11.72, charging: true);
    Check(scheduler.Select(new[] { false, true, true, false }) == 2, "The larger head start wins the tie.");
    Near(1.72, scheduler.GetChargeSeconds(2));
    Check(scheduler.Admit(2, second) && scheduler.Finish(2, second), "Finish the opening action.");
    Near(0, scheduler.GetGauge(2));
    scheduler.Tick(11.92, charging: true);
    scheduler.BeginRound();
    Near(0.10, scheduler.GetGauge(2));
    Near(1, scheduler.GetGauge(1));
    scheduler.Observe(1, new IntPtr(33), 50, alive: true);
    Near(0, scheduler.GetGauge(1));
});

Case("CTB bridge ignores frame clocks and completes its selected native actor once", () =>
{
    var scheduler = new MeterActionScheduler(3) { UseCtb = true };
    var fast = new IntPtr(11);
    var slow = new IntPtr(22);
    scheduler.Observe(1, fast, 20, true);
    scheduler.Observe(2, slow, 10, true);
    double firstDue = scheduler.Ctb.GetNextReady(1);
    scheduler.Tick(0, true);
    scheduler.Tick(3600, true);
    Near(0, scheduler.Ctb.Now);
    Near(firstDue, scheduler.Ctb.GetNextReady(1));
    Check(scheduler.Select(new[] { false, true, true }) == 1, "Native slot zero remains a sentinel.");
    scheduler.Tick(999999, true);
    Near(firstDue, scheduler.Ctb.Now);
    Check(!scheduler.Finish(1, fast) && scheduler.Admit(1, fast), "A command is not a completed native action.");
    Check(scheduler.Finish(1, fast) && !scheduler.Finish(1, fast), "Completion is exactly once.");
    Near(firstDue * 2, scheduler.Ctb.GetNextReady(1));
});

Case("CTB native round maintenance preserves future turns and repeat eligibility", () =>
{
    var scheduler = new MeterActionScheduler(3) { UseCtb = true };
    var hero = new IntPtr(11);
    var enemy = new IntPtr(22);
    scheduler.Observe(1, hero, 100, true);
    scheduler.Observe(2, enemy, 25, true);
    var mask = new[] { false, true, true };
    Check(scheduler.Select(mask) == 1 && scheduler.Admit(1, hero) && scheduler.Finish(1, hero), "First hero turn.");
    double due = scheduler.Ctb.GetNextReady(2);
    scheduler.BeginRound();
    scheduler.Tick(98765, false);
    Near(due, scheduler.Ctb.GetNextReady(2));
    Check(scheduler.CanRepeat(1, hero), "Native bookkeeping must not impose a full-party turn barrier.");
    Check(scheduler.Select(mask) == 1, "Fast hero acts again before the slower enemy.");
});

Case("CTB Wait advances a selected turn without admitting a native action", () =>
{
    var scheduler = new MeterActionScheduler(3) { UseCtb = true };
    var hero = new IntPtr(11);
    var enemy = new IntPtr(22);
    scheduler.Observe(1, hero, 100, true);
    scheduler.Observe(2, enemy, 100, true);
    var mask = new[] { false, true, true };
    Check(scheduler.Select(mask) == 1, "Choose hero.");
    double now = scheduler.Ctb.Now;
    Check(scheduler.SpendCtbMenuTurn(1, hero, waitCost), "Wait consumes the selected opportunity.");
    Check(scheduler.PendingSlot == 0 && !scheduler.IsAdmitted, "Both reservations clear after Wait.");
    Check(scheduler.Ctb.GetNextReady(1) > now && !scheduler.SpendCtbMenuTurn(1, hero, waitCost), "Wait has positive recovery and cannot repeat without a turn.");
    Check(scheduler.Select(mask) == 2, "Waiting yields to the enemy at the same timestamp.");
});

Case("CTB forecasts preserve native selection exclusions and invalidate replaced candidates", () =>
{
    var scheduler = new MeterActionScheduler(4) { UseCtb = true };
    var hero = new IntPtr(11);
    scheduler.Observe(1, hero, 200, true);
    scheduler.Observe(2, new IntPtr(22), 150, true);
    scheduler.Observe(3, new IntPtr(33), 140, true);
    // All three are living. Native readiness rejected slot 2 at turn selection.
    scheduler.CtbEligible[1] = true;
    scheduler.CtbEligible[3] = true;
    Check(scheduler.Select(scheduler.CtbEligible) == 1, "Expected the hero to own the command.");
    var candidates = new[] { false, true, true, true };
    var forecast = scheduler.ForecastCtb(4, candidates, hero, 1);
    Check(forecast[0].Current && forecast[0].Slot == 1 && forecast[1].Slot == 3
        && forecast.All(entry => entry.Slot != 2), "Forecast admitted a native-ineligible actor.");
    Check(scheduler.ForecastCtb(4, candidates, IntPtr.Zero, 1).Length == 0
        && scheduler.ForecastCtb(4, candidates, new IntPtr(44), 1).Length == 0,
        "A KO or replacement in the pending slot inherited the forecast before the next observation.");
    var spent = scheduler.ForecastCtb(4, new[] { false, false, true, true }, hero, 1);
    Check(spent[0].Current && spent[0].Identity == hero.ToInt64() && spent.Skip(1).All(entry => entry.Slot == 3),
        "A valid pending actor may own the current action while being ineligible for future turns.");
    Check(scheduler.Admit(1, hero) && scheduler.Finish(1, hero)
        && scheduler.Select(scheduler.CtbEligible) == forecast[1].Slot, "Next native selection differed from the preview.");

    scheduler.Observe(3, new IntPtr(44), 140, true);
    Check(scheduler.PendingSlot == 0 && !scheduler.CtbEligible[3], "A replacement inherited its predecessor's eligibility.");
    var changed = scheduler.ForecastCtb(4, new[] { false, false, true, true }, IntPtr.Zero, 1);
    Check(changed.Length == 0, "Newly blocked or replaced actors were projected before the next selection.");
    scheduler.Reset();
    Check(!scheduler.CtbEligible[1], "A new encounter retained a previous selection decision.");
});

Case("CTB bridge clears an unadmitted command when native actor identity changes", () =>
{
    var scheduler = new MeterActionScheduler(2) { UseCtb = true };
    var oldActor = new IntPtr(11);
    var replacement = new IntPtr(12);
    scheduler.Observe(1, oldActor, 50, true);
    Check(scheduler.Select(new[] { false, true }) == 1, "Reserve command.");
    scheduler.Observe(1, replacement, 50, true);
    Check(scheduler.PendingSlot == 0 && scheduler.Ctb.PendingSlot == -1, "Native and CTB reservations must both clear.");
    Check(!scheduler.Admit(1, oldActor) && !scheduler.SpendCtbMenuTurn(1, oldActor, waitCost), "Stale identity cannot act or Wait.");
    Check(scheduler.Select(new[] { false, true }) == 1 && scheduler.Admit(1, replacement), "Replacement can receive its own turn.");
});

Case("An early full menu gains turn ownership only when CTB promotes it", () =>
{
    var scheduler = new MeterActionScheduler(3) { UseCtb = true };
    var hero = new IntPtr(11);
    scheduler.Observe(1, hero, 100, true);
    bool[] mask = { false, true, false };
    Check(scheduler.Select(mask) == 1 && scheduler.Admit(1, hero), "Admit the first action.");
    Check(!NativeHeroMenuInputPolicy.OwnsTurn(2, scheduler.PendingSlot, queued: true, activeAtbInput: false)
        && !NativeHeroMenuInputPolicy.OwnsTurn(1, scheduler.PendingSlot, queued: true, activeAtbInput: false),
        "An early menu must allow playback for both a different ally and the same hero acting twice.");
    Check(scheduler.Finish(1, hero) && scheduler.Select(mask) == 1, "Reserve the following turn.");
    Check(NativeHeroMenuInputPolicy.OwnsTurn(1, scheduler.PendingSlot, queued: false, activeAtbInput: false)
        && scheduler.SpendCtbMenuTurn(1, hero, waitCost), "The promoted native menu can now execute its Wait.");
});

Case("Native Wait row confirmation exits as party command three without invoking a hero command", () =>
{
    var decision = NativeHeroMenuInputPolicy.Evaluate(6, new[] { true, true, true, true },
        confirm: true, cancel: false, up: false, down: false);
    Check(decision.Kind == NativeHeroMenuInputKind.ConfirmParty && decision.PartyCommand == 3,
        "The extended final row must route to the mode-owned Auto/Wait handoff.");
});

Case("Idle or canceled Wait cannot fall through to stock Auto or consume a CTB opportunity", () =>
{
    var scheduler = new MeterActionScheduler(2) { UseCtb = true };
    var hero = new IntPtr(11);
    scheduler.Observe(1, hero, 100, true);
    scheduler.Select(new[] { false, true });
    double due = scheduler.Ctb.GetNextReady(1);
    var enabled = new[] { true, true, true, true };
    var idle = NativeHeroMenuInputPolicy.Evaluate(6, enabled, false, false, false, false);
    var canceled = NativeHeroMenuInputPolicy.Evaluate(6, enabled, false, true, false, false);
    Check(idle.Kind == NativeHeroMenuInputKind.Consume && idle.PartyCommand == 0,
        "Idle on Wait must not execute the stock handler or select a party action.");
    Check(canceled.Kind == NativeHeroMenuInputKind.Cancel && canceled.PartyCommand == 0,
        "Cancel must keep the selected opportunity without choosing Wait.");
    Check(scheduler.PendingSlot == 1 && !scheduler.IsAdmitted, "Menu input must not consume initiative.");
    Near(due, scheduler.Ctb.GetNextReady(1));
});

Case("A confirmed native Wait consumes its selected CTB opportunity once", () =>
{
    var scheduler = new MeterActionScheduler(3) { UseCtb = true };
    var hero = new IntPtr(11);
    scheduler.Observe(1, hero, 100, true);
    scheduler.Observe(2, new IntPtr(22), 100, true);
    var mask = new[] { false, true, true };
    Check(scheduler.Select(mask) == 1, "Hero should own the native command menu.");
    var choice = NativeHeroMenuInputPolicy.Evaluate(6, new[] { true, true, true, true }, true, false, false, false);
    Check(choice.Kind == NativeHeroMenuInputKind.ConfirmParty && choice.PartyCommand == 3
        && scheduler.SpendCtbMenuTurn(1, hero, waitCost), "Native Wait must reach the CTB handoff.");
    Check(!scheduler.SpendCtbMenuTurn(1, hero, waitCost) && scheduler.PendingSlot == 0 && scheduler.Ctb.PendingSlot == -1,
        "Duplicate menu completion must not consume another opportunity.");
    Check(scheduler.Select(mask) == 2, "The next actor must be free to proceed after native Wait.");
});

Case("A failed Run spends one full turn and preserves tied allies and enemies in CTB order", () =>
{
    var scheduler = new MeterActionScheduler(4) { UseCtb = true };
    var runner = new IntPtr(11);
    var ally = new IntPtr(22);
    var enemy = new IntPtr(33);
    scheduler.Observe(1, runner, 100, true);
    scheduler.Observe(2, ally, 100, true);
    scheduler.Observe(3, enemy, 100, true);
    var eligible = new[] { false, true, true, true };
    Check(scheduler.Select(eligible) == 1, "Opening tie should select the runner first.");
    double opening = scheduler.Ctb.Now;
    double recovery = CtbActionTiming.Describe(CtbActionKind.Attack).Multiplier;
    Check(!scheduler.SpendCtbMenuTurn(1, ally, recovery), "A different actor cannot spend the runner's turn.");
    Check(scheduler.SpendCtbMenuTurn(1, runner, recovery), "The runner should spend the selected opportunity.");
    Near(opening + scheduler.Ctb.RecoveryForSpeed(100), scheduler.Ctb.GetNextReady(1));
    Near(opening, scheduler.Ctb.GetNextReady(2));
    Near(opening, scheduler.Ctb.GetNextReady(3));
    Check(!scheduler.SpendCtbMenuTurn(1, runner, recovery) && !scheduler.Finish(1, runner),
        "Repeated failure feedback must not spend another turn.");
    Check(scheduler.Select(eligible) == 2 && scheduler.Admit(2, ally) && scheduler.Finish(2, ally),
        "The tied ally should receive the next opportunity.");
    Check(scheduler.Select(eligible) == 3 && scheduler.Admit(3, enemy) && scheduler.Finish(3, enemy),
        "The tied enemy should follow without waiting for a new native round.");
    Check(scheduler.Select(eligible) == 1, "The runner should return after normal recovery.");
});

Case("Native hero command confirmations remain with native Attack Rune and Item selectors", () =>
{
    var enabled = new[] { true, true, true, true };
    for (int row = 0; row < enabled.Length; row++)
    {
        var decision = NativeHeroMenuInputPolicy.Evaluate(row, enabled, true, false, false, false);
        Check(decision.Kind == NativeHeroMenuInputKind.PassNative && decision.Row == row && decision.PartyCommand == 0,
            "Hero commands must retain native resource and target selection.");
    }
});

Case("Native extended menu preserves Run Bribe and Auto or Wait command numbers", () =>
{
    var enabled = new[] { true, true, true, true };
    for (int row = 4; row < 7; row++)
    {
        var decision = NativeHeroMenuInputPolicy.Evaluate(row, enabled, true, false, false, false);
        Check(decision.Kind == NativeHeroMenuInputKind.ConfirmParty && decision.PartyCommand == row - 3,
            "The stock party-command number changed.");
    }
});

Case("Native menu wraps through Wait and skips disabled hero commands", () =>
{
    var enabled = new[] { true, false, false, true };
    var up = NativeHeroMenuInputPolicy.Evaluate(0, enabled, false, false, true, false);
    Check(up.Kind == NativeHeroMenuInputKind.Move && up.Row == 6, "Up from Attack should reach Wait or Auto.");
    var down = NativeHeroMenuInputPolicy.Evaluate(6, enabled, false, false, false, true);
    Check(down.Kind == NativeHeroMenuInputKind.Move && down.Row == 0, "Down from Wait should wrap to Attack.");
    var skip = NativeHeroMenuInputPolicy.Evaluate(0, enabled, false, false, false, true);
    Check(skip.Kind == NativeHeroMenuInputKind.Move && skip.Row == 3, "Disabled hero commands must be skipped.");
    var reverse = NativeHeroMenuInputPolicy.Evaluate(3, enabled, false, false, true, false);
    Check(reverse.Row == 0, "Reverse navigation must skip the same disabled commands.");
});

Case("All disabled hero commands still leave a finite path among party rows", () =>
{
    var disabled = new[] { false, false, false, false };
    int row = 6;
    for (int i = 0; i < 100; i++)
    {
        var decision = NativeHeroMenuInputPolicy.Evaluate(row, disabled, false, false, false, true);
        Check(decision.Kind == NativeHeroMenuInputKind.Move && decision.Row is >= 4 and <= 6,
            "Disabled hero rows should never trap the native cursor.");
        row = decision.Row;
    }
    Check(NativeHeroMenuInputPolicy.Evaluate(4, disabled, false, false, true, false).Row == 6,
        "Reverse navigation should wrap directly between enabled party rows.");
});

Case("Opposing directions do not move and confirmation keeps native input priority", () =>
{
    var enabled = new[] { true, true, true, true };
    var opposing = NativeHeroMenuInputPolicy.Evaluate(6, enabled, false, false, true, true);
    Check(opposing.Kind == NativeHeroMenuInputKind.Consume && opposing.Row == 6 && opposing.PartyCommand == 0,
        "Simultaneous Up and Down should leave Wait unselected.");
    var confirm = NativeHeroMenuInputPolicy.Evaluate(6, enabled, true, true, true, false);
    Check(confirm.Kind == NativeHeroMenuInputKind.ConfirmParty && confirm.PartyCommand == 3,
        "Confirm must retain priority over Cancel and repeated directions.");
});

Case("Committed command recovery drives the admitted forecast and actual completion through the same bridge", () =>
{
    foreach (var command in new[]
    {
        (CtbActionKind.Wait, 0), (CtbActionKind.Defend, 0), (CtbActionKind.Attack, 0),
        (CtbActionKind.Item, 0), (CtbActionKind.Rune, 1), (CtbActionKind.Rune, 2),
        (CtbActionKind.Rune, 3), (CtbActionKind.Rune, 4), (CtbActionKind.Rune, 0),
        (CtbActionKind.EnemyAttack, 0)
    })
    {
        var scheduler = new MeterActionScheduler(3) { UseCtb = true };
        var hero = new IntPtr(11);
        var enemy = new IntPtr(22);
        scheduler.Observe(1, hero, 100, true);
        scheduler.Observe(2, enemy, 75, true);
        var mask = new[] { false, true, true };
        Check(scheduler.Select(mask) == 1 && scheduler.Admit(1, hero), "The native action must own its opportunity.");
        var cost = CtbActionTiming.Describe(command.Item1, command.Item2);
        scheduler.SetCtbRecovery(cost.Multiplier);
        Near(cost.Multiplier, scheduler.CtbRecoveryMultiplier);
        var forecast = scheduler.Ctb.Forecast(16, mask, scheduler.CtbRecoveryMultiplier);
        var returning = forecast.First(entry => !entry.Current && entry.Slot == 1 && entry.Identity == hero.ToInt64());
        Check(scheduler.IsAdmitted && scheduler.Ctb.IsAdmitted, "Playback preview must retain native ownership.");
        Check(scheduler.Finish(1, hero), "Native completion should consume the committed command cost.");
        Near(returning.ReadyAt, scheduler.Ctb.GetNextReady(1));
        Near(1, scheduler.CtbRecoveryMultiplier);
        Check(scheduler.PendingSlot == 0 && scheduler.Ctb.PendingSlot == -1 && !scheduler.IsAdmitted,
            "Finishing must clear both reservations and the old command cost.");
        Check(!scheduler.Finish(1, hero), "Repeated native completion must not reschedule a command twice.");
    }
});

Case("Invalid committed recovery is rejected without replacing the last valid native command cost", () =>
{
    var scheduler = new MeterActionScheduler(2) { UseCtb = true };
    var hero = new IntPtr(11);
    scheduler.Observe(1, hero, 100, true);
    var mask = new[] { false, true };
    Check(scheduler.Select(mask) == 1 && scheduler.Admit(1, hero), "Admit the Rune action.");
    double multiplier = CtbActionTiming.RecoveryMultiplier(CtbActionKind.Rune, 3);
    scheduler.SetCtbRecovery(multiplier);
    var before = scheduler.Ctb.Forecast(5, mask, scheduler.CtbRecoveryMultiplier);
    foreach (double invalid in new[] { 0, -1, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
    {
        bool rejected = false;
        try { scheduler.SetCtbRecovery(invalid); }
        catch (ArgumentOutOfRangeException) { rejected = true; }
        Check(rejected, "Invalid recovery must not enter the native-completion bridge.");
        Near(multiplier, scheduler.CtbRecoveryMultiplier);
        Check(scheduler.IsAdmitted && scheduler.PendingSlot == 1, "Rejected recovery changed native ownership.");
    }
    var after = scheduler.Ctb.Forecast(5, mask, scheduler.CtbRecoveryMultiplier);
    Check(before.Select(entry => (entry.Slot, entry.ReadyAt)).SequenceEqual(after.Select(entry => (entry.Slot, entry.ReadyAt))),
        "Rejected values changed the committed preview.");
    Check(scheduler.Finish(1, hero), "The original valid native command must still complete.");
    Near(2550, scheduler.Ctb.GetNextReady(1));
    Near(1, scheduler.CtbRecoveryMultiplier);
});

Case("CTB Wait uses the half interval once and released commands cannot leak recovery into later turns", () =>
{
    var scheduler = new MeterActionScheduler(3) { UseCtb = true };
    var hero = new IntPtr(11);
    scheduler.Observe(1, hero, 100, true);
    scheduler.Observe(2, new IntPtr(22), 100, true);
    var mask = new[] { false, true, true };
    Check(scheduler.Select(mask) == 1, "Reserve the hero's native menu.");
    scheduler.SetCtbRecovery(CtbActionTiming.RecoveryMultiplier(CtbActionKind.Rune, 4));
    double started = scheduler.Ctb.Now;
    double wait = CtbActionTiming.RecoveryMultiplier(CtbActionKind.Wait);
    var preview = scheduler.Ctb.Forecast(8, mask, wait);
    var returning = preview.First(entry => !entry.Current && entry.Slot == 1);
    Check(!scheduler.SpendCtbMenuTurn(1, new IntPtr(99), waitCost) && scheduler.SpendCtbMenuTurn(1, hero, waitCost),
        "Only the owning native actor may consume Wait.");
    Near(started + scheduler.Ctb.RecoveryForSpeed(100, wait), scheduler.Ctb.GetNextReady(1));
    Near(returning.ReadyAt, scheduler.Ctb.GetNextReady(1));
    Check(!scheduler.SpendCtbMenuTurn(1, hero, waitCost) && !scheduler.Finish(1, hero), "Wait must not also complete as a native attack.");
    Check(scheduler.PendingSlot == 0 && scheduler.Ctb.PendingSlot == -1 && !scheduler.IsAdmitted,
        "Wait must release the native menu and logical reservation together.");
    Near(1, scheduler.CtbRecoveryMultiplier);
    Check(scheduler.Select(mask) == 2, "The equal-time enemy should act after the hero yields.");
    Check(scheduler.Admit(2, new IntPtr(22)) && scheduler.Finish(2, new IntPtr(22)),
        "The next native enemy should complete with a fresh default cost.");
    Near(2000, scheduler.Ctb.GetNextReady(2));

    Check(scheduler.Select(mask) == 1, "The waiting hero should receive its next menu.");
    scheduler.SetCtbRecovery(CtbActionTiming.RecoveryMultiplier(CtbActionKind.Rune, 3));
    Check(!scheduler.CancelReservation(1, new IntPtr(99)), "A different actor must not cancel the command.");
    Near(1.55, scheduler.CtbRecoveryMultiplier);
    Check(scheduler.CancelReservation(1, hero), "Cancel the actual command owner.");
    Near(1, scheduler.CtbRecoveryMultiplier);

    Check(scheduler.Select(mask) == 1, "A canceled opportunity should remain available.");
    scheduler.SetCtbRecovery(CtbActionTiming.RecoveryMultiplier(CtbActionKind.Rune, 4));
    scheduler.Observe(1, hero, 100, false);
    Check(scheduler.PendingSlot == 0 && scheduler.Ctb.PendingSlot == -1, "KO must release both reservations.");
    Near(1, scheduler.CtbRecoveryMultiplier);
    Check(scheduler.Select(mask) == 2 && scheduler.Admit(2, new IntPtr(22))
        && scheduler.Finish(2, new IntPtr(22)), "A KO'd command must not change the surviving enemy's recovery.");
    Near(3000, scheduler.Ctb.GetNextReady(2));
});

Console.WriteLine($"{passed} ATB / CTB bridge checks passed.");
