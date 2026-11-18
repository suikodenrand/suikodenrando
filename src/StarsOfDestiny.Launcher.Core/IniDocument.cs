using System;
using System.Collections.Generic;
using System.Linq;

namespace StarsOfDestiny.Launcher.Core;

/// <summary>Edits selected INI values while retaining comments, unknown keys and line endings.</summary>
public sealed class IniDocument
{
    private readonly List<Line> _lines;
    private readonly string _newline;
    private sealed record Line(string Text, string Ending);

    private IniDocument(List<Line> lines, string newline)
    {
        _lines = lines;
        _newline = newline;
    }

    public static IniDocument Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var lines = new List<Line>();
        int start = 0;
        string? newline = null;
        for (int index = 0; index < text.Length; index++)
        {
            if (text[index] is not ('\r' or '\n')) continue;
            int length = text[index] == '\r' && index + 1 < text.Length && text[index + 1] == '\n' ? 2 : 1;
            string ending = text.Substring(index, length);
            lines.Add(new Line(text.Substring(start, index - start), ending));
            newline ??= ending;
            index += length - 1;
            start = index + 1;
        }
        if (start < text.Length) lines.Add(new Line(text[start..], ""));
        return new IniDocument(lines, newline ?? Environment.NewLine);
    }

    public IEnumerable<(string Section, string Key, string Value)> Entries()
    {
        string section = "";
        foreach (var line in _lines)
        {
            if (TrySection(line.Text, out string name)) section = name;
            else if (TryKey(line.Text, out string key, out int equals))
                yield return (section, key, line.Text[(equals + 1)..].Trim());
        }
    }

    public string Get(string section, string key, string defaultValue = "")
    {
        ValidateName(section, nameof(section));
        ValidateName(key, nameof(key));
        string current = "";
        string? result = null;
        foreach (var line in _lines)
        {
            if (TrySection(line.Text, out string name)) { current = name; continue; }
            if (!Same(current, section) || !TryKey(line.Text, out string candidate, out int equals) || !Same(candidate, key)) continue;
            if (result is not null) throw new InvalidOperationException($"Duplicate setting [{section}] {key}.");
            result = line.Text[(equals + 1)..].Trim();
        }
        return result ?? defaultValue;
    }

    public void Set(string section, string key, string value)
    {
        ValidateName(section, nameof(section));
        ValidateName(key, nameof(key));
        ArgumentNullException.ThrowIfNull(value);
        if (value.Any(character => character is '\r' or '\n' or '\0'))
            throw new ArgumentException("An INI value must fit on one line.", nameof(value));
        _ = Get(section, key);
        string current = "";
        int sectionEnd = -1;
        bool foundSection = false;
        for (int index = 0; index < _lines.Count; index++)
        {
            var line = _lines[index];
            if (TrySection(line.Text, out string name))
            {
                if (Same(current, section) && !Same(name, section)) sectionEnd = index;
                current = name;
                if (Same(name, section)) { foundSection = true; sectionEnd = -1; }
                continue;
            }
            if (!Same(current, section) || !TryKey(line.Text, out string candidate, out int equals) || !Same(candidate, key)) continue;
            string suffix = line.Text[(equals + 1)..];
            int leading = suffix.Length - suffix.TrimStart().Length;
            int trailing = suffix.Length - suffix.TrimEnd().Length;
            // A blank original value has only leading whitespace.
            string right = suffix.Trim().Length == 0 ? "" : suffix[^trailing..];
            _lines[index] = line with { Text = line.Text[..(equals + 1)] + suffix[..leading] + value + right };
            return;
        }

        if (!foundSection)
        {
            EnsureLastLineEnding();
            if (_lines.Count > 0 && _lines[^1].Text.Length > 0) _lines.Add(new Line("", _newline));
            _lines.Add(new Line("[" + section + "]", _newline));
            _lines.Add(new Line(key + " = " + value, _newline));
            return;
        }
        int insert = sectionEnd < 0 ? _lines.Count : sectionEnd;
        // Keep the blank separation before the next section.
        while (insert > 0 && string.IsNullOrWhiteSpace(_lines[insert - 1].Text)) insert--;
        if (insert > 0 && _lines[insert - 1].Ending.Length == 0)
            _lines[insert - 1] = _lines[insert - 1] with { Ending = _newline };
        _lines.Insert(insert, new Line(key + " = " + value, _newline));
    }

    public string Render() => string.Concat(_lines.Select(line => line.Text + line.Ending));

    private void EnsureLastLineEnding()
    {
        if (_lines.Count > 0 && _lines[^1].Ending.Length == 0)
            _lines[^1] = _lines[^1] with { Ending = _newline };
    }

    private static bool TrySection(string line, out string name)
    {
        string trimmed = line.Trim().TrimStart('\uFEFF');
        if (trimmed.Length >= 2 && trimmed[0] == '[' && trimmed[^1] == ']')
        { name = trimmed[1..^1]; return true; }
        name = "";
        return false;
    }

    private static bool TryKey(string line, out string key, out int equals)
    {
        string trimmed = line.TrimStart();
        equals = line.IndexOf('=');
        if (equals >= 0 && !trimmed.StartsWith('#') && !trimmed.StartsWith(';'))
        { key = line[..equals].Trim(); return key.Length > 0; }
        key = "";
        return false;
    }

    private static bool Same(string left, string right) => string.Equals(left, right, StringComparison.Ordinal);
    private static void ValidateName(string value, string parameter)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Any(character => character is '\r' or '\n' or '\0' or '[' or ']' or '='))
            throw new ArgumentException("Invalid INI section or key.", parameter);
    }
}
