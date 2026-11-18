using System;
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
            var definition = SettingsCatalog.Find(field.File, field.Section, field.Key);
            row.Cells[3].Value = definition is null ? _workspace.Documents[field.File].Get(field.Section, field.Key) : _workspace.Get(definition.Id);
        }
    }

    private Control BuildAdvanced()
    {
        var page = Grid(1, 2);
        page.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        page.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        page.Controls.Add(Note("Edit config values below; changes are applied only when you save. Comments are preserved. The legacy chest master switch is managed automatically so saved chest policies remain active."), 0, 0);
        _advanced = new DataGridView { Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.CellSelect,
            BackgroundColor = System.Drawing.Color.White, BorderStyle = BorderStyle.FixedSingle };
        _advanced.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Config", ReadOnly = true, FillWeight = 30 });
        _advanced.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Section", ReadOnly = true, FillWeight = 18 });
        _advanced.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Setting", ReadOnly = true, FillWeight = 30 });
        _advanced.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Value", FillWeight = 22 });
        foreach (var document in _workspace!.Documents)
        {
            var values = document.Value.Entries().ToDictionary(entry => (entry.Section, entry.Key), entry => entry.Value);
            foreach (var definition in SettingsCatalog.All.Where(definition => definition.FileName == document.Key))
                values[(definition.Section, definition.Key)] = _workspace.Get(definition.Id);
            foreach (var entry in values.OrderBy(pair => pair.Key.Section, StringComparer.Ordinal).ThenBy(pair => pair.Key.Key, StringComparer.Ordinal))
            {
                if (document.Key.Equals(SettingsCatalog.S1File, StringComparison.OrdinalIgnoreCase)
                    && entry.Key.Section.Equals("Treasure", StringComparison.OrdinalIgnoreCase)
                    && entry.Key.Key.Equals("Enabled", StringComparison.OrdinalIgnoreCase)) continue;
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
            var definition = SettingsCatalog.Find(field.File, field.Section, field.Key);
            string value = row.Cells[3].Value?.ToString() ?? "";
            if (definition is null) _workspace.Documents[field.File].Set(field.Section, field.Key, value);
            else _workspace.StageValue(definition, value);
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
            _workspace.StagePreset(File.ReadAllText(dialog.FileName));
            RebuildPages();
            _status.Text = "Preset loaded into the launcher. Apply settings to write it to the game.";
        }
        catch (Exception error) { ShowError(error); }
    }
}
