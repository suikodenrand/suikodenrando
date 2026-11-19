using SuikodenRandomizer.Core.Combat;

int passed = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
void Near(double expected, double actual)
    => Check(Math.Abs(expected - actual) < 1e-8, $"Expected {expected}, got {actual}.");
void Case(string name, Action action)
{
    action();
    Console.WriteLine("PASS " + name);
    passed++;
}
void Throws<T>(Action action) where T : Exception
{
    try { action(); }
    catch (T) { return; }
    throw new InvalidOperationException("Expected " + typeof(T).Name);
}
int Execute(CtbTimeline timeline, bool[] mask, double recovery = 1)
{
    int slot = timeline.Select(mask);
    Check(slot >= 0, "A turn was expected.");
    long id = timeline.GetIdentity(slot);
    Check(timeline.Admit(slot, id), "Native admission must succeed.");
    Check(timeline.Complete(slot, id, recovery), "Confirmed completion must succeed.");
    return slot;
}

Case("SPD controls initiative and faster actors repeat independently", () =>
{
    var timeline = new CtbTimeline(2);
    timeline.Observe(0, 10, 200, true);
    timeline.Observe(1, 11, 50, true);
    bool[] mask = { true, true };
    int[] actual = Enumerable.Range(0, 8).Select(_ => Execute(timeline, mask)).ToArray();
    Check(actual.SequenceEqual(new[] { 0, 0, 0, 0, 1, 0, 0, 0 }), "Fast actors must not wait for a stock round barrier.");
    Near(3500, timeline.Now);
});

Case("Logical time has no frame, animation, or thinking-time input", () =>
{
    var first = new CtbTimeline(3);
    for (int i = 0; i < 3; i++) first.Observe(i, i + 1, 75 + 25 * i, true);
    var second = first.Clone();
    bool[] mask = { true, true, true };
    for (int i = 0; i < 40; i++)
    {
        int a = Execute(first, mask);
        for (int sample = 0; sample < 30; sample++)
            for (int slot = 0; slot < 3; slot++) second.Observe(slot, slot + 1, 75 + 25 * slot, true);
        Check(a == Execute(second, mask), "Observing extra frames must not change initiative.");
        Near(first.Now, second.Now);
    }
});

Case("Early-game SPD values retain distinct initiative", () =>
{
    var timeline = new CtbTimeline(3);
    timeline.Observe(0, 10, 10, true);
    timeline.Observe(1, 11, 20, true);
    timeline.Observe(2, 12, 25, true);
    Near(10000, timeline.GetNextReady(0));
    Near(5000, timeline.GetNextReady(1));
    Near(4000, timeline.GetNextReady(2));
    Check(Execute(timeline, new[] { true, true, true }) == 2, "Low SPD actors collapsed into a shared minimum rate.");
    Near(100000, timeline.RecoveryForSpeed(0));
    Near(100000, timeline.RecoveryForSpeed(-10));
});

Case("Slot zero is valid and equal-time ties remain stable", () =>
{
    var timeline = new CtbTimeline(3);
    for (int i = 0; i < 3; i++) timeline.Observe(i, i + 1, 100, true);
    bool[] mask = { true, true, true };
    for (int i = 0; i < 9; i++) Check(Execute(timeline, mask) == i % 3, "Stable slot ordering was lost.");
});

Case("One reservation and exactly one admission and completion", () =>
{
    var timeline = new CtbTimeline(2);
    timeline.Observe(0, 10, 100, true);
    timeline.Observe(1, 11, 100, true);
    bool[] mask = { true, true };
    Check(timeline.Select(mask) == 0 && timeline.Select(mask) == -1, "Concurrent turns must not be selected.");
    Check(!timeline.Complete(0, 10) && !timeline.Admit(0, 99), "Unadmitted or stale identity completed.");
    Check(timeline.Admit(0, 10) && !timeline.Admit(0, 10), "Duplicate admission accepted.");
    Check(!timeline.CancelReservation(0, 10), "Admitted native action cannot be canceled by menu logic.");
    Check(timeline.Complete(0, 10) && !timeline.Complete(0, 10), "Duplicate completion accepted.");
    Near(2000, timeline.GetNextReady(0));
});

Case("KO clears a pending command and revival gets fresh initiative", () =>
{
    var timeline = new CtbTimeline(2);
    timeline.Observe(0, 10, 100, true);
    timeline.Observe(1, 11, 50, true);
    bool[] mask = { true, true };
    Check(timeline.Select(mask) == 0, "Wrong initial actor.");
    timeline.Observe(0, 10, 100, false);
    Check(timeline.PendingSlot == -1 && !timeline.Admit(0, 10), "KO left a queued command.");
    Check(Execute(timeline, mask) == 1, "Surviving enemy must continue.");
    timeline.Observe(0, 10, 100, true);
    Near(timeline.Now + 1000, timeline.GetNextReady(0));
});

