using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SuikodenRandomizer.Dialogue;

int passed = 0;
var cases = new List<string>();
void Check(bool condition, string reason) { if (!condition) throw new InvalidOperationException(reason); }
void Equal(string? expected, string? actual) => Check(string.Equals(expected, actual, StringComparison.Ordinal), "Expected " + expected + "; got " + actual);
void Original(string raw, string? actual) => Check(ReferenceEquals(raw, actual), "Fallback must return the whole original string.");
void Case(string name, Action action) { action(); cases.Add(name); passed++; Console.WriteLine("PASS " + name); }
void Reject(Action action) { bool rejected = false; try { action(); } catch (ArgumentException) { rejected = true; } Check(rejected, "Invalid plan was accepted."); }
DialogueMessageKey Key(ulong id = 1, string game = "GSD2", string language = "en") => new(game, language, id);
DialogueNameSpan Span(string raw, string name, int role, bool upper = false, int searchFrom = 0)
    => new(raw.IndexOf(name, searchFrom, StringComparison.Ordinal), name.Length, name, role, upper);
DialogueRewritePlan Plan(string raw, params DialogueNameSpan[] spans)
    => new(Key(), DialogueRewritePlan.ComputeSourceHash(raw), raw.Length, spans);
DialogueRewriteCatalog Catalog(string raw, params DialogueNameSpan[] spans) => new(new[] { Plan(raw, spans) });
string? Name(int role) => role switch { 28 => "Gadget", 42 => "Tuta", 22 => "Gengen", _ => null };

