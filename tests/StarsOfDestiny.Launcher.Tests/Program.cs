using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using StarsOfDestiny.Launcher.Core;

int passed = 0;
string root = Path.Combine(Path.GetTempPath(), "stars-launcher-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);

void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
void Reject(Action action)
{
    try { action(); }
    catch (Exception error) when (error is ArgumentException or IOException or InvalidOperationException or UnauthorizedAccessException) { return; }
    throw new InvalidOperationException("Expected operation to be rejected.");
}
void Case(string name, Action run)
{
    run();
    passed++;
    Console.WriteLine("PASS " + name);
}
string NewGame()
{
    string game = Path.Combine(root, Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(Path.Combine(game, "BepInEx", "config"));
    string plugins = Path.Combine(game, "BepInEx", "plugins");
    Directory.CreateDirectory(plugins);
    // Metadata-only supported-host fixture; never executed or loaded as a game plugin.
    File.Copy(typeof(object).Assembly.Location, Path.Combine(plugins, "SuikodenRandomizer.dll"));
    return game;
}
string Config(string game, string name) => Path.Combine(game, "BepInEx", "config", name);

try
{
    Case("INI read and render retain exact comments, unknown values, mixed newlines and no final newline", () =>
    {
        string text = "# settings\r\n[Shuffle]\r\nSeed = test\nUnknown = literal # text\r\n; trailing";
        var ini = IniDocument.Parse(text);
        Check(ini.Render() == text, "Read changed the original INI.");
        Check(ini.Get("Shuffle", "Seed") == "test" && ini.Get("shuffle", "seed") == "", "INI key lookup must match BepInEx casing.");
    });

    Case("Updating a setting retains original spacing, case, comments and section boundaries", () =>
    {
        var ini = IniDocument.Parse("# untouched\r\n[Shuffle]\r\n  Seed  =   old  \r\nOther = keep\r\n\r\n[Start]\r\nPoint = Opening\r\n");
        ini.Set("Shuffle", "Seed", "new");
        Check(ini.Render() == "# untouched\r\n[Shuffle]\r\n  Seed  =   new  \r\nOther = keep\r\n\r\n[Start]\r\nPoint = Opening\r\n", "Setting update changed unrelated bytes.");
        ini.Set("Shuffle", "Mode", "Shuffle");
        Check(ini.Render().Contains("Other = keep\r\nMode = Shuffle\r\n\r\n[Start]", StringComparison.Ordinal), "Missing key was inserted into the wrong section.");
        ini.Set("Treasure", "Enabled", "false");
        Check(ini.Get("Treasure", "Enabled") == "false", "New section value is missing.");
    });

    Case("INI rejects duplicate settings and value injection", () =>
    {
        var duplicate = IniDocument.Parse("[Shuffle]\nSeed=a\nSeed=b\n");
        Reject(() => duplicate.Get("Shuffle", "Seed"));
        Reject(() => duplicate.Set("Shuffle", "Seed", "one"));
        Reject(() => IniDocument.Parse("").Set("Shuffle", "Seed", "a\n[Cheat]\nMoneyMultiplier=100"));
        Reject(() => IniDocument.Parse("").Set("Bad]", "Key", "x"));
    });

    Case("Repeated sections and case-sensitive keys match the game's lookup", () =>
    {
        var ini = IniDocument.Parse("[Shuffle]\nseed=unused\n[Start]\nPoint=Opening\n[Shuffle]\nSeed=old\n");
        ini.Set("Shuffle", "Seed", "new");
        Check(ini.Render().EndsWith("[Shuffle]\nSeed=new\n", StringComparison.Ordinal)
            && ini.Get("Shuffle", "seed") == "unused" && ini.Get("Shuffle", "Seed") == "new",
            "An edit created a duplicate in an earlier section or changed a different key.");
    });

    Case("Known preset keys are canonicalized and unknown keys retain their exact case", () =>
    {
        string game = NewGame();
        var workspace = SettingsWorkspace.Load(game, () => false);
        string Preset(string text) => System.Text.Json.JsonSerializer.Serialize(new {
            schema = "stars-of-destiny-settings-v1",
            configFiles = new System.Collections.Generic.Dictionary<string, string> { [SettingsCatalog.S1File] = text }
        });
        workspace.StagePreset(Preset("[shuffle]\nseed=literal\n[Custom]\nX=1\nx=2"));
        var doc = workspace.Documents[SettingsCatalog.S1File];
        Check(workspace.Get("s1.seed") == "literal" && doc.Get("shuffle", "seed") == ""
            && doc.Get("Custom", "X") == "1" && doc.Get("Custom", "x") == "2", "Incorrect preset casing.");
        try { workspace.StagePreset(Preset("[Shuffle]\nSeed=a\nseed=b")); throw new Exception("Duplicate aliases accepted."); }
        catch (InvalidDataException) { }
        Check(workspace.Get("s1.seed") == "literal", "Rejected preset changed a staged seed.");
    });

    Case("Launcher seeds match the BepInEx string reader and survive its next config save", () =>
    {
        string game = NewGame();
        var workspace = SettingsWorkspace.Load(game, () => false);
        foreach (string seed in new[] { @"a\question", @"C:\test", "Viktor's \"rune\"", @"end\" })
        {
            workspace.Set("s1.seed", seed);
            workspace.Save();
            Check(workspace.Get("s1.seed") == seed, "Raw escape spelling changed the effective seed.");
        }
        foreach (string seed in new[] { @"a\nb", @"a\\b", @"a\t", "tab\t" })
            Reject(() => workspace.Set("s1.seed", seed));
        Reject(() => workspace.Set("s2.seed", @"star\moon"));
        var def = SettingsCatalog.Find("s1.seed");
        workspace.StageValue(def, @"literal\n");
        Reject(() => workspace.Save());
        Check(workspace.Get("s1.seed") == @"literal\n" && workspace.IsDirty, "Rejected seed edit was lost.");
        workspace.Reload();
        File.WriteAllText(Config(game, SettingsCatalog.S1File), "[Shuffle]\nSeed = Viktor\\'s rune\n");
        workspace.Reload();
        Check(workspace.Get("s1.seed") == "Viktor's rune" && workspace.Validate().Count == 0,
            "An existing BepInEx-serialized seed did not retain its meaning.");
    });

    Case("Two launcher windows validate disjoint changes against the committed settings", () =>
    {
        string game = NewGame();
        using var staging = new ManualResetEventSlim();
        using var finish = new ManualResetEventSlim();
        int checks = 0;
        var first = SettingsWorkspace.Load(game, () =>
        {
            if (++checks == 2)
            {
                staging.Set();
                if (!finish.Wait(TimeSpan.FromSeconds(15))) throw new TimeoutException();
            }
            return false;
        });
        var second = SettingsWorkspace.Load(game, () => false);
        first.SetBoolean("s1.unites", true);
        second.SetBoolean("atb.Enabled", true);
        second.SetBoolean("atb.ActiveAtb", true);
        var saving = Task.Run(() => first.Save());
        try
        {
            Check(staging.Wait(TimeSpan.FromSeconds(15)), "First window failed to stage its change.");
            Reject(() => second.Save());
        }
        finally { finish.Set(); saving.GetAwaiter().GetResult(); }
        Reject(() => second.Save());
        Check(!File.Exists(Config(game, SettingsCatalog.AtbFile)) && second.IsDirty && second.GetBoolean("atb.ActiveAtb")
            && second.GetBoolean("s1.unites"), "Concurrent saves committed a conflict or discarded staged edits.");
        second.SetBoolean("s1.unites", false);
        second.Save();
        Check(second.Validate().Count == 0 && !second.GetBoolean("s1.unites") && second.GetBoolean("atb.ActiveAtb"),
            "The user could not resolve the conflict and retry.");
    });

    Case("Workspace load and no-op save never create default configuration files", () =>
    {
        string game = NewGame();
        var workspace = SettingsWorkspace.Load(game, () => false);
        Check(workspace.Get("s1.seed") == "agilen-080" && !workspace.IsDirty, "Missing defaults changed state.");
        var saved = workspace.Save();
        Check(saved.ChangedFiles.Count == 0 && saved.BackupDirectory is null, "No-op save made a backup.");
        Check(Directory.GetFiles(Path.Combine(game, "BepInEx", "config"), "*", SearchOption.AllDirectories).Length == 0,
            "No-op save created a file.");
    });

    Case("Treasure apply resumes legacy pauses without changing new-campaign choices or saved policies", () =>
    {
        foreach (bool shuffle in new[] { false, true })
        {
            string game = NewGame();
            string text = "# legacy treasure settings\r\n[Treasure]\r\nEnabled = false\r\nShuffleChests = "
                + (shuffle ? "true" : "false") + "\r\nUnknown = retained\r\n";
            byte[] original = new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes(text)).ToArray();
            File.WriteAllBytes(Config(game, SettingsCatalog.S1File), original);
            string save = Path.Combine(game, "Save", "slot18.sav");
            string sidecar = Path.Combine(game, "BepInEx", "config", "avatar-shuffle", "treasure-save-bindings", "snapshot.json");
            Directory.CreateDirectory(Path.GetDirectoryName(save)!);
            Directory.CreateDirectory(Path.GetDirectoryName(sidecar)!);
            byte[] nativeBytes = { 1, 2, 3, 255 };
            byte[] policyBytes = Encoding.UTF8.GetBytes("{\"enabled\":true,\"policy\":\"retained\"}\n");
            File.WriteAllBytes(save, nativeBytes);
            File.WriteAllBytes(sidecar, policyBytes);
            var workspace = SettingsWorkspace.Load(game, () => false);
            Check(!workspace.IsDirty && !workspace.GetBoolean("s1.treasureEnabled"), "Loading changed the legacy pause.");
            workspace.PrepareTreasureSettingsForApply();
            Check(workspace.GetBoolean("s1.treasureEnabled") && workspace.GetBoolean("s1.chests") == shuffle,
                "Preparation changed the new-campaign choice or left the global pause active.");
            var saved = workspace.SaveChanges();
            Check(saved.ChangedFiles.SequenceEqual(new[] { SettingsCatalog.S1File }) && saved.BackupDirectory is not null,
                "Treasure migration did not save exactly the S1 config.");
            Check(File.ReadAllBytes(Path.Combine(saved.BackupDirectory!, SettingsCatalog.S1File)).SequenceEqual(original),
                "The original paused config was not backed up exactly.");
            byte[] expected = new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes(
                text.Replace("Enabled = false", "Enabled = true", StringComparison.Ordinal))).ToArray();
            Check(File.ReadAllBytes(Config(game, SettingsCatalog.S1File)).SequenceEqual(expected),
                "Migration changed comments, encoding or unrelated configuration.");
            Check(File.ReadAllBytes(save).SequenceEqual(nativeBytes) && File.ReadAllBytes(sidecar).SequenceEqual(policyBytes),
                "Applying the launcher default rewrote a save or its stored chest policy.");
            Check(!workspace.IsDirty, "Successful migration left the workspace dirty.");
        }
    });

    Case("Treasure apply preparation does not create a missing S1 config on no-op or S2-only saves", () =>
    {
        string game = NewGame();
        var workspace = SettingsWorkspace.Load(game, () => false);
        workspace.PrepareTreasureSettingsForApply();
        Check(!workspace.IsDirty && workspace.SaveChanges().ChangedFiles.Count == 0,
            "A missing treasure gate staged default configuration.");
        Check(!File.Exists(Config(game, SettingsCatalog.S1File)), "No-op preparation created S1 settings.");
        workspace.Set("s2.seed", "s2-only-edit");
        workspace.PrepareTreasureSettingsForApply();
        var saved = workspace.SaveChanges();
        Check(saved.ChangedFiles.SequenceEqual(new[] { SettingsCatalog.S2File })
            && !File.Exists(Config(game, SettingsCatalog.S1File)), "An S2-only edit created S1 settings.");
    });

    Case("Treasure apply handles a paused gate staged through presets or the advanced document editor", () =>
    {
        string game = NewGame();
        var workspace = SettingsWorkspace.Load(game, () => false);
        workspace.Documents[SettingsCatalog.S1File].Set("Treasure", "Enabled", "false");
        workspace.Documents[SettingsCatalog.S1File].Set("Treasure", "ShuffleChests", "true");
        workspace.PrepareTreasureSettingsForApply();
        workspace.SaveChanges();
        var reloaded = SettingsWorkspace.Load(game, () => false);
        Check(reloaded.GetBoolean("s1.treasureEnabled") && reloaded.GetBoolean("s1.chests"),
            "A staged legacy preset silently kept chest replacement paused.");
    });

    Case("Treasure preparation leaves invalid gate values for validation rejection", () =>
    {
        string game = NewGame();
        string original = "[Treasure]\nEnabled = invalid\nShuffleChests = true\n";
        File.WriteAllText(Config(game, SettingsCatalog.S1File), original);
        var workspace = SettingsWorkspace.Load(game, () => false);
        workspace.PrepareTreasureSettingsForApply();
        Check(!workspace.IsDirty && workspace.Validate().Any(error => error.StartsWith("s1.treasureEnabled:", StringComparison.Ordinal)),
            "Preparation masked an invalid setting.");
        Reject(() => workspace.SaveChanges());
        Check(File.ReadAllText(Config(game, SettingsCatalog.S1File)) == original, "Invalid configuration was overwritten.");
    });

    Case("Raw workspace saves retain their existing no-op semantics for legacy treasure pauses", () =>
    {
        string game = NewGame();
        string original = "[Treasure]\nEnabled=false\nShuffleChests=false\n";
        File.WriteAllText(Config(game, SettingsCatalog.S1File), original);
        var workspace = SettingsWorkspace.Load(game, () => false);
        Check(workspace.SaveChanges().ChangedFiles.Count == 0 && File.ReadAllText(Config(game, SettingsCatalog.S1File)) == original,
            "Raw SaveChanges normalized configuration without explicit apply preparation.");
    });

    Case("Save commits only changed documents and backs up exact original UTF-8 BOM bytes", () =>
    {
        string game = NewGame();
        byte[] original = new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes("# keep\r\n[Shuffle]\r\nSeed = old\r\nUnrelated = ✓\r\n")).ToArray();
        File.WriteAllBytes(Config(game, SettingsCatalog.S1File), original);
        var workspace = SettingsWorkspace.Load(game, () => false);
        workspace.Set("s1.seed", "new");
        workspace.SetBoolean("shared.enabled", false);
        var saved = workspace.Save();
        Check(saved.ChangedFiles.Count == 2 && saved.BackupDirectory is not null, "Wrong set of changed files.");
        Check(File.ReadAllBytes(Path.Combine(saved.BackupDirectory!, SettingsCatalog.S1File)).SequenceEqual(original), "Backup differs from original bytes.");
        byte[] current = File.ReadAllBytes(Config(game, SettingsCatalog.S1File));
        Check(current.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }), "UTF-8 BOM was lost.");
        Check(File.ReadAllText(Config(game, SettingsCatalog.S1File)).Contains("Unrelated = ✓\r\n", StringComparison.Ordinal), "Unknown value was lost.");
        Check(!workspace.IsDirty && !File.Exists(Config(game, SettingsCatalog.S2File)), "Save created an untouched file.");
    });

    Case("An external edit rejects the entire transaction before overwriting another file", () =>
    {
        string game = NewGame();
        File.WriteAllText(Config(game, SettingsCatalog.S1File), "[Shuffle]\nSeed=before\n");
        File.WriteAllText(Config(game, SettingsCatalog.SharedFile), "[General]\nEnabled=true\n");
        var workspace = SettingsWorkspace.Load(game, () => false);
        workspace.Set("s1.seed", "after");
        workspace.SetBoolean("shared.enabled", false);
        File.AppendAllText(Config(game, SettingsCatalog.SharedFile), "# external\n");
        Reject(() => workspace.Save());
        Check(File.ReadAllText(Config(game, SettingsCatalog.S1File)).Contains("Seed=before", StringComparison.Ordinal), "First config was overwritten before conflict rejection.");
        Check(File.ReadAllText(Config(game, SettingsCatalog.SharedFile)).Contains("# external", StringComparison.Ordinal), "External edit was lost.");
    });

    Case("A newly created config conflicts with a missing-file snapshot", () =>
    {
        string game = NewGame();
        var workspace = SettingsWorkspace.Load(game, () => false);
        workspace.SetBoolean("s1.chests", true);
        File.WriteAllText(Config(game, SettingsCatalog.S1File), "# created elsewhere");
        Reject(() => workspace.Save());
        Check(File.ReadAllText(Config(game, SettingsCatalog.S1File)) == "# created elsewhere", "New external file was overwritten.");
    });

    Case("Running-game guard blocks both initial apply and a game starting during staging", () =>
    {
        string game = NewGame();
        var blocked = SettingsWorkspace.Load(game, () => true);
        blocked.SetBoolean("s1.chests", true);
        Reject(() => blocked.Save());
        Check(!File.Exists(Config(game, SettingsCatalog.S1File)), "Running-game guard wrote config.");
        int checks = 0;
        var racing = SettingsWorkspace.Load(game, () => ++checks >= 2);
        racing.SetBoolean("s1.chests", true);
        Reject(() => racing.Save());
        Check(!File.Exists(Config(game, SettingsCatalog.S1File)), "Staging race changed config.");
        Check(Directory.GetFiles(Path.Combine(game, "BepInEx", "config"), "*.tmp", SearchOption.TopDirectoryOnly).Length == 0,
            "Staging failure left a temporary file.");
    });

    Case("Partial multi-file commit failure restores already-published files", () =>
    {
        if (!OperatingSystem.IsWindows()) return;
        string game = NewGame();
        string first = Config(game, SettingsCatalog.S1File);
        string second = Config(game, SettingsCatalog.S2File);
        File.WriteAllText(first, "[Shuffle]\nSeed=first-old\n");
        File.WriteAllText(second, "[Shuffle]\nSeed=second-old\n");
        var workspace = SettingsWorkspace.Load(game, () => false);
        workspace.Set("s1.seed", "first-new");
        workspace.Set("s2.seed", "second-new");
        using (var locked = new FileStream(second, FileMode.Open, FileAccess.Read, FileShare.Read)) Reject(() => workspace.Save());
        Check(File.ReadAllText(first).Contains("first-old", StringComparison.Ordinal), "First commit was not rolled back.");
        Check(File.ReadAllText(second).Contains("second-old", StringComparison.Ordinal), "Locked original was changed.");
        Check(workspace.IsDirty, "Failed transaction discarded staged settings.");
        workspace.Save();
        Check(File.ReadAllText(first).Contains("first-new", StringComparison.Ordinal), "Retry did not commit.");
    });

    Case("Seed normalization and fixed roster validation use the actual game contracts", () =>
    {
        string game = NewGame();
        var workspace = SettingsWorkspace.Load(game, () => false);
        workspace.Set("s1.seed", "  Cafe\u0301  ");
        Check(workspace.Get("s1.seed") == "Café", "Seed was not normalized.");
        Reject(() => workspace.Set("s1.seed", " "));
        Reject(() => workspace.Set("s2.seed", new string('x', 129)));
        Reject(() => workspace.Set("s1.seed", "seed\0"));
        Reject(() => workspace.Set("s1.companions", "11,11,12,13,14"));
        Reject(() => workspace.Set("s2.companions", "251,252,253,254,255"));
        workspace.Set("s1.mode", "FixedChosen");
        Reject(() => workspace.Save());
        workspace.Set("s1.companions", "11,12,13,14,15");
        workspace.Save();
    });

    Case("Direct generic document edits still undergo known-setting validation", () =>
    {
        string game = NewGame();
        var workspace = SettingsWorkspace.Load(game, () => false);
        workspace.Documents[SettingsCatalog.S1File].Set("Shuffle", "Seed", "");
        Reject(() => workspace.Save());
        workspace.Documents[SettingsCatalog.S1File].Set("Shuffle", "Seed", "okay");
        workspace.Documents[SettingsCatalog.FixFile].Set("Misc", "SaveSlots", "3");
        Reject(() => workspace.Save());
        workspace.Set("fix.SaveSlots", "18");
        Reject(() => workspace.Set("fix.ExperienceMultiplier", "NaN"));
        workspace.Save();
    });

    Case("Joining weapon policies default safely and save independently for both games", () =>
    {
        string game = NewGame();
        File.WriteAllText(Config(game, SettingsCatalog.S1File), "# keep this comment\n[Shuffle]\nSeed = first\nUnknown = retain\n");
        var workspace = SettingsWorkspace.Load(game, () => false);
        Check(workspace.Get("s1.joiningWeaponLevel") == "TirVariance" && workspace.Get("s2.joiningWeaponLevel") == "RiouVariance",
            "The launcher defaults do not match each game's native joining weapon policy.");
        Check(!workspace.IsDirty, "Reading the policy defaults changed configuration.");
        workspace.Set("s1.joiningWeaponLevel", "Level1");
        workspace.Set("s2.joiningWeaponLevel", "matchrole");
        Reject(() => workspace.Set("s1.joiningWeaponLevel", "13"));
        workspace.Save();
        var restored = SettingsWorkspace.Load(game, () => false);
        Check(restored.Get("s1.joiningWeaponLevel") == "Level1" && restored.Get("s2.joiningWeaponLevel") == "MatchRole",
            "The per-game policies did not persist.");
        Check(File.ReadAllText(Config(game, SettingsCatalog.S1File)).Contains("# keep this comment\n", StringComparison.Ordinal)
            && File.ReadAllText(Config(game, SettingsCatalog.S1File)).Contains("Unknown = retain\n", StringComparison.Ordinal),
            "Adding a joining weapon policy lost unrelated configuration.");
        restored.Documents[SettingsCatalog.S2File].Set("Shuffle", "JoiningWeaponLevel", "Unsupported");
        Reject(() => restored.Save());
    });

    Case("Published S1 TirVariance configs remain editable while S2 rejects that policy", () =>
    {
        string game = NewGame();
        File.WriteAllText(Config(game, SettingsCatalog.S1File), "[Shuffle]\nJoiningWeaponLevel = TirVariance\nSeed = published\n");
        var workspace = SettingsWorkspace.Load(game, () => false);
        workspace.Set("s1.seed", "next-campaign");
        workspace.Save();
        Check(workspace.Get("s1.joiningWeaponLevel") == "TirVariance", "Saving another field changed the existing S1 weapon policy.");
        Reject(() => workspace.Set("s2.joiningWeaponLevel", "TirVariance"));
        Reject(() => workspace.Set("s1.joiningWeaponLevel", "RiouVariance"));
        Check(workspace.Get("shared.view") == "Hidden" && !workspace.GetBoolean("atb.Enabled"), "Preview defaults exposed the overlay or enabled ATB.");
    });

    Case("Recruitment equipment defaults do not materialize missing settings or migrate legacy choices", () =>
    {
        string game = NewGame();
        string original = "# legacy equipment choice\n[Shuffle]\nJoiningEquipment = Donor\nUnknown = retain\n";
        File.WriteAllText(Config(game, SettingsCatalog.S1File), original);
        var workspace = SettingsWorkspace.Load(game, () => false);
        Check(workspace.Get("s1.joiningEquipment") == "Donor" && workspace.Get("s2.joiningEquipment") == "MatchRole",
            "Explicit legacy equipment or the new-campaign default was lost.");
        Check(!workspace.IsDirty, "Reading equipment defaults staged a config write.");
        workspace.Set("s1.seed", "keep-donor-equipment");
        workspace.Save();
        Check(workspace.Get("s1.joiningEquipment") == "Donor" && !File.Exists(Config(game, SettingsCatalog.S2File)),
            "An unrelated edit migrated an equipment choice or wrote the other game's defaults.");
        Check(File.ReadAllText(Config(game, SettingsCatalog.S1File)).StartsWith(original, StringComparison.Ordinal),
            "The explicit legacy policy or unknown configuration was altered.");
        Reject(() => workspace.Set("s1.joiningEquipment", "Level1"));
        Reject(() => workspace.Set("s2.joiningEquipment", "UseOverpoweredGear"));
    });

    Case("Both recruitment equipment choices roundtrip independently in all three modes", () =>
    {
        string game = NewGame();
        var workspace = SettingsWorkspace.Load(game, () => false);
        workspace.Set("s1.companions", "11,12,13,14,15");
        workspace.Set("s2.companions", string.Join(",", SuikodenRandomizer.Core.Suikoden2.Gsd2CharacterCatalog.FixedCompanionIds.Take(5)));
        foreach (string mode in new[] { "Shuffle", "FixedChosen", "FixedRandom" })
        {
            workspace.Set("s1.mode", mode);
            workspace.Set("s2.mode", mode);
            foreach (string policy in new[] { "matchrole", "Donor" })
            {
                workspace.Set("s1.joiningEquipment", policy);
                workspace.Set("s2.joiningEquipment", policy == "Donor" ? "MatchRole" : "Donor");
                workspace.Save();
                workspace.Reload();
                Check(workspace.Get("s1.mode") == mode && workspace.Get("s2.mode") == mode, "An equipment edit changed the character mode.");
                Check(workspace.Get("s1.joiningEquipment") == (policy == "Donor" ? "Donor" : "MatchRole")
                    && workspace.Get("s2.joiningEquipment") == (policy == "Donor" ? "MatchRole" : "Donor"),
                    "Equipment choices did not persist independently across modes and reloads.");
                Check(workspace.Get("s1.joiningWeaponLevel") == "TirVariance" && workspace.Get("s2.joiningWeaponLevel") == "RiouVariance",
                    "Recruitment equipment changed the separate joining-weapon defaults.");
            }
        }
    });

    Case("Advanced and imported original-role equipment requests reject older hosts before backups or writes", () =>
    {
        string game = NewGame();
        string host = Path.Combine(game, "BepInEx", "plugins", "SuikodenRandomizer.dll");
        File.Copy(typeof(SettingsWorkspace).Assembly.Location, host, true);
        Check(Version.Parse(GameInstallation.Inspect(game).HostVersion!) < GameInstallation.JoiningEquipmentMinimumVersion,
            "Unsupported host fixture must be older than recruitment equipment support.");
        File.WriteAllText(Config(game, SettingsCatalog.S1File), "[Shuffle]\nSeed = original\n");
        File.WriteAllText(Config(game, SettingsCatalog.S2File), "[Shuffle]\nSeed = original-s2\nJoiningWeaponLevel = MatchRole\n");
        var workspace = SettingsWorkspace.Load(game, () => false);
        workspace.Set("s1.seed", "legacy-unrelated-edit");
        workspace.Save();
        Check(!File.ReadAllText(Config(game, SettingsCatalog.S1File)).Contains("JoiningEquipment", StringComparison.Ordinal),
            "An older host's unrelated edit materialized the unsupported equipment option.");
        string[] before = SettingsCatalog.FileNames.Select(name => File.Exists(Config(game, name)) ? File.ReadAllText(Config(game, name)) : "<missing>").ToArray();
        int backups = Directory.GetDirectories(Path.Combine(game, "BepInEx", "config", "StarsOfDestiny.Launcher", "backups")).Length;
        foreach (string id in new[] { "s1.joiningEquipment", "s2.joiningEquipment" })
        {
            var setting = SettingsCatalog.Find(id);
            workspace.Documents[setting.FileName].Set(setting.Section, setting.Key, "MatchRole");
            Reject(() => workspace.Save());
            Check(SettingsCatalog.FileNames.Select(name => File.Exists(Config(game, name)) ? File.ReadAllText(Config(game, name)) : "<missing>").SequenceEqual(before),
                "Unsupported Advanced/preset equipment partially committed configuration.");
            Check(Directory.GetDirectories(Path.Combine(game, "BepInEx", "config", "StarsOfDestiny.Launcher", "backups")).Length == backups,
                "Unsupported equipment created a backup before compatibility rejection.");
            workspace.Reload();
        }
        workspace.Set("s1.joiningEquipment", "Donor");
        workspace.Set("s2.joiningEquipment", "Donor");
        workspace.Save();
        Check(workspace.Get("s1.joiningEquipment") == "Donor" && workspace.Get("s2.joiningEquipment") == "Donor",
            "The legacy-compatible equipment option was blocked.");
    });

    Case("Equipment presets retain both choices and unknown config while applying through the normal transaction", () =>
    {
        string source = NewGame();
        var workspace = SettingsWorkspace.Load(source, () => false);
        workspace.Set("s1.joiningEquipment", "Donor");
        workspace.Set("s2.joiningEquipment", "MatchRole");
        workspace.Documents[SettingsCatalog.S1File].Set("Custom", "Unrelated", "preserved");
        string preset = System.Text.Json.JsonSerializer.Serialize(new { schema = "stars-of-destiny-settings-v1",
            configFiles = workspace.Documents.ToDictionary(pair => pair.Key, pair => pair.Value.Render()) });
        string target = NewGame();
        var imported = SettingsWorkspace.Load(target, () => false);
        imported.StagePreset(preset);
        imported.Save();
        Check(imported.Get("s1.joiningEquipment") == "Donor" && imported.Get("s2.joiningEquipment") == "MatchRole",
            "Preset serialization or generic document application discarded recruitment gear choices.");
        Check(imported.Documents[SettingsCatalog.S1File].Get("Custom", "Unrelated") == "preserved",
            "Import discarded an unknown config field.");
    });

    Case("Partial presets validate the combined settings and stage every file together", () =>
    {
        string game = NewGame();
        var workspace = SettingsWorkspace.Load(game, () => false);
        workspace.Set("s1.seed", "unsaved-edit");
        workspace.SetBoolean("atb.Enabled", true);
        workspace.SetBoolean("ctb.Enabled", true);
        string Preset(string battleText) => System.Text.Json.JsonSerializer.Serialize(new {
            schema = "stars-of-destiny-settings-v1",
            configFiles = new System.Collections.Generic.Dictionary<string, string> {
                [SettingsCatalog.S1File] = "[Shuffle]\nSeed=preset-edit",
                [SettingsCatalog.AtbFile] = battleText
            }
        });
        var before = SettingsCatalog.FileNames.Select(name => workspace.Documents[name].Render()).ToArray();
        try
        {
            workspace.StagePreset(Preset("[Preview]\nEnabled=false"));
            throw new Exception("Preset would leave CTB enabled with the preview disabled.");
        }
        catch (InvalidDataException error) when (error.Message.Contains("CTB requires", StringComparison.Ordinal)) { }
        Check(SettingsCatalog.FileNames.Select(name => workspace.Documents[name].Render()).SequenceEqual(before),
            "Rejected preset lost a pending edit or partially imported an earlier file.");
        workspace.StagePreset(Preset("[CTB]\rEnabled=true\rBreak=false"));
        Check(workspace.Get("s1.seed") == "preset-edit" && workspace.GetBoolean("atb.Enabled")
            && workspace.GetBoolean("ctb.Enabled") && !workspace.GetBoolean("ctb.Break"),
            "Partial import lost an inherited setting or missed an imported value.");
        Check(!File.Exists(Config(game, SettingsCatalog.S1File)) && !File.Exists(Config(game, SettingsCatalog.AtbFile)),
            "Staging a preset wrote a config before Apply.");
    });

    Case("Host changes during staging reject original-role equipment before committing", () =>
    {
        string game = NewGame();
        int runningChecks = 0;
        var workspace = SettingsWorkspace.Load(game, () =>
        {
            if (++runningChecks == 2)
                File.Copy(typeof(SettingsWorkspace).Assembly.Location, Path.Combine(game, "BepInEx", "plugins", "SuikodenRandomizer.dll"), true);
            return false;
        });
        workspace.Set("s1.joiningEquipment", "MatchRole");
        Reject(() => workspace.Save());
        Check(!File.Exists(Config(game, SettingsCatalog.S1File)), "Equipment committed after the host became unsupported.");
        Check(!Directory.EnumerateFiles(Path.Combine(game, "BepInEx", "config"), "*.tmp").Any(), "Rejected equipment left a pending config.");
    });

    Case("S2 RiouVariance and legacy weapon choices roundtrip independently without migrating existing choices", () =>
    {
        string game = NewGame();
        File.WriteAllText(Config(game, SettingsCatalog.S2File), "# old choice\n[Shuffle]\nJoiningWeaponLevel = MatchRole\n");
        var workspace = SettingsWorkspace.Load(game, () => false);
        Check(workspace.Get("s2.joiningWeaponLevel") == "MatchRole" && !workspace.IsDirty, "Loading changed the existing weapon policy.");
        workspace.Set("s2.seed", "legacy-weapon-choice");
        workspace.Save();
        Check(workspace.Get("s2.joiningWeaponLevel") == "MatchRole", "An unrelated edit migrated an existing weapon policy.");
        foreach (string policy in new[] { "riouvariance", "Level1", "MatchRole" })
        {
            workspace.Set("s2.joiningWeaponLevel", policy);
            workspace.Save();
            workspace.Reload();
            Check(workspace.Get("s2.joiningWeaponLevel") == (policy == "riouvariance" ? "RiouVariance" : policy), "S2 weapon choice did not roundtrip.");
            Check(workspace.Get("s1.joiningWeaponLevel") == "TirVariance", "S2 choice changed S1's default.");
        }
        Check(File.ReadAllText(Config(game, SettingsCatalog.S2File)).StartsWith("# old choice\n", StringComparison.Ordinal), "Weapon choice removed comments.");
    });

    Case("Advanced or preset RiouVariance cannot commit to an unsupported host, while legacy choices still work", () =>
    {
        string game = NewGame();
        string host = Path.Combine(game, "BepInEx", "plugins", "SuikodenRandomizer.dll");
        File.Copy(typeof(SettingsWorkspace).Assembly.Location, host, true);
        Check(Version.Parse(GameInstallation.Inspect(game).HostVersion!) < GameInstallation.RiouJoiningWeaponMinimumVersion,
            "Unsupported-host fixture unexpectedly supports RiouVariance.");
        string original = "[Shuffle]\nSeed = old\nJoiningWeaponLevel = MatchRole\n";
        File.WriteAllText(Config(game, SettingsCatalog.S2File), original);
        var workspace = SettingsWorkspace.Load(game, () => false);
        workspace.Set("s1.seed", "first-must-not-commit");
        workspace.Documents[SettingsCatalog.S2File].Set("Shuffle", "JoiningWeaponLevel", "RiouVariance");
        Reject(() => workspace.Save());
        Check(!File.Exists(Config(game, SettingsCatalog.S1File)) && File.ReadAllText(Config(game, SettingsCatalog.S2File)) == original,
            "Unsupported RiouVariance partially committed a preset or Advanced edit.");
        Check(!Directory.Exists(Path.Combine(game, "BepInEx", "config", "StarsOfDestiny.Launcher")), "Unsupported policy created a settings backup before rejection.");
        foreach (string policy in new[] { "Level1", "MatchRole" })
        {
            workspace.Set("s2.joiningWeaponLevel", policy);
            workspace.Save();
            Check(SettingsWorkspace.Load(game, () => false).Get("s2.joiningWeaponLevel") == policy, "A supported legacy policy was blocked on the older host.");
        }
    });

    Case("Missing S2 policy on an older host does not block no-op or S1-only edits", () =>
    {
        string game = NewGame();
        string host = Path.Combine(game, "BepInEx", "plugins", "SuikodenRandomizer.dll");
        File.Copy(typeof(SettingsWorkspace).Assembly.Location, host, true);
        var workspace = SettingsWorkspace.Load(game, () => false);
        Check(workspace.Get("s2.joiningWeaponLevel") == "RiouVariance", "The new S2 default is missing.");
        workspace.Save();
        workspace.Set("s1.seed", "only-s1");
        workspace.Save();
        Check(!File.Exists(Config(game, SettingsCatalog.S2File)), "S1-only edit materialized unsupported S2 defaults.");
        workspace.Set("s2.seed", "s2-needs-new-host");
        Reject(() => workspace.Save());
        Check(!File.Exists(Config(game, SettingsCatalog.S2File)), "S2 edit silently relied on an unsupported native default.");
    });

    Case("A host downgrade during settings staging rejects before committing RiouVariance", () =>
    {
        string game = NewGame();
        int runningChecks = 0;
        var workspace = SettingsWorkspace.Load(game, () =>
        {
            if (++runningChecks == 2)
                File.Copy(typeof(SettingsWorkspace).Assembly.Location, Path.Combine(game, "BepInEx", "plugins", "SuikodenRandomizer.dll"), true);
            return false;
        });
        workspace.Set("s2.joiningWeaponLevel", "RiouVariance");
        Reject(() => workspace.Save());
        Check(!File.Exists(Config(game, SettingsCatalog.S2File)), "RiouVariance committed after the host became unsupported.");
        Check(!Directory.EnumerateFiles(Path.Combine(game, "BepInEx", "config"), "*.tmp").Any(), "Compatibility rejection left a pending config.");
    });

    Case("ATB and legacy one-shot conflicts reject before changing any files, without blocking S2 Unites", () =>
    {
        string game = NewGame();
        var workspace = SettingsWorkspace.Load(game, () => false);
        workspace.SetBoolean("s1.unites", true);
        workspace.SetBoolean("atb.Enabled", true);
        workspace.SetBoolean("atb.ActiveAtb", true);
        Reject(() => workspace.Save());
        Check(!Directory.EnumerateFiles(Path.Combine(game, "BepInEx", "config")).Any(), "Incompatible ATB settings were written.");
        workspace.SetBoolean("atb.Enabled", false);
        Check(workspace.Validate().Count == 0, "Disabled ATB should allow S1 Unites.");
        workspace.SetBoolean("atb.Enabled", true);
        workspace.SetBoolean("s1.unites", false);
        workspace.SetBoolean("s2.unites", true);
        Check(workspace.Validate().Count == 0, "S2 Unite settings incorrectly block S1 ATB.");
        workspace.SetBoolean("atb.ActiveAtb", false);
        workspace.SetBoolean("atb.OneShotAttack", true);
        workspace.SetBoolean("s1.talismanPreview", true);
        Reject(() => workspace.Save());
        workspace.SetBoolean("s1.talismanPreview", false);
        workspace.Save();
        var restored = SettingsWorkspace.Load(game, () => false);
        Check(restored.GetBoolean("s2.unites") && restored.GetBoolean("atb.Enabled") && !restored.GetBoolean("s1.unites"), "Valid per-game settings did not persist.");
    });

    Case("UTF-16 config encoding survives editing", () =>
    {
        string game = NewGame();
        File.WriteAllText(Config(game, SettingsCatalog.S1File), "[Shuffle]\r\nSeed = old\r\n", new UnicodeEncoding(false, true));
        var workspace = SettingsWorkspace.Load(game, () => false);
        workspace.Set("s1.seed", "new");
        workspace.Save();
        Check(File.ReadAllBytes(Config(game, SettingsCatalog.S1File)).AsSpan().StartsWith(new byte[] { 0xFF, 0xFE }), "UTF-16 BOM was lost.");
        Check(File.ReadAllText(Config(game, SettingsCatalog.S1File)).Contains("Seed = new", StringComparison.Ordinal), "UTF-16 value did not update.");
    });

    Case("Unused partial companions do not block another mode and native speed remains integral", () =>
    {
        string game = NewGame();
        var workspace = SettingsWorkspace.Load(game, () => false);
        workspace.Documents[SettingsCatalog.S1File].Set("Shuffle", "Companions", "11,12");
        workspace.Set("s1.mode", "Shuffle");
        workspace.Save();
        workspace.Set("s1.mode", "FixedChosen");
        Reject(() => workspace.Save());
        workspace.Set("s1.mode", "FixedRandom");
        workspace.Set("fix.SpeedHackFactor", "2");
        Reject(() => workspace.Set("fix.SpeedHackFactor", "1.5"));
        workspace.Save();
    });

    Case("CTB settings have safe defaults and reserve IDs are bounded and normalized", () =>
    {
        var workspace = SettingsWorkspace.Load(NewGame(), () => false);
        Check(!workspace.GetBoolean("ctb.Enabled") && workspace.GetBoolean("ctb.Break")
            && workspace.GetBoolean("ctb.ReserveSupport") && workspace.Get("ctb.ReserveRoleIds") == "", "CTB defaults changed.");
        workspace.Set("ctb.ReserveRoleIds", " 35, 17 ");
        Check(workspace.Get("ctb.ReserveRoleIds") == "35,17", "Reserve IDs did not normalize.");
        foreach (string invalid in new[] { "35,35", "-1", "108", "1,2,3", "35,", "Kai" })
            Reject(() => workspace.Set("ctb.ReserveRoleIds", invalid));
    });

    Case("CTB rejects Unite conflicts and requires the battle preview master switch", () =>
    {
        var workspace = SettingsWorkspace.Load(NewGame(), () => false);
        workspace.SetBoolean("ctb.Enabled", true);
        Check(workspace.Validate().Count != 0, "A disabled preview cannot provide CTB.");
        workspace.SetBoolean("atb.Enabled", true);
        workspace.SetBoolean("s1.unites", true);
        Check(workspace.Validate().Any(message => message.Contains("Unite", StringComparison.Ordinal)), "CTB incorrectly allowed shuffled Unites.");
        workspace.SetBoolean("s1.unites", false);
        workspace.SetBoolean("s2.unites", true);
        Check(workspace.Validate().Count == 0, "S2 Unites should remain independent of S1 CTB.");
    });

    Case("CTB cannot be applied to a missing or older native preview", () =>
    {
        string game = NewGame();
        var workspace = SettingsWorkspace.Load(game, () => false);
        workspace.SetBoolean("atb.Enabled", true);
        workspace.SetBoolean("ctb.Enabled", true);
        Reject(() => workspace.Save());
        Check(!File.Exists(Config(game, SettingsCatalog.AtbFile)), "Unsupported CTB settings were written.");
        File.Copy(typeof(SettingsWorkspace).Assembly.Location,
            Path.Combine(game, "BepInEx", "plugins", "SuikodenRandomizer.S1BattlePreview.dll"));
        Reject(() => workspace.Save());
        Check(!File.Exists(Config(game, SettingsCatalog.AtbFile)), "Older preview accepted CTB settings.");
    });

    Console.WriteLine(passed + " launcher settings checks passed.");
}
finally { Directory.Delete(root, true); }