Case("Playback lookahead matches the next real reservation for consecutive and alternating turns", () =>
{
    var timeline = new CtbTimeline(5);
    bool[] mask = { false, true, true, true, true };
    int[] speeds = { 0, 120, 120, 100, 100 };
    for (int slot = 1; slot <= 4; slot++) timeline.Observe(slot, slot, speeds[slot], true);
    var pairs = new HashSet<(bool, bool)>();
    for (int turn = 0; turn < 50; turn++)
    {
        int slot = timeline.Select(mask);
        Check(timeline.Admit(slot, slot), "Could not admit current actor.");
        double now = timeline.Now;
        double cost = turn % 2 == 0 ? 0.75 : 1.55;
        var next = timeline.PeekOpportunity(mask, cost)!.Value;
        var copy = timeline.Forecast(2, mask, cost)[1];
        Check(next.Slot == copy.Slot && next.Identity == copy.Identity, "Lookahead disagrees with the strip.");
        Near(next.ReadyAt, copy.ReadyAt);
        Near(now, timeline.Now);
        Check(timeline.IsAdmitted && timeline.PendingSlot == slot, "Lookahead released the actor still playing.");
        Check(timeline.Complete(slot, slot, cost), "Could not complete current actor.");
        var actual = timeline.PeekOpportunity(mask)!.Value;
        Check(actual.Slot == next.Slot, "Completion changed an unaffected next slot.");
        Near(next.ReadyAt, actual.ReadyAt);
        pairs.Add((slot <= 2, next.Slot <= 2));
    }
    Check(pairs.Count == 4, "Exercise both same-side handoffs and both side changes.");
});

Case("An early choice can lose its turn to KO, a changed order or the acting hero's own return", () =>
{
    var timeline = new CtbTimeline(4);
    bool[] mask = { false, true, true, true };
    timeline.Observe(1, 11, 200, true);
    timeline.Observe(2, 22, 110, true);
    timeline.Observe(3, 33, 100, true);
    Check(timeline.Select(mask) == 1 && timeline.Admit(1, 11), "Expected first hero.");
    Check(timeline.PeekOpportunity(mask, 0.75)!.Value.Slot == 1, "Fast hero should win a second opportunity.");
    Check(timeline.PeekOpportunity(mask, 1.55)!.Value.Slot == 2, "Rune recovery should allow the other hero ahead.");
    timeline.Advance(3, 33, 0.25);
    Check(timeline.PeekOpportunity(mask, 1.55)!.Value.Slot == 3, "Advancing an opponent should interrupt the chain.");
    timeline.Observe(3, 33, 100, false);
    timeline.Observe(2, 22, 110, false);
    Check(timeline.PeekOpportunity(mask, 1.55)!.Value.Slot == 1, "Dead actors were still projected.");
    Array.Clear(mask);
    Check(!timeline.PeekOpportunity(mask, 1.55).HasValue, "Empty mask created a follow-up.");
});

Case("Changing actor identity discards readiness and Break state", () =>
{
    var timeline = new CtbTimeline(1);
    timeline.Observe(0, 10, 100, true);
    timeline.ApplyBreak(0, 10, 100);
    timeline.Observe(0, 20, 100, true);
    Check(!timeline.IsBroken(0) && timeline.GetBreakRemaining(0) == 100, "Replacement inherited the old actor's Break.");
    Check(!timeline.ApplyBreak(0, 10, 100).Accepted && timeline.Delay(0, 10) == 0, "Stale effects affected replacement.");
});

Case("Eligibility masks exclude statuses without deleting actor state", () =>
{
    var timeline = new CtbTimeline(2);
    timeline.Observe(0, 10, 200, true);
    timeline.Observe(1, 11, 100, true);
    bool[] mask = { false, true };
    Check(Execute(timeline, mask) == 1, "Excluded actor was selected.");
    mask[0] = true;
    Check(timeline.Select(mask) == 0, "Eligible returning actor lost existing initiative.");
    Near(1000, timeline.Now);
});

Case("SPD changes preserve remaining readiness and unchanged observations are inert", () =>
{
    var timeline = new CtbTimeline(2);
    timeline.Observe(0, 10, 100, true);
    timeline.Observe(1, 11, 200, true);
    Execute(timeline, new[] { true, true });
    Near(500, timeline.Now);
    timeline.Observe(0, 10, 200, true);
    Near(750, timeline.GetNextReady(0));
    for (int i = 0; i < 20; i++) timeline.Observe(0, 10, 200, true);
    Near(750, timeline.GetNextReady(0));
});

Case("Wait incurs positive recovery even with a tiny cost", () =>
{
    var timeline = new CtbTimeline(1);
    timeline.Observe(0, 10, int.MaxValue, true);
    bool[] mask = { true };
    for (int i = 0; i < 20; i++)
    {
        Check(timeline.Select(mask) == 0, "Wait lost its actor.");
        double before = timeline.Now;
        Check(timeline.Wait(0, 10, 1e-12), "Wait failed.");
        Check(timeline.GetNextReady(0) >= before + timeline.MinimumRecovery, "Wait created a zero-time loop.");
    }
    Check(!timeline.Wait(0, 10), "Wait was consumed twice.");
});

