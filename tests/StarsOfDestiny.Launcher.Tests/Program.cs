using System;
using System.IO;
using System.Linq;
using System.Text;
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
        Check(ini.Get("shuffle", "seed") == "test", "INI key lookup should be case insensitive.");
    });

    Case("Updating a setting retains original spacing, case, comments and section boundaries", () =>
    {
        var ini = IniDocument.Parse("# untouched\r\n[Shuffle]\r\n  Seed  =   old  \r\nOther = keep\r\n\r\n[Start]\r\nPoint = Opening\r\n");
        ini.Set("shuffle", "Seed", "new");
        Check(ini.Render() == "# untouched\r\n[Shuffle]\r\n  Seed  =   new  \r\nOther = keep\r\n\r\n[Start]\r\nPoint = Opening\r\n", "Setting update changed unrelated bytes.");
        ini.Set("Shuffle", "Mode", "Shuffle");
        Check(ini.Render().Contains("Other = keep\r\nMode = Shuffle\r\n\r\n[Start]", StringComparison.Ordinal), "Missing key was inserted into the wrong section.");
        ini.Set("Treasure", "Enabled", "false");
        Check(ini.Get("Treasure", "Enabled") == "false", "New section value is missing.");
    });

    Case("INI rejects duplicate settings and value injection", () =>
    {
        var duplicate = IniDocument.Parse("[Shuffle]\nSeed=a\nseed=b\n");
        Reject(() => duplicate.Get("Shuffle", "Seed"));
        Reject(() => duplicate.Set("Shuffle", "Seed", "one"));
        Reject(() => IniDocument.Parse("").Set("Shuffle", "Seed", "a\n[Cheat]\nMoneyMultiplier=100"));
        Reject(() => IniDocument.Parse("").Set("Bad]", "Key", "x"));
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

    Console.WriteLine(passed + " launcher settings checks passed.");
}
finally { Directory.Delete(root, true); }
