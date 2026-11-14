using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using BepInEx.Configuration;
using HarmonyLib;
using SuikodenRandomizer.Dialogue;
using SuikodenRandomizer.Shared;

namespace SuikodenRandomizer.Unified;

internal sealed class NativeDialogueContext
{
    internal NativeDialogueContext(string game, Func<bool> isCurrent, Func<int, string?> resolveName,
        Func<int?> maxUtf16Length, Func<int, bool>? shouldReplaceRole = null)
    {
        Game = game; IsCurrent = isCurrent; ResolveName = resolveName;
        MaxUtf16Length = maxUtf16Length; ShouldReplaceRole = shouldReplaceRole;
    }
    internal string Game { get; }
    internal Func<bool> IsCurrent { get; }
    internal Func<int, string?> ResolveName { get; }
    internal Func<int?> MaxUtf16Length { get; }
    internal Func<int, bool>? ShouldReplaceRole { get; }
}

/// <summary>Return-only native adapter. It never writes localization tables, IDs, event state or saves.</summary>
internal static class DialogueNative
{
    private static DialogueRewriteGate? _gate;
    private static ConfigEntry<bool>? _enabled;
    private static bool _installed;
    private static string? _failure;
    private static long _s1Changed, _s2Changed;
    private static readonly HashSet<string> Warnings = new(StringComparer.Ordinal);
    internal static bool Enabled => _installed && _enabled?.Value == true;

    internal static void Install(ConfigFile config)
    {
        var harmony = new Harmony(UnifiedPlugin.PluginGuid + ".Dialogue");
        try
        {
            _enabled = config.Bind("Dialogue", "ReplaceCharacterMentions", true,
                "Test feature: use the active campaign's replacement names in supported English dialogue and backlog. Unknown or ambiguous messages keep their original text.");
            var plans = new List<DialogueRewritePlan>();
            var aliases = new List<DialogueMessageAlias>();
            LoadCatalog("GSD2", plans, aliases);
            LoadCatalog("GSD1", plans, aliases);
            _gate = new DialogueRewriteGate(new DialogueRewriteCatalog(plans, aliases));
            UnifiedPlugin.ApplyPatches(harmony, new[] { typeof(DialogueRawTextPatch) }.Concat(DialogueS2.PatchTypes).Concat(DialogueS1.PatchTypes));
            _installed = true;
            UnifiedPlugin.HostLog.LogInfo("[DIALOGUE] English name replacement test feature ready; "
                + plans.Count(p => p.Key.Game == "GSD2") + " S2 plans and "
                + plans.Count(p => p.Key.Game == "GSD1") + " S1 plans; " + aliases.Count
                + " one-hop aliases. Enabled=" + _enabled.Value + ". Only admitted dialogue scopes can change returned text.");
        }
        catch (Exception error)
        {
            _installed = false; _gate = null; _failure = error.GetType().Name;
            try { harmony.UnpatchSelf(); } catch { }
            UnifiedPlugin.HostLog.LogError("[DIALOGUE] Dialogue replacement disabled; the randomizer engines remain available. " + error);
        }
    }

    internal static IDisposable Enter(ActiveGame game, Func<NativeDialogueContext?> capture)
    {
        if (_gate is null) return EmptyLease.Instance;
        DialogueRewriteContext? context = null;
        try
        {
            string expectedGame = game == ActiveGame.Gsd2 ? "GSD2" : game == ActiveGame.Gsd1 ? "GSD1" : "";
            if (Enabled && expectedGame.Length != 0 && UnifiedPlugin.Observe().Game == game
                && global::TextMasterData.CurrentLanguage == global::TextMasterData.Lang.English)
            {
                var snapshot = capture();
                if (snapshot is not null && snapshot.Game == expectedGame)
                {
                    // Both snapshots already check their exact game owner and
                    // ownership generation before touching native name state.
                    bool Current() => Enabled && snapshot.IsCurrent()
                        && global::TextMasterData.CurrentLanguage == global::TextMasterData.Lang.English;
                    context = new DialogueRewriteContext(expectedGame, "en", Current,
                        snapshot.ResolveName, snapshot.MaxUtf16Length, snapshot.ShouldReplaceRole);
                }
            }
        }
        catch (Exception error) { WarnOnce("scope:" + game, error); }
        // Always push a barrier, including inactive or foreign native entries.
        return _gate.Enter(context);
    }

    internal static IDisposable SuppressNames() => _gate?.SuppressNames() ?? EmptyLease.Instance;