Case("Delay has a per-recipient cap and cannot cause permanent turn denial", () =>
{
    var timeline = new CtbTimeline(2);
    timeline.Observe(0, 10, 400, true);
    timeline.Observe(1, 11, 100, true);
    double delay = 0;
    for (int i = 0; i < 100; i++) delay += timeline.Delay(1, 11);
    Near(750, delay);
    Near(1750, timeline.GetNextReady(1));
    bool[] mask = { true, true };
    int count = 0;
    while (Execute(timeline, mask) != 1 && ++count < 20) timeline.Delay(1, 11);
    Check(count < 20, "The enemy was locked out forever.");
    Near(250, timeline.Delay(1, 11));
});

Case("Advance is capped and never creates an immediate repeat", () =>
{
    var timeline = new CtbTimeline(2);
    timeline.Observe(0, 10, 100, true);
    timeline.Observe(1, 11, 100, true);
    double advance = 0;
    for (int i = 0; i < 100; i++) advance += timeline.Advance(1, 11);
    Near(500, advance);
    Near(500, timeline.GetNextReady(1));
    Check(timeline.Select(new[] { true, true }) == 1, "Advance did not alter turn order.");
    Check(timeline.Advance(1, 11) == 0 && timeline.Delay(1, 11) == 0, "The currently selected actor moved.");
});

Case("Opening upkeep uses the median for even and odd rosters", () =>
{
    Near(1250, CtbTimeline.OpeningUpkeepInterval(new List<double> { 2000, 500 }));
    Near(1000, CtbTimeline.OpeningUpkeepInterval(new List<double> { 2000, 500, 1000 }));
});

Case("Break delays an overdue enemy from the current battle time", () =>
{
    var timeline = new CtbTimeline(1);
    timeline.Observe(0, 10, 100, true);
    timeline.AdvanceTo(2500);
    Near(0, timeline.Delay(0, 10, 0));
    Near(1000, timeline.GetNextReady(0));
    Near(500, timeline.ApplyBreak(0, 10, 100).Delay);
    Near(3000, timeline.GetNextReady(0));
    Near(250, timeline.Delay(0, 10, 0.5));
    timeline.AdvanceTo(4000);
    Near(0, timeline.Delay(0, 10));
    Near(3250, timeline.GetNextReady(0));
});

Case("Break triggers once on confirmed damage and resets after target action", () =>
{
    var timeline = new CtbTimeline(2);
    timeline.Observe(0, 10, 100, true);
    timeline.Observe(1, 11, 100, true);
    CtbBreakResult first = timeline.ApplyBreak(1, 11, 40);
    Check(first.Accepted && !first.Triggered && first.Remaining == 60, "Partial stagger was incorrect.");
    CtbBreakResult second = timeline.ApplyBreak(1, 11, 100);
    Check(second.Triggered && second.Applied == 60 && timeline.IsBroken(1), "Break threshold was not crossed once.");
    Near(500, second.Delay);
    Check(!timeline.ApplyBreak(1, 11, 100).Accepted, "Broken targets cannot be broken repeatedly.");
    Execute(timeline, new[] { true, true });
    Check(Execute(timeline, new[] { true, true }) == 1, "Broken actor never recovered a turn.");
    Check(!timeline.IsBroken(1) && timeline.GetBreakRemaining(1) == 100, "Break did not recover after target action.");
});

Case("Break shares delay budget rather than bypassing delay protection", () =>
{
    var timeline = new CtbTimeline(1);
    timeline.Observe(0, 10, 100, true);
    timeline.Delay(0, 10, 0.75);
    CtbBreakResult broken = timeline.ApplyBreak(0, 10, 100);
    Check(broken.Triggered, "Break vulnerability must still register.");
    Near(0, broken.Delay);
    Near(1750, timeline.GetNextReady(0));
});

Case("Solo boss SPD scales the native value and rounds once, including above 255", () =>
{
    var timeline = new CtbTimeline(1);
    foreach (var (native, expected) in new[] { (17, 39), (20, 46), (40, 92), (65, 150),
                 (75, 173), (80, 185), (130, 300), (160, 370) })
    {
        Near(expected, CtbTimeline.ApplySoloBossSpeedBonus(native, 131m));
        Near(100000d / expected, timeline.RecoveryForSpeed(expected));
    }
    Near(0, CtbTimeline.ApplySoloBossSpeedBonus(0, 131m));
    Near(65, CtbTimeline.ApplySoloBossSpeedBonus(65, 0));
    Near(101, CtbTimeline.ApplySoloBossSpeedBonus(100, 0.5m));
    Near(369, CtbTimeline.ApplySoloBossSpeedBonus(160, 130.77m));
    Near(250, timeline.RecoveryForSpeed(CtbTimeline.ApplySoloBossSpeedBonus(160, 300m)));
});

Case("The opening boss classification is independent of both configured Break delays", () =>
{
    var musicOnly = CtbTimeline.ClassifyFormation(1, false, true, 0.5, 0.5);
    var traitOnly = CtbTimeline.ClassifyFormation(1, true, false, 0, 0);
    var group = CtbTimeline.ClassifyFormation(4, true, true, 0.5, 0.5);
    var ordinary = CtbTimeline.ClassifyFormation(1, false, false, 0, 0);
    Check(musicOnly.SoloBoss && traitOnly.SoloBoss && !group.SoloBoss && !ordinary.SoloBoss,
        "Equal Break settings changed which formations receive the solo boss SPD bonus.");
});

