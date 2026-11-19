using SuikodenRandomizer.Shared;

namespace StarsOfDestiny.CtbEditor;

internal sealed partial class EditorForm
{
    internal static void Verify(string scratch)
    {
        if (!Path.IsPathFullyQualified(scratch) || Directory.Exists(scratch) && Directory.EnumerateFileSystemEntries(scratch).Any())
            throw new ArgumentException("UI verification requires an explicitly supplied, empty scratch directory.");
        var game = Path.GetFullPath(Path.Combine(scratch, "Test game"));
        var config = Path.Combine(game, "BepInEx", "config");
        var toolFolder = Path.Combine(game, "StarsOfDestiny Launcher");
        Directory.CreateDirectory(config);
        Directory.CreateDirectory(toolFolder);
        File.WriteAllText(Path.Combine(game, Program.GameExe), "");
        Check(Program.FindNearbyGame(toolFolder) == game, "game detection from installed tools folder");
        string profilePath = Path.Combine(config, CtbTuningProfile.FileName);

        using var form = new EditorForm(game) { ShowInTaskbar = false, Opacity = 0 };
        form.Show();
        Application.DoEvents();
        var dragon = CtbTuningCatalog.Enemies.Single(e => e.Name == "Zombie Dragon");
        Check(form.Selected?.Key == dragon.SourceId && !form._override.Checked && form._speed.Value == dragon.BaseSpd,
            "initial Zombie Dragon native SPD");
        Check(form._bossSpeedBonus.Value == 0 && form._bossSpeedBonus.Minimum == 0
            && form._bossSpeedBonus.Maximum == 300 && form._bossSpeedBonus.DecimalPlaces == 2, "solo bonus range");
        Check(!form._normalScaling.Checked && !form._blend.Checked && !form._partyWeight.Enabled && !form._variation.Enabled
            && form._partyWeight.Value == 75 && form._variation.Value == 0, "fixed mode and blend defaults");
        form._normalScaling.Checked = true;
        Check(form.Profile!.NormalEnemyScalingEnabled && !form.Profile.SoloBossBlendEnabled && form._save.Enabled,
            "enabling normal enemy scaling independently");
        form._blend.Checked = true;
        form._partyWeight.Value = 72;
        form._variation.Value = 5;
        Check(form._partyWeight.Enabled && form._variation.Enabled && form._speedRule.Text.Contains("level 17")
            && form._blendNote.Text.Contains("28%"), "eligible boss and guide-level share");
        var defaultSize = form.Size;
        form.Size = form.MinimumSize;
        Application.DoEvents();
        var visible = form._encounterGroup.RectangleToScreen(form._encounterGroup.ClientRectangle);
        foreach (var field in new[] { form._partyWeight, form._variation, form._bossSpeedBonus, form._boss, form._other })
            foreach (Control control in field.Parent!.Controls)
            {
                var bounds = control.RectangleToScreen(control.ClientRectangle);
                Check(visible.Contains(bounds), $"{field.AccessibleName} {control.GetType().Name} {bounds} in {visible} at minimum size");
            }
        var normalSettings = form._normalScaling.Parent!;
        var normalBounds = normalSettings.Parent!.RectangleToScreen(normalSettings.Parent.ClientRectangle);
        var clientBounds = form.RectangleToScreen(form.ClientRectangle);
        Check(clientBounds.Contains(normalBounds) && clientBounds.Contains(visible)
            && clientBounds.Contains(form._save.RectangleToScreen(form._save.ClientRectangle)), "both settings groups and save fit at minimum size");
        foreach (Control field in normalSettings.Controls)
            Check(normalBounds.Contains(field.RectangleToScreen(field.ClientRectangle)), "normal encounter controls at minimum size");
        Check(form._grid.Height > form._grid.ColumnHeadersHeight + 3 * form._grid.RowTemplate.Height
            && form._grid.Parent!.RectangleToScreen(form._grid.Bounds).Bottom <= normalBounds.Top,
            "combatant list remains usable above normal encounter settings");
        form.Size = defaultSize;
        form._override.Checked = true;
        form._speed.Value = 87;
        form._bossSpeedBonus.Value = 57.25m;
        form._boss.Value = 17;
        form._other.Value = 45;
        form._search.Text = "Bandit";
        Check(form._grid.Rows.Cast<DataGridViewRow>().Where(r => ((Actor)r.Tag!).Name == "Bandit")
            .Select(r => r.Cells[1].Value).Distinct().Count() >= 2, "separate same-name enemy variants");
        form._search.Text = "Red Soldier Ant";
        Check(form._speedRule.Text.Contains("fixed SPD takes priority over party scaling"), "ordinary enemy fixed SPD takes priority");
        form._normalScaling.Checked = false;
        Check(form._speedRule.Text.Contains("scaling is off"), "native or fixed SPD with normal scaling disabled");
        form._normalScaling.Checked = true;
        form._search.Text = "va7_map_17_battle_01_monster_data_00";
        Check(form.Selected?.Name == "Queen Ant" && form._speedRule.Text.Contains("uses fixed tuning"),
            "scripted Queen Ant uses fixed tuning with normal scaling enabled");
        form._search.Text = "vf2_map_00_battle_00_monster_data_02";
        Check(form.Selected?.Name == "Queen Ant" && form._speedRule.Text.Contains("priority over party scaling"),
            "ordinary Queen Ant variant has its own scaling eligibility");
        form._search.Text = "Venus Shell";
        Check(form.Selected?.Name == "Venus Shell" && form._speed.Maximum == 400
            && form._speedRule.Text.Contains("level 55"), "enemy SPD range and Venus Shell guide");
        string venus = form.Selected!.Key;
        form._override.Checked = true;
        form._speed.Value = 400;
        form._search.Clear();
        form._side.SelectedIndex = 1;
        Check(form._speed.Maximum == 255 && !form.Profile!.HasStarOverrides, "switching from a 400 SPD enemy to Stars");
        form._search.Text = "Mathiu";
        Check(form.Selected?.Speed == null && form.Selected?.Name == "Mathiu" && !form._override.Enabled, "support Star");
        form._search.Text = "Viktor";
        int star = form.Selected!.StarId!.Value;
        Check(form._override.Enabled && form._base.Text.Contains("live SPD") && form._speed.Value == form.Selected.Speed,
            "level-one reference without a previous battle");
        form._override.Checked = true;
        form._speed.Value = 99;
        form._dragon.PerformClick();
        Check(form._speed.Value == 87 && form._override.Checked, "retained edited SPD while filtering");
        form._save.PerformClick();
        Check(!form._dirty && File.Exists(profilePath), "Save button");
        var saved = CtbTuningProfile.Load(profilePath);
        Check(saved.GetEnemySpd(dragon.SourceId) == 87 && saved.GetEnemySpd(venus) == 400 && saved.GetStarSpd(star) == 99
            && saved.SoloBossSpdBonusPercent == 57.25m && saved.SoloBossBlendEnabled && saved.NormalEnemyScalingEnabled
            && saved.SoloBossPartyWeightPercent == 72 && saved.SoloBossVariationPercent == 5
            && saved.SoloBossBreakPercent == 17 && saved.OtherBreakPercent == 45, "saved profile values");
        using (var image = new Bitmap(form.Width, form.Height))
        {
            form.DrawToBitmap(image, new Rectangle(Point.Empty, form.Size));
            image.Save(Path.Combine(scratch, "ctb-editor.png"), System.Drawing.Imaging.ImageFormat.Png);
        }
        form.Close();

        using var reopened = new EditorForm(game) { ShowInTaskbar = false, Opacity = 0 };
        reopened.Show();
        Application.DoEvents();
        Check(reopened._speed.Value == 87 && reopened._bossSpeedBonus.Value == 57.25m &&
            reopened._normalScaling.Checked && reopened._blend.Checked && reopened._partyWeight.Value == 72 && reopened._variation.Value == 5 &&
            reopened._boss.Value == 17 && reopened._other.Value == 45, "reopened editor");
        reopened._search.Text = "Venus Shell";
        Check(reopened._override.Checked && reopened._speed.Enabled && reopened._speed.Value == 400, "reopened enemy SPD 400");
        reopened._dragon.PerformClick();
        reopened._bossSpeedBonus.Value = 300;
        Check(reopened.Profile!.SoloBossSpdBonusPercent == 300 && reopened._speed.Value == 87,
            "editing the bonus preserves a fixed fighter SPD");
        ((FlowLayoutPanel)reopened._bossSpeedBonus.Parent!).Controls.OfType<Button>().Single().PerformClick();
        Check(reopened._bossSpeedBonus.Value == 0 && reopened._boss.Value == 17 && reopened._other.Value == 45,
            "resetting the solo bonus preserves Break settings");
        reopened._override.Checked = false;
        var stale = new TuningSession(game);
        foreach (var row in reopened._encounterGroup.Controls[0].Controls.OfType<FlowLayoutPanel>())
            row.Controls.OfType<Button>().Single().PerformClick();
        reopened._normalScaling.Checked = false;
        Check(reopened._blend.Checked && reopened.Profile!.SoloBossBlendEnabled && reopened._speedRule.Text.Contains("level 17"),
            "disabling normal enemy scaling preserves solo boss settings");
        reopened._blend.Checked = false;
        Check(!reopened._partyWeight.Enabled && !reopened._variation.Enabled && !reopened._speedRule.Text.Contains("Solo blend"),
            "switching back restores fixed mode");
        reopened._save.PerformClick();
        saved = CtbTuningProfile.Load(profilePath);
        var defaults = new CtbTuningProfile();
        Check(!saved.GetEnemySpd(dragon.SourceId).HasValue && saved.GetStarSpd(star) == 99 && saved.SoloBossSpdBonusPercent == 0
            && !saved.SoloBossBlendEnabled && !saved.NormalEnemyScalingEnabled && saved.SoloBossPartyWeightPercent == defaults.SoloBossPartyWeightPercent
            && saved.SoloBossVariationPercent == defaults.SoloBossVariationPercent
            && saved.SoloBossBreakPercent == defaults.SoloBossBreakPercent && saved.OtherBreakPercent == defaults.OtherBreakPercent,
            "individual resets and unaffected Star");
        bool rejected = false;
        try { stale.Save(); }
        catch (IOException) { rejected = true; }
        Check(rejected, "stale writer protection");
        string lastParty = Path.Combine(game, "BepInEx", "cache", CtbTuningLastParty.FileName);
        CtbTuningLastParty.Write(lastParty, new Dictionary<int, int> { [star] = 90, [8] = 275 });
        reopened._reload.PerformClick();
        reopened._side.SelectedIndex = 1;
        reopened._search.Text = "Viktor";
        Check(reopened._override.Checked && reopened._speed.Value == 99 && reopened._base.Text.Contains("Last battle SPD 90"),
            "battle reference preserves existing override");
        reopened._override.Checked = false;
        Check(reopened._speed.Value == 90 && !reopened._speed.Enabled, "clearing returns to live Star SPD");
        reopened._override.Checked = true;
        Check(reopened.Profile!.GetStarSpd(star) == 90, "new override starts at sampled SPD");
        reopened._override.Checked = false;
        reopened._save.PerformClick();
        Check(!CtbTuningProfile.Load(profilePath).GetStarSpd(star).HasValue, "sample remains read-only after clearing");
        reopened._search.Text = "Tir McDohl";
        Check(reopened._speed.Value == 255 && reopened._base.Text.Contains("Last battle SPD 275"), "wide native SPD reference");
        File.WriteAllText(lastParty, "{broken");
        reopened._reload.PerformClick();
        Check(reopened._speed.Value == reopened.Selected!.Speed && !reopened._base.Text.Contains("Last battle SPD"),
            "malformed optional battle reference");
        reopened.Close();
        Console.WriteLine("CTB editor WinForms checks passed.");
    }

    private static void Check(bool condition, string feature)
    {
        if (!condition) throw new InvalidOperationException("CTB editor check failed for " + feature + ".");
    }
}
