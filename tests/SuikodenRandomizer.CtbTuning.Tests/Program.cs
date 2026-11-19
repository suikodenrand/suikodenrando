using SuikodenRandomizer.Shared;

static void Check(bool value, string message)
{
    if (!value) throw new InvalidOperationException(message);
}

static void Reject(Action action)
{
    try { action(); }
    catch (InvalidDataException) { return; }
    throw new InvalidOperationException("Accepted an invalid CTB tuning profile.");
}

BossCeilingTests.Run();
var stars = CtbTuningCatalog.Stars;
var enemies = CtbTuningCatalog.Enemies;
Check(stars.Count == 108 && stars.Select(s => s.Id).Distinct().Count() == 108 &&
    stars.Select(s => s.StarNumber).SequenceEqual(Enumerable.Range(1, 108)), "The tablet roster is incomplete.");
Check(stars.Count(s => s.BaseSpd.HasValue) == 76 && stars.Single(s => s.Id == 8).BaseSpd == 15 &&
    stars.Single(s => s.Name == "Mathiu").BaseSpd is null, "Unexpected playable roster.");
Check(enemies.Count == 136 && enemies.Select(e => e.SourceId).Distinct().Count() == 136 &&
    enemies.Select(e => e.Name).Distinct().Count() == 96, "Lost enemy definitions or variants.");
var dragon = enemies.Single(e => e.Name == "Zombie Dragon");
var shell = enemies.Single(e => e.Name == "Venus Shell");
Check((dragon.Level, dragon.BaseSpd, dragon.Hp) == (30, 65, 3700), "Zombie Dragon data mismatch.");
var guided = enemies.Where(enemy => CtbTuningSoloBossGuide.TryGet(enemy.SourceId, out _)).ToArray();
Check(guided.Length == 10 && guided.Select(e => e.Name).Order().SequenceEqual(new[]
    { "Ain Gide", "Clay Doll", "Crystal Core", "Crystal Core", "Dragon", "Gigantes", "Neclord", "Sonya", "Venus Shell", "Zombie Dragon" }),
    "Unexpected solo boss definitions in the blend guide.");
Check(guided.Single(e => e.Name == "Neclord").Hp == 7500 && guided.Single(e => e.Name == "Clay Doll").Hp == 300 &&
    !CtbTuningSoloBossGuide.TryGet(null, out _) && !CtbTuningSoloBossGuide.TryGet("Zombie Dragon", out _),
    "A scripted fight, field variant, or unknown definition qualified for the solo blend.");
var expectedGuides = new Dictionary<string, (int Level, int SixStarTarget)>
{
    ["Clay Doll"] = (6, 52), ["Zombie Dragon"] = (17, 111), ["Gigantes"] = (24, 139), ["Dragon"] = (26, 146),
    ["Neclord"] = (47, 220), ["Crystal Core"] = (51, 234), ["Venus Shell"] = (55, 249),
    ["Sonya"] = (56, 252), ["Ain Gide"] = (57, 256)
};
foreach (var enemy in guided)
{
    CtbTuningSoloBossGuide.TryGet(enemy.SourceId, out var guide);
    Check((guide.RecommendedLevel, CtbTuningSoloBossGuide.Calculate(guide, 6, 600, 0, 0).Speed) == expectedGuides[enemy.Name],
        $"Incorrect recommended level or Star SPD for {enemy.Name}.");
}
CtbTuningSoloBossGuide.TryGet(dragon.SourceId, out var dragonGuide);
CtbTuningSoloBossGuide.TryGet(shell.SourceId, out var shellGuide);
Check(CtbTuningSoloBossGuide.Calculate(dragonGuide, 6, 433, 75, 0).Speed == 136 &&
    CtbTuningSoloBossGuide.Calculate(dragonGuide, 6, 433, 72, 0).Speed == 135 &&
    CtbTuningSoloBossGuide.Calculate(dragonGuide, 6, 433, 100, 0).Speed == 144,
    "Recorded Zombie Dragon party did not match the blended and party-only targets.");