Case("The solo Zombie Dragon at 65 SPD loses a quarter interval only when Break fills", () =>
{
    var timeline = new CtbTimeline(1);
    timeline.Observe(0, 10, 65, true);
    double fraction = CtbTimeline.ClassifyFormation(1, targetIsBoss: true, bossMusic: true).BreakDelay;
    double original = timeline.GetNextReady(0);
    Near(100000d / 65, original);
    var partial = timeline.ApplyBreak(0, 10, 45, fraction);
    Check(!partial.Triggered && partial.Remaining == 55, "The solo boss broke before its threshold.");
    Near(original, timeline.GetNextReady(0));
    var broken = timeline.ApplyBreak(0, 10, 55, fraction);
    Check(broken.Triggered && timeline.IsBroken(0), "The solo boss never broke.");
    Near(25000d / 65, broken.Delay);
    Near(original + 25000d / 65, timeline.GetNextReady(0));
    Check(!timeline.ApplyBreak(0, 10, 100, fraction).Accepted, "A broken boss accepted a second delay.");
    Execute(timeline, new[] { true });
    Check(!timeline.IsBroken(0) && timeline.GetBreakRemaining(0) == 100, "A solo boss failed to recover its gauge.");
});

Case("A solo ordinary enemy keeps the default half-interval Break", () =>
{
    var timeline = new CtbTimeline(1);
    timeline.Observe(0, 10, 80, true);
    var broken = timeline.ApplyBreak(0, 10, 100, CtbTimeline.ClassifyFormation(1, targetIsBoss: false).BreakDelay);
    Near(625, broken.Delay); // Half of the same 1,250-unit interval.
    Near(0.5, CtbTimeline.ClassifyFormation(0, targetIsBoss: true).BreakDelay);
});

Case("Tuning a solo boss to 80 SPD changes its interval and uses the selected Break percentage", () =>
{
    var timeline = new CtbTimeline(1);
    timeline.Observe(0, 10, 80, true);
    Near(1250, timeline.GetNextReady(0));
    double fraction = CtbTimeline.ClassifyFormation(1, true, true, soloBossDelay: 0.40, otherDelay: 0.10).BreakDelay;
    var result = timeline.ApplyBreak(0, 10, 100, fraction);
    Near(500, result.Delay);
    Near(1750, timeline.GetNextReady(0));
});

Case("Other enemy Break tuning covers lone ordinary enemies and every member of a boss group", () =>
{
    Near(0.10, CtbTimeline.ClassifyFormation(1, false, false, 0.40, 0.10).BreakDelay);
    Near(0.10, CtbTimeline.ClassifyFormation(3, true, true, 0.40, 0.10).BreakDelay);
    var timeline = new CtbTimeline(1);
    timeline.Observe(0, 10, 65, true);
    double fraction = CtbTimeline.ClassifyFormation(1, true, false, 0, 0.50).BreakDelay;
    var result = timeline.ApplyBreak(0, 10, 100, fraction);
    Check(result.Triggered, "Zero delay prevented the Break itself.");
    Near(0, result.Delay);
    Near(100000d / 65, timeline.GetNextReady(0));
});

Case("Solo story bosses with only boss music or only the boss trait receive the same delay", () =>
{
    // The early Clay Doll and Queen Ant use boss music without the monster trait.
    // The Assassin has the monster trait without boss music.
    Near(0.25, CtbTimeline.ClassifyFormation(1, targetIsBoss: false, bossMusic: true).BreakDelay);
    Near(0.25, CtbTimeline.ClassifyFormation(1, targetIsBoss: true, bossMusic: false).BreakDelay);
    Near(0.25, CtbTimeline.ClassifyFormation(1, targetIsBoss: true, bossMusic: true).BreakDelay);
    Near(0.5, CtbTimeline.ClassifyFormation(2, targetIsBoss: false, bossMusic: true).BreakDelay);
});

Case("A boss group keeps half-interval Break when only one opening enemy survives", () =>
{
    const int startingEnemyCount = 3;
    var timeline = new CtbTimeline(startingEnemyCount);
    for (int slot = 0; slot < startingEnemyCount; slot++) timeline.Observe(slot, slot + 10, 80, true);
    Near(0.5, CtbTimeline.ClassifyFormation(startingEnemyCount, targetIsBoss: true).BreakDelay);
    timeline.Observe(0, 0, 0, false);
    timeline.Observe(1, 0, 0, false);
    double fraction = CtbTimeline.ClassifyFormation(startingEnemyCount, targetIsBoss: true, bossMusic: true).BreakDelay;
    var broken = timeline.ApplyBreak(2, 12, 100, fraction);
    Check(broken.Triggered, "The surviving enemy failed to break.");
    Near(625, broken.Delay);
    Near(1875, timeline.GetNextReady(2));
});

