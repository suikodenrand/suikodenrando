using System;
using System.Collections.Generic;
using System.Text;

namespace SuikodenRandomizer.Dialogue;

/// <summary>Pure, source-pinned presentation transform. No game, filesystem, runtime or locale I/O.</summary>
public sealed class DialogueRewriteCatalog
{
    public const int MaxAliasHops = 64;
    private readonly Dictionary<DialogueMessageKey, DialogueRewritePlan> _plans = new Dictionary<DialogueMessageKey, DialogueRewritePlan>();
    private readonly Dictionary<DialogueMessageKey, ulong> _aliases = new Dictionary<DialogueMessageKey, ulong>();
    public DialogueRewriteCatalog(IEnumerable<DialogueRewritePlan> plans, IEnumerable<DialogueMessageAlias>? aliases = null)
    {
        if (plans is null) throw new ArgumentNullException(nameof(plans));
        foreach (var plan in plans)
        {
            if (plan is null) throw new ArgumentException("Null rewrite plan.", nameof(plans));
            if (_plans.ContainsKey(plan.Key)) throw new ArgumentException("Duplicate message plan.", nameof(plans));
            _plans.Add(plan.Key, plan);
        }
        if (aliases is null) return;
        foreach (var alias in aliases)
        {
            if (alias is null) throw new ArgumentException("Null alias.", nameof(aliases));
            if (_plans.ContainsKey(alias.Key) || _aliases.ContainsKey(alias.Key))
                throw new ArgumentException("Alias keys cannot duplicate plans or other aliases.", nameof(aliases));
            _aliases.Add(alias.Key, alias.TargetMessageId);
        }
    }

    internal bool HasEntry(DialogueMessageKey key) => _plans.ContainsKey(key) || _aliases.ContainsKey(key);

    public string? Rewrite(string? rawText, DialogueMessageKey? request,
        Func<int, string?>? resolveAlreadyProjectedCanonicalRoleName, bool alreadyProjected = false,
        Func<int, bool>? shouldReplaceRole = null)
    {
        if (rawText is null || alreadyProjected || !request.HasValue || !request.Value.HasContext
            || resolveAlreadyProjectedCanonicalRoleName is null) return rawText;
        var plan = Resolve(request.Value);
        if (plan is null || plan.Spans.Count == 0 || rawText.Length != plan.SourceUtf16Length) return rawText;
        if (!string.Equals(DialogueRewritePlan.ComputeSourceHash(rawText), plan.SourceSha256, StringComparison.Ordinal)) return rawText;
        foreach (var span in plan.Spans)
            if (string.CompareOrdinal(rawText, span.StartUtf16, span.ExpectedText, 0, span.Length) != 0) return rawText;

        var resolved = new Dictionary<int, string>();
        var admitted = new Dictionary<int, bool>();
        var outputNames = new string[plan.Spans.Count];
        bool changed = false;
        try
        {
            // Complete all resolution and validation before producing any output.
            for (int index = 0; index < plan.Spans.Count; index++)
            {
                var span = plan.Spans[index];
                if (!admitted.TryGetValue(span.CanonicalRoleId, out bool replace))
                {
                    replace = shouldReplaceRole?.Invoke(span.CanonicalRoleId) ?? true;
                    admitted.Add(span.CanonicalRoleId, replace);
                }
                if (!replace)
                {
                    // Unassigned Fixed Roster roles and identity mappings keep
                    // their exact original alias/case; they need no name lookup.
                    outputNames[index] = span.ExpectedText;
                    continue;
                }
                if (!resolved.TryGetValue(span.CanonicalRoleId, out string? name))
                {
                    name = resolveAlreadyProjectedCanonicalRoleName(span.CanonicalRoleId);
                    if (name is null || !NameSafety.IsSafe(name)) return rawText;
                    resolved.Add(span.CanonicalRoleId, name);
                }
                string output = span.UppercaseOutput ? name.ToUpperInvariant() : name;
                if (!NameSafety.IsSafe(output)) return rawText;
                outputNames[index] = output;
                changed |= !string.Equals(output, span.ExpectedText, StringComparison.Ordinal);
            }
        }
        catch (Exception) { return rawText; }
        if (!changed) return rawText;

        var result = new StringBuilder();
        int cursor = 0;
        for (int index = 0; index < plan.Spans.Count; index++)
        {
            var span = plan.Spans[index];
            result.Append(rawText, cursor, span.StartUtf16 - cursor);
            result.Append(outputNames[index]);
            cursor = span.StartUtf16 + span.Length;
        }
        result.Append(rawText, cursor, rawText.Length - cursor);
        return result.ToString();
    }

    private DialogueRewritePlan? Resolve(DialogueMessageKey key)
    {
        var visited = new HashSet<DialogueMessageKey>();
        for (int hop = 0; hop <= MaxAliasHops; hop++)
        {
            if (_plans.TryGetValue(key, out var plan)) return plan;
            if (hop == MaxAliasHops || !visited.Add(key) || !_aliases.TryGetValue(key, out ulong next)) return null;
            key = new DialogueMessageKey(key.Game, key.Language, next);
        }
        return null;
    }
}
