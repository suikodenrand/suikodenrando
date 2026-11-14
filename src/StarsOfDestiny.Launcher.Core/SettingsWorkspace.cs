using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace StarsOfDestiny.Launcher.Core;

public sealed record SettingsSaveResult(string? BackupDirectory, IReadOnlyList<string> ChangedFiles);

/// <summary>Owns one optimistic snapshot of the supported configuration files.</summary>
public sealed class SettingsWorkspace
{
    private readonly Func<bool> _isGameRunning;
    private readonly Dictionary<string, Snapshot> _snapshots = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IniDocument> _documents = new(StringComparer.OrdinalIgnoreCase);
    private sealed record Snapshot(byte[]? Bytes, string Text, Encoding Encoding, byte[] Preamble);
    private sealed record Pending(string Name, string Target, string Temporary, byte[] Bytes, Snapshot Original);

    private SettingsWorkspace(string gameFolder, Func<bool> isGameRunning)
    {
        GameFolder = gameFolder;
        ConfigDirectory = Path.Combine(gameFolder, "BepInEx", "config");
        _isGameRunning = isGameRunning;
        Documents = new ReadOnlyDictionary<string, IniDocument>(_documents);
        Reload();
    }

    public string GameFolder { get; }
    public string ConfigDirectory { get; }
    public IReadOnlyDictionary<string, IniDocument> Documents { get; }
    public bool IsDirty => _documents.Any(pair => pair.Value.Render() != _snapshots[pair.Key].Text);
    public bool IsGameRunning => _isGameRunning();

    public static SettingsWorkspace Load(string gameFolder, Func<bool>? isGameRunning = null)
    {
        if (string.IsNullOrWhiteSpace(gameFolder)) throw new ArgumentException("Select the Suikoden game folder.", nameof(gameFolder));
        string full = Path.GetFullPath(gameFolder);
        if (!Directory.Exists(full)) throw new DirectoryNotFoundException("Game folder does not exist.");
        return new SettingsWorkspace(full, isGameRunning ?? GameIsRunning);
    }

    public void Reload()
    {
        var snapshots = new Dictionary<string, Snapshot>(StringComparer.OrdinalIgnoreCase);
        foreach (string name in SettingsCatalog.FileNames) snapshots.Add(name, Read(Path.Combine(ConfigDirectory, name)));
        _snapshots.Clear();
        _documents.Clear();
        foreach (var snapshot in snapshots)
        {
            _snapshots.Add(snapshot.Key, snapshot.Value);
            _documents.Add(snapshot.Key, IniDocument.Parse(snapshot.Value.Text));
        }
    }

    public string Get(string id)
    {
        var setting = SettingsCatalog.Find(id);
        return Documents[setting.FileName].Get(setting.Section, setting.Key, setting.DefaultValue);
    }
    public bool GetBoolean(string id) => bool.Parse(Get(id));
    public void Set(string id, string value)
    {
        var setting = SettingsCatalog.Find(id);
        Documents[setting.FileName].Set(setting.Section, setting.Key, SettingsCatalog.Normalize(setting, value));
    }
    public void SetBoolean(string id, bool value) => Set(id, value ? "true" : "false");
    public IReadOnlyList<string> Validate() => SettingsCatalog.Validate(Documents);
    public SettingsSaveResult Save() => SaveChanges();