Case("Partial stagger persists across a target turn and HP threshold changes preserve damage", () =>
{
    var timeline = new CtbTimeline(1);
    timeline.Observe(0, 10, 100, true);
    timeline.ApplyBreak(0, 10, 40);
    Execute(timeline, new[] { true });
    Check(timeline.GetBreakRemaining(0) == 60, "Unbroken gauge lost accrued stagger.");
    timeline.ConfigureBreak(0, 200);
    Check(timeline.GetBreakMaximum(0) == 200 && timeline.GetBreakRemaining(0) == 160, "Threshold change discarded confirmed hits.");
    timeline.ApplyBreak(0, 10, 160);
    timeline.ConfigureBreak(0, 300);
    Check(timeline.IsBroken(0) && timeline.GetBreakRemaining(0) == 0, "Threshold change canceled an active Break.");
});

Case("Reading the next event neither reserves nor advances logical time", () =>
{
    var timeline = new CtbTimeline(2);
    timeline.Observe(0, 10, 100, true);
    timeline.Observe(1, 11, 200, true);
    bool[] mask = { true, true };
    Near(500, timeline.PeekNextTime(mask));
    Near(0, timeline.Now);
    Check(timeline.PendingSlot == -1, "A peek reserved an action.");
    Check(double.IsPositiveInfinity(timeline.PeekNextTime(new[] { false, false })), "No eligible event should have a finite time.");
    timeline.Select(mask);
    Near(timeline.Now, timeline.PeekNextTime(mask));
});

Case("Maintenance advances only logical time and revival begins after its heartbeat", () =>
{
    var timeline = new CtbTimeline(2);
    timeline.Observe(0, 10, 100, true);
    timeline.Observe(1, 11, 50, false);
    timeline.AdvanceTo(1500);
    Near(1500, timeline.Now);
    Near(1000, timeline.GetNextReady(0));
    timeline.Observe(1, 11, 100, true);
    Near(2500, timeline.GetNextReady(1));
    timeline.Observe(0, 10, 200, true);
    Near(1500, timeline.GetNextReady(0));
    Near(1500, timeline.PeekNextTime(new[] { true, true }));
    var forecast = timeline.Forecast(8, new[] { true, true });
    Near(1500, timeline.Now);
    foreach (var entry in forecast)
    {
        Check(Execute(timeline, new[] { true, true }) == entry.Slot, "Maintenance projection disagreed with actual scheduling.");
        Near(entry.ReadyAt, timeline.Now);
    }
});

Case("Maintenance cannot advance through a selected or admitted actor", () =>
{
    var timeline = new CtbTimeline(1);
    timeline.Observe(0, 10, 100, true);
    timeline.Select(new[] { true });
    Throws<InvalidOperationException>(() => timeline.AdvanceTo(2000));
    Near(1000, timeline.Now);
    timeline.Admit(0, 10);
    Throws<InvalidOperationException>(() => timeline.AdvanceTo(2000));
    timeline.Complete(0, 10);
    timeline.AdvanceTo(2000);
    Throws<ArgumentOutOfRangeException>(() => timeline.AdvanceTo(1999));
    Throws<ArgumentOutOfRangeException>(() => timeline.AdvanceTo(double.NaN));
    Throws<ArgumentOutOfRangeException>(() => timeline.AdvanceTo(double.PositiveInfinity));
    Near(2000, timeline.Now);
});

Case("Forecast is deterministic and does not consume real turns or Break state", () =>
{
    var timeline = new CtbTimeline(3);
    for (int i = 0; i < 3; i++) timeline.Observe(i, i + 1, 100 + 25 * i, true);
    timeline.ApplyBreak(1, 2, 70);
    bool[] mask = { true, true, true };
    double initialDue = timeline.GetNextReady(1);
    var forecast = timeline.Forecast(20, mask);
    Check(forecast.Length == 20 && timeline.PendingSlot == -1, "Forecast changed a reservation.");
    Near(0, timeline.Now);
    Near(initialDue, timeline.GetNextReady(1));
    Check(timeline.GetBreakRemaining(1) == 30, "Forecast consumed Break state.");
    foreach (var entry in forecast)
    {
        Check(Execute(timeline, mask) == entry.Slot, "Default-action projection disagreed with execution.");
        Near(entry.ReadyAt, timeline.Now);
    }
});

Case("Forecast preserves an admitted current actor and projects its completion", () =>
{
    var timeline = new CtbTimeline(2);
    timeline.Observe(0, 10, 100, true);
    timeline.Observe(1, 11, 50, true);
    bool[] mask = { true, true };
    timeline.Select(mask);
    timeline.Admit(0, 10);
    var forecast = timeline.Forecast(3, mask);
    Check(forecast[0].Current && forecast[0].Slot == 0, "Current action missing from forecast.");
    Check(timeline.IsAdmitted && timeline.PendingSlot == 0, "Forecast completed the native action.");
    Near(1000, timeline.GetNextReady(0));
});

Case("Forecast respects exclusions and terminates with no actors", () =>
{
    var timeline = new CtbTimeline(2);
    timeline.Observe(0, 10, 100, true);
    Check(timeline.Forecast(10, new[] { false, true }).Length == 0, "Excluded or absent actors appeared.");
    Check(timeline.Forecast(0, new[] { true, true }).Length == 0, "Zero-length projection failed.");
});