var shellBlend = CtbTuningSoloBossGuide.Calculate(shellGuide, 6, 1405, 75, 0);
Check(Math.Abs(shellBlend.RawSpeed - 413.39109) < .00001 && shellBlend.Speed == 400 &&
    CtbTuningSoloBossGuide.Calculate(shellGuide, 6, 1405, 75, -5).Speed == 393 &&
    CtbTuningSoloBossGuide.Calculate(dragonGuide, 6, 433, 75, 5).Speed == 143,
    "Blend must apply encounter variation before rounding and applying the CTB speed cap.");
Check(CtbTuningSoloBossGuide.Calculate(dragonGuide, 1, 1, 100, -15).Speed == 1 &&
    CtbTuningSoloBossGuide.Calculate(new(1, 31.5), 1, 30, 0, 0).Speed == 11 &&
    CtbTuningSoloBossGuide.Calculate(dragonGuide, 3, 150, 0, 0).Speed == 55,
    "Minimum SPD, rounding, or short-party scaling changed.");
Check(CtbTuningNormalScaling.IsOrdinaryFormation(100) && !CtbTuningNormalScaling.IsOrdinaryFormation(int.MinValue) &&
    !CtbTuningNormalScaling.IsOrdinaryFormation(0x40000000) && !CtbTuningNormalScaling.IsOrdinaryFormation(0),
    "Story formations must be excluded even when they reuse ordinary enemies.");
Check(enemies.Count(e => CtbTuningNormalScaling.TryGetEnemyLevel(e.SourceId, out _)) == 100 &&
    CtbTuningNormalScaling.TryGetEnemyLevel("a_data_battle_00_monster_data_02", out int crowLevel) && crowLevel == 4 &&
    CtbTuningNormalScaling.TryGetEnemyLevel("vb3_map_05_battle_02_monster_data_00", out _) &&
    !CtbTuningNormalScaling.TryGetEnemyLevel("vb6_map_01_battle_00_monster_data_04", out _) &&
    !CtbTuningNormalScaling.TryGetEnemyLevel("vzv_map_00_battle_00_monster_data_02", out _) &&
    !CtbTuningNormalScaling.TryGetEnemyLevel(null, out _) && !CtbTuningNormalScaling.TryGetEnemyLevel("Crow", out _) &&
    guided.All(e => !CtbTuningNormalScaling.TryGetEnemyLevel(e.SourceId, out _)),
    "Incorrect eligibility for authored enemy definitions or scripted variants.");
foreach (var (partySpd, levels, speeds, expected) in new (int, int[], int[], int[])[]
{
    (77, [4, 7, 4], [27, 20, 27], [26, 19, 26]),
    (85, [8, 8], [22, 22], [20, 20]),
    (93, [8], [22], [20]),
    (93, [8, 8], [22, 22], [20, 20]),
    (96, [4, 7, 4], [27, 20, 27], [29, 21, 29])
})
{
    var scale = CtbTuningNormalScaling.Calculate(4, partySpd, levels);
    Check(speeds.Select(spd => CtbTuningNormalScaling.Apply(spd, scale.Multiplier)).SequenceEqual(expected),
        $"Incorrect normal enemy speeds for the {partySpd} SPD starting party.");
}
int[] mixedLevels = [10, 8, 7, 3];
var mixed = CtbTuningNormalScaling.Calculate(4, 120, mixedLevels);
Check(mixedLevels.SequenceEqual([10, 8, 7, 3]) && mixed.MedianLevel == 7.5 &&
    Math.Abs(mixed.ReferenceSpd - 30.21122018914474) < 0.000001 &&
    mixed.Multiplier == CtbTuningNormalScaling.Calculate(2, 60, [8, 7]).Multiplier,
    "A mixed pack must use the median level without changing slot order, and a mean over the living party.");
