using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Forms;
using StarsOfDestiny.Launcher.Core;
using SuikodenRandomizer.Core;
using SuikodenRandomizer.Core.Suikoden2;

namespace StarsOfDestiny.Launcher;

internal sealed partial class MainForm : Form
{
    private const string AppTitle = "Stars of Destiny Launcher";
    private static readonly string VersionedTitle = AppTitle + " " + typeof(MainForm).Assembly.GetName().Version?.ToString(3);
    private static readonly Color Navy = Color.FromArgb(14, 43, 69);
    private static readonly Color Muted = Color.FromArgb(84, 94, 105);
    private readonly TextBox _folder = new() { Dock = DockStyle.Fill, ReadOnly = true };
    private readonly TextBox _seed = new() { Dock = DockStyle.Fill, MaxLength = 128 };
    private readonly ComboBox _game = new AppComboBox() { Dock = DockStyle.Fill };
    private readonly Label _installation = new() { Dock = DockStyle.Fill, AutoSize = false, ForeColor = Muted };
    private readonly Label _status = new() { AutoSize = false, Dock = DockStyle.Fill, ForeColor = Muted };
    private readonly Label _running = new() { AutoSize = false, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill, Padding = new Point(12, 6) };
    private readonly Button _apply = Button("Apply settings");
    private readonly Button _launch = Button("Save && launch game");
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 2000 };
    private SettingsWorkspace? _workspace;
    private DataGridView? _advanced;
    private bool _loading;
    private bool _wasAdvanced;
    private string GameId => _game.SelectedIndex == 1 ? "s2" : "s1";
    private string GameName => _game.SelectedIndex == 1 ? "Suikoden II" : "Suikoden I";

    internal MainForm(string? gameFolder)
    {
        Text = VersionedTitle;
        Font = new Font("Segoe UI", 9F);
        BackColor = Color.FromArgb(244, 245, 247);
        ClientSize = new Size(1150, 830);
        MinimumSize = new Size(1020, 750);
        StartPosition = FormStartPosition.CenterScreen;
        Icon = SystemIcons.Application;
        KeyPreview = true;

        var shell = Grid(1, 4);
        shell.Padding = new Padding(12, 0, 12, 10);
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 150));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 73));
        var header = new Panel { Dock = DockStyle.Fill, BackColor = Navy, Margin = new Padding(0, 0, 0, 9) };
        header.Controls.Add(new Label { Text = "STARS OF DESTINY", Font = new Font(Font.FontFamily, 15, FontStyle.Bold),
            ForeColor = Color.White, Dock = DockStyle.Left, Width = 270, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(12, 0, 0, 0) });
        header.Controls.Add(new Label { Text = "Randomizer settings and game launcher", ForeColor = Color.FromArgb(203, 215, 226),
            Dock = DockStyle.Right, Width = 350, TextAlign = ContentAlignment.MiddleRight, Padding = new Padding(0, 0, 12, 0) });
        shell.Controls.Add(header, 0, 0);
        shell.Controls.Add(BuildTop(), 0, 1);
        shell.Controls.Add(_tabs, 0, 2);
        shell.Controls.Add(BuildFooter(), 0, 3);
        Controls.Add(shell);

        _game.Items.AddRange(new object[] { "Suikoden I", "Suikoden II" });
        _game.SelectedIndex = 0;
        _game.SelectedIndexChanged += (_, _) => RebuildPages();
        _tabs.SelectedIndexChanged += (_, _) =>
        {
            if (_loading || _workspace is null) return;
            bool advanced = _tabs.SelectedTab?.Text == "Advanced";
            if (advanced) RefreshAdvancedValues();
            else if (_wasAdvanced) RebuildPages();
            _wasAdvanced = advanced;
        };
        _seed.TextChanged += (_, _) => Set(GameId + ".seed", _seed.Text);
        _apply.Click += (_, _) => ApplySettings(false);
        _launch.Click += (_, _) => ApplySettings(true);
        KeyDown += (_, e) =>
        {
            if (e.Control && e.KeyCode == Keys.S) { ApplySettings(false); e.SuppressKeyPress = true; }
            if (e.KeyCode == Keys.F5) { LoadInstallation(_folder.Text); e.SuppressKeyPress = true; }
        };
        FormClosing += (_, e) => { if (!CanDiscard()) e.Cancel = true; };
        _timer.Tick += (_, _) => RefreshStatus();
        _timer.Start();
        _folder.Text = gameFolder ?? ReadPreferredFolder() ?? GameInstallation.DetectGameFolder() ?? "";
        Shown += (_, _) => LoadInstallation(_folder.Text);
    }

    private Control BuildTop()
    {
        var top = Grid(3, 1);
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 24));
        var install = Group("Game installation");
        var installGrid = Grid(1, 3);
        installGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
        installGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 31));
        installGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        installGrid.Controls.Add(_folder, 0, 0);
        var browse = Button("Browse…");
        browse.Click += (_, _) =>
        {
            using var dialog = new FolderBrowserDialog { Description = "Choose the Suikoden I and II HD Remaster game folder", UseDescriptionForTitle = true,
                InitialDirectory = _folder.Text, ShowNewFolderButton = false };
            if (dialog.ShowDialog(this) == DialogResult.OK) LoadInstallation(dialog.SelectedPath);
        };
        var reload = Button("Reload");
        reload.Click += (_, _) => LoadInstallation(_folder.Text);
        installGrid.Controls.Add(Buttons(browse, reload), 0, 1);
        installGrid.Controls.Add(_installation, 0, 2);
        install.Controls.Add(installGrid);
        top.Controls.Add(install, 0, 0);

        var campaign = Group("Campaign settings");
        var fields = Grid(2, 3);
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 52));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        fields.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        fields.Controls.Add(TextLabel("Game"), 0, 0);
        fields.Controls.Add(_game, 1, 0);
        fields.Controls.Add(TextLabel("Seed"), 0, 1);
        fields.Controls.Add(_seed, 1, 1);
        var random = Button("New seed");
        random.Click += (_, _) => _seed.Text = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(5)).ToLowerInvariant();
        fields.Controls.Add(Buttons(random), 1, 2);
        campaign.Controls.Add(fields);
        top.Controls.Add(campaign, 1, 0);

        var actions = Grid(1, 3);
        actions.Padding = new Padding(6, 7, 0, 6);
        actions.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        actions.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        actions.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _launch.BackColor = Navy;
        _launch.ForeColor = Color.White;
        _launch.FlatStyle = FlatStyle.Flat;
        _launch.Dock = DockStyle.Fill;
        _apply.Dock = DockStyle.Fill;
        actions.Controls.Add(_launch, 0, 0);
        actions.Controls.Add(_apply, 0, 1);
        var loadPreset = Button("Load settings…");
        loadPreset.Click += (_, _) => ImportPreset();
        var savePreset = Button("Save settings…");
        savePreset.Click += (_, _) => ExportPreset();
        actions.Controls.Add(Buttons(loadPreset, savePreset), 0, 2);
        top.Controls.Add(actions, 2, 0);
        return top;
    }

    private Control BuildFooter()
    {
        var footer = Grid(2, 2);
        footer.Padding = new Padding(0, 8, 0, 0);
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 260));
        footer.RowStyles.Add(new RowStyle(SizeType.Absolute, 27));
        footer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        footer.Controls.Add(_running, 0, 0);
        footer.Controls.Add(_status, 0, 1);
        var log = Button("Open game log");
        log.Click += (_, _) => OpenPath(Path.Combine(_folder.Text, "BepInEx", "LogOutput.log"));
        var config = Button("Config folder");
        config.Click += (_, _) => OpenPath(Path.Combine(_folder.Text, "BepInEx", "config"));
        footer.Controls.Add(Buttons(log, config), 1, 0);
        return footer;
    }

    private void LoadInstallation(string folder)
    {
        if (!CanDiscard()) return;
        try
        {
            if (!File.Exists(Path.Combine(folder, "Suikoden I and II HD Remaster.exe")))
                throw new DirectoryNotFoundException("Choose the folder containing Suikoden I and II HD Remaster.exe.");
            _workspace = SettingsWorkspace.Load(folder, () => GameInstallation.IsGameRunning(folder));
            _folder.Text = _workspace.GameFolder;
            RebuildPages();
            _status.Text = "Settings loaded. Changes are applied when you save.";
        }
        catch (Exception error) { ShowError(error); }
    }

    private void RebuildPages()
    {
        if (_workspace is null) return;
        int selected = _tabs.SelectedIndex;
        _loading = true;
        try
        {
            while (_tabs.TabPages.Count > 0) { var tab = _tabs.TabPages[0]; _tabs.TabPages.Remove(tab); tab.Dispose(); }
            _seed.Text = Get(GameId + ".seed");
            AddPage("Characters", BuildCharacters());
            AddPage("Treasure", BuildTreasure());
            AddPage("Battle & ATB", BuildBattle());
            AddPage("Quality of Life", BuildQualityOfLife());
            AddPage("Display & Sound", BuildDisplay());
            AddPage("Dialogue & UI", BuildInterface());
            AddPage("Advanced", BuildAdvanced());
            _tabs.SelectedIndex = Math.Clamp(selected, 0, _tabs.TabPages.Count - 1);
        }
        finally { _loading = false; }
        RefreshStatus();
    }

    private Control BuildCharacters()
    {
        var page = Grid(2, 3);
        page.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48));
        page.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52));
        page.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
        page.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        page.RowStyles.Add(new RowStyle(SizeType.Absolute, 135));
        var note = Note("Seed, character mode, companions, start point, and joining weapon level apply to new games. Loaded campaigns restore their saved settings.");
        page.Controls.Add(note, 0, 0);
        page.SetColumnSpan(note, 2);
        var modeGroup = Group("Character randomization");
        var companions = Group("Fixed roster companions");
        companions.Enabled = Get(GameId + ".mode") == "FixedChosen";
        var modes = Stack();
        modes.Controls.Add(Check("shared.enabled", "Enable Stars of Destiny"));
        modes.Controls.Add(Note("Controls the randomizer and its shared in-game interface."));
        foreach (var mode in new[] { ("Shuffle", "Shuffle character roles"), ("FixedChosen", "Fixed roster — choose five companions"), ("FixedRandom", "Fixed roster — random five companions") })
        {
            var radio = new RadioButton { Text = mode.Item2, AutoSize = true, Checked = Get(GameId + ".mode") == mode.Item1, Margin = new Padding(4, 8, 4, 3) };
            radio.CheckedChanged += (_, _) =>
            {
                if (!radio.Checked) return;
                Set(GameId + ".mode", mode.Item1);
                companions.Enabled = mode.Item1 == "FixedChosen";
            };
            modes.Controls.Add(radio);
        }
        modes.Controls.Add(Check(GameId + ".includeHero", GameId == "s1" ? "Include Tir in character shuffle" : "Include Riou in character shuffle"));
        modeGroup.Controls.Add(modes);
        page.Controls.Add(modeGroup, 0, 1);

        var roster = Grid(2, 7);
        roster.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 105));
        roster.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var hint = Note("Choose five distinct companions for Fixed roster — choose five. The protagonist is included automatically.");
        roster.Controls.Add(hint, 0, 0);
        roster.SetColumnSpan(hint, 2);
        roster.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        var choices = GameId == "s1" ? CombatCatalog.NormalCombatIds.Select(id => new Hero(id, CombatCatalog.GetName(id))).ToArray()
            : Gsd2CharacterCatalog.FixedCompanionIds.Select(id => new Hero(id, Gsd2CharacterCatalog.GetName(id))).ToArray();
        choices = choices.OrderBy(hero => hero.Name, StringComparer.CurrentCulture).ToArray();
        string[] selectedIds = Get(GameId + ".companions").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var comboBoxes = new List<ComboBox>();
        for (int index = 0; index < 5; index++)
        {
            var combo = new AppComboBox { Dock = DockStyle.Fill };
            combo.Items.AddRange(choices.Cast<object>().ToArray());
            if (index < selectedIds.Length && int.TryParse(selectedIds[index], out int selectedId))
                combo.SelectedItem = choices.FirstOrDefault(hero => hero.Id == selectedId);
            comboBoxes.Add(combo);
            combo.SelectedIndexChanged += (_, _) =>
                Set(GameId + ".companions", string.Join(",", comboBoxes.Select(box => box.SelectedItem).OfType<Hero>().Select(hero => hero.Id)));
            roster.Controls.Add(TextLabel("Companion " + (index + 1)), 0, index + 1);
            roster.Controls.Add(combo, 1, index + 1);
            roster.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        }
        companions.Controls.Add(roster);
        page.Controls.Add(companions, 1, 1);

        var start = Group("Starting point");
        var startFields = Stack();
        startFields.Controls.Add(Choice(GameId + ".start", "New game starts", GameId == "s1" ? new[] { "Opening", "BeforeViktor" } : new[] { "Opening", "ViktorCamp" }));
        startFields.Controls.Add(Note(GameId == "s1" ? "BeforeViktor skips the opening to the tested Viktor checkpoint." : "ViktorCamp starts in the camp storage room before Pohl's chores."));
        start.Controls.Add(startFields);
        page.Controls.Add(start, 0, 2);
        var weapons = Group("Joining weapon level");
        var weaponFields = Stack();
        var weaponChoices = new List<KeyValuePair<string, string>>
        {
            new KeyValuePair<string, string>("MatchRole", "Match original character"),
            new KeyValuePair<string, string>("Level1", "Level 1")
        };
        if (GameId == "s1") weaponChoices.Insert(0, new("TirVariance", "Tir's current weapon level ±3 (default)"));
        else weaponChoices.Insert(0, new("RiouVariance", "Riou's current weapon level ±3 (default)"));
        var weaponLevel = new AppComboBox { Dock = DockStyle.Top, DisplayMember = "Value" };
        var weaponVersion = GameId == "s1" ? new Version(0, 8, 14) : GameInstallation.RiouJoiningWeaponMinimumVersion;
        if (!SupportsHost(weaponVersion))
        {
            weaponLevel.Enabled = false;
            weapons.Text += " (requires plug-in " + weaponVersion + ")";
        }
        weaponLevel.Items.AddRange(weaponChoices.Cast<object>().ToArray());
        weaponLevel.SelectedItem = weaponChoices.FirstOrDefault(choice => choice.Key == Get(GameId + ".joiningWeaponLevel"));
        weaponLevel.SelectedIndexChanged += (_, _) =>
        {
            if (weaponLevel.SelectedItem is KeyValuePair<string, string> choice)
                Set(GameId + ".joiningWeaponLevel", choice.Key);
        };
        weaponFields.Controls.Add(weaponLevel);
        weaponFields.Controls.Add(Note(GameId == "s1"
            ? "Tir ±3 uses his current weapon level with a seeded offset from −3 to +3 when a character first joins. The policy is saved per campaign."
            : "Riou ±3 uses his current weapon level with a seeded offset from −3 to +3 when a character first joins. The policy is saved per campaign."));
        weapons.Controls.Add(weaponFields);
        page.Controls.Add(weapons, 1, 2);
        return page;
    }

    private sealed record Hero(int Id, string Name)
    {
        public override string ToString() => Name + "  (#" + Id + ")";
    }

    private string Get(string id) => _workspace!.Get(id);
    private void Set(string id, string value)
    {
        if (_loading || _workspace is null) return;
        var setting = SettingsCatalog.Find(id);
        _workspace.Documents[setting.FileName].Set(setting.Section, setting.Key, value);
        _status.Text = "Unsaved settings. Apply them before starting the game.";
        RefreshStatus();
    }

    private void RefreshStatus()
    {
        if (_workspace is null) { _apply.Enabled = _launch.Enabled = false; return; }
        bool running = GameInstallation.IsGameRunning(_workspace.GameFolder);
        _running.Text = running ? "Game is running — close it before applying settings." : _workspace.IsDirty ? "Ready • unsaved changes" : "Ready • settings saved";
        _running.ForeColor = running ? Color.FromArgb(151, 91, 19) : Color.FromArgb(30, 103, 68);
        _apply.Enabled = _launch.Enabled = !running;
        try
        {
            var status = GameInstallation.Inspect(_workspace.GameFolder);
            _installation.Text = status.HostInstalled ? "Stars of Destiny " + status.HostVersion + " • " + (status.FixInstalled ? "Suikoden Fix available" : "Suikoden Fix not found") : "Stars of Destiny plug-in not found";
        }
        catch (Exception error)
        {
            _installation.Text = error.Message;
            _apply.Enabled = _launch.Enabled = false;
        }
        Text = VersionedTitle + (_workspace.IsDirty ? " *" : "");
    }

    private void ApplySettings(bool launch)
    {
        if (_workspace is null) return;
        try
        {
            _advanced?.EndEdit();
            var result = _workspace.SaveChanges();
            _status.Text = result.ChangedFiles.Count == 0 ? "Settings are up to date." : "Settings saved. Backup in " + result.BackupDirectory;
            try { File.WriteAllText(PreferencePath, JsonSerializer.Serialize(new { gameFolder = _workspace.GameFolder })); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            { _status.Text += " The launcher could not remember this folder."; }
            RefreshStatus();
            if (launch) GameInstallation.LaunchGame(_workspace.GameFolder);
        }
        catch (Exception error) { ShowError(error); }
    }

    private bool CanDiscard() => _workspace?.IsDirty != true || MessageBox.Show(this,
        "Discard the unsaved launcher changes?", AppTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
    private void ShowError(Exception error) { _status.Text = error.Message; MessageBox.Show(this, error.Message, AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Information); }
    private static void OpenPath(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path)) return;
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
    }
    private static string PreferencePath => Path.Combine(AppContext.BaseDirectory, "launcher-preferences.json");
    private static string? ReadPreferredFolder()
    {
        try { using var json = JsonDocument.Parse(File.ReadAllText(PreferencePath)); return json.RootElement.GetProperty("gameFolder").GetString(); }
        catch { return null; }
    }

    private void AddPage(string title, Control control)
    {
        var page = new TabPage(title) { BackColor = BackColor, Padding = new Padding(8), AutoScroll = true };
        control.Dock = DockStyle.Fill;
        page.Controls.Add(control);
        _tabs.TabPages.Add(page);
    }
    private CheckBox Check(string id, string label)
    {
        var control = new CheckBox { Text = label, AutoSize = true, Checked = bool.TryParse(Get(id), out bool value) && value, Margin = new Padding(4, 7, 4, 5) };
        if (id is "shared.enabled" or "s1.treasureEnabled")
        {
            if (!SupportsHost(new Version(0, 11, 1)))
            {
                control.Enabled = false;
                control.Text += " (requires plug-in 0.11.1)";
            }
        }
        control.CheckedChanged += (_, _) => Set(id, control.Checked ? "true" : "false");
        return control;
    }
    private bool SupportsHost(Version minimum)
    {
        string? installed = GameInstallation.Inspect(_workspace!.GameFolder).HostVersion;
        return Version.TryParse(installed, out var version) && version >= minimum;
    }
    private Control Choice(string id, string label, string[] choices)
    {
        var row = Grid(2, 1);
        row.Height = 33;
        row.Dock = DockStyle.Top;
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        var combo = new AppComboBox { Dock = DockStyle.Fill };
        combo.Items.AddRange(choices);
        combo.SelectedItem = choices.FirstOrDefault(option => string.Equals(option, Get(id), StringComparison.OrdinalIgnoreCase));
        combo.SelectedIndexChanged += (_, _) => { if (combo.SelectedItem is string value) Set(id, value); };
        row.Controls.Add(TextLabel(label), 0, 0);
        row.Controls.Add(combo, 1, 0);
        return row;
    }
    private Control Number(string id, string label, decimal minimum, decimal maximum, int decimals = 0)
    {
        var row = Grid(2, 1);
        row.Height = 34;
        row.Dock = DockStyle.Top;
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
        decimal.TryParse(Get(id), NumberStyles.Float, CultureInfo.InvariantCulture, out decimal value);
        var number = new NumericUpDown { Dock = DockStyle.Fill, Minimum = minimum, Maximum = maximum, DecimalPlaces = decimals,
            Increment = decimals == 0 ? 1 : 0.1m, Value = Math.Clamp(value, minimum, maximum) };
        number.ValueChanged += (_, _) => Set(id, number.Value.ToString(CultureInfo.InvariantCulture));
        row.Controls.Add(TextLabel(label), 0, 0);
        row.Controls.Add(number, 1, 0);
        return row;
    }
    private static TableLayoutPanel Grid(int columns, int rows) => new() { ColumnCount = columns, RowCount = rows, Dock = DockStyle.Fill, Margin = Padding.Empty };
    private static GroupBox Group(string title) => new() { Text = title, Dock = DockStyle.Fill, Padding = new Padding(12, 20, 12, 10), Margin = new Padding(0, 0, 8, 8) };
    private static Panel Stack() => new SettingsStack();
    private static Label TextLabel(string text) => new() { Text = text, AutoSize = false, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
    private static Label Note(string text) => new() { Text = text, ForeColor = Muted, Height = 48, Width = 470, Dock = DockStyle.Top, Margin = new Padding(4, 4, 4, 8) };
    private static Button Button(string title) => new() { Text = title, AutoSize = true, Height = 29, Padding = new Padding(5, 0, 5, 0), Margin = new Padding(2, 0, 4, 3) };
    private static FlowLayoutPanel Buttons(params Button[] buttons)
    {
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty, AutoSize = true };
        panel.Controls.AddRange(buttons);
        return panel;
    }

    private sealed class SettingsStack : Panel
    {
        private bool _arranging;
        internal SettingsStack()
        {
            Dock = DockStyle.Fill;
            AutoScroll = true;
        }
        protected override void OnLayout(LayoutEventArgs e)
        {
            if (_arranging) return;
            _arranging = true;
            try
            {
                base.OnLayout(e);
                int width = Math.Max(100, ClientSize.Width - Padding.Horizontal - SystemInformation.VerticalScrollBarWidth - 4);
                int y = Padding.Top + AutoScrollPosition.Y;
                foreach (Control child in Controls)
                {
                    int available = Math.Max(80, width - child.Margin.Horizontal);
                    child.Dock = DockStyle.None;
                    child.Anchor = AnchorStyles.Top | AnchorStyles.Left;
                    int height = child.Height;
                    if (child is Label)
                    {
                        height = TextRenderer.MeasureText(child.Text, child.Font, new Size(available, 1000),
                            TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl).Height + 3;
                    }
                    else if (child is CheckBox or RadioButton)
                    {
                        child.MaximumSize = new Size(available, 0);
                        height = child.GetPreferredSize(new Size(available, 0)).Height;
                    }
                    y += child.Margin.Top;
                    child.SetBounds(Padding.Left + child.Margin.Left, y, available, height);
                    y += height + child.Margin.Bottom;
                }
                AutoScrollMinSize = new Size(0, y - AutoScrollPosition.Y + Padding.Bottom);
            }
            finally { _arranging = false; }
        }
    }

    private sealed class AppComboBox : ComboBox
    {
        internal AppComboBox()
        {
            DropDownStyle = ComboBoxStyle.DropDownList;
            DrawMode = DrawMode.OwnerDrawFixed;
        }
        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            e.DrawBackground();
            if (e.Index >= 0)
                TextRenderer.DrawText(e.Graphics, GetItemText(Items[e.Index]), Font, e.Bounds, e.ForeColor,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            e.DrawFocusRectangle();
        }
    }
}