    internal static void Rewrite(string category, int sid, ref string result)
    {
        if (!Enabled || _gate is null) return;
        string original = result;
        try
        {
            string? rewritten = _gate.Rewrite(original, category, sid, out string? game);
            if (game is null || !Enabled) return;
            result = rewritten!;
            long count = game == "GSD2" ? Interlocked.Increment(ref _s2Changed) : Interlocked.Increment(ref _s1Changed);
            if (count <= 12)
                UnifiedPlugin.HostLog.LogInfo("[DIALOGUE] " + game + " message=" + unchecked((uint)sid)
                    + " returned with replacement names; original UTF16=" + original.Length + ", new=" + result.Length + ".");
        }
        catch (Exception error) { result = original; WarnOnce("lookup", error); }
    }

    internal static string Status(ActiveGame game)
    {
        if (!_installed) return "Dialogue names: unavailable" + (_failure is null ? "." : " (" + _failure + ").");
        if (!Enabled) return "Dialogue names: off.";
        string eligibility;
        try
        {
            eligibility = global::TextMasterData.CurrentLanguage == global::TextMasterData.Lang.English
                ? "eligible for supported English dialogue"
                : "inactive in the current language";
        }
        catch { eligibility = "enabled, current language unavailable"; }
        long changed = game == ActiveGame.Gsd2 ? Interlocked.Read(ref _s2Changed) : Interlocked.Read(ref _s1Changed);
        return "Dialogue names: " + eligibility + ". Rewritten lookups this session: " + changed + ".";
    }
    internal static void TraceStatus(ActiveGame game) => UnifiedPlugin.HostLog.LogInfo("[DIALOGUE] " + game + " " + Status(game));

    private static void WarnOnce(string stage, Exception error)
    {
        lock (Warnings)
        {
            if (Warnings.Count >= 8 || !Warnings.Add(stage)) return;
            UnifiedPlugin.HostLog?.LogWarning("[DIALOGUE] Original text retained at " + stage + ": " + error.GetType().Name + ": " + error.Message);
        }
    }

    private static void LoadCatalog(string game, List<DialogueRewritePlan> plans, List<DialogueMessageAlias> aliases)
    {
        using Stream stream = typeof(DialogueNative).Assembly.GetManifestResourceStream("StarsOfDestiny.Dialogue." + game + ".en.json")
            ?? throw new InvalidDataException("Missing embedded " + game + " dialogue catalog.");
        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement;
        if (root.GetProperty("game").GetString() != game || root.GetProperty("language").GetString() != "en")
            throw new InvalidDataException("Unexpected dialogue catalog context.");
        var current = new HashSet<ulong>();
        foreach (var row in root.GetProperty("plans").EnumerateArray())
        {
            ulong sid = row.GetProperty("messageId").GetUInt64();
            if (sid > uint.MaxValue || game == "GSD2" && (sid < 1_000_000_000 || sid >= 2_000_000_000))
                throw new InvalidDataException("Dialogue message outside the admitted runtime ID policy.");
            var spans = row.GetProperty("spans").EnumerateArray().Select(span => new DialogueNameSpan(
                span.GetProperty("startUtf16").GetInt32(), span.GetProperty("length").GetInt32(),
                span.GetProperty("expectedText").GetString()!, span.GetProperty("canonicalRoleId").GetInt32(),
                span.TryGetProperty("uppercaseOutput", out var uppercase) && uppercase.GetBoolean())).ToArray();
            if (spans.Length == 0 || !current.Add(sid)) throw new InvalidDataException("Empty or duplicate dialogue plan.");
            plans.Add(new DialogueRewritePlan(new DialogueMessageKey(game, "en", sid),
                row.GetProperty("sourceSha256").GetString()!, row.GetProperty("sourceUtf16Length").GetInt32(), spans));
        }
        foreach (var row in root.GetProperty("aliases").EnumerateArray())
        {
            ulong sid = row.GetProperty("messageId").GetUInt64(), target = row.GetProperty("targetMessageId").GetUInt64();
            if (sid > uint.MaxValue || !current.Contains(target))
                throw new InvalidDataException("Native dialogue aliases must target an admitted plan in one hop.");
            aliases.Add(new DialogueMessageAlias(new DialogueMessageKey(game, "en", sid), target));
        }
    }

    private sealed class EmptyLease : IDisposable
    {
        internal static readonly EmptyLease Instance = new();
        public void Dispose() { }
    }
}

[HarmonyPatch(typeof(global::TextMasterData), "GetSystemText", new Type[] { typeof(string), typeof(int) })]
internal static class DialogueRawTextPatch
{
    [HarmonyPostfix]
    private static void Postfix(string __0, int __1, ref string __result) => DialogueNative.Rewrite(__0, __1, ref __result);
}
