using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Forms;
using StarsOfDestiny.Launcher.Core;

namespace StarsOfDestiny.Launcher;

internal sealed partial class MainForm
{
    private void RefreshAdvancedValues()
    {
        if (_advanced is null || _workspace is null) return;
        foreach (DataGridViewRow row in _advanced.Rows)
        {
            var field = ((string File, string Section, string Key))row.Tag!;
            var definition = SettingsCatalog.All.FirstOrDefault(setting => setting.FileName == field.File && setting.Section == field.Section && setting.Key == field.Key);
            row.Cells[3].Value = _workspace.Documents[field.File].Get(field.Section, field.Key, definition?.DefaultValue ?? "");
        }
    }

    private static IEnumerable<(string Section, string Key, string Value)> Entries(string text)
    {
        string section = "";
        foreach (string line in text.Split('\n'))
        {
            string trimmed = line.Trim();
            if (trimmed.StartsWith('[') && trimmed.EndsWith(']')) { section = trimmed[1..^1]; continue; }
            if (trimmed.StartsWith('#') || trimmed.StartsWith(';')) continue;
            int equals = trimmed.IndexOf('=');
            if (equals > 0) yield return (section, trimmed[..equals].Trim(), trimmed[(equals + 1)..].Trim());
        }
    }

    private Control BuildAdvanced()
    {
        var page = Grid(1, 2);
        page.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        page.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        page.Controls.Add(Note("All keys in the five supported config files are shown below, including options without a dedicated control. Values are applied only when you save. Config comments are preserved."), 0, 0);
        _advanced = new DataGridView { Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.CellSelect,
            BackgroundColor = System.Drawing.Color.White, BorderStyle = BorderStyle.FixedSingle };
        _advanced.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Config", ReadOnly = true, FillWeight = 30 });
        _advanced.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Section", ReadOnly = true, FillWeight = 18 });
        _advanced.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Setting", ReadOnly = true, FillWeight = 30 });
        _advanced.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Value", FillWeight = 22 });
        foreach (var document in _workspace!.Documents)
        {
            var values = Entries(document.Value.Render()).ToDictionary(entry => (entry.Section, entry.Key), entry => entry.Value);
            foreach (var definition in SettingsCatalog.All.Where(definition => definition.FileName == document.Key))
                values.TryAdd((definition.Section, definition.Key), definition.DefaultValue);
            foreach (var entry in values.OrderBy(pair => pair.Key.Section, StringComparer.Ordinal).ThenBy(pair => pair.Key.Key, StringComparer.Ordinal))
            {
                int index = _advanced.Rows.Add(document.Key.Replace("agilen.", "", StringComparison.Ordinal).Replace(".cfg", "", StringComparison.Ordinal),
                    entry.Key.Section, entry.Key.Key, entry.Value);
                _advanced.Rows[index].Tag = (document.Key, entry.Key.Section, entry.Key.Key);
            }
        }
        _advanced.CellEndEdit += (_, e) =>
        {
            if (e.RowIndex < 0 || _loading) return;
            var row = _advanced.Rows[e.RowIndex];
            var field = ((string File, string Section, string Key))row.Tag!;
            _workspace.Documents[field.File].Set(field.Section, field.Key, row.Cells[3].Value?.ToString() ?? "");
            _status.Text = "Advanced settings changed. Apply settings to save them.";
            RefreshStatus();
        };
        page.Controls.Add(_advanced, 0, 1);
        return page;
    }

    private void ExportPreset()
    {
        if (_workspace is null) return;
        using var dialog = new SaveFileDialog { Filter = "Stars of Destiny settings (*.json)|*.json", FileName = "StarsOfDestiny-settings.json" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            _advanced?.EndEdit();
            var errors = _workspace.Validate();
            if (errors.Count != 0) throw new InvalidDataException(string.Join(Environment.NewLine, errors));
            var preset = new { schema = "stars-of-destiny-settings-v1", configFiles = _workspace.Documents.ToDictionary(pair => pair.Key, pair => pair.Value.Render()) };
            File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(preset, new JsonSerializerOptions { WriteIndented = true }));
            _status.Text = "Settings preset saved to " + dialog.FileName;
        }
        catch (Exception error) { ShowError(error); }
    }

    private void ImportPreset()
    {
        if (_workspace is null) return;
        using var dialog = new OpenFileDialog { Filter = "Stars of Destiny settings (*.json)|*.json" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            if (new FileInfo(dialog.FileName).Length > 1024 * 1024) throw new InvalidDataException("The settings preset is too large.");
            using var json = JsonDocument.Parse(File.ReadAllText(dialog.FileName), new JsonDocumentOptions { MaxDepth = 5 });
            if (json.RootElement.GetProperty("schema").GetString() != "stars-of-destiny-settings-v1") throw new InvalidDataException("Unsupported settings preset.");
            var staged = new Dictionary<string, IniDocument>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in json.RootElement.GetProperty("configFiles").EnumerateObject())
            {
                if (!_workspace.Documents.ContainsKey(file.Name) || !staged.TryAdd(file.Name, IniDocument.Parse(file.Value.GetString() ?? "")))
                    throw new InvalidDataException("The preset contains an unknown or repeated config file.");
            }
            var validation = _workspace.Documents.ToDictionary(pair => pair.Key, pair => staged.TryGetValue(pair.Key, out var imported) ? imported : pair.Value);
            var errors = SettingsCatalog.Validate(validation);
            if (errors.Count != 0) throw new InvalidDataException(string.Join(Environment.NewLine, errors));
            foreach (var file in staged)
                foreach (var entry in Entries(file.Value.Render())) _workspace.Documents[file.Key].Set(entry.Section, entry.Key, entry.Value);
            RebuildPages();
            _status.Text = "Preset loaded into the launcher. Apply settings to write it to the game.";
        }
        catch (Exception error) { ShowError(error); }
    }
}
