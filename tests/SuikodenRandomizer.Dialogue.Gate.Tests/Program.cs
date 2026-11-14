using SuikodenRandomizer.Dialogue;

const string Raw = "Gengen met Georg Prime.♂㈱∠";
const int Sid = 1000000001;
int passed = 0;
DialogueRewritePlan Plan(string game = "GSD2", ulong sid = Sid, string text = Raw, int firstRole = 28) => new(
    new DialogueMessageKey(game, "en", sid), DialogueRewritePlan.ComputeSourceHash(text), text.Length,
    new[] { new DialogueNameSpan(0, 6, "Gengen", firstRole), new DialogueNameSpan(11, 11, "Georg Prime", 11) });
DialogueRewriteGate Gate() => new(new DialogueRewriteCatalog(new[] { Plan(), Plan("GSD1", firstRole: 0), Plan(sid: 3000000001) }));
DialogueRewriteContext Context(Func<bool>? current = null, Func<int, string?>? name = null,
    Func<int?>? cap = null, string game = "GSD2", string language = "en", Func<int, bool>? replace = null) => new(
        game, language, current ?? (() => true), name ?? (role => role == 11 ? "Bob" : "Humphrey"), cap, replace);
void Equal(string? expected, string? actual) { if (expected != actual) throw new Exception($"Expected '{expected}', got '{actual}'."); }
void Check(bool value) { if (!value) throw new Exception("Failed assertion."); }
void Case(string title, Action check)
{
    try { check(); passed++; Console.WriteLine("PASS " + title); }
    catch (Exception error) { Console.Error.WriteLine("FAIL " + title + ": " + error); Environment.Exit(1); }
}
Case("No native window scope means no rewrite", () => Equal(Raw, Gate().Rewrite(Raw, "message", Sid)));
Case("Known raw lookup changes once and preserves the original native suffix", () =>
{
    var gate = Gate(); using var scope = gate.Enter(Context());
    Equal("Humphrey met Bob.♂㈱∠", gate.Rewrite(Raw, "message", Sid));
    Equal("Humphrey met Bob.♂㈱∠", gate.Rewrite("Humphrey met Bob.♂㈱∠", "message", Sid));
});
Case("S1 role zero resolves under its own game context", () =>
{
    var gate = Gate(); var seen = new List<int>();
    using var scope = gate.Enter(Context(game: "GSD1", name: role => { seen.Add(role); return role == 0 ? "Kai" : "Cleo"; }));
    Equal("Kai met Cleo.♂㈱∠", gate.Rewrite(Raw, "message", Sid)); Check(seen.Contains(0));
});
Case("Wrong category, language and unknown game stay original", () =>
{
    var gate = Gate();
    using (gate.Enter(Context())) Equal(Raw, gate.Rewrite(Raw, "add_message", Sid));
    using (gate.Enter(Context(language: "ja"))) Equal(Raw, gate.Rewrite(Raw, "message", Sid));
    using (gate.Enter(Context(game: "OTHER"))) Equal(Raw, gate.Rewrite(Raw, "message", Sid));
});
Case("Unadmitted nested invocation creates a barrier then restores the outer scope", () =>
{
    var gate = Gate(); using var outer = gate.Enter(Context());
    using (gate.Enter(null)) Equal(Raw, gate.Rewrite(Raw, "message", Sid));
    Equal("Humphrey met Bob.♂㈱∠", gate.Rewrite(Raw, "message", Sid));
});
Case("Nested foreign game cannot inherit the outer canonical resolver", () =>
{
    var gate = Gate(); using var outer = gate.Enter(Context());
    using (gate.Enter(Context(game: "GSD1", current: () => false))) Equal(Raw, gate.Rewrite(Raw, "message", Sid));
    Equal("Humphrey met Bob.♂㈱∠", gate.Rewrite(Raw, "message", Sid));
});
Case("Native name lookup suppression survives nested getters and restores", () =>
{
    var gate = Gate(); using var scope = gate.Enter(Context());
    using (gate.SuppressNames())
    {
        using (gate.SuppressNames()) Equal(Raw, gate.Rewrite(Raw, "message", Sid));
        Equal(Raw, gate.Rewrite(Raw, "message", Sid));
    }
    Equal("Humphrey met Bob.♂㈱∠", gate.Rewrite(Raw, "message", Sid));
});
Case("Resolving a replacement cannot recursively rewrite another raw message", () =>
{
    var gate = Gate(); int resolved = 0;
    using var scope = gate.Enter(Context(name: role => { resolved++; Equal(Raw, gate.Rewrite(Raw, "message", Sid)); return "Kai"; }));
    Equal("Kai met Kai.♂㈱∠", gate.Rewrite(Raw, "message", Sid)); Check(resolved == 2);
});
Case("Ownership revoked before lookup does not resolve a name", () =>
{
    var gate = Gate(); using var scope = gate.Enter(Context(current: () => false, name: _ => throw new Exception("must not resolve")));
    Equal(Raw, gate.Rewrite(Raw, "message", Sid));
});
Case("Ownership revoked during final name resolution rolls back all edits", () =>
{
    var gate = Gate(); bool current = true;
    using var scope = gate.Enter(Context(current: () => current, name: role => { if (role == 11) current = false; return "Kai"; }));
    Equal(Raw, gate.Rewrite(Raw, "message", Sid));
});
Case("Ownership revoked while checking native capacity rolls back", () =>
{
    var gate = Gate(); bool current = true;
    using var scope = gate.Enter(Context(current: () => current, cap: () => { current = false; return 961; }));
    Equal(Raw, gate.Rewrite(Raw, "message", Sid));
});
Case("A native capacity bound is inclusive and never truncates the suffix", () =>
{
    var gate = Gate(); const string expected = "Humphrey met Bob.♂㈱∠";
    using (gate.Enter(Context(cap: () => expected.Length))) Equal(expected, gate.Rewrite(Raw, "message", Sid));
    using (gate.Enter(Context(cap: () => expected.Length - 1))) Equal(Raw, gate.Rewrite(Raw, "message", Sid));
    using (gate.Enter(Context(cap: () => 0))) Equal(Raw, gate.Rewrite(Raw, "message", Sid));
    using (gate.Enter(Context(cap: () => null))) Equal(expected, gate.Rewrite(Raw, "message", Sid));
});
Case("Native callback errors and missing names preserve the whole original", () =>
{
    var gate = Gate();
    using (gate.Enter(Context(current: () => throw new Exception("probe")))) Equal(Raw, gate.Rewrite(Raw, "message", Sid));
    using (gate.Enter(Context(name: role => role == 11 ? null : "Kai"))) Equal(Raw, gate.Rewrite(Raw, "message", Sid));
    using (gate.Enter(Context(cap: () => throw new Exception("capacity")))) Equal(Raw, gate.Rewrite(Raw, "message", Sid));
});
Case("Signed native SIDs preserve all bits for large localized row IDs", () =>
{
    var gate = Gate(); using var scope = gate.Enter(Context());
    Equal("Humphrey met Bob.♂㈱∠", gate.Rewrite(Raw, "message", unchecked((int)3000000001U)));
});
Case("Fixed roster leaves an unassigned full alias intact beside an assigned role", () =>
{
    var gate = Gate(); using var scope = gate.Enter(Context(
        name: role => role == 28 ? "Humphrey" : throw new Exception("unassigned role must not resolve"), replace: role => role == 28));
    Equal("Humphrey met Georg Prime.♂㈱∠", gate.Rewrite(Raw, "message", Sid));
});
Case("Role admission errors roll back before output and all identity roles stay verbatim", () =>
{
    var gate = Gate();
    using (gate.Enter(Context(replace: role => role == 11 ? throw new Exception("assignment changed") : true)))
        Equal(Raw, gate.Rewrite(Raw, "message", Sid));
    using (gate.Enter(Context(replace: _ => false, name: _ => throw new Exception("identity role"))))
        Equal(Raw, gate.Rewrite(Raw, "message", Sid));
});
Case("Synchronous finalization ends the scope before later iterator steps", () =>
{
    var gate = Gate();
    try { using var scope = gate.Enter(Context()); throw new InvalidOperationException("native method"); }
    catch (InvalidOperationException) { }
    Equal(Raw, gate.Rewrite(Raw, "message", Sid));
    using (gate.Enter(Context())) Equal("Humphrey met Bob.♂㈱∠", gate.Rewrite(Raw, "message", Sid));
});
Case("Thread-local scopes never flow into a worker thread", () =>
{
    var gate = Gate(); using var scope = gate.Enter(Context());
    Task.Run(() => Equal(Raw, gate.Rewrite(Raw, "message", Sid))).GetAwaiter().GetResult();
    Equal("Humphrey met Bob.♂㈱∠", gate.Rewrite(Raw, "message", Sid));
});
Case("Misnested scope disposal fails closed and duplicate disposal is harmless", () =>
{
    var gate = Gate(); var outer = gate.Enter(Context()); var inner = gate.Enter(Context());
    outer.Dispose(); outer.Dispose(); inner.Dispose();
    using var next = gate.Enter(Context()); Equal(Raw, gate.Rewrite(Raw, "message", Sid));
});
Case("Cross-thread scope disposal invalidates the original thread context", () =>
{
    var gate = Gate(); var scope = gate.Enter(Context());
    Task.Run(scope.Dispose).GetAwaiter().GetResult();
    Equal(Raw, gate.Rewrite(Raw, "message", Sid));
});
Console.WriteLine($"Passed {passed} dialogue gate checks.");
