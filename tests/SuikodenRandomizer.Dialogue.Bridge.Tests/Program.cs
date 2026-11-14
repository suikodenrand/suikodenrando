using System.Reflection;
using System.Text.Json;
using BepInEx.Configuration;
using HarmonyLib;
using SuikodenRandomizer.Shared;
using SuikodenRandomizer.Unified;

if (args.Length != 2) throw new ArgumentException("Use <original S2 English JSON> <original S1 English JSON>.");
string ReadRow(string path, int sid)
{
    using var data = JsonDocument.Parse(File.ReadAllText(path));
    return data.RootElement.GetProperty("data").GetProperty("textList").EnumerateArray()
        .Single(row => row.GetProperty("id").GetUInt64() == (ulong)sid).GetProperty("text").GetString()!;
}
const int Camp = 1002020116, CampAlias = 1002110116, S1LowBody = 1345;
string camp = ReadRow(args[0], Camp), low = ReadRow(args[1], S1LowBody);
string expected = camp.Replace("Gengen", "Humphrey", StringComparison.Ordinal).Replace("Tuta", "Tengaar", StringComparison.Ordinal);
var postfix = typeof(DialogueRawTextPatch).GetMethod("Postfix", BindingFlags.Static | BindingFlags.NonPublic)!;
string Raw(string original, int id, string category = "message")
{
    var parameters = new object[] { category, id, original };
    postfix.Invoke(null, parameters);
    return (string)parameters[2];
}
NativeDialogueContext Snapshot(ActiveGame game, Func<int, string?> names, Func<int, bool>? replace = null, Func<int?>? cap = null)
{
    var token = UnifiedPlugin.Router.Capture();
    return new NativeDialogueContext(game == ActiveGame.Gsd2 ? "GSD2" : "GSD1",
        () => token.Game == game && UnifiedPlugin.Router.IsCurrent(token), names, cap ?? (() => null), replace);
}
string? S2Name(int role) => role switch { 28 => "Humphrey", 22 => "Tengaar", _ => null };
void Game(ActiveGame game) => UnifiedPlugin.Router.ObserveGameContext(game, game == ActiveGame.Gsd1, game == ActiveGame.Gsd2);
void Check(bool condition) { if (!condition) throw new Exception("Failed assertion."); }
int passed = 0;
void Case(string name, Action action)
{
    action(); passed++; Console.WriteLine("PASS " + name);
}

