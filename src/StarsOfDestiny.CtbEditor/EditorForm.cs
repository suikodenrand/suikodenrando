using SuikodenRandomizer.Shared;

namespace StarsOfDestiny.CtbEditor;

internal sealed partial class EditorForm : Form
{
    private static readonly Color Navy = Color.FromArgb(14, 43, 69);
    private static readonly Color Muted = Color.FromArgb(80, 93, 106);
    private readonly TextBox _folder = new() { ReadOnly = true, Dock = DockStyle.Fill };
    private readonly TextBox _search = new() { Dock = DockStyle.Fill, PlaceholderText = "Search name or ID" };
    private readonly ComboBox _side = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly Button _dragon = new() { Text = "Zombie Dragon", AutoSize = true };
    private readonly DataGridView _grid = new()
    {
        Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
        AllowUserToResizeRows = false, MultiSelect = false, RowHeadersVisible = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        BackgroundColor = Color.White, BorderStyle = BorderStyle.FixedSingle,
        EnableHeadersVisualStyles = false, ColumnHeadersHeight = 32
    };
    private readonly Label _actorName = new() { AutoSize = true, ForeColor = Navy, MaximumSize = new Size(255, 0) };
    private readonly Label _actorId = Note();
    private readonly Label _base = Note();
    private readonly Label _speedRule = Note();
    private readonly CheckBox _override = new() { Text = "Override CTB SPD", AutoSize = true };
    private readonly NumericUpDown _speed = Number(1, CtbTuningProfile.MaxEnemySpd);
    private readonly CheckBox _normalScaling = new() { Text = "Adapt enemy SPD to the opening party", AutoSize = true };
    private readonly CheckBox _blend = new() { Text = "Blend solo boss SPD", AutoSize = true };
    private readonly NumericUpDown _partyWeight = Number(0, 100);
    private readonly NumericUpDown _variation = Number(0, CtbTuningProfile.MaxVariationPercent);
    private readonly Label _blendNote = Note();
    private readonly NumericUpDown _bossSpeedBonus = Number(0, 300);
    private readonly NumericUpDown _boss = Number(0, 75);
    private readonly NumericUpDown _other = Number(0, 75);
    private readonly Label _status = new() { AutoEllipsis = true, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Button _save = new() { Text = "Save changes", AutoSize = true, Enabled = false };
    private readonly GroupBox _actorGroup = new() { Text = "Selected combatant", Dock = DockStyle.Fill };
    private readonly GroupBox _encounterGroup = new() { Text = "Solo bosses and Break", Dock = DockStyle.Fill };
    private readonly Button _reload = new() { Text = "Reload", AutoSize = true };
    private TuningSession? _session;
    private bool _updating, _dirty;

    private sealed record Actor(string Name, string Key, string DisplayId, int? Level, int? Hp, int? Speed, int? StarId);
    private static readonly Actor[] Enemies = CtbTuningCatalog.Enemies.Select(e =>
        new Actor(e.Name, e.SourceId, e.SourceId, e.Level, e.Hp, e.BaseSpd, null)).OrderBy(e => e.Name).ThenBy(e => e.Level).ThenBy(e => e.Key).ToArray();
    private static readonly Actor[] Stars = CtbTuningCatalog.Stars.Select(s =>
        new Actor(s.Name, s.Id.ToString(), $"Star {s.StarNumber} · ID {s.Id}", null, null, s.BaseSpd, s.Id)).OrderBy(s => s.Name).ToArray();
    private Actor? Selected => _grid.CurrentRow?.Tag as Actor;
    private CtbTuningProfile? Profile => _session?.Profile;

    internal EditorForm(string? folder)
    {
        Text = "Stars of Destiny · Suikoden I CTB editor";
        Font = new Font("Segoe UI", 9.5f);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(1010, 650);
        MinimumSize = new Size(870, 640);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(244, 245, 247);

        var shell = Table(1, 3);
        shell.Padding = new Padding(12);
        shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(shell);

        var bar = Table(4, 1);
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bar.Controls.Add(FieldLabel("Game folder"), 0, 0);
        bar.Controls.Add(_folder, 1, 0);
        var browse = new Button { Text = "Browse…", AutoSize = true };
        bar.Controls.Add(browse, 2, 0);
        bar.Controls.Add(_reload, 3, 0);
        shell.Controls.Add(bar, 0, 0);

        var body = Table(2, 1);
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 340));
        shell.Controls.Add(body, 0, 1);
        var actors = Table(1, 3);
        actors.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        actors.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        actors.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        body.Controls.Add(actors, 0, 0);
        var filters = Table(4, 1);
        filters.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        filters.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        filters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        filters.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _side.Items.AddRange(["Enemies", "Stars"]);
        _side.SelectedIndex = 0;
        _side.AccessibleName = "Combatant type";
        _search.AccessibleName = "Search combatants";
        filters.Controls.Add(_side, 0, 0);
        filters.Controls.Add(FieldLabel("Search"), 1, 0);
        filters.Controls.Add(_search, 2, 0);
        filters.Controls.Add(_dragon, 3, 0);
        actors.Controls.Add(filters, 0, 0);
        _grid.ColumnHeadersDefaultCellStyle.BackColor = Navy;
        _grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
        _grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(245, 248, 250);
        foreach (var (name, weight) in new[] { ("Name", 34), ("ID", 26), ("Level", 9), ("HP", 12), ("Game SPD", 14), ("Fixed SPD", 13) })
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = name, FillWeight = weight, MinimumWidth = 42,
                SortMode = DataGridViewColumnSortMode.NotSortable });
        actors.Controls.Add(_grid, 0, 1);

        var normal = new GroupBox { Text = "Normal encounters", Dock = DockStyle.Fill,
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
        var normalSettings = Table(1, 2);
        normalSettings.Padding = new Padding(5, 0, 5, 2);
        normalSettings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        normalSettings.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        normalSettings.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        normalSettings.Controls.Add(_normalScaling, 0, 0);
        normalSettings.Controls.Add(new Label { AutoSize = true, MaximumSize = new Size(430, 0), ForeColor = Muted,
            Text = "Uses 50% of the SPD gap from average Stars at the enemies’ median level. " +
                "The group keeps its opening adjustment (10% slower to 25% faster) through knockouts." }, 0, 1);
        normal.Controls.Add(normalSettings);
        actors.Controls.Add(normal, 0, 2);

        var options = Table(1, 2);
        options.RowStyles.Add(new RowStyle(SizeType.Percent, 40));
        options.RowStyles.Add(new RowStyle(SizeType.Percent, 60));
        body.Controls.Add(options, 1, 0);
        options.Controls.Add(_actorGroup, 0, 0);
        var stats = Stack();
        _actorGroup.Controls.Add(stats);
        _actorName.Font = new Font(Font.FontFamily, 12, FontStyle.Bold);
        _actorId.Font = new Font("Consolas", 8.5f);
        _speed.AccessibleName = "Absolute CTB speed";
        stats.Controls.AddRange([_actorName, _actorId, _base, _override, _speed, _speedRule]);
        options.Controls.Add(_encounterGroup, 0, 1);
        var encounter = Stack();
        _encounterGroup.Controls.Add(encounter);
        var defaults = new CtbTuningProfile();
        _bossSpeedBonus.DecimalPlaces = 2;
        encounter.Controls.Add(_blend);
        AddPercentRow(encounter, "Party weight", _partyWeight, defaults.SoloBossPartyWeightPercent);
        AddPercentRow(encounter, "Fight variation (±)", _variation, defaults.SoloBossVariationPercent);
        encounter.Controls.Add(_blendNote);
        AddPercentRow(encounter, "Solo SPD bonus", _bossSpeedBonus, defaults.SoloBossSpdBonusPercent);
        AddPercentRow(encounter, "Solo Break delay", _boss, defaults.SoloBossBreakPercent);
        AddPercentRow(encounter, "Other Break delay", _other, defaults.OtherBreakPercent);
        encounter.Controls.Add(Note("Break delays the enemy by this share of its normal turn interval."));

        var footer = Table(2, 1);
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.Controls.Add(_status, 0, 0);
        footer.Controls.Add(_save, 1, 0);
        shell.Controls.Add(footer, 0, 2);

        _search.TextChanged += (_, _) => ListActors();
        _side.SelectedIndexChanged += (_, _) => ListActors();
        _dragon.Click += (_, _) => SelectDragon();
        _grid.SelectionChanged += (_, _) => { if (!_updating) DisplayActor(); };
        _override.CheckedChanged += (_, _) => EditSpeed();
        _speed.ValueChanged += (_, _) => EditSpeed();
        _normalScaling.CheckedChanged += (_, _) => EditEncounterSettings();
        _blend.CheckedChanged += (_, _) => EditEncounterSettings();
        _partyWeight.ValueChanged += (_, _) => EditEncounterSettings();
        _variation.ValueChanged += (_, _) => EditEncounterSettings();
        _bossSpeedBonus.ValueChanged += (_, _) => EditEncounterSettings();
        _boss.ValueChanged += (_, _) => EditEncounterSettings();
        _other.ValueChanged += (_, _) => EditEncounterSettings();
        _save.Click += (_, _) => SaveChanges();
        _reload.Click += (_, _) => { if (CanLeave()) OpenFolder(_folder.Text); };
        browse.Click += (_, _) => Browse();
        FormClosing += (_, e) => e.Cancel = !CanLeave();
        if (!string.IsNullOrWhiteSpace(folder)) OpenFolder(folder);
        else { _status.Text = "Choose your Suikoden game folder to edit CTB settings."; _reload.Enabled = false; }
        SelectDragon();
        _encounterGroup.Enabled = Profile != null;
        _normalScaling.Enabled = Profile != null;
    }

    private static TableLayoutPanel Table(int columns, int rows) => new() { ColumnCount = columns, RowCount = rows,
        Dock = DockStyle.Fill, AutoSize = true, Margin = Padding.Empty, GrowStyle = TableLayoutPanelGrowStyle.FixedSize };
    private static FlowLayoutPanel Stack() => new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown,
        WrapContents = false, AutoScroll = true, Padding = new Padding(7, 2, 6, 2) };
    private static NumericUpDown Number(int min, int max) => new() { Minimum = min, Maximum = max, Width = 67 };
    private static Label FieldLabel(string value) => new() { Text = value, AutoSize = true, Anchor = AnchorStyles.Left };
    private static Label Note(string value = "") => new() { Text = value, AutoSize = true, MaximumSize = new Size(300, 0),
        ForeColor = Muted, Margin = new Padding(3, 6, 3, 5) };

    private static void AddPercentRow(FlowLayoutPanel panel, string name, NumericUpDown value, decimal defaultValue)
    {
        value.AccessibleName = name + " percentage";
        value.Width = 58;
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        var label = new Label { Text = name, Width = 132, AutoSize = false, TextAlign = ContentAlignment.MiddleLeft,
            Anchor = AnchorStyles.Left };
        var reset = new Button { Text = $"Reset {defaultValue:0.##}%", AccessibleName = $"Reset {name}", AutoSize = true };
        reset.Click += (_, _) => value.Value = defaultValue;
        row.Controls.AddRange([label, value, FieldLabel("%"), reset]);
        panel.Controls.Add(row);
    }

    private void SelectDragon()
    {
        _side.SelectedIndex = 0;
        _search.Clear();
        var dragon = Enemies.FirstOrDefault(e => e.Name.Equals("Zombie Dragon", StringComparison.OrdinalIgnoreCase));
        ListActors(dragon?.Key);
        _grid.Focus();
    }

    private void ListActors(string? selectedKey = null)
    {
        selectedKey ??= Selected?.Key;
        var filter = _search.Text.Trim();
        _updating = true;
        try
        {
            _grid.Rows.Clear();
            bool stars = _side.SelectedIndex == 1;
            _grid.Columns[2].Visible = _grid.Columns[3].Visible = !stars;
            _grid.Columns[1].HeaderText = stars ? "Star / ID" : "Source ID";
            _grid.Columns[4].HeaderText = stars ? "Lv. 1 SPD" : "Game SPD";
            foreach (var actor in stars ? Stars : Enemies)
            {
                if (!actor.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
                    && !actor.DisplayId.Contains(filter, StringComparison.OrdinalIgnoreCase)) continue;
                int i = _grid.Rows.Add(actor.Name, actor.DisplayId, actor.Level?.ToString() ?? "", actor.Hp?.ToString() ?? "", actor.Speed?.ToString() ?? "—",
                    Override(actor)?.ToString() ?? "—");
                _grid.Rows[i].Tag = actor;
                _grid.Rows[i].Cells[1].ToolTipText = actor.DisplayId;
                if (actor.Key == selectedKey) _grid.CurrentCell = _grid.Rows[i].Cells[0];
            }
        }
        finally { _updating = false; }
        DisplayActor();
    }

    private int? Override(Actor actor) => actor.StarId is int id ? Profile?.GetStarSpd(id) : Profile?.GetEnemySpd(actor.Key);

    private void DisplayActor()
    {
        var actor = Selected;
        _updating = true;
        try
        {
            _actorGroup.Enabled = actor != null && Profile != null;
            _actorName.Text = actor?.Name ?? "No matching combatants";
            _actorId.Text = actor?.DisplayId ?? "";
            var spd = actor == null ? null : Override(actor);
            var last = actor?.StarId is int starId ? _session?.LastBattleSpd(starId) : null;
            _override.Checked = spd.HasValue;
            int maxSpeed = actor?.StarId is int ? CtbTuningProfile.MaxStarSpd : CtbTuningProfile.MaxEnemySpd;
            _speed.Maximum = maxSpeed;
            _speed.Value = Math.Clamp(spd ?? last ?? actor?.Speed ?? 1, 1, maxSpeed);
            _speed.Enabled = spd.HasValue;
            _override.Enabled = actor?.Speed.HasValue == true;
            _base.Text = actor == null ? "" : !actor.Speed.HasValue ? "Non-combat Star. This Star has no battle SPD."
                : actor.StarId.HasValue ? (last.HasValue ? $"Last battle SPD {last}. " : "") +
                    $"Level 1 SPD {actor.Speed}. The default follows live SPD from level and equipment."
                : $"Game SPD {actor.Speed}   ·   Level {actor.Level}   ·   HP {actor.Hp}";
            _speedRule.Text = "";
            if (actor?.Speed.HasValue == true && actor.StarId == null)
            {
                if (CtbTuningSoloBossGuide.TryGet(actor.Key, out var guide))
                    _speedRule.Text = _blend.Checked
                        ? $"Solo blend · Recommended level {guide.RecommendedLevel}. Party ceiling uses each fighter’s average at level {CtbTuningBossCeiling.ReferenceLevel(guide.RecommendedLevel)} plus gear SPD. Fixed boss SPD applies with blend off."
                        : "A fixed SPD takes priority over the solo boss bonus.";
                else if (CtbTuningNormalScaling.TryGetEnemyLevel(actor.Key, out _))
                    _speedRule.Text = _normalScaling.Checked
                        ? "In normal encounters, a fixed SPD takes priority over party scaling."
                        : "Normal encounter scaling is off. This enemy uses its game SPD or your fixed SPD.";
                else
                    _speedRule.Text = "This encounter uses fixed tuning. Individual SPD overrides take priority over any solo boss bonus.";
            }
            else if ((_blend.Checked || _normalScaling.Checked) && actor?.Speed.HasValue == true)
                _speedRule.Text = "Active Stars contribute their opening SPD to adaptive enemy speeds.";
        }
        finally { _updating = false; }
    }

    private void EditSpeed()
    {
        if (_updating || Profile == null || Selected is not { Speed: not null } a) return;
        int? value = _override.Checked ? (int)_speed.Value : null;
        if (a.StarId is int id) Profile.SetStarSpd(id, value);
        else Profile.SetEnemySpd(a.Key, value);
        _grid.CurrentRow!.Cells[5].Value = value?.ToString() ?? "—";
        _speed.Enabled = value.HasValue;
        if (!value.HasValue) DisplayActor();
        MarkDirty();
    }

    private void EditEncounterSettings()
    {
        if (_updating || Profile == null) return;
        Profile.NormalEnemyScalingEnabled = _normalScaling.Checked;
        Profile.SoloBossBlendEnabled = _blend.Checked;
        Profile.SoloBossPartyWeightPercent = (int)_partyWeight.Value;
        Profile.SoloBossVariationPercent = (int)_variation.Value;
        Profile.SoloBossSpdBonusPercent = _bossSpeedBonus.Value;
        Profile.SoloBossBreakPercent = (int)_boss.Value;
        Profile.OtherBreakPercent = (int)_other.Value;
        ShowBlendSettings();
        DisplayActor();
        MarkDirty();
    }

    private void ShowBlendSettings()
    {
        _partyWeight.Parent!.Enabled = _variation.Parent!.Enabled = _blend.Checked;
        _blendNote.Text = _blend.Checked
            ? $"{_partyWeight.Value}% opening party (growth ceiling) + {100 - _partyWeight.Value}% recommended-level Stars. Aim for 3 party attacks per boss attack."
            : "Blend uses party and recommended-level SPD when a supported boss opens alone.";
    }

    private void MarkDirty()
    {
        _dirty = _save.Enabled = true;
        _status.Text = "Unsaved changes";
    }

    private void OpenFolder(string folder)
    {
        try
        {
            var session = new TuningSession(folder);
            _session = session;
            _folder.Text = session.GameFolder;
            _updating = true;
            _normalScaling.Checked = session.Profile.NormalEnemyScalingEnabled;
            _blend.Checked = session.Profile.SoloBossBlendEnabled;
            _partyWeight.Value = session.Profile.SoloBossPartyWeightPercent;
            _variation.Value = session.Profile.SoloBossVariationPercent;
            _bossSpeedBonus.Value = session.Profile.SoloBossSpdBonusPercent;
            _boss.Value = session.Profile.SoloBossBreakPercent;
            _other.Value = session.Profile.OtherBreakPercent;
            _updating = false;
            ShowBlendSettings();
            _dirty = _save.Enabled = false;
            _encounterGroup.Enabled = true;
            _normalScaling.Enabled = true;
            _status.Text = "Saved changes take effect at the start of the next encounter.";
            ListActors();
        }
        catch (Exception e)
        {
            if (_session == null) _folder.Text = folder;
            _status.Text = $"Could not open profile. {e.Message}";
        }
        _reload.Enabled = !string.IsNullOrWhiteSpace(_folder.Text);
    }

    private void Browse()
    {
        using var picker = new FolderBrowserDialog { UseDescriptionForTitle = true,
            Description = "Select the Suikoden I and II HD Remaster game folder", ShowNewFolderButton = false,
            InitialDirectory = _folder.Text };
        if (picker.ShowDialog(this) == DialogResult.OK && CanLeave()) OpenFolder(picker.SelectedPath);
    }

    private bool SaveChanges()
    {
        if (_session == null) return false;
        try
        {
            _session.Save();
            _dirty = _save.Enabled = false;
            _status.Text = "Saved. Changes take effect at the start of the next encounter.";
            return true;
        }
        catch (Exception e) { _status.Text = $"Could not save. {e.Message}"; return false; }
    }

    private bool CanLeave() => !_dirty || MessageBox.Show(this, "Save these CTB changes before continuing?", Text,
        MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question) switch
        { DialogResult.Yes => SaveChanges(), DialogResult.No => true, _ => false };
}
