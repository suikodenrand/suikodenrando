# Reviewed dialogue name spans

This managed component changes explicitly planned name spans in an exact original message. It has no game references, native hooks, file I/O or full dialogue catalog. The 0.10.2 combined host links the same source and supplies native caller, ownership, language and live-name evidence.

`DialogueRewriteCatalog.Rewrite(rawText, request, resolveAlreadyProjectedCanonicalRoleName, alreadyProjected)` returns the whole original on unknown context, missing plan, source mismatch, unresolved/cyclic alias, missing or unsafe name, or resolver exception. No partial result escapes. Invalid catalog definitions throw during construction.

The optional `shouldReplaceRole` predicate lets a consumer leave an unassigned or identity role unchanged while still replacing other roles in the message. A false result preserves that role's exact source fragments, including full aliases and case, without invoking its name resolver. Admission and name resolution run once per canonical role.

`DialogueRewriteGate` manages synchronous, thread-local dialogue scopes. An unadmitted nested entry blocks the outer scope. Name suppression prevents a nested native lookup from rewriting again, and ownership is checked before and after resolving names and reading an optional native UTF-16 copy bound. An invalid bound, stale scope or oversized result keeps the original without truncating it.

```csharp
var key = new DialogueMessageKey("GSD2", "en", 1000000001UL);
var plan = new DialogueRewritePlan(key,
    DialogueRewritePlan.ComputeSourceHash(original), original.Length,
    new[] { new DialogueNameSpan(0, 6, "Gengen", 28) });
var catalog = new DialogueRewriteCatalog(new[] { plan });
string? display = catalog.Rewrite(original, key, canonicalRole => GetCurrentName(canonicalRole));
```

The resolver receives the original canonical role ID and returns its **already projected** current display name. Do not map that ID to a donor and then call a name API that already reads projected role names. In Suikoden II, both `G2_name_get` and `G2_name_get_org` read the current role name table; `_org` does not mean an untouched original table. Fixed rosters require current saved assignments, not only a seed mapping.

`SourceHashAlgorithm` is `sha256-utf16le-codeunits-v1`: SHA256 over each exact UTF-16 code unit, low byte then high byte, without a BOM or normalization. `ComputeSourceHash` deliberately preserves unpaired source surrogates. The matching Python protocol is `hashlib.sha256(text.encode('utf-16-le', errors='surrogatepass')).hexdigest()`. Lengths and offsets count UTF-16 code units, not Unicode scalar values or UTF-8 bytes.

Plans copy and sort their input spans and expose immutable data. Each span requires an expected literal name, canonical role, and optional invariant uppercase output. Non-name code units are copied unchanged. Each canonical role is resolved once per call. All replacements read original source offsets, so cyclic permutations and longer names cannot cascade.

Aliases are bounded row-to-row aliases within one game and language. Cycles, external unresolved targets and paths over 64 edges stay unchanged. Native resource adapters must first be interpreted under their actual contract: this generic resolver does not assert that a native one-hop adapter is recursive. Plan/alias key collisions and duplicate keys are rejected rather than choosing an implicit precedence.

Literal expected names and resolved target names reject the observed native command/selector glyph set `☆◎∠∨≒♂〒※㈱①②③④⑤⑥⑦⑧⑨⑩■●★▼▲◆|Λ`, controls, line separators, Unicode format characters, markup delimiters, ampersands, backslashes and malformed surrogate sequences. This includes command introducers even when they would require further operands; replacement names cannot inject them. Native controls and dynamic name tokens in the source are opaque; they must not be included in name spans. Names split by native controls are unsupported until separately reviewed. A registry author must also exclude unsafe source positions such as tag attributes and raw flag arguments; the generic library does not parse the native grammar.

The catalog builder owns semantic admission. A word boundary does not prove a character mention: `Wind Feather Ornament` is an item, shared labels can identify different entities, full aliases such as `Georg Prime` must be replaced as a whole, and protagonist names can be player-selected. Use original language-specific identity evidence and per-occurrence review. Never infer arbitrary hero aliases or use UI catalog labels as a localization registry.

Source hashes normally reject a second transform of changed output. They cannot prove provenance when an output happens to equal another valid original message. Callers must pass original lookup results only and set `alreadyProjected` for known processed text. The prototype supplies no global processed-string cache.

The console project under `tests/SuikodenRandomizer.Dialogue.Tests` links this library and tests exact-source fallback, immutable inputs, cyclic permutations, opaque controls, UTF-16 pairs/unpaired source units, injection refusal, aliases, language/game isolation and explicit double-projection provenance. Passing these tests does not qualify a native hook or every generated mention plan.
