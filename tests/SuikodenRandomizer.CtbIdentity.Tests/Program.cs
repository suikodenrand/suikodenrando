using SuikodenRandomizer.Core.Combat;

int passed = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
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
CtbEnemyBadge Need(CtbEnemyBadge? badge)
    => badge ?? throw new InvalidOperationException("A live badge was expected.");

Case("Repeated observations retain one badge and its native actor binding", () =>
{
    var identities = new CtbEnemyIdentity();
    CtbEnemyBadge first = Need(identities.Observe(7, 1007, true));
    for (int i = 0; i < 50; i++)
    {
        CtbEnemyBadge observed = Need(identities.Observe(7, 1007, true));
        Check(observed.Id == first.Id && observed.Encounter == first.Encounter
            && observed.NativeIdentity == 1007 && observed.Slot == 7, "Frame sampling changed presentation identity.");
    }
    Check(identities.AllocatedCount == 1 && identities.Matches(first), "Repeated sampling allocated extra badges.");
});

Case("Six primary symbols and colours match the accessible palette", () =>
{
    var identities = new CtbEnemyIdentity();
    uint[] expectedColours = { 0x56B4E9, 0xE69F00, 0x009E73, 0xCC79A7, 0xD55E00, 0xF0E442 };
    CtbEnemyShape[] expectedShapes = { CtbEnemyShape.Circle, CtbEnemyShape.Triangle, CtbEnemyShape.Square,
        CtbEnemyShape.Diamond, CtbEnemyShape.Cross, CtbEnemyShape.Star };
    var colours = new HashSet<uint>();
    var shapes = new HashSet<CtbEnemyShape>();
    for (int i = 0; i < 6; i++)
    {
        CtbEnemyBadge badge = Need(identities.Observe(i + 1, 100 + i, true));
        Check(badge.Id == i + 1 && badge.Label == "E" + (i + 1), "Badge numbers must be explicit and unique.");
        Check(badge.Rgb == expectedColours[i] && badge.Shape == expectedShapes[i], "Palette or symbol contract changed.");
        Check(colours.Add(badge.Rgb) && shapes.Add(badge.Shape), "Primary identity cues must be distinct.");
    }
});

Case("KO removes a target marker without renumbering survivors and revival restores its badge", () =>
{
    var identities = new CtbEnemyIdentity();
    var first = Need(identities.Observe(7, 1007, true));
    var second = Need(identities.Observe(8, 1008, true));
    Check(!identities.Observe(7, 1007, false).HasValue && !identities.Get(7, 1007).HasValue,
        "A KO must not remain targetable.");
    Check(!identities.Matches(first) && Need(identities.Get(8, 1008)).Id == second.Id,
        "KO renumbered a different enemy or retained a live marker.");
    var revived = Need(identities.Observe(7, 1007, true));
    Check(revived.Id == first.Id && revived.Shape == first.Shape && revived.Rgb == first.Rgb
        && identities.AllocatedCount == 2, "Revival must restore the original enemy badge.");
});

Case("Native enemy-list reorder and KO compaction preserve matching target badges", () =>
{
    var identities = new CtbEnemyIdentity();
    var a = Need(identities.Observe(7, 1007, true));
    var b = Need(identities.Observe(8, 1008, true));
    var c = Need(identities.Observe(9, 1009, true));
    long[] native = new long[33];
    native[7] = 1007; native[8] = 1008; native[9] = 1009;
    int[] reordered = { 9, 7, 8 };
    Check(Need(identities.ResolveNativeListTarget(reordered, 0, native)).Id == c.Id
        && Need(identities.ResolveNativeListTarget(reordered, 1, native)).Id == a.Id
        && Need(identities.ResolveNativeListTarget(reordered, 2, native)).Id == b.Id,
        "Display order must never become enemy identity.");
    identities.Observe(8, 1008, false);
    int[] compacted = { 7, 9 };
    Check(Need(identities.ResolveNativeListTarget(compacted, 1, native)).Id == c.Id,
        "A compacted list must retain the surviving enemy badge.");
});

Case("Replacing or removing a slot never recycles its retired badge", () =>
{
    var identities = new CtbEnemyIdentity();
    var old = Need(identities.Observe(7, 1007, true));
    identities.Observe(7, 1007, false);
    var replacement = Need(identities.Observe(7, 2007, true));
    Check(replacement.Id > old.Id && !identities.Get(7, 1007).HasValue && !identities.Matches(old),
        "A replacement inherited the retired enemy identity.");
    identities.Observe(7, 0, false);
    var reusedPointer = Need(identities.Observe(7, 1007, true));
    Check(reusedPointer.Id > replacement.Id && identities.AllocatedCount == 3,
        "An old native address returning after retirement must receive a new badge.");
});