Check(CtbTuningNormalScaling.Calculate(1, 1, [99]).Multiplier == 0.9 &&
    CtbTuningNormalScaling.Calculate(1, 400, [1]).Multiplier == 1.25 &&
    CtbTuningNormalScaling.Apply(22, 0.9) == 20 && CtbTuningNormalScaling.Apply(22, 1.25) == 28 &&
    CtbTuningNormalScaling.Apply(25, 1.0) == 25 && CtbTuningNormalScaling.Apply(399, 1.25) == 400 &&
    CtbTuningNormalScaling.Apply(0, 1.25) == 0, "Adjustment bounds, native unavailable SPD, or rounding changed.");
foreach (var enemy in guided)
{
    CtbTuningSoloBossGuide.TryGet(enemy.SourceId, out var bossGuide);
    double reference = CtbTuningNormalScaling.Calculate(1, 40, [bossGuide.RecommendedLevel]).ReferenceSpd;
    Check(Math.Abs(reference - bossGuide.AverageStarSpd) < 1e-10, "Star growth references disagree at a known level.");
}
var folder = Directory.CreateTempSubdirectory("s1-ctb-").FullName;
try
{
    string path = Path.Combine(folder, CtbTuningProfile.FileName);
    var profile = CtbTuningProfile.Load(path);
    Check(profile.SoloBossBreakPercent == 25 && profile.OtherBreakPercent == 50 && profile.SoloBossSpdBonusPercent == 0 &&
        !profile.NormalEnemyScalingEnabled && !profile.HasStarOverrides && !profile.HasEnemyOverrides && !File.Exists(path),
        "Missing profile changed defaults.");
    Reject(() => profile.SetStarSpd(27, 20));
    Reject(() => profile.SetEnemySpd("Zombie Dragon", 20));
    Reject(() => profile.SetEnemySpd(dragon.SourceId, 0));
    Reject(() => profile.SetEnemySpd(shell.SourceId, 401));
    Reject(() => profile.SetStarSpd(8, 256));
    Reject(() => profile.OtherBreakPercent = 76);
    Reject(() => profile.SoloBossBreakPercent = -1);
    Reject(() => profile.SoloBossSpdBonusPercent = -0.01m);
    Reject(() => profile.SoloBossSpdBonusPercent = 300.01m);
    Reject(() => profile.SoloBossPartyWeightPercent = -1);
    Reject(() => profile.SoloBossPartyWeightPercent = 101);
    Reject(() => profile.SoloBossVariationPercent = -1);
    Reject(() => profile.SoloBossVariationPercent = 16);
    profile.SoloBossBreakPercent = 0;
    profile.OtherBreakPercent = 75;
    profile.SoloBossSpdBonusPercent = 130.77m;
    foreach (var star in stars.Where(s => s.BaseSpd.HasValue)) profile.SetStarSpd(star.Id, 255);
    for (int i = 0; i < enemies.Count; i++) profile.SetEnemySpd(enemies[i].SourceId, i + 1);
    string savedVersion = profile.Save(path);
    Check(savedVersion == Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))),
        "Save returned a digest for different bytes.");
    var loaded = CtbTuningProfile.Load(path);
    Check(loaded.SoloBossBreakPercent == 0 && loaded.OtherBreakPercent == 75, "Lost Break values.");
    Check(loaded.SoloBossSpdBonusPercent == 130.77m, "Lost fractional solo boss SPD bonus.");
    foreach (var star in stars) Check(loaded.GetStarSpd(star.Id) == (star.BaseSpd.HasValue ? 255 : (int?)null), "Lost Star SPD.");
    for (int i = 0; i < enemies.Count; i++) Check(loaded.GetEnemySpd(enemies[i].SourceId) == i + 1, "Lost enemy variant.");
    foreach (var star in stars.Where(s => s.BaseSpd.HasValue)) loaded.SetStarSpd(star.Id, null);
    foreach (var enemy in enemies) loaded.SetEnemySpd(enemy.SourceId, null);
    loaded.Save(path);
    var cleared = CtbTuningProfile.Load(path);
    Check(!cleared.HasEnemyOverrides && !cleared.HasStarOverrides, "Cleared overrides persisted.");

    string Clean(string starJson = "{}", string enemyJson = "{}") =>
        $$"""{"schemaVersion":1,"soloBossBreakPercent":25,"otherBreakPercent":50,"starSpd":{{starJson}},"enemySpd":{{enemyJson}}}""";
    string WithBonus(string value) => Clean().Insert(1, $"\"soloBossSpdBonusPercent\":{value},");
    File.WriteAllText(path, Clean("""{"8":72}""", "{\"" + dragon.SourceId + "\":150}"));
    var legacy = CtbTuningProfile.Load(path);
    Check(!legacy.NormalEnemyScalingEnabled && !legacy.SoloBossBlendEnabled && legacy.SoloBossPartyWeightPercent == 75 && legacy.SoloBossVariationPercent == 0,
        "Installing blend support altered a legacy profile's fixed-speed mode.");
    Check(legacy.SoloBossSpdBonusPercent == 0m && legacy.GetStarSpd(8) == 72 && legacy.GetEnemySpd(dragon.SourceId) == 150,
        "Loading a profile without the optional bonus changed existing overrides.");
    legacy.Save(path);
    Check(!File.ReadAllText(path).Contains("soloBossSpdBonusPercent"), "Zero bonus changed the original schema.");
    legacy.SoloBossSpdBonusPercent = 131;
    legacy.Save(path);
    var trial = CtbTuningProfile.Load(path);
    trial.SetEnemySpd(shell.SourceId, 400);
    trial.Save(path);
    var venusTrial = CtbTuningProfile.Load(path);
    Check(venusTrial.GetEnemySpd(shell.SourceId) == 400 && venusTrial.GetEnemySpd(dragon.SourceId) == 150 &&
        venusTrial.GetStarSpd(8) == 72 && venusTrial.SoloBossSpdBonusPercent == 131 &&
        venusTrial.SoloBossBreakPercent == 25 && venusTrial.OtherBreakPercent == 50,
        "The 400 SPD Venus Shell trial did not preserve the other tuning settings.");
    venusTrial.NormalEnemyScalingEnabled = true;
    venusTrial.SoloBossBlendEnabled = true;
    venusTrial.SoloBossPartyWeightPercent = 72;
    venusTrial.SoloBossVariationPercent = 5;
    venusTrial.Save(path);
    var blendTrial = CtbTuningProfile.Load(path);
    Check(blendTrial.NormalEnemyScalingEnabled && blendTrial.SoloBossBlendEnabled && blendTrial.SoloBossPartyWeightPercent == 72 && blendTrial.SoloBossVariationPercent == 5 &&
        blendTrial.GetEnemySpd(dragon.SourceId) == 150 && blendTrial.GetEnemySpd(shell.SourceId) == 400 &&
        blendTrial.SoloBossSpdBonusPercent == 131 && blendTrial.SoloBossBreakPercent == 25 && blendTrial.GetStarSpd(8) == 72,
        "Saving the adaptive trial lost settings or fixed-mode fallback values.");
    blendTrial.NormalEnemyScalingEnabled = false;
    blendTrial.SoloBossBlendEnabled = false;
    blendTrial.SoloBossPartyWeightPercent = 75;
    blendTrial.SoloBossVariationPercent = 0;
    blendTrial.Save(path);
    string restoredFixedProfile = File.ReadAllText(path);
    Check(!restoredFixedProfile.Contains("soloBossBlendEnabled") && !restoredFixedProfile.Contains("soloBossPartyWeightPercent") &&
        !restoredFixedProfile.Contains("soloBossVariationPercent") && !restoredFixedProfile.Contains("normalEnemyScalingEnabled"),
        "Default blend settings changed the legacy profile format.");
    foreach (string extra in new[] { "\"soloBossBlendEnabled\":1", "\"soloBossBlendEnabled\":\"true\"", "\"soloBossPartyWeightPercent\":101",
        "\"soloBossVariationPercent\":0.5", "\"soloBossVariationPercent\":16", "\"soloBossBlendEnabled\":true,\"soloBossBlendEnabled\":false",
        "\"normalEnemyScalingEnabled\":1", "\"normalEnemyScalingEnabled\":true,\"normalEnemyScalingEnabled\":false" })
    {
        File.WriteAllText(path, Clean().Insert(1, extra + ","));
        Reject(() => CtbTuningProfile.Load(path));
    }
    foreach (decimal percent in new[] { 0m, 130.77m, 300m })
    {
        File.WriteAllText(path, WithBonus(percent.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        Check(CtbTuningProfile.Load(path).SoloBossSpdBonusPercent == percent, "Valid explicit bonus did not load.");
    }
    foreach (string input in new[] { "", "null", "[]", "{}", Clean().Replace("\"schemaVersion\":1", "\"schemaVersion\":2"),
        Clean().Replace("\"schemaVersion\":1", "\"schemaVersion\":1,\"schemaVersion\":1"), Clean().Replace("25", "26.5"),
        Clean().Replace("50", "76"), Clean().Replace("otherBreakPercent", "otherBreak"), Clean("[]"),
        Clean("""{"8":1,"08":2}"""), Clean("""{"8":1,"8":2}"""), Clean("""{"8":null}"""), Clean("""{"27":50}"""),
        Clean("""{"8":256}"""), Clean("{}", $$"""{"{{shell.SourceId}}":401}"""),
        Clean("{}", """{"not-an-enemy":255}"""), Clean("{}", "{\"" + dragon.SourceId + "\":5,\"" + dragon.SourceId + "\":6}"),
        WithBonus("null"), WithBonus("\"130.77\""), WithBonus("1e1000"), WithBonus("[]"), WithBonus("-0.01"),
        WithBonus("300.01"), WithBonus("130.77,\"soloBossSpdBonusPercent\":100"), new string(' ', 65537) })
    {
        File.WriteAllText(path, input);
        Reject(() => CtbTuningProfile.Load(path));
        Check(File.ReadAllText(path) == input, "Loading changed an invalid file.");
    }
    new CtbTuningProfile().Save(path);
    var alternate = new CtbTuningProfile { SoloBossBreakPercent = 45, OtherBreakPercent = 70, SoloBossSpdBonusPercent = 130.77m };
    if (OperatingSystem.IsWindows())
    {
        // Simulate Windows tools opening the existing file without sharing delete.
        using (var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var release = new Thread(() => { Thread.Sleep(45); held.Dispose(); });
            release.Start();
            try { alternate.Save(path); }
            finally { release.Join(); }
        }
        Check(CtbTuningProfile.Load(path).SoloBossBreakPercent == 45, "A brief lock prevented the save.");
        byte[] previous = File.ReadAllBytes(path);
        bool refused = false;
        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            try { new CtbTuningProfile().Save(path); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { refused = true; }
        }
        Check(refused && previous.SequenceEqual(File.ReadAllBytes(path)), "A lasting lock lost the original profile.");
    }
    Parallel.Invoke(
        () =>
        {
            for (int i = 0; i < 20; i++)
                (i % 2 == 0 ? alternate : new CtbTuningProfile()).Save(path);
        },
        () =>
        {
            for (int i = 0; i < 80; i++)
            {
                var snapshot = CtbTuningProfile.Load(path);
                Check((snapshot.SoloBossBreakPercent, snapshot.OtherBreakPercent, snapshot.SoloBossSpdBonusPercent)
                    is (25, 50, 0m) or (45, 70, 130.77m),
                    "Observed partial write.");
            }
        });
    Check(Directory.GetFiles(folder).Length == 1, "Temporary files remained.");
    Console.WriteLine("PASS CTB tuning catalog, per-actor overrides, normal enemy scaling, solo boss bonus and blend, strict validation, and atomic writes.");
}
finally { Directory.Delete(folder, recursive: true); }