Case("Exact source spans support longer names, possessives and preserve native control code units", () =>
{
    string raw = "♂▼7∠Gengen's guard is Gadget. ♂㈱ ♂◆8";
    var catalog = Catalog(raw, Span(raw, "Gengen", 28), Span(raw, "Gadget", 42));
    Equal("♂▼7∠Long Chan-Chan's guard is Bob. ♂㈱ ♂◆8", catalog.Rewrite(raw, Key(), id => id == 28 ? "Long Chan-Chan" : "Bob"));
});
Case("A cyclic character permutation is applied once from the original spans", () =>
{
    string raw = "Gengen and Gadget met Tuta.";
    Equal("Gadget and Tuta met Gengen.", Catalog(raw, Span(raw, "Gengen", 28), Span(raw, "Gadget", 42), Span(raw, "Tuta", 22)).Rewrite(raw, Key(), Name));
});
Case("Ambiguous unplanned Feather item words remain literal", () =>
{
    string raw = "Feather: Wind Feather Ornament. Gengen agrees.";
    var calls = new List<int>();
    Equal("Feather: Wind Feather Ornament. Gadget agrees.", Catalog(raw, Span(raw, "Gengen", 28)).Rewrite(raw, Key(), id => { calls.Add(id); return Name(id); }));
    Check(calls.SequenceEqual(new[] { 28 }), "An unplanned ambiguous word acquired an identity.");
});
Case("A full reviewed name span avoids leaving a canonical surname behind", () =>
{
    string raw = "Georg Prime met Gengen.";
    Equal("Long Chan-Chan met Gadget.", Catalog(raw, Span(raw, "Georg Prime", 11), Span(raw, "Gengen", 28)).Rewrite(raw, Key(), id => id == 11 ? "Long Chan-Chan" : Name(id)));
});
Case("Same-length source hash mismatch does not invoke name resolution", () =>
{
    string raw = "Gengen met Tuta."; var catalog = Catalog(raw, Span(raw, "Gengen", 28)); int calls = 0;
    string changed = "Gengex met Tuta."; Original(changed, catalog.Rewrite(changed, Key(), _ => { calls++; return "Bob"; }));
    Check(calls == 0, "Hash-mismatched text invoked a resolver.");
});
Case("Whitespace, control and UTF16 length changes fail source pinning", () =>
{
    string raw = "Gengen∠Tuta"; var catalog = Catalog(raw, Span(raw, "Gengen", 28));
    foreach (string changed in new[] { raw + " ", raw.Replace('∠', '\n'), raw.Replace('∠', ' ') })
        Original(changed, catalog.Rewrite(changed, Key(), _ => throw new Exception("Unexpected resolution")));
});
Case("A wrong expected fragment fails even with a correct whole-source hash", () =>
{
    string raw = "Gengen";
    var plan = new DialogueRewritePlan(Key(), DialogueRewritePlan.ComputeSourceHash(raw), raw.Length, new[] { new DialogueNameSpan(0, 6, "Foobar", 28) });
    Original(raw, new DialogueRewriteCatalog(new[] { plan }).Rewrite(raw, Key(), _ => throw new Exception("Unexpected resolution")));
});
Case("Null, default, unknown game, unknown language, unknown ID and missing resolver are neutral", () =>
{
    string raw = "Gengen"; var catalog = Catalog(raw, Span(raw, raw, 28));
    Check(catalog.Rewrite(null, Key(), Name) is null, "Null changed.");
    foreach (DialogueMessageKey? key in new DialogueMessageKey?[] { null, default(DialogueMessageKey), new(null, "en", 1), new("GSD2", null, 1), Key(game: "OTHER"), Key(language: "fr"), Key(999) })
        Original(raw, catalog.Rewrite(raw, key, Name));
    Original(raw, catalog.Rewrite(raw, Key(), null));
});
Case("One message ID is isolated by game and exact language context", () =>
{
    string raw = "Gengen";
    var keys = new[] { Key(), Key(game: "GSD1"), Key(language: "fr") };
    var plans = keys.Select((key, i) => new DialogueRewritePlan(key, DialogueRewritePlan.ComputeSourceHash(raw), raw.Length, new[] { new DialogueNameSpan(0, raw.Length, raw, i) }));
    var catalog = new DialogueRewriteCatalog(plans);
    Equal("English S2", catalog.Rewrite(raw, keys[0], id => new[] { "English S2", "English S1", "French" }[id]));
    Equal("English S1", catalog.Rewrite(raw, keys[1], id => new[] { "English S2", "English S1", "French" }[id]));
    Equal("French", catalog.Rewrite(raw, keys[2], id => new[] { "English S2", "English S1", "French" }[id]));
    Original(raw, catalog.Rewrite(raw, Key(language: "EN"), Name));
});
Case("Unsigned IDs beyond Int32 and row aliases preserve source hash validation", () =>
{
    string raw = "Gengen"; var target = Key(ulong.MaxValue);
    var plan = new DialogueRewritePlan(target, DialogueRewritePlan.ComputeSourceHash(raw), raw.Length, new[] { Span(raw, raw, 28) });
    var catalog = new DialogueRewriteCatalog(new[] { plan }, new[] { new DialogueMessageAlias(Key(3001000089UL), uint.MaxValue), new DialogueMessageAlias(Key(uint.MaxValue), ulong.MaxValue) });
    Equal("Gadget", catalog.Rewrite(raw, Key(3001000089UL), Name));
    Equal("Gadget", catalog.Rewrite(raw, Key(uint.MaxValue), Name));
    Original(raw, catalog.Rewrite(raw, Key(3001000089UL, "GSD1"), Name));
});
Case("External unresolved alias targets remain original without touching a resolver", () =>
{
    string raw = "Gengen"; var catalog = new DialogueRewriteCatalog(Array.Empty<DialogueRewritePlan>(), new[] { new DialogueMessageAlias(Key(), 999) }); int calls = 0;
    Original(raw, catalog.Rewrite(raw, Key(), _ => { calls++; return "Gadget"; })); Check(calls == 0, "External alias resolved a name.");
});
Case("Self and multi-node alias cycles terminate unchanged", () =>
{
    string raw = "Gengen";
    foreach (var aliases in new[] { new[] { new DialogueMessageAlias(Key(), 1) }, new[] { new DialogueMessageAlias(Key(), 2), new DialogueMessageAlias(Key(2), 1) } })
        Original(raw, new DialogueRewriteCatalog(Array.Empty<DialogueRewritePlan>(), aliases).Rewrite(raw, Key(), _ => throw new Exception("Unexpected resolution")));
});
Case("Alias hop limit permits exactly64 and refuses longer chains", () =>
{
    string raw = "Gengen";
    foreach (int hops in new[] { DialogueRewriteCatalog.MaxAliasHops, DialogueRewriteCatalog.MaxAliasHops + 1 })
    {
        var plan = new DialogueRewritePlan(Key((ulong)hops), DialogueRewritePlan.ComputeSourceHash(raw), raw.Length, new[] { Span(raw, raw, 28) });
        var aliases = Enumerable.Range(0, hops).Select(i => new DialogueMessageAlias(Key((ulong)i), (ulong)i + 1));
        var result = new DialogueRewriteCatalog(new[] { plan }, aliases).Rewrite(raw, Key(0), Name);
        if (hops == DialogueRewriteCatalog.MaxAliasHops) Equal("Gadget", result); else Original(raw, result);
    }
});
Case("Caller-owned arrays and lists cannot mutate plans or the catalog", () =>
{
    string raw = "Gengen"; var spans = new[] { Span(raw, raw, 28) }; var plan = Plan(raw, spans);
    var plans = new List<DialogueRewritePlan> { plan }; var aliases = new[] { new DialogueMessageAlias(Key(2), 1) };
    var catalog = new DialogueRewriteCatalog(plans, aliases);
    spans[0] = new DialogueNameSpan(0, raw.Length, raw, 22); plans.Clear(); aliases[0] = new DialogueMessageAlias(Key(2), 999);
    Equal("Gadget", catalog.Rewrite(raw, Key(2), Name));
    Check(((ICollection<DialogueNameSpan>)plan.Spans).IsReadOnly, "Span collection is mutable.");
});
Case("Invalid, overlapping, duplicate and conflicting catalog data is rejected", () =>
{
    string raw = "Gengen Tuta"; var span = Span(raw, "Gengen", 28); var plan = Plan(raw, span);
    Reject(() => new DialogueNameSpan(-1, 1, "A", 1)); Reject(() => new DialogueNameSpan(0, 2, "A", 1));
    Reject(() => new DialogueNameSpan(0, 1, "A", -1)); Reject(() => new DialogueNameSpan(0, 2, "♂㈱", 1));
    Reject(() => new DialogueRewritePlan(Key(game: "other"), new string('0', 64), 0, Array.Empty<DialogueNameSpan>()));
    Reject(() => new DialogueRewritePlan(Key(), "not a hash", raw.Length, new[] { span }));
    Reject(() => Plan(raw, span, new DialogueNameSpan(1, 2, "en", 1)));
    Reject(() => Plan(raw, new DialogueNameSpan(raw.Length, 1, "A", 1)));
    Reject(() => new DialogueRewriteCatalog(new[] { plan, plan }));
    Reject(() => new DialogueRewriteCatalog(new[] { plan }, new[] { new DialogueMessageAlias(Key(), 2) }));
    Reject(() => new DialogueRewriteCatalog(Array.Empty<DialogueRewritePlan>(), new[] { new DialogueMessageAlias(Key(), 2), new DialogueMessageAlias(Key(), 3) }));
});
Case("Reviewed spans are sorted without changing their source meaning", () =>
{
    string raw = "Gengen Tuta"; Equal("Gadget Gengen", Catalog(raw, Span(raw, "Tuta", 22), Span(raw, "Gengen", 28)).Rewrite(raw, Key(), Name));
});
Case("A missing second name rolls back the whole presentation result", () =>
{
    string raw = "Gengen Tuta"; var catalog = Catalog(raw, Span(raw, "Gengen", 28), Span(raw, "Tuta", 22));
    Original(raw, catalog.Rewrite(raw, Key(), id => id == 28 ? "Gadget" : null));
});
Case("A throwing second name leaves the whole original intact", () =>
{
    string raw = "Gengen Tuta"; var catalog = Catalog(raw, Span(raw, "Gengen", 28), Span(raw, "Tuta", 22));
    Original(raw, catalog.Rewrite(raw, Key(), id => id == 28 ? "Gadget" : throw new InvalidOperationException("name lookup")));
});
Case("Control, markup, empty and malformed target names cannot inject parser commands", () =>
{
    string raw = "Gengen Tuta"; var catalog = Catalog(raw, Span(raw, "Gengen", 28), Span(raw, "Tuta", 22));
    string[] unsafeNames = { "", "   ", "A♂▼7", "A∠B", "A\0B", "A\nB", "A\rB", "A\tB", "A\u001bB", "A\u007fB", "<b>A</b>", "A>", "[name]", "{name}", "&lt;", "A\\B", "A\u2028B", "A\u2029B", "A\u202eB", "A\U000e0001B", "\ud800", "\udc00" };
    foreach (string name in unsafeNames) Original(raw, catalog.Rewrite(raw, Key(), id => id == 28 ? "Gadget" : name));
});
Case("Every observed native command and selector glyph is refused in literal and resolved names", () =>
{
    string raw = "Gengen Tuta"; var catalog = Catalog(raw, Span(raw, "Gengen", 28), Span(raw, "Tuta", 22));
    foreach (char glyph in "☆◎∠∨≒♂〒※㈱①②③④⑤⑥⑦⑧⑨⑩■●★▼▲◆|Λ")
    {
        string injected = "A" + glyph + "B";
        // The first name has a valid replacement: the second must still reject
        // the WHOLE output rather than leave a partially rewritten message.
        Original(raw, catalog.Rewrite(raw, Key(), id => id == 28 ? "Gadget" : injected));
        Reject(() => new DialogueNameSpan(0, injected.Length, injected, 22));
    }
});
Case("Observed native source glyphs remain opaque and unchanged outside reviewed names", () =>
{
    string controls = "☆◎∠∨≒♂〒※㈱①②③④⑤⑥⑦⑧⑨⑩■●★▼▲◆|Λ";
    string raw = controls + " Gengen " + controls;
    Equal(controls + " Gadget " + controls, Catalog(raw, Span(raw, "Gengen", 28)).Rewrite(raw, Key(), Name));
});
Case("Valid surrogate pairs use UTF16 offsets rather than Unicode scalar offsets", () =>
{
    string raw = "😀 Gengen!"; Check(raw.IndexOf("Gengen", StringComparison.Ordinal) == 3, "Test is not UTF16.");
    Equal("😀 🐦 Bird!", Catalog(raw, Span(raw, "Gengen", 28)).Rewrite(raw, Key(), _ => "🐦 Bird"));
    var wrong = new DialogueNameSpan(2, 6, "Gengen", 28);
    Original(raw, Catalog(raw, wrong).Rewrite(raw, Key(), _ => "Bird"));
});
Case("Hashing preserves exact unpaired source code units and untouched source suffixes", () =>
{
    string raw = "\ud800 Gengen \udc00";
    byte[] bytes = { 0, 0xd8, 0x20, 0, (byte)'G', 0, (byte)'e', 0, (byte)'n', 0, (byte)'g', 0, (byte)'e', 0, (byte)'n', 0, 0x20, 0, 0, 0xdc };
    Equal(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), DialogueRewritePlan.ComputeSourceHash(raw));
    Check(DialogueRewritePlan.ComputeSourceHash("\ud800") != DialogueRewritePlan.ComputeSourceHash("\ud801"), "Fallback encoding collapsed different source code units.");
    Equal("\ud800 Gadget \udc00", Catalog(raw, Span(raw, "Gengen", 28)).Rewrite(raw, Key(), Name));
});
Case("Source hash algorithm has no BOM and is distinct from UTF8", () =>
{
    string raw = "A😀B";
    Equal(Convert.ToHexString(SHA256.HashData(Encoding.Unicode.GetBytes(raw))).ToLowerInvariant(), DialogueRewritePlan.ComputeSourceHash(raw));
    Check(DialogueRewritePlan.ComputeSourceHash(raw) != Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant(), "Wrong hash encoding.");
    Equal("sha256-utf16le-codeunits-v1", DialogueRewritePlan.SourceHashAlgorithm);
});
Case("One canonical role name resolves once even for mixed uppercase occurrences", () =>
{
    string raw = "Gadget GADGET"; int calls = 0;
    var prior = CultureInfo.CurrentCulture; CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
    try
    {
        Equal("Shin SHIN", Catalog(raw, Span(raw, "Gadget", 42), Span(raw, "GADGET", 42, true)).Rewrite(raw, Key(), _ => { calls++; return "Shin"; }));
        Check(calls == 1, "Repeated role lookup observed multiple states.");
    }
    finally { CultureInfo.CurrentCulture = prior; }
});
Case("Already-projected provenance prevents double mapping even under another message key", () =>
{
    string raw = "Gengen", secondRaw = "Gadget";
    var first = Plan(raw, Span(raw, raw, 28));
    var second = new DialogueRewritePlan(Key(2), DialogueRewritePlan.ComputeSourceHash(secondRaw), secondRaw.Length, new[] { Span(secondRaw, secondRaw, 42) });
    var catalog = new DialogueRewriteCatalog(new[] { first, second });
    string output = catalog.Rewrite(raw, Key(), Name)!; Equal("Gadget", output);
    Original(output, catalog.Rewrite(output, Key(), Name)); // Same key fails original hash.
    Original(output, catalog.Rewrite(output, Key(2), _ => throw new Exception("Unexpected resolution"), alreadyProjected: true));
    Equal("Tuta", catalog.Rewrite(secondRaw, Key(2), Name)); // A genuine second original message still works.
});
Case("Identity mapping and zero-span plans return the original reference", () =>
{
    string raw = "Gengen"; Original(raw, Catalog(raw, Span(raw, raw, 28)).Rewrite(raw, Key(), _ => raw));
    Original(raw, Catalog(raw).Rewrite(raw, Key(), _ => throw new Exception("Unexpected resolution")));
});
Case("Resolver receives canonical role ID rather than applying a donor ID a second time", () =>
{
    string raw = "Gengen"; var calls = new List<int>();
    Equal("Gadget", Catalog(raw, Span(raw, raw, 28)).Rewrite(raw, Key(), canonical => { calls.Add(canonical); return canonical == 28 ? "Gadget" : "wrong second projection"; }));
    Check(calls.SequenceEqual(new[] { 28 }), "Resolver received donor ID.");
});

Console.WriteLine(passed + "/" + passed + " pure-managed dialogue rewrite cases passed.");
if (args.Length == 1)
    File.WriteAllText(args[0], JsonSerializer.Serialize(new { passed, failed = 0, cases, hashAlgorithm = DialogueRewritePlan.SourceHashAlgorithm,
        scope = "Actual pure-managed library; no game APIs, native adapters, text hooks, runtime attachments or installed writes." }, new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
