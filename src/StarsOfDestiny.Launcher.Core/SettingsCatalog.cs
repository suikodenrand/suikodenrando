using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using SuikodenRandomizer.Core.Avatars;
using SuikodenRandomizer.Core.Suikoden2;

namespace StarsOfDestiny.Launcher.Core;

public enum SettingKind { Boolean, Seed, Choice, Companions, Integer, Number }

public sealed record SettingDefinition(string Id, string FileName, string Section, string Key,
    string DefaultValue, SettingKind Kind, IReadOnlyList<string>? Choices = null, double Minimum = 0, double Maximum = 100)
{
    public string Game => FileName == SettingsCatalog.S1File ? "S1" : FileName == SettingsCatalog.S2File ? "S2"
        : FileName == SettingsCatalog.AtbFile ? "ATB" : FileName == SettingsCatalog.FixFile ? "Fix" : "Shared";
}

public static class SettingsCatalog
{
    public const string S1File = "agilen.suikoden.recruitment_randomizer.cfg";
    public const string S2File = "agilen.suikoden2.recruitment_randomizer.cfg";
    public const string SharedFile = "agilen.starsofdestiny.cfg";
    public const string AtbFile = "agilen.suikoden1.battle-preview.cfg";
    public const string FixFile = "d3xMachina.suikoden_fix.cfg";
    public static IReadOnlyList<string> FileNames { get; } = Array.AsReadOnly(new[] { S1File, S2File, SharedFile, AtbFile, FixFile });
    public static IReadOnlyList<SettingDefinition> All { get; } = Array.AsReadOnly(Build().ToArray());

    public static SettingDefinition Find(string id) => All.FirstOrDefault(setting => setting.Id == id)
        ?? throw new ArgumentException("Unknown launcher setting " + id, nameof(id));
    public static SettingDefinition? Find(string file, string section, string key, StringComparison comparison = StringComparison.Ordinal) =>
        All.FirstOrDefault(setting => setting.FileName.Equals(file, StringComparison.OrdinalIgnoreCase)
            && setting.Section.Equals(section, comparison) && setting.Key.Equals(key, comparison));

    // BepInEx 6 build 697 preserves unescaped drive paths and decodes these
    // escapes in other strings. Its own writer escapes quotes but keeps backslashes.
    internal static string DecodeSeed(string raw)
    {
        if (!raw.Contains('\\') || Regex.IsMatch(raw, @"^""?\w:\\(?!\\)(?!.+\\\\)")) return raw;
        return Regex.Replace(raw, @"\\([0abtnvfr'""\\])", match => match.Value[1] switch
        {
            '0' => "\0", 'a' => "\a", 'b' => "\b", 't' => "\t", 'n' => "\n",
            'v' => "\v", 'f' => "\f", 'r' => "\r", var literal => literal.ToString()
        });
    }