Case("Reset clears encounter state including identity, reservations, and Break", () =>
{
    var timeline = new CtbTimeline(1);
    timeline.Observe(0, 10, 100, true);
    timeline.ApplyBreak(0, 10, 100);
    timeline.Select(new[] { true });
    timeline.Reset();
    Check(timeline.GetIdentity(0) == 0 && !timeline.IsBroken(0) && timeline.PendingSlot == -1, "Encounter state leaked.");
    Near(0, timeline.Now);
    Check(timeline.Select(new[] { true }) == -1, "Reset retained a combatant.");
});

Case("Invalid configuration and effects are rejected before state mutation", () =>
{
    Throws<ArgumentOutOfRangeException>(() => new CtbTimeline(0));
    Throws<ArgumentOutOfRangeException>(() => new CtbTimeline(1, double.NaN));
    var timeline = new CtbTimeline(1);
    timeline.Observe(0, 10, 100, true);
    Throws<ArgumentException>(() => timeline.Select(Array.Empty<bool>()));
    Throws<ArgumentOutOfRangeException>(() => timeline.Delay(0, 10, -1));
    Throws<ArgumentOutOfRangeException>(() => timeline.Advance(0, 10, double.PositiveInfinity));
    Throws<ArgumentOutOfRangeException>(() => timeline.ApplyBreak(0, 10, -1));
    Throws<ArgumentOutOfRangeException>(() => timeline.Forecast(257, new[] { true }));
    timeline.Select(new[] { true });
    Throws<ArgumentOutOfRangeException>(() => timeline.Wait(0, 10, 0));
    Check(!timeline.IsAdmitted, "Invalid Wait consumed admission.");
});

Case("Action timing policy uses explicit positive command costs and proven Rune ranks", () =>
{
    var expected = new[]
    {
        (Kind: CtbActionKind.Wait, Rank: 0, Cost: 0.5),
        (Kind: CtbActionKind.Defend, Rank: 0, Cost: 0.75),
        (Kind: CtbActionKind.Attack, Rank: 0, Cost: 1.0),
        (Kind: CtbActionKind.Item, Rank: 0, Cost: 0.85),
        (Kind: CtbActionKind.EnemyAttack, Rank: 0, Cost: 1.0),
        (Kind: CtbActionKind.Rune, Rank: 1, Cost: 1.15),
        (Kind: CtbActionKind.Rune, Rank: 2, Cost: 1.35),
        (Kind: CtbActionKind.Rune, Rank: 3, Cost: 1.55),
        (Kind: CtbActionKind.Rune, Rank: 4, Cost: 1.75)
    };
    var timeline = new CtbTimeline(1);
    foreach (var value in expected)
    {
        CtbActionCost cost = CtbActionTiming.Describe(value.Kind, value.Rank);
        Check(cost.Kind == value.Kind && cost.Rank == value.Rank && !string.IsNullOrEmpty(cost.Label),
            "The timing policy lost the command classification.");
        Near(value.Cost, cost.Multiplier);
        Near(cost.Multiplier, CtbActionTiming.RecoveryMultiplier(value.Kind, value.Rank));
        foreach (int speed in new[] { int.MinValue, 0, 1, 10, 25, 100, int.MaxValue })
        {
            double interval = timeline.RecoveryForSpeed(speed, cost.Multiplier);
            Check(double.IsFinite(interval) && interval > 0, "Native SPD must always produce a positive finite command interval.");
        }
    }
    Throws<ArgumentOutOfRangeException>(() => CtbActionTiming.Describe((CtbActionKind)12345));
});

Case("Unknown or invalid Rune rank uses a documented safe recovery without inventing a rank", () =>
{
    foreach (int rank in new[] { int.MinValue, -1, 0, 5, int.MaxValue })
    {
        var cost = CtbActionTiming.Describe(CtbActionKind.Rune, rank);
        Check(cost.Rank == 0 && cost.Label == "Rune", "Unknown Rune data must not be labeled a proven spell rank.");
        Near(1.35, cost.Multiplier);
    }
    Check(CtbActionTiming.Describe(CtbActionKind.Item, 4).Rank == 0,
        "Irrelevant Rune metadata must not change a non-Rune command.");
});

Case("Choosing Item instead of Attack changes the hero's projected position", () =>
{
    var timeline = new CtbTimeline(2);
    timeline.Observe(0, 10, 100, true);
    timeline.Observe(1, 11, 80, true);
    timeline.Delay(1, 11, 0.5); // Enemy is due at 1875; the hero selects at 1000.
    bool[] mask = { true, true };
    timeline.Select(mask);
    var item = timeline.Forecast(4, mask, CtbActionTiming.RecoveryMultiplier(CtbActionKind.Item));
    var attack = timeline.Forecast(4, mask, CtbActionTiming.RecoveryMultiplier(CtbActionKind.Attack));
    Check(item[0].Current && item[1].Slot == 0 && attack[1].Slot == 1,
        "The pending command should visibly change which actor comes next.");
    Near(1850, item[1].ReadyAt);
    Near(1875, attack[1].ReadyAt);
    Check(timeline.PendingSlot == 0 && !timeline.IsAdmitted, "Menu preview must leave the selected hero reserved.");
    Near(1000, timeline.Now);
});