Case("A newly observed dead actor does not consume a badge until revived", () =>
{
    var identities = new CtbEnemyIdentity();
    Check(!identities.Observe(7, 1007, false).HasValue && identities.AllocatedCount == 0,
        "Dead roster entries should not allocate visible identities.");
    var other = Need(identities.Observe(8, 1008, true));
    var revived = Need(identities.Observe(7, 1007, true));
    Check(other.Id == 1 && revived.Id == 2, "First visible identity allocation was inconsistent.");
});

Case("One native identity cannot alias two simultaneous slots even when KO", () =>
{
    var identities = new CtbEnemyIdentity();
    var first = Need(identities.Observe(7, 1007, true));
    Throws<InvalidOperationException>(() => identities.Observe(8, 1007, true));
    Check(identities.Matches(first) && identities.AllocatedCount == 1 && !identities.Get(8, 1007).HasValue,
        "Rejected aliases must leave the established binding intact.");
    identities.Observe(7, 1007, false);
    Throws<InvalidOperationException>(() => identities.Observe(8, 1007, true));
    identities.Observe(7, 0, false);
    Check(Need(identities.Observe(8, 1007, true)).Id == 2, "A cleared identity may receive a fresh binding.");
});

Case("Thirty-two actors retain distinct numbered identities when colour and shape cycle", () =>
{
    var identities = new CtbEnemyIdentity(33);
    var badges = new List<CtbEnemyBadge>();
    for (int slot = 1; slot <= 32; slot++) badges.Add(Need(identities.Observe(slot, 1000 + slot, true)));
    Check(badges.Select(x => x.Id).Distinct().Count() == 32
        && badges.Select(x => x.Label).Distinct().Count() == 32, "Large encounters require unique numbers.");
    Check(badges[0].Rgb == badges[6].Rgb && badges[0].Shape == badges[6].Shape
        && badges[0].Label != badges[6].Label && badges[31].Label == "E32",
        "Palette cycling must preserve explicit different identities.");
});

Case("New actors after many KOs keep increasing numbers within an encounter", () =>
{
    var identities = new CtbEnemyIdentity(2);
    for (int i = 1; i <= 80; i++)
    {
        var badge = Need(identities.Observe(1, 1000 + i, true));
        Check(badge.Id == i && badge.Label == "E" + i, "A retired number was reused.");
        identities.Observe(1, 1000 + i, false);
    }
    Check(identities.AllocatedCount == 80, "Allocated count must include retired identities.");
});

Case("Reset starts a new encounter and invalidates badges despite identical native addresses", () =>
{
    var identities = new CtbEnemyIdentity();
    var before = Need(identities.Observe(7, 1007, true));
    identities.Reset();
    Check(identities.AllocatedCount == 0 && !identities.Get(7, 1007).HasValue && !identities.Matches(before),
        "Previous encounter identity survived Reset.");
    var after = Need(identities.Observe(7, 1007, true));
    Check(after.Id == 1 && after.Encounter != before.Encounter && identities.Matches(after) && !identities.Matches(before),
        "Encounter generation must distinguish reused native addresses and display numbers.");
});

Case("Target resolution rejects stale native identity snapshots and malformed maps", () =>
{
    var identities = new CtbEnemyIdentity(10);
    identities.Observe(7, 1007, true);
    long[] stale = new long[10];
    stale[7] = 999;
    Check(!identities.ResolveNativeListTarget(new[] { 7 }, 0, stale).HasValue,
        "A stale target snapshot must not select a different enemy.");
    Check(!identities.ResolveNativeListTarget(Array.Empty<int>(), 0, stale).HasValue
        && !identities.ResolveNativeListTarget(new[] { 7 }, -1, stale).HasValue
        && !identities.ResolveNativeListTarget(new[] { 7 }, 1, stale).HasValue
        && !identities.ResolveNativeListTarget(new[] { -1 }, 0, stale).HasValue
        && !identities.ResolveNativeListTarget(new[] { 20 }, 0, stale).HasValue
        && !identities.ResolveNativeListTarget(new[] { 7 }, 0, Array.Empty<long>()).HasValue,
        "Invalid list indices must fail closed.");
});

Case("Missing identities and out-of-range lookups fail closed without allocation", () =>
{
    var identities = new CtbEnemyIdentity(3);
    Check(!identities.Observe(1, 0, true).HasValue && identities.AllocatedCount == 0,
        "A missing native actor must not receive a badge.");
    Check(!identities.Get(1, 0).HasValue && !identities.Get(-1, 1).HasValue && !identities.Get(3, 1).HasValue,
        "Invalid native target queries must return no badge.");
    Throws<ArgumentOutOfRangeException>(() => identities.Observe(3, 3, true));
    Throws<ArgumentOutOfRangeException>(() => new CtbEnemyIdentity(0));
});

Console.WriteLine($"{passed} CTB enemy identity checks passed.");
