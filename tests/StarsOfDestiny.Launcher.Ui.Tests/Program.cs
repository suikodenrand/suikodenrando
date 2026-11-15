using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using StarsOfDestiny.Launcher.Core;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.EnableVisualStyles();
        string game = Path.Combine(Path.GetTempPath(), "stars-launcher-ui-tests-" + Guid.NewGuid().ToString("N"));
        string config = Path.Combine(game, "BepInEx", "config");
        string preference = Path.Combine(AppContext.BaseDirectory, "launcher-preferences.json");
        byte[]? originalPreference = File.Exists(preference) ? File.ReadAllBytes(preference) : null;
        Directory.CreateDirectory(config);
        File.WriteAllText(Path.Combine(game, GameInstallation.GameExeName), "Fake executable used only for settings UI tests.");
        string originalS1 = "# S1 comment must survive\r\n[Shuffle]\r\nSeed = first-seed\r\nMode = Shuffle\r\nCompanions = \r\nIncludeTir = false\r\n\r\n[Treasure]\r\nEnabled = false\r\nShuffleChests = false\r\n\r\n[Custom]\r\nUnrelated = retained\r\n";
        File.WriteAllText(Path.Combine(config, SettingsCatalog.S1File), originalS1);
        File.WriteAllText(Path.Combine(config, SettingsCatalog.S2File), "[Shuffle]\r\nSeed = second-seed\r\nMode = Shuffle\r\nCompanions = \r\nIncludeProtagonist = false\r\n");
        File.WriteAllText(Path.Combine(config, SettingsCatalog.SharedFile), "[General]\r\nEnabled = true\r\n[Interface]\r\nViewMode = Full\r\n");

        Form? form = null;
        int passed = 0;
        try
        {
            var type = Assembly.Load("StarsOfDestiny.Launcher").GetType("StarsOfDestiny.Launcher.MainForm", true)!;
            string versionedTitle = "Stars of Destiny Launcher " + type.Assembly.GetName().Version!.ToString(3);
            form = (Form)Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null, args: new object?[] { game }, culture: null)!;
            Call(form, "LoadInstallation", game);
            var workspace = Field<SettingsWorkspace>(form, "_workspace");
            var seed = Field<TextBox>(form, "_seed");
            var gameChoice = Field<ComboBox>(form, "_game");
            var tabs = Field<TabControl>(form, "_tabs");
            _ = form.Handle;
            _ = tabs.Handle;
            Check(seed.Text == "first-seed" && tabs.TabPages.Count == 7, "Actual form did not load its settings and pages.");
            Check(form.Text == versionedTitle, "The title does not display the assembly version.");
            Check(!workspace.IsDirty, "Building controls changed the original configuration.");
            Passed("Actual form loads the temporary installation without staging settings");

            var weaponChoice = WeaponChoice(tabs);
            Check(!weaponChoice.Enabled && weaponChoice.Parent!.Parent!.Text.Contains("requires plug-in 0.8.14", StringComparison.Ordinal),
                "A missing host did not disable joining weapon selection with the required version.");
            Check(!EquipmentChoice(tabs).Enabled && EquipmentChoice(tabs).Parent!.Parent!.Text.Contains("requires plug-in 0.14.4", StringComparison.Ordinal),
                "A missing host did not disable original-role inventory selection with the required version.");
            gameChoice.SelectedIndex = 1;
            Check(!WeaponChoice(tabs).Enabled && WeaponChoice(tabs).Parent!.Parent!.Text.Contains("requires plug-in 0.14.3", StringComparison.Ordinal),
                "A missing host did not disable S2 joining weapon selection with the RiouVariance minimum.");
            Check(!EquipmentChoice(tabs).Enabled, "Missing S2 host incorrectly exposed original-role inventory support.");
            gameChoice.SelectedIndex = 0;
            string plugins = Path.Combine(game, "BepInEx", "plugins");
            Directory.CreateDirectory(plugins);
            string host = Path.Combine(plugins, "SuikodenRandomizer.dll");
            // Only assembly metadata is inspected; these fixtures are never loaded as plug-ins.
            File.Copy(typeof(SettingsWorkspace).Assembly.Location, host, true);
            Check(Version.Parse(GameInstallation.Inspect(game).HostVersion!) < new Version(0, 11, 2), "Older-host fixture must remain below the feature version.");
            Call(form, "LoadInstallation", game);
            Check(!WeaponChoice(tabs).Enabled, "An older host did not disable joining weapon selection.");
            Check(!EquipmentChoice(tabs).Enabled, "An older host did not disable original-role inventory selection.");
            File.Copy(type.Assembly.Location, host, true);
            Call(form, "LoadInstallation", game);
            workspace = Field<SettingsWorkspace>(form, "_workspace");
            weaponChoice = WeaponChoice(tabs);
            Check(weaponChoice.Enabled && weaponChoice.Parent!.Parent!.Text == "Joining weapon level", "A supported host did not enable the weapon selection normally.");
            Check(EquipmentChoice(tabs).Enabled && EquipmentChoice(tabs).Parent!.Parent!.Text == "Joining inventory & equipment",
                "A supported host did not enable first-join inventory selection.");
            Check(!workspace.IsDirty, "Host compatibility checks staged configuration changes.");
            Passed("Joining weapon controls require the appropriate host version for each game without changing settings");

            var treasurePage = tabs.TabPages.Cast<TabPage>().Single(page => page.Text == "Treasure");
            var chestSwitch = Descendants<CheckBox>(treasurePage).Single();
            Check(chestSwitch.Text == "Enable chest shuffle for new campaigns" && !chestSwitch.Checked,
                "Treasure must have one clearly named new-campaign checkbox.");
            Check(!workspace.GetBoolean("s1.treasureEnabled") && !workspace.IsDirty,
                "Loading the new treasure controls changed a legacy paused configuration.");
            chestSwitch.Checked = true;
            Check(workspace.GetBoolean("s1.chests"), "Checking the new-campaign option did not stage chest shuffle.");
            chestSwitch.Checked = false;
            Check(!workspace.GetBoolean("s1.chests"), "Unchecking the new-campaign option did not stage original rewards.");
            Check(Descendants<ListView>(treasurePage).Count() == 1
                && Descendants<Button>(treasurePage).Any(button => button.Text == "Reset selected save's chest settings"),
                "The separate saved-policy controls are missing.");
            Check(!Field<DataGridView>(form, "_advanced").Rows.Cast<DataGridViewRow>().Any(candidate =>
                candidate.Tag is ValueTuple<string, string, string> field && field.Item1 == SettingsCatalog.S1File
                && field.Item2 == "Treasure" && field.Item3 == "Enabled"),
                "Advanced still exposes the removed chest master switch.");
            Passed("One new-campaign chest checkbox preserves saved-policy controls and hides the legacy master gate");

            Check(weaponChoice.Items.Count == 3 && SelectedWeaponPolicy(weaponChoice) == "TirVariance",
                "S1 joining weapon choices did not include and default to Tir ±3.");
            weaponChoice.SelectedIndex = 2;
            Check(workspace.Get("s1.joiningWeaponLevel") == "Level1", "S1 joining weapon choice was not staged.");
            Passed("Joining weapon selection shows friendly choices and stages Level 1 for S1");
            var equipmentChoice = EquipmentChoice(tabs);
            Check(equipmentChoice.Items.Count == 2 && SelectedWeaponPolicy(equipmentChoice) == "MatchRole",
                "S1 first-join inventory does not default to original role equipment.");
            Check(((KeyValuePair<string, string>)equipmentChoice.SelectedItem!).Value == "Original role's starting inventory (default)",
                "The equipment choice does not explain that carried inventory is included.");
            equipmentChoice.SelectedIndex = 1;
            Check(workspace.Get("s1.joiningEquipment") == "Donor", "S1 inventory choice was not staged.");
            Passed("Original-role and replacement starting inventory choices stage independently from weapon level");

            var battlePage = tabs.TabPages.Cast<TabPage>().Single(page => page.Text == "Battle & ATB");
            var uniteSwitch = Descendants<CheckBox>(battlePage).Single(control => control.Text == "Enable shuffled-character Unite attacks");
            Check(uniteSwitch.Enabled, "The supported host did not enable S1 Unite selection.");
            uniteSwitch.Checked = true;
            Check(workspace.GetBoolean("s1.unites"), "The S1 Unite control did not stage its setting.");
            Check(!Descendants<CheckBox>(battlePage).Single(control => control.Text == "Enable ATB preview").Enabled,
                "An absent optional ATB component still exposed its enable control.");
            Passed("Native Unite control stages per-game settings while absent optional ATB controls remain disabled");

            // Metadata-only fixture: never loaded into the game or activated as a reviewed ATB build.
            string atbFixture = Path.Combine(plugins, "SuikodenRandomizer.S1BattlePreview.dll");
            File.Copy(type.Assembly.Location, atbFixture);
            Call(form, "RebuildPages");
            battlePage = tabs.TabPages.Cast<TabPage>().Single(page => page.Text == "Battle & ATB");
            var atbSwitch = Descendants<CheckBox>(battlePage).Single(control => control.Text == "Enable ATB preview");
            Check(atbSwitch.Enabled && !atbSwitch.Checked, "Installed optional ATB did not start with an enabled control and disabled setting.");
            atbSwitch.Checked = true;
            var activeAtb = Descendants<CheckBox>(battlePage).Single(control => control.Text == "Schedule actions with SPD meters");
            activeAtb.Checked = true;
            Check(workspace.Validate().Any(error => error.Contains("standard turn-based", StringComparison.Ordinal)), "Actual ATB controls did not detect the S1 Unite incompatibility.");
            uniteSwitch = Descendants<CheckBox>(battlePage).Single(control => control.Text == "Enable shuffled-character Unite attacks");
            uniteSwitch.Checked = false;
            Check(workspace.Validate().Count == 0, "Turning off S1 Unites did not make the optional ATB configuration valid.");
            atbSwitch.Checked = false;
            activeAtb.Checked = false;
            uniteSwitch.Checked = true;
            File.Delete(atbFixture);
            Passed("Installed optional ATB controls expose and resolve the native Unite incompatibility");

            seed.Text = "";
            Check(workspace.Get("s1.seed") == "" && workspace.Validate().Count > 0, "An empty intermediate seed was not staged for later validation.");
            seed.Text = "edited-first-seed";
            Check(workspace.Get("s1.seed") == "edited-first-seed" && workspace.Validate().Count == 0, "Valid seed edit did not recover.");
            Check(form.Text == versionedTitle + " *", "Dirty status reverted the title to a stale version.");
            Passed("Seed editing tolerates an empty intermediate value and recovers before Apply");

            var characters = tabs.TabPages.Cast<TabPage>().Single(page => page.Text == "Characters");
            var chosen = Descendants<RadioButton>(characters).Single(control => control.Text.Contains("choose five", StringComparison.Ordinal));
            chosen.Checked = true;
            var roster = Descendants<GroupBox>(characters).Single(group => group.Text == "Fixed roster companions");
            var companions = Descendants<ComboBox>(roster).ToArray();
            Check(companions.Length == 5 && roster.Enabled, "Fixed roster controls did not enable.");
            for (int index = 0; index < companions.Length; index++)
            {
                companions[index].SelectedIndex = index;
                int stagedCount = workspace.Get("s1.companions").Split(',', StringSplitOptions.RemoveEmptyEntries).Length;
                Check(stagedCount == index + 1, "Partial roster edits were rejected or incorrectly serialized.");
            }
            Check(workspace.Get("s1.mode") == "FixedChosen" && workspace.Validate().Count == 0, "Five valid companion selections were not accepted.");
            Check(EquipmentChoice(tabs).Enabled && SelectedWeaponPolicy(EquipmentChoice(tabs)) == "Donor",
                "Fixed roster mode disabled or reset the first-join inventory choice.");
            string rosterValue = workspace.Get("s1.companions");
            Passed("Five fixed companions can be selected through intermediate incomplete states");

            gameChoice.SelectedIndex = 1;
            Check(seed.Text == "second-seed", "Changing to S2 showed S1's seed.");
            Check(WeaponChoice(tabs).Enabled && WeaponChoice(tabs).Items.Count == 3, "S2 joining weapon choices did not include all three supported policies.");
            Check(SelectedWeaponPolicy(WeaponChoice(tabs)) == "RiouVariance", "S2 did not default to Riou's weapon policy.");
            Check(((KeyValuePair<string, string>)WeaponChoice(tabs).SelectedItem!).Value == "Riou's current weapon level ±3 (default)",
                "The S2 default does not explain the Riou weapon reference and variance.");
            Check(EquipmentChoice(tabs).Enabled && SelectedWeaponPolicy(EquipmentChoice(tabs)) == "MatchRole",
                "S2 inventory default inherited S1's staged replacement choice.");
            battlePage = tabs.TabPages.Cast<TabPage>().Single(page => page.Text == "Battle & ATB");
            uniteSwitch = Descendants<CheckBox>(battlePage).Single(control => control.Text == "Enable shuffled-character Unite attacks");
            Check(uniteSwitch.Enabled && !uniteSwitch.Checked, "S2 Unite control inherited S1's value or was disabled with S1 ATB.");
            uniteSwitch.Checked = true;
            Check(workspace.GetBoolean("s2.unites") && workspace.GetBoolean("s1.unites"), "Unite switches did not preserve independent per-game settings.");
            seed.Text = "edited-second-seed";
            gameChoice.SelectedIndex = 0;
            Check(seed.Text == "edited-first-seed" && workspace.Get("s1.companions") == rosterValue,
                "Changing games lost S1 seed or roster edits.");
            Check(SelectedWeaponPolicy(WeaponChoice(tabs)) == "Level1", "Changing games lost S1's joining weapon selection.");
            Check(SelectedWeaponPolicy(EquipmentChoice(tabs)) == "Donor", "Changing games lost S1's joining inventory selection.");
            gameChoice.SelectedIndex = 1;
            Check(seed.Text == "edited-second-seed", "Changing back to S2 lost its seed edit.");
            Passed("Switching games preserves each game's staged seed and roster");

            tabs.SelectedTab = tabs.TabPages.Cast<TabPage>().Single(page => page.Text == "Advanced");
            var advanced = Field<DataGridView>(form, "_advanced");
            var row = advanced.Rows.Cast<DataGridViewRow>().Single(candidate => candidate.Tag is ValueTuple<string, string, string> field
                && field.Item1 == SettingsCatalog.S2File && field.Item2 == "Shuffle" && field.Item3 == "Seed");
            Check(row.Cells[3].Value?.ToString() == "edited-second-seed", "Advanced grid did not refresh dedicated control changes.");
            EditCell(advanced, row, "second-from-advanced");
            var weaponRow = advanced.Rows.Cast<DataGridViewRow>().Single(candidate => candidate.Tag is ValueTuple<string, string, string> field
                && field.Item1 == SettingsCatalog.S2File && field.Item2 == "Shuffle" && field.Item3 == "JoiningWeaponLevel");
            EditCell(advanced, weaponRow, "Level1");
            var equipmentRow = advanced.Rows.Cast<DataGridViewRow>().Single(candidate => candidate.Tag is ValueTuple<string, string, string> field
                && field.Item1 == SettingsCatalog.S2File && field.Item2 == "Shuffle" && field.Item3 == "JoiningEquipment");
            EditCell(advanced, equipmentRow, "Donor");
            Check(workspace.Get("s2.seed") == "second-from-advanced", "Advanced edit was not staged in the document.");
            tabs.SelectedTab = tabs.TabPages.Cast<TabPage>().Single(page => page.Text == "Characters");
            Check(seed.Text == "second-from-advanced", "Leaving Advanced did not refresh the seed control.");
            Check(SelectedWeaponPolicy(WeaponChoice(tabs)) == "Level1", "Advanced did not refresh the joining weapon control.");
            Check(SelectedWeaponPolicy(EquipmentChoice(tabs)) == "Donor", "Advanced did not refresh the first-join inventory control.");
            EquipmentChoice(tabs).SelectedIndex = 0;
            Check(workspace.Get("s2.joiningEquipment") == "MatchRole", "S2 original-role inventory was not staged after an Advanced edit.");
            gameChoice.SelectedIndex = 0;
            Check(seed.Text == "edited-first-seed", "Advanced S2 edit changed the S1 seed.");
            Passed("Advanced values and dedicated controls synchronize in both directions");

            tabs.SelectedTab = tabs.TabPages.Cast<TabPage>().Single(page => page.Text == "Advanced");
            advanced = Field<DataGridView>(form, "_advanced");
            var custom = advanced.Rows.Cast<DataGridViewRow>().Single(candidate => candidate.Tag is ValueTuple<string, string, string> field
                && field.Item1 == SettingsCatalog.S1File && field.Item2 == "Custom" && field.Item3 == "Unrelated");
            EditCell(advanced, custom, "edited unknown key");
            Call(form, "ApplySettings", false);
            Check(!workspace.IsDirty, "Applying valid UI settings did not finish successfully.");
            Check(form.Text == versionedTitle, "Successful Apply did not restore the versioned clean title.");
            string savedS1 = File.ReadAllText(Path.Combine(config, SettingsCatalog.S1File));
            string savedS2 = File.ReadAllText(Path.Combine(config, SettingsCatalog.S2File));
            Check(savedS1.Contains("# S1 comment must survive\r\n", StringComparison.Ordinal)
                && savedS1.Contains("Seed = edited-first-seed", StringComparison.Ordinal)
                && savedS1.Contains("Companions = " + rosterValue, StringComparison.Ordinal)
                && savedS1.Contains("Unrelated = edited unknown key", StringComparison.Ordinal), "Saved S1 config lost staged settings or comments.");
            Check(savedS2.Contains("Seed = second-from-advanced", StringComparison.Ordinal), "Saved S2 config lost the Advanced edit.");
            Check(IniDocument.Parse(savedS1).Get("Treasure", "Enabled") == "true"
                && IniDocument.Parse(savedS1).Get("Treasure", "ShuffleChests") == "false",
                "Apply must clear an old global pause while preserving the unchecked new-campaign default.");
            Check(savedS1.Contains("JoiningWeaponLevel = Level1", StringComparison.Ordinal)
                && savedS2.Contains("JoiningWeaponLevel = Level1", StringComparison.Ordinal), "Joining weapon policies were not saved for both games.");
            Check(savedS1.Contains("JoiningEquipment = Donor", StringComparison.Ordinal)
                && savedS2.Contains("JoiningEquipment = MatchRole", StringComparison.Ordinal), "First-join inventory policies were not saved independently.");
            string backupRoot = Path.Combine(config, "StarsOfDestiny.Launcher", "backups");
            string backup = Directory.GetDirectories(backupRoot).Single();
            Check(File.ReadAllText(Path.Combine(backup, SettingsCatalog.S1File)) == originalS1, "UI Apply did not create the exact pre-edit backup.");
            Check(File.Exists(preference), "UI Apply did not remember the selected game folder.");
            Passed("Actual Apply writes the staged files, preserves comments and creates original backups");

            Console.WriteLine(passed + " actual WinForms launcher checks passed.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
        finally
        {
            form?.Dispose();
            if (originalPreference is null) File.Delete(preference);
            else File.WriteAllBytes(preference, originalPreference);
            Directory.Delete(game, true);
        }

        void Passed(string name) { passed++; Console.WriteLine("PASS " + name); }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static T Field<T>(Form form, string name) =>
        (T)form.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;

    private static void Call(Form form, string name, params object?[] arguments)
    {
        try { form.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, arguments); }
        catch (TargetInvocationException error) { throw error.InnerException ?? error; }
    }

    private static IEnumerable<T> Descendants<T>(Control root) where T : Control
    {
        foreach (Control control in root.Controls)
        {
            if (control is T match) yield return match;
            foreach (var descendant in Descendants<T>(control)) yield return descendant;
        }
    }

    private static ComboBox WeaponChoice(TabControl tabs) => Descendants<ComboBox>(
        Descendants<GroupBox>(tabs.TabPages.Cast<TabPage>().Single(page => page.Text == "Characters"))
            .Single(group => group.Text.StartsWith("Joining weapon level", StringComparison.Ordinal))).Single();

    private static ComboBox EquipmentChoice(TabControl tabs) => Descendants<ComboBox>(
        Descendants<GroupBox>(tabs.TabPages.Cast<TabPage>().Single(page => page.Text == "Characters"))
            .Single(group => group.Text.StartsWith("Joining inventory & equipment", StringComparison.Ordinal))).Single();

    private static string SelectedWeaponPolicy(ComboBox choice) => choice.SelectedItem is KeyValuePair<string, string> selected
        ? selected.Key : throw new InvalidOperationException("The joining weapon policy has no selected value.");

    private static void EditCell(DataGridView grid, DataGridViewRow row, string value)
    {
        row.Cells[3].Value = value;
        typeof(DataGridView).GetMethod("OnCellEndEdit", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(grid, new object[] { new DataGridViewCellEventArgs(3, row.Index) });
    }
}