Case("Higher Rune rank can move a hero behind an additional enemy opportunity", () =>
{
    var timeline = new CtbTimeline(2);
    timeline.Observe(0, 10, 100, true);
    timeline.Observe(1, 11, 90, true);
    bool[] mask = { true, true };
    timeline.Select(mask);
    var quick = timeline.Forecast(8, mask, CtbActionTiming.RecoveryMultiplier(CtbActionKind.Wait));
    var heavy = timeline.Forecast(8, mask, CtbActionTiming.RecoveryMultiplier(CtbActionKind.Rune, 4));
    Check(quick[2].Slot == 0 && heavy[2].Slot == 1 && heavy[3].Slot == 0,
        "A heavy Rune should permit an enemy opportunity that a quick command avoids.");
    Near(1500, quick[2].ReadyAt);
    Near(2750, heavy[3].ReadyAt);
});

Case("Pending command forecasts match actual completion both before and after native admission", () =>
{
    foreach (bool admitted in new[] { false, true })
    foreach (var value in new[]
    {
        (CtbActionKind.Attack, 0), (CtbActionKind.Defend, 0), (CtbActionKind.Item, 0),
        (CtbActionKind.Rune, 1), (CtbActionKind.Rune, 2), (CtbActionKind.Rune, 3),
        (CtbActionKind.Rune, 4), (CtbActionKind.Rune, 0), (CtbActionKind.EnemyAttack, 0)
    })
    {
        var timeline = new CtbTimeline(3);
        timeline.Observe(0, 10, 100, true);
        timeline.Observe(1, 11, 80, true);
        timeline.Observe(2, 12, 65, true);
        bool[] mask = { true, true, true };
        timeline.Select(mask);
        if (admitted) timeline.Admit(0, 10);
        double multiplier = CtbActionTiming.RecoveryMultiplier(value.Item1, value.Item2);
        var forecast = timeline.Forecast(20, mask, multiplier);
        Check(forecast[0].Slot == 0 && forecast[0].Current && timeline.IsAdmitted == admitted,
            "Preview changed admission or omitted the active command.");
        if (!admitted) Check(timeline.Admit(0, 10), "Native admission should still be available after preview.");
        Check(timeline.Complete(0, 10, multiplier), "Confirmed action completion failed.");
        foreach (var entry in forecast.Skip(1))
        {
            Check(Execute(timeline, mask) == entry.Slot, "Conditional order disagreed with native-completion scheduling.");
            Near(entry.ReadyAt, timeline.Now);
        }
    }
});

Case("Only the current action receives its known recovery and future assumptions remain ordinary Attack", () =>
{
    var timeline = new CtbTimeline(1);
    timeline.Observe(0, 10, 100, true);
    bool[] mask = { true };
    timeline.Select(mask);
    timeline.Admit(0, 10);
    var forecast = timeline.Forecast(4, mask, 1.75);
    Near(1000, forecast[0].ReadyAt);
    Near(2750, forecast[1].ReadyAt);
    Near(3750, forecast[2].ReadyAt);
    Near(4750, forecast[3].ReadyAt);
    Check(timeline.IsAdmitted && timeline.PendingSlot == 0, "Playback preview completed the real native action.");
});

Case("A forecast with no current reservation does not apply a discarded menu choice to future actions", () =>
{
    var timeline = new CtbTimeline(1);
    timeline.Observe(0, 10, 100, true);
    bool[] mask = { true };
    timeline.Select(mask);
    timeline.Forecast(4, mask, 1.75);
    Check(!timeline.CancelReservation(0, 99) && !timeline.Complete(0, 99, 1.75),
        "A wrong identity changed the previewed actor's reservation.");
    Check(timeline.CancelReservation(0, 10), "A real canceled choice must release the reservation.");
    var canceled = timeline.Forecast(3, mask, 1.75);
    Near(1000, canceled[0].ReadyAt);
    Near(2000, canceled[1].ReadyAt);
    Near(3000, canceled[2].ReadyAt);
    Check(!canceled[0].Current && timeline.PendingSlot == -1, "Canceled choices must not remain committed.");
    Check(Execute(timeline, mask) == 0, "The hero should still be able to choose another action.");
    Near(2000, timeline.GetNextReady(0));
});

Case("Action-sensitive previews retain Break advance and delay state without consuming any of them", () =>
{
    var timeline = new CtbTimeline(3);
    timeline.Observe(0, 10, 100, true);
    timeline.Observe(1, 11, 100, true);
    timeline.Observe(2, 12, 50, true);
    timeline.ApplyBreak(1, 11, 100);
    timeline.Advance(2, 12, 0.25);
    bool[] mask = { true, true, true };
    timeline.Select(mask);
    var control = timeline.Clone();
    var expected = control.Forecast(12, mask, 1.55);
    var preview = timeline.Forecast(12, mask, 1.55);
    Check(expected.Select(x => (x.Slot, x.ReadyAt)).SequenceEqual(preview.Select(x => (x.Slot, x.ReadyAt))),
        "A cloned preview lost conditional scheduling effects.");
    Check(timeline.IsBroken(1) && timeline.GetBreakRemaining(1) == 0,
        "A preview consumed the real enemy's Break recovery.");
    for (int slot = 0; slot < 3; slot++) Near(control.GetNextReady(slot), timeline.GetNextReady(slot));
    Near(control.Delay(1, 11, 0.5), timeline.Delay(1, 11, 0.5));
    Near(control.Advance(2, 12, 0.5), timeline.Advance(2, 12, 0.5));
});

