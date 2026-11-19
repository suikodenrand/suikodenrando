using SuikodenRandomizer.Core.Combat;

int passed = 0;
void Check(bool condition, string failure)
{
    if (!condition) throw new InvalidOperationException(failure);
}
void Case(string title, Action test)
{
    test();
    Console.WriteLine("PASS " + title);
    passed++;
}
CtbReserveCandidate Fighter(int role, int donor, bool recruited = true, bool capable = true, bool initialized = true)
    => new(role, donor, "Fighter " + donor, recruited, capable, initialized);
int[] Roles(CtbReserveCandidate[] candidates) => candidates.Select(candidate => candidate.RoleId).ToArray();

Case("Active canonical roles and displayed donors cannot support their own party", () =>
{
    var candidates = new[] { Fighter(1, 50), Fighter(2, 60), Fighter(3, 70) };
    var chosen = CtbReserveSupport.Select(candidates, new[] { 1 }, new[] { 60 });
    Check(Roles(chosen).SequenceEqual(new[] { 3 }), "Both identity domains must be excluded.");
});

Case("Dead, unrecruited, unsupported, uninitialized and invalid identities are excluded", () =>
{
    var candidates = new[] { Fighter(1, 11, recruited: false), Fighter(2, 12, capable: false),
        Fighter(3, 13, initialized: false), Fighter(-1, 14), Fighter(5, -1), Fighter(6, 16) };
    var chosen = CtbReserveSupport.Select(candidates, Array.Empty<int>(), Array.Empty<int>());
    Check(Roles(chosen).SequenceEqual(new[] { 6 }), "Only fully eligible snapshots may support.");
});

Case("Automatic selection is stable regardless of input order and respects capacity", () =>
{
    var candidates = new[] { Fighter(9, 19), Fighter(3, 13), Fighter(7, 17), Fighter(1, 11) };
    var chosen = CtbReserveSupport.Select(candidates, Array.Empty<int>(), Array.Empty<int>());
    var reversed = CtbReserveSupport.Select(candidates.Reverse(), Array.Empty<int>(), Array.Empty<int>());
    Check(Roles(chosen).SequenceEqual(new[] { 1, 3 }) && Roles(chosen).SequenceEqual(Roles(reversed)),
        "Automatic selection must be deterministic and limited to two by default.");
    Check(CtbReserveSupport.Select(candidates, Array.Empty<int>(), Array.Empty<int>(), maximum: 0).Length == 0,
        "Zero capacity must disable support.");
});

Case("Explicit support roster preserves preference order and never fills missing choices", () =>
{
    var candidates = new[] { Fighter(1, 11), Fighter(3, 13), Fighter(7, 17), Fighter(9, 19, recruited: false) };
    var chosen = CtbReserveSupport.Select(candidates, Array.Empty<int>(), Array.Empty<int>(), new[] { 7, 99, 9, 1 });
    Check(Roles(chosen).SequenceEqual(new[] { 7, 1 }), "Explicit order must be retained while unavailable choices are skipped.");
    Check(CtbReserveSupport.Select(candidates, Array.Empty<int>(), Array.Empty<int>(), new[] { 99, 9 }).Length == 0,
        "Missing choices must not fall back to automatic selection.");
    Check(CtbReserveSupport.Select(candidates, Array.Empty<int>(), Array.Empty<int>(), Array.Empty<int>()).Length == 0,
        "An explicitly empty roster must stay empty.");
});

Case("Duplicate roles and shuffled donors cannot occupy two support slots", () =>
{
    var candidates = new[] { Fighter(1, 20), Fighter(1, 20), Fighter(2, 20), Fighter(3, 30) };
    var chosen = CtbReserveSupport.Select(candidates, Array.Empty<int>(), Array.Empty<int>(), maximum: 4);
    Check(Roles(chosen).SequenceEqual(new[] { 1, 3 }), "Duplicate role or donor must be selected at most once.");
    var preferred = CtbReserveSupport.Select(candidates, Array.Empty<int>(), Array.Empty<int>(), new[] { 3, 3, 2, 1 });
    Check(Roles(preferred).SequenceEqual(new[] { 3, 2 }), "Duplicate preferences cannot duplicate a support.");
});

Case("An empty or entirely excluded reserve roster is harmless", () =>
{
    Check(CtbReserveSupport.Select(Array.Empty<CtbReserveCandidate>(), Array.Empty<int>(), Array.Empty<int>()).Length == 0,
        "An empty roster must produce no support.");
    Check(CtbReserveSupport.Select(new[] { Fighter(1, 11) }, new[] { 1 }, Array.Empty<int>()).Length == 0,
        "No eligible reserve must produce no support.");
});

Case("Assist cadence counts completed hero actions and resets between encounters", () =>
{
    var support = new CtbReserveSupport();
    Check(!support.RecordHeroAction() && !support.RecordHeroAction() && !support.RecordHeroAction(),
        "Default assist cannot happen before four completed hero actions.");
    Check(support.RecordHeroAction() && support.ActionsSinceAssist == 0, "The fourth action must trigger exactly once.");
    Check(!support.RecordHeroAction(), "The next action must not retrigger the same assist.");
    support.Reset();
    Check(support.ActionsSinceAssist == 0 && !support.RecordHeroAction(), "An encounter reset must clear previous progress.");
    var everyAction = new CtbReserveSupport(1);
    Check(everyAction.RecordHeroAction() && everyAction.RecordHeroAction(), "A configured one-action cadence must work.");
});

Case("Invalid cadence and negative capacity are rejected", () =>
{
    bool cadenceRejected = false, capacityRejected = false;
    try { _ = new CtbReserveSupport(0); } catch (ArgumentOutOfRangeException) { cadenceRejected = true; }
    try { _ = CtbReserveSupport.Select(Array.Empty<CtbReserveCandidate>(), Array.Empty<int>(), Array.Empty<int>(), maximum: -1); }
    catch (ArgumentOutOfRangeException) { capacityRejected = true; }
    Check(cadenceRejected && capacityRejected, "Invalid values must not create zero-time assist loops.");
});

Case("Rally skips a capped late ally and advances the next eligible fighter once", () =>
{
    var timeline = new CtbTimeline(4);
    timeline.Observe(1, 11, 10, true);
    timeline.Observe(2, 22, 100, true);
    timeline.Observe(3, 33, 100, true);
    Check(timeline.Advance(1, 11, 0.5) == 5000, "The slow ally should have exhausted its per-turn advance.");
    var allies = new (int Slot, long Identity)[] { (1, 11), (2, 22), (3, 33) };
    var rally = CtbReserveSupport.Rally(timeline, allies);
    Check(rally == (2, 150d), "Rally lost its assist on the capped ally or broke slot-order ties.");
    Check(timeline.GetNextReady(1) == 5000 && timeline.GetNextReady(2) == 850
        && timeline.GetNextReady(3) == 1000, "Rally must advance exactly one eligible ally.");
    Check(CtbReserveSupport.Rally(timeline, new[] { (1, 11L), (2, 99L) }) == (-1, 0d),
        "A capped or replaced fighter must not consume a reserve rotation.");
});

Console.WriteLine($"CTB reserve support: {passed} checks passed.");
