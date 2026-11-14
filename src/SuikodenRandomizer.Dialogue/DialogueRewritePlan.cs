using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace SuikodenRandomizer.Dialogue;

/// <summary>Exact, case-sensitive game/language context. Default or unknown request keys do not rewrite.</summary>
public readonly struct DialogueMessageKey : IEquatable<DialogueMessageKey>
{
    public DialogueMessageKey(string? game, string? language, ulong messageId)
    { Game = game; Language = language; MessageId = messageId; }
    public string? Game { get; }
    public string? Language { get; }
    public ulong MessageId { get; }
    internal bool HasContext => (Game == "GSD1" || Game == "GSD2") && !string.IsNullOrWhiteSpace(Language);
    public bool Equals(DialogueMessageKey other) => string.Equals(Game, other.Game, StringComparison.Ordinal)
        && string.Equals(Language, other.Language, StringComparison.Ordinal) && MessageId == other.MessageId;
    public override bool Equals(object? other) => other is DialogueMessageKey key && Equals(key);
    public override int GetHashCode()
    {
        unchecked { return ((Game?.GetHashCode() ?? 0) * 397 ^ (Language?.GetHashCode() ?? 0)) * 397 ^ MessageId.GetHashCode(); }
    }
}

/// <summary>An explicitly reviewed name occurrence, indexed in original UTF-16 code units.</summary>
public sealed class DialogueNameSpan
{
    public DialogueNameSpan(int startUtf16, int length, string expectedText, int canonicalRoleId, bool uppercaseOutput = false)
    {
        if (startUtf16 < 0) throw new ArgumentOutOfRangeException(nameof(startUtf16));
        if (length <= 0) throw new ArgumentOutOfRangeException(nameof(length));
        if (expectedText is null) throw new ArgumentNullException(nameof(expectedText));
        if (expectedText.Length != length) throw new ArgumentException("Expected text must have exactly the span's UTF-16 length.", nameof(expectedText));
        if (!NameSafety.IsSafe(expectedText)) throw new ArgumentException("A name span must contain a safe literal name, never native control data.", nameof(expectedText));
        if (canonicalRoleId < 0) throw new ArgumentOutOfRangeException(nameof(canonicalRoleId));
        StartUtf16 = startUtf16; Length = length; ExpectedText = expectedText;
        CanonicalRoleId = canonicalRoleId; UppercaseOutput = uppercaseOutput;
    }
    public int StartUtf16 { get; }
    public int Length { get; }
    public string ExpectedText { get; }
    public int CanonicalRoleId { get; }
    public bool UppercaseOutput { get; }
}

/// <summary>Immutable plan containing hashes and name fragments, never requiring full dialogue storage.</summary>
public sealed class DialogueRewritePlan
{
    public const string SourceHashAlgorithm = "sha256-utf16le-codeunits-v1";
    public DialogueRewritePlan(DialogueMessageKey key, string sourceSha256, int sourceUtf16Length, IEnumerable<DialogueNameSpan> spans)
    {
        if (!key.HasContext) throw new ArgumentException("Plans require GSD1/GSD2 and an explicit language.", nameof(key));
        if (sourceUtf16Length < 0) throw new ArgumentOutOfRangeException(nameof(sourceUtf16Length));
        if (sourceSha256 is null) throw new ArgumentNullException(nameof(sourceSha256));
        string hash = sourceSha256.ToLowerInvariant();
        if (hash.Length != 64 || hash.Any(c => !((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))))
            throw new ArgumentException("Expected a SHA256 digest.", nameof(sourceSha256));
        if (spans is null) throw new ArgumentNullException(nameof(spans));
        var copy = spans.ToArray();
        if (copy.Any(s => s is null)) throw new ArgumentException("Null name span.", nameof(spans));
        Array.Sort(copy, (left, right) => left.StartUtf16.CompareTo(right.StartUtf16));
        int end = 0;
        foreach (var span in copy)
        {
            if (span.StartUtf16 < end || span.StartUtf16 > sourceUtf16Length - span.Length)
                throw new ArgumentException("Name spans must be nonoverlapping and inside the original source.", nameof(spans));
            end = span.StartUtf16 + span.Length;
        }
        Key = key; SourceSha256 = hash; SourceUtf16Length = sourceUtf16Length;
        Spans = new ReadOnlyCollection<DialogueNameSpan>(copy);
    }
    public DialogueMessageKey Key { get; }
    public string SourceSha256 { get; }
    public int SourceUtf16Length { get; }
    public IReadOnlyList<DialogueNameSpan> Spans { get; }

    /// <summary>SHA256 of exact little-endian UTF-16 code units, no BOM or fallback conversion.</summary>
    public static string ComputeSourceHash(string source)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));
        var bytes = new byte[checked(source.Length * 2)];
        for (int index = 0; index < source.Length; index++)
        { bytes[index * 2] = (byte)source[index]; bytes[index * 2 + 1] = (byte)(source[index] >> 8); }
        using (var hash = SHA256.Create())
        {
            var result = new StringBuilder(64);
            foreach (byte value in hash.ComputeHash(bytes)) result.Append(value.ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
            return result.ToString();
        }
    }
}

/// <summary>Row-to-row alias within the same game/language. Unresolved external targets are permitted.</summary>
public sealed class DialogueMessageAlias
{
    public DialogueMessageAlias(DialogueMessageKey key, ulong targetMessageId)
    {
        if (!key.HasContext) throw new ArgumentException("Aliases require an explicit game and language.", nameof(key));
        Key = key; TargetMessageId = targetMessageId;
    }
    public DialogueMessageKey Key { get; }
    public ulong TargetMessageId { get; }
}

internal static class NameSafety
{
    // Observed original native structural commands and selector glyphs. None
    // belong in a literal name, even when a command would need more operands.
    private const string NativeParserGlyphs = "☆◎∠∨≒♂〒※㈱①②③④⑤⑥⑦⑧⑨⑩■●★▼▲◆|Λ";
    internal static bool IsSafe(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        for (int index = 0; index < value.Length; index++)
        {
            char c = value[index];
            if (char.IsControl(c) || NativeParserGlyphs.IndexOf(c) >= 0 || c == '\u2028' || c == '\u2029'
                || c == '<' || c == '>' || c == '[' || c == ']' || c == '{' || c == '}' || c == '&' || c == '\\') return false;
            if (char.IsHighSurrogate(c))
            {
                if (index + 1 >= value.Length || !char.IsLowSurrogate(value[index + 1])) return false;
                if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(value, index) == System.Globalization.UnicodeCategory.Format) return false;
                index++;
            }
            else if (char.IsLowSurrogate(c)) return false;
            else if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.Format) return false;
        }
        return true;
    }
}