Case("The Wait preview matches the actual no-animation Wait opportunity", () =>
{
    var timeline = new CtbTimeline(2);
    timeline.Observe(0, 10, 100, true);
    timeline.Observe(1, 11, 80, true);
    bool[] mask = { true, true };
    timeline.Select(mask);
    double multiplier = CtbActionTiming.RecoveryMultiplier(CtbActionKind.Wait);
    var forecast = timeline.Forecast(8, mask, multiplier);
    Check(timeline.Wait(0, 10, multiplier), "Wait should consume exactly the previewed logical cost.");
    Near(1500, timeline.GetNextReady(0));
    foreach (var entry in forecast.Skip(1))
    {
        Check(Execute(timeline, mask) == entry.Slot, "Wait order preview did not match execution.");
        Near(entry.ReadyAt, timeline.Now);
    }
    Check(!timeline.Wait(0, 10, multiplier), "Preview must not make Wait consumable twice.");
});

Case("A calculated SPD change after selection updates both conditional forecast and native completion", () =>
{
    foreach (bool admitted in new[] { false, true })
    {
        var timeline = new CtbTimeline(2);
        timeline.Observe(0, 10, 100, true);
        timeline.Observe(1, 11, 80, true);
        bool[] mask = { true, true };
        timeline.Select(mask);
        if (admitted) timeline.Admit(0, 10);
        timeline.Observe(0, 10, 200, true);
        var forecast = timeline.Forecast(10, mask, 1.75);
        var returning = forecast.First(entry => !entry.Current && entry.Slot == 0);
        Near(1875, returning.ReadyAt);
        if (!admitted) timeline.Admit(0, 10);
        Check(timeline.Complete(0, 10, 1.75), "A current SPD update must preserve native action ownership.");
        Near(returning.ReadyAt, timeline.GetNextReady(0));
        foreach (var entry in forecast.Skip(1))
        {
            Check(Execute(timeline, mask) == entry.Slot, "Post-selection SPD preview disagreed with execution.");
            Near(entry.ReadyAt, timeline.Now);
        }
    }
});

Case("Native eligibility changes after selection remain exclusions in conditional future turns", () =>
{
    var timeline = new CtbTimeline(3);
    timeline.Observe(0, 10, 100, true);
    timeline.Observe(1, 11, 80, true);
    timeline.Observe(2, 12, 60, true);
    bool[] mask = { true, true, true };
    timeline.Select(mask);
    mask[1] = false; // A native status excludes this enemy while preserving its identity and due time.
    var forecast = timeline.Forecast(12, mask, 1.55);
    Check(forecast.All(entry => entry.Slot != 1), "Excluded native actor still appeared in the conditional forecast.");
    timeline.Admit(0, 10);
    timeline.Complete(0, 10, 1.55);
    foreach (var entry in forecast.Skip(1))
    {
        Check(Execute(timeline, mask) == entry.Slot, "Status-filtered forecast disagreed with real eligibility.");
        Near(entry.ReadyAt, timeline.Now);
    }
    Check(timeline.GetIdentity(1) == 11, "Forecast must not erase a temporarily excluded native actor.");
});

Case("A previewed command cannot complete after KO or native slot replacement", () =>
{
    foreach (bool replaced in new[] { false, true })
    {
        var timeline = new CtbTimeline(2);
        timeline.Observe(0, 10, 100, true);
        timeline.Observe(1, 11, 80, true);
        bool[] mask = { true, true };
        timeline.Select(mask);
        timeline.Admit(0, 10);
        timeline.Forecast(8, mask, 1.75);
        timeline.Observe(0, replaced ? 20 : 10, 100, replaced);
        Check(timeline.PendingSlot == -1 && !timeline.Complete(0, 10, 1.75),
            "A preview retained a stale action after native eligibility or identity changed.");
        var current = timeline.Forecast(8, mask, 1.75);
        Check(current.All(entry => entry.Identity != 10), "Stale identity remained in a later conditional forecast.");
    }
});

Case("Invalid pending-action forecast costs are rejected without changing reservations", () =>
{
    var timeline = new CtbTimeline(1);
    timeline.Observe(0, 10, 100, true);
    bool[] mask = { true };
    timeline.Select(mask);
    foreach (double invalid in new[] { 0, -1, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        Throws<ArgumentOutOfRangeException>(() => timeline.Forecast(8, mask, invalid));
    Check(timeline.PendingSlot == 0 && !timeline.IsAdmitted, "Invalid preview costs altered native admission.");
    Near(1000, timeline.Now);
    Near(1000, timeline.GetNextReady(0));
});

Console.WriteLine($"{passed} CTB checks passed.");