var config = new ConfigFile();
Case("Actual embedded runtime catalogs load together with a dedicated patch group", () =>
{
    DialogueNative.Install(config);
    if (!DialogueNative.Enabled) throw new InvalidOperationException(string.Join("\n", UnifiedPlugin.HostLog.Messages));
    Check(UnifiedPlugin.Registered.SequenceEqual(new[] { typeof(DialogueRawTextPatch), typeof(DialogueS2), typeof(DialogueS1) }));
    Check(UnifiedPlugin.HostLog.Messages.Any(message => message.Contains("933 S2 plans and 690 S1 plans; 2600 one-hop aliases")));
});
Game(ActiveGame.Gsd2);
Case("Original S2 camp body and direct alias use the same source-pinned replacement", () =>
{
    Check(expected != camp && camp.Contains("♂㈱", StringComparison.Ordinal));
    using var scope = DialogueNative.Enter(ActiveGame.Gsd2, () => Snapshot(ActiveGame.Gsd2, S2Name));
    Check(Raw(camp, Camp) == expected && Raw(camp, CampAlias) == expected);
    Check(expected.Contains("♂㈱", StringComparison.Ordinal));
    Check(Raw(camp, Camp) == expected);
    Check(DialogueNative.Status(ActiveGame.Gsd2) == "Dialogue names: eligible for supported English dialogue. Rewritten lookups this session: 3.");
    Check(DialogueNative.Status(ActiveGame.Gsd1).Contains("Rewritten lookups this session: 0."));
});
Case("No caller scope, mismatched source, unrelated text table and suppressed name return original", () =>
{
    Check(Raw(camp, Camp) == camp);
    using var scope = DialogueNative.Enter(ActiveGame.Gsd2, () => Snapshot(ActiveGame.Gsd2, S2Name));
    Check(Raw(camp + " ", Camp) == camp + " ");
    Check(Raw(camp, Camp, "add_message") == camp);
    using (DialogueNative.SuppressNames()) Check(Raw(camp, Camp) == camp);
});
Case("Fixed roster preserves an unassigned Tuta mention alongside replaced Gengen", () =>
{
    using var scope = DialogueNative.Enter(ActiveGame.Gsd2, () => Snapshot(ActiveGame.Gsd2, S2Name, role => role == 28));
    Check(Raw(camp, Camp) == camp.Replace("Gengen", "Humphrey", StringComparison.Ordinal));
});
Case("Unadmitted foreign and throwing native captures block an admitted outer scope", () =>
{
    using var outer = DialogueNative.Enter(ActiveGame.Gsd2, () => Snapshot(ActiveGame.Gsd2, S2Name));
    using (DialogueNative.Enter(ActiveGame.Gsd1, () => throw new Exception("foreign callback must not be called"))) Check(Raw(camp, Camp) == camp);
    using (DialogueNative.Enter(ActiveGame.Gsd2, () => throw new Exception("synthetic capture failure"))) Check(Raw(camp, Camp) == camp);
    Check(Raw(camp, Camp) == expected);
});
Case("Ownership or language changing inside native name resolution rolls back", () =>
{
    using (DialogueNative.Enter(ActiveGame.Gsd2, () => Snapshot(ActiveGame.Gsd2, role =>
    { UnifiedPlugin.Router.Invalidate(); return S2Name(role); }))) Check(Raw(camp, Camp) == camp);
    using (DialogueNative.Enter(ActiveGame.Gsd2, () => Snapshot(ActiveGame.Gsd2, role =>
    { TextMasterData.CurrentLanguage = TextMasterData.Lang.Japanese; return S2Name(role); }))) Check(Raw(camp, Camp) == camp);
    using (DialogueNative.Enter(ActiveGame.Gsd2, () => throw new Exception("foreign language callback must not be called"))) Check(Raw(camp, Camp) == camp);
    Check(DialogueNative.Status(ActiveGame.Gsd2).Contains("inactive in the current language"));
    TextMasterData.CurrentLanguage = TextMasterData.Lang.English;
});
Case("Shared user setting off prevents rewriting even inside a live scope", () =>
{
    using var scope = DialogueNative.Enter(ActiveGame.Gsd2, () => Snapshot(ActiveGame.Gsd2, S2Name));
    var setting = config.Bind("Dialogue", "ReplaceCharacterMentions", true, "");
    setting.Value = false;
    Check(Raw(camp, Camp) == camp && DialogueNative.Status(ActiveGame.Gsd2) == "Dialogue names: off.");
    setting.Value = true;
});
Game(ActiveGame.Gsd1);
Case("An authentic S1 low-ID body survives policy and obeys the native copy cap", () =>
{
    Check(low.Contains("Kwanda Rosman", StringComparison.Ordinal));
    int bound = 961;
    using var scope = DialogueNative.Enter(ActiveGame.Gsd1, () => Snapshot(ActiveGame.Gsd1, role => role == 25 ? "Kai" : null, cap: () => bound));
    string transformed = low.Replace("Kwanda Rosman", "Kai", StringComparison.Ordinal);
    Check(Raw(low, S1LowBody) == transformed);
    bound = transformed.Length - 1;
    Check(Raw(low, S1LowBody) == low);
    Check(Raw(camp, Camp) == camp);
});
Case("A dialogue patch failure unpatches only the dedicated group and fails open", () =>
{
    UnifiedPlugin.FailPatch = true;
    DialogueNative.Install(config);
    Check(!DialogueNative.Enabled && Harmony.Unpatched.SequenceEqual(new[] { UnifiedPlugin.PluginGuid + ".Dialogue" }));
    using var scope = DialogueNative.Enter(ActiveGame.Gsd1, () => Snapshot(ActiveGame.Gsd1, _ => "Kai"));
    Check(Raw(low, S1LowBody) == low);
});
Console.WriteLine($"Passed {passed} compiled bridge checks using original English text, embedded catalogs and native doubles.");