    public static string Normalize(SettingDefinition setting, string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        string trimmed = value.Trim();
        switch (setting.Kind)
        {
            case SettingKind.Boolean:
                if (!bool.TryParse(trimmed, out bool flag)) throw Invalid(setting);
                return flag ? "true" : "false";
            case SettingKind.Seed:
                if (value.Length > 128 || value.Any(char.IsControl)) throw Invalid(setting);
                if (setting.FileName == S2File) return Gsd2AvatarProfile.NormalizeSeed(value);
                string seed = trimmed.Normalize(NormalizationForm.FormC);
                if (seed.Length is 0 or > 128) throw Invalid(setting);
                string nativeValue = seed.Replace("'", @"\'").Replace("\"", "\\\"");
                if (DecodeSeed(nativeValue) != seed)
                    throw new ArgumentException("This seed contains an escape sequence the game changes when saving its settings.");
                return seed;
            case SettingKind.Choice:
                return setting.Choices!.FirstOrDefault(choice => string.Equals(choice, trimmed, StringComparison.OrdinalIgnoreCase))
                    ?? throw Invalid(setting);
            case SettingKind.Companions:
                if (trimmed.Length == 0) return "";
                string[] values = trimmed.Split(',');
                if (values.Length != 5 || values.Any(part => !int.TryParse(part.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out _)))
                    throw Invalid(setting);
                int[] ids = values.Select(part => int.Parse(part.Trim(), CultureInfo.InvariantCulture)).ToArray();
                if (setting.FileName == S1File) _ = FixedRosterProfile.CreateChosen(ids);
                else _ = Gsd2AvatarProfile.CreateChosen("launcher-validation", ids);
                return string.Join(",", ids);
            case SettingKind.Integer:
                if (!int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out int integer)
                    || integer < setting.Minimum || integer > setting.Maximum) throw Invalid(setting);
                if (setting.Id == "fix.SaveSlots" && integer != -1 && integer < 16) throw Invalid(setting);
                return integer.ToString(CultureInfo.InvariantCulture);
            case SettingKind.Number:
                if (!double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out double number)
                    || !double.IsFinite(number) || number < setting.Minimum || number > setting.Maximum) throw Invalid(setting);
                return number.ToString("G", CultureInfo.InvariantCulture);
            default: throw Invalid(setting);
        }
    }

    public static IReadOnlyList<string> Validate(IReadOnlyDictionary<string, IniDocument> documents)
    {
        var errors = new List<string>();
        foreach (var setting in All)
        {
            if (setting.Kind == SettingKind.Companions && !documents[setting.FileName].Get("Shuffle", "Mode", "Shuffle")
                .Equals("FixedChosen", StringComparison.OrdinalIgnoreCase)) continue;
            try
            {
                string raw = documents[setting.FileName].Get(setting.Section, setting.Key, setting.DefaultValue);
                _ = Normalize(setting, setting.Kind == SettingKind.Seed ? DecodeSeed(raw) : raw);
            }
            catch (ArgumentException exception) { errors.Add(setting.Id + ": " + exception.Message); }
            catch (InvalidOperationException exception) { errors.Add(setting.Id + ": " + exception.Message); }
        }
        foreach (string file in new[] { S1File, S2File })
        {
            if (documents[file].Get("Shuffle", "Mode", "Shuffle").Equals("FixedChosen", StringComparison.OrdinalIgnoreCase)
                && string.IsNullOrWhiteSpace(documents[file].Get("Shuffle", "Companions")))
                errors.Add((file == S1File ? "S1" : "S2") + " fixed roster requires five companions.");
        }
        bool Enabled(string file, string section, string key, bool fallback = false) =>
            bool.TryParse(documents[file].Get(section, key, fallback ? "true" : "false"), out bool enabled) && enabled;
        if (Enabled(AtbFile, "Preview", "Enabled")
            && (Enabled(AtbFile, "Preview", "ActiveAtb") || Enabled(AtbFile, "Preview", "OneShotAttack"))
            && (Enabled(S1File, "Battle", "ShuffledUnitesPreview") || Enabled(S1File, "Battle", "ShuffledTalismanPreview")))
            errors.Add("S1 shuffled Unite attacks require standard turn-based combat. Turn off S1 shuffled-character Unite attacks (and legacy Talisman preview) before enabling ATB or the one-action test.");
        return errors.AsReadOnly();
    }

    private static ArgumentException Invalid(SettingDefinition setting) => new("Invalid value for " + setting.Section + " / " + setting.Key + ".");

    private static IEnumerable<SettingDefinition> Build()
    {
        foreach (var game in new[] { (Prefix: "s1", File: S1File), (Prefix: "s2", File: S2File) })
        {
            yield return new(game.Prefix + ".seed", game.File, "Shuffle", "Seed", "agilen-080", SettingKind.Seed);
            yield return new(game.Prefix + ".mode", game.File, "Shuffle", "Mode", "Shuffle", SettingKind.Choice, new[] { "Shuffle", "FixedChosen", "FixedRandom" });
            yield return new(game.Prefix + ".companions", game.File, "Shuffle", "Companions", "", SettingKind.Companions);
            yield return new(game.Prefix + ".joiningWeaponLevel", game.File, "Shuffle", "JoiningWeaponLevel", game.Prefix == "s1" ? "TirVariance" : "RiouVariance", SettingKind.Choice,
                game.Prefix == "s1" ? new[] { "TirVariance", "MatchRole", "Level1" } : new[] { "RiouVariance", "MatchRole", "Level1" });
            yield return new(game.Prefix + ".joiningEquipment", game.File, "Shuffle", "JoiningEquipment", "MatchRole", SettingKind.Choice,
                new[] { "MatchRole", "Donor" });
            yield return new(game.Prefix + ".unites", game.File, "Battle", "ShuffledUnitesPreview", "false", SettingKind.Boolean);
            yield return new(game.Prefix + ".includeHero", game.File, "Shuffle", game.Prefix == "s1" ? "IncludeTir" : "IncludeProtagonist", "false", SettingKind.Boolean);
            yield return new(game.Prefix + ".start", game.File, "Start", "Point", "Opening", SettingKind.Choice,
                game.Prefix == "s1" ? new[] { "Opening", "BeforeViktor" } : new[] { "Opening", "ViktorCamp" });
        }
        yield return new("s1.treasureEnabled", S1File, "Treasure", "Enabled", "true", SettingKind.Boolean);
        yield return new("s1.talismanPreview", S1File, "Battle", "ShuffledTalismanPreview", "false", SettingKind.Boolean);
        yield return new("s1.chests", S1File, "Treasure", "ShuffleChests", "false", SettingKind.Boolean);
        yield return new("shared.enabled", SharedFile, "General", "Enabled", "true", SettingKind.Boolean);
        yield return new("shared.dialogue", SharedFile, "Dialogue", "ReplaceCharacterMentions", "true", SettingKind.Boolean);
        yield return new("shared.view", SharedFile, "Interface", "ViewMode", "Hidden", SettingKind.Choice, new[] { "Full", "Compact", "Hidden" });
        foreach (string key in new[] { "Enabled", "OneShotAttack", "ActiveAtb", "RepeatTurns", "ReadyCommands", "NativeReadyMenus", "NativeSpdMeters", "ActiveInput", "EnemyDexCharge", "OpeningCharge" })
            yield return new("atb." + key, AtbFile, "Preview", key, key is "ActiveInput" or "EnemyDexCharge" or "OpeningCharge" ? "true" : "false", SettingKind.Boolean);
        foreach (var group in new[]
        {
            (Section: "Misc", Keys: new[] { "RememberBattleSpeed", "ExitApplication", "ResetGame", "PauseGame", "UncapMoney", "StallionBoons" }),
            (Section: "Movement", Keys: new[] { "DisableDiagonalMovements", "ToggleDash" }),
            (Section: "Skip", Keys: new[] { "SkipSplashscreens", "SkipMovies", "InstantMessage", "InstantRichmondInvestigation", "InstantShop" }),
            (Section: "Visual", Keys: new[] { "DisableVignette", "DisableMaskedVignette", "DisableDepthOfField", "SmoothSprites", "DisableAutoSaveNotification" }),
            (Section: "Cheat", Keys: new[] { "RecoverHpAfterBattle", "RecoverMpAfterBattle", "RareFindsAlwaysInStock", "EasyMinigames", "BetterLeona", "BetterSanchez", "MoreTeleportLocations" })
        })
            foreach (string key in group.Keys)
                yield return new("fix." + key, FixFile, group.Section, key,
                    key is "ExitApplication" or "ResetGame" or "PauseGame" or "ToggleDash" or "SkipSplashscreens" or "SkipMovies" or "InstantMessage" or "InstantShop" ? "true" : "false", SettingKind.Boolean);
        yield return new("fix.SaveAnywhere", FixFile, "Misc", "SaveAnywhere", "-1", SettingKind.Integer, Minimum: -1, Maximum: 99);
        yield return new("fix.SaveSlots", FixFile, "Misc", "SaveSlots", "-1", SettingKind.Integer, Minimum: -1, Maximum: 99);
        foreach (string key in new[] { "PlayerDamageMultiplier", "MonsterDamageMultiplier", "MonsterHealthMultiplier", "ExperienceMultiplier", "MoneyMultiplier", "LootMultiplier", "EncounterRateMultiplier" })
            yield return new("fix." + key, FixFile, "Cheat", key, "1", SettingKind.Number, Minimum: 0, Maximum: 100);
        yield return new("fix.SpeedHackFactor", FixFile, "Skip", "SpeedHackFactor", "1", SettingKind.Integer, Minimum: 1, Maximum: 100);
        yield return new("fix.FPS", FixFile, "Display", "FPS", "-1", SettingKind.Integer, Minimum: -1, Maximum: 360);
        yield return new("fix.Vsync", FixFile, "Display", "Vsync", "-1", SettingKind.Integer, Minimum: -1, Maximum: 1);
        foreach (string key in new[] { "Width", "Height" })
            yield return new("fix." + key, FixFile, "Display", key, "-1", SettingKind.Integer, Minimum: -1, Maximum: 16384);
        yield return new("fix.Fullscreen", FixFile, "Display", "Fullscreen", "-1", SettingKind.Integer, Minimum: -1, Maximum: 3);
        yield return new("fix.NoFrameSkip", FixFile, "Display", "NoFrameSkip", "false", SettingKind.Boolean);
        yield return new("fix.BloomMultiplier", FixFile, "Visual", "BloomMultiplier", "1", SettingKind.Number, Minimum: 0, Maximum: 10);
        yield return new("fix.SpedUpMusic", FixFile, "Audio", "SpedUpMusic", "-1", SettingKind.Integer, Minimum: -1, Maximum: 3);
        yield return new("fix.SpedUpSoundEffect", FixFile, "Audio", "SpedUpSoundEffect", "-1", SettingKind.Integer, Minimum: -1, Maximum: 1);
        foreach (string key in new[] { "DisableMessageWindowSound", "DisableStartledSound" })
            yield return new("fix." + key, FixFile, "Audio", key, "false", SettingKind.Boolean);
        yield return new("fix.EditSave", FixFile, "Advanced", "EditSave", "false", SettingKind.Boolean);
        yield return new("fix.BackupSave", FixFile, "Advanced", "BackupSave", "0", SettingKind.Integer, Minimum: 0, Maximum: 100);
    }
}