    public SettingsSaveResult SaveChanges()
    {
        if (IsGameRunning) throw new InvalidOperationException("Close Suikoden before applying settings.");
        var errors = Validate();
        if (errors.Count > 0) throw new InvalidOperationException(string.Join(Environment.NewLine, errors));
        string[] changed = SettingsCatalog.FileNames.Where(name => Documents[name].Render() != _snapshots[name].Text).ToArray();
        if (changed.Length == 0) return new SettingsSaveResult(null, Array.Empty<string>());
        bool changesS2 = changed.Contains(SettingsCatalog.S2File, StringComparer.OrdinalIgnoreCase);
        if (changesS2) GameInstallation.RequireRiouJoiningWeaponSupport(GameFolder, Get("s2.joiningWeaponLevel"));
        foreach (string name in changed) RequireUnchanged(name);
        Directory.CreateDirectory(ConfigDirectory);
        string transaction = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff", System.Globalization.CultureInfo.InvariantCulture)
            + "-" + Guid.NewGuid().ToString("N");
        string backup = Path.Combine(ConfigDirectory, "StarsOfDestiny.Launcher", "backups", transaction);
        Directory.CreateDirectory(backup);
        var pending = new List<Pending>();
        var committed = new List<Pending>();
        try
        {
            foreach (string name in changed)
            {
                Snapshot original = _snapshots[name];
                string target = Path.Combine(ConfigDirectory, name);
                string temporary = target + "." + transaction + ".tmp";
                byte[] content = original.Encoding.GetBytes(Documents[name].Render());
                byte[] bytes = original.Preamble.Concat(content).ToArray();
                var file = new Pending(name, target, temporary, bytes, original);
                pending.Add(file);
                if (original.Bytes is not null) WriteDurably(Path.Combine(backup, name), original.Bytes);
                WriteDurably(temporary, bytes);
            }
            if (IsGameRunning) throw new InvalidOperationException("Suikoden started while settings were being prepared. Close it and try again.");
            if (changesS2) GameInstallation.RequireRiouJoiningWeaponSupport(GameFolder, Get("s2.joiningWeaponLevel"));
            foreach (var file in pending)
            {
                RequireUnchanged(file.Name);
                File.Move(file.Temporary, file.Target, true);
                committed.Add(file);
            }
            Reload();
            return new SettingsSaveResult(backup, Array.AsReadOnly(changed));
        }
        catch (Exception failure)
        {
            var rollbackErrors = new List<Exception>();
            foreach (var file in committed.AsEnumerable().Reverse())
            {
                try
                {
                    if (!File.Exists(file.Target) || !File.ReadAllBytes(file.Target).SequenceEqual(file.Bytes))
                        throw new IOException("Configuration changed during rollback. The backup is retained at " + backup);
                    if (file.Original.Bytes is null) File.Delete(file.Target);
                    else
                    {
                        string restore = file.Target + "." + transaction + ".restore";
                        try { WriteDurably(restore, file.Original.Bytes); File.Move(restore, file.Target, true); }
                        finally { if (File.Exists(restore)) File.Delete(restore); }
                    }
                }
                catch (Exception error) { rollbackErrors.Add(error); }
            }
            if (rollbackErrors.Count != 0)
                throw new AggregateException("Settings could not be saved or fully restored. Backups are at " + backup,
                    new[] { failure }.Concat(rollbackErrors));
            throw;
        }
        finally
        {
            foreach (var file in pending) if (File.Exists(file.Temporary)) File.Delete(file.Temporary);
        }
    }

    private void RequireUnchanged(string name)
    {
        string path = Path.Combine(ConfigDirectory, name);
        byte[]? expected = _snapshots[name].Bytes;
        bool matches = expected is null ? !File.Exists(path)
            : File.Exists(path) && File.ReadAllBytes(path).SequenceEqual(expected);
        if (!matches) throw new IOException(name + " changed since it was loaded. Reload settings before applying changes.");
    }

    private static Snapshot Read(string path)
    {
        if (!File.Exists(path)) return new Snapshot(null, "", new UTF8Encoding(false, true), Array.Empty<byte>());
        byte[] bytes = File.ReadAllBytes(path);
        Encoding encoding;
        byte[] preamble;
        if (bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF })) { encoding = new UTF8Encoding(false, true); preamble = new byte[] { 0xEF, 0xBB, 0xBF }; }
        else if (bytes.AsSpan().StartsWith(new byte[] { 0xFF, 0xFE })) { encoding = new UnicodeEncoding(false, false, true); preamble = new byte[] { 0xFF, 0xFE }; }
        else if (bytes.AsSpan().StartsWith(new byte[] { 0xFE, 0xFF })) { encoding = new UnicodeEncoding(true, false, true); preamble = new byte[] { 0xFE, 0xFF }; }
        else { encoding = new UTF8Encoding(false, true); preamble = Array.Empty<byte>(); }
        string text = encoding.GetString(bytes, preamble.Length, bytes.Length - preamble.Length);
        return new Snapshot(bytes, text, encoding, preamble);
    }

    private static void WriteDurably(string path, byte[] bytes)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        stream.Write(bytes);
        stream.Flush(true);
    }

    public static bool GameIsRunning()
    {
        var processes = Process.GetProcessesByName("Suikoden I and II HD Remaster");
        try { return processes.Length > 0; }
        finally { foreach (var process in processes) process.Dispose(); }
    }
}
