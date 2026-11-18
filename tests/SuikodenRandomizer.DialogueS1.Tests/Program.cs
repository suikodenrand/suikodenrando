extern alias GSD1;
using HarmonyLib;
using SuikodenRandomizer.AvatarPlugin;
using SuikodenRandomizer.Shared;
using SuikodenRandomizer.Unified;
using System.Reflection;
using System.Text.Json;

var cases = new List<string>();
void Check(bool value, string why) { if (!value) throw new InvalidOperationException(why); }
void Case(string name, Action run) { run(); cases.Add(name); Console.WriteLine("PASS " + name); }
AvatarRuntime World()
{
    var runtime = new AvatarRuntime();
    DialogueNative.Current = null; DialogueNative.ThrowEnter = DialogueNative.ThrowSuppress = false;
    DialogueNative.NameDepth = DialogueNative.Enters = DialogueNative.Disposes = 0;
    GSD1.Fhuman_c.Calls.Clear(); GSD1.Fhuman_c.Resolve = (id, _) => "Name" + id;
    GSD1.G1_event_h.Value = 961; GSD1.G1_event_h.Reads = 0; GSD1.G1_event_h.ThrowOnRead = false;
    return runtime;
}
NativeDialogueContext Context() => DialogueS1.Capture() ?? throw new InvalidOperationException("Expected active context");
IDisposable? Prefix(Type type)
{
    object?[] values = { null };
    type.GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, values);
    return (IDisposable?)values[0];
}
Exception? Finalize(Type type, Exception? original, IDisposable? scope)
    => (Exception?)type.GetMethod("Finalizer", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new object?[] { original, scope });

Case("The adapter exposes seven exact native method contracts without ID rewrite arguments", () =>
{
    Check(DialogueS1.PatchTypes.Length == 7 && DialogueS1.PatchTypes.Distinct().Count() == 7, "Patch inventory changed");
    foreach (var type in DialogueS1.PatchTypes)
    {
        var patch = type.GetCustomAttribute<HarmonyPatch>()!;
        var method = patch.DeclaringType.GetMethod(patch.MethodName, patch.Arguments);
        Check(method is not null, "Missing exact target " + patch.MethodName);
        var prefix = type.GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic)!;
        Check(prefix.GetParameters().Length == 1 && prefix.GetParameters()[0].IsOut
            && prefix.GetParameters()[0].ParameterType == typeof(IDisposable).MakeByRefType(), "Prefix can rewrite native args");
        if (type == typeof(DialogueS1LoadBarrierPatch) || type == typeof(DialogueS1SaveBarrierPatch))
            Check(prefix.GetCustomAttribute<HarmonyPriority>()?.Value == int.MaxValue, "Transaction barrier no longer runs first");
    }
    var first = DialogueS1.PatchTypes; first[0] = typeof(string);
    Check(DialogueS1.PatchTypes[0] == typeof(DialogueS1EventMessagePatch), "Caller changed patch registry");
});
Case("Inactive, preparing, fixed-loading and save-scope states do not admit text", () =>
{
    var runtime = World(); runtime.Active = false; Check(DialogueS1.Capture() is null, "Inactive admitted");
    runtime = World(); runtime.Change("preparing"); Check(DialogueS1.Capture() is null, "Preparing admitted");
    runtime = World(); runtime.Change("loading"); Check(DialogueS1.Capture() is null, "Fixed loading admitted");
    runtime = World();
    foreach (bool nativePassthrough in new[] { false, true })
    {
        AvatarSaveCall.Current = new AvatarSaveCall { Runtime = runtime, ExecutionEpoch = runtime.Epoch, Previous = null, NativePassthrough = nativePassthrough };
        Check(DialogueS1.Capture() is null, "Saving admitted");
    }
    AvatarSaveCall.Current = null;
});
Case("Publication captures exact runtime and campaign and rejects every replaced identity", () =>
{
    foreach (string change in new[] { "state", "profile", "assignments", "originals", "proxies", "manager", "game", "preparing", "loading" })
    {
        var runtime = World(); var context = Context(); Check(context.IsCurrent(), "Initial publication rejected");
        runtime.Change(change); Check(!context.IsCurrent(), "Stale " + change + " stayed current");
    }
    World(); var old = Context(); AvatarRuntime.Current = new AvatarRuntime(); Check(!old.IsCurrent(), "Different runtime accepted");
});
Case("Foreign ownership and changed epochs reject before native singleton reads", () =>
{
    foreach (bool epoch in new[] { false, true })
    {
        var runtime = World(); var context = Context();
        if (epoch) runtime.Epoch++; else runtime.Owns = false;
        GSD1.OldSrcBase.Reads = GSD1.GlobalWork.Reads = 0; GSD1.OldSrcBase.ThrowOnRead = true;
        Check(!context.IsCurrent() && context.ResolveName(28) is null && context.MaxUtf16Length() == 0, "Foreign context accepted");
        Check(GSD1.OldSrcBase.Reads == 0 && GSD1.GlobalWork.Reads == 0, "Foreign context dereferenced native singletons");
    }
    GSD1.OldSrcBase.ThrowOnRead = false;
});
Case("Actual native game or manager replacement invalidates the captured publication", () =>
{
    World(); var context = Context(); GSD1.OldSrcBase.game_work = new GSD1.GAME_WORK(); Check(!context.IsCurrent(), "New game pointer accepted");
    World(); context = Context(); GSD1.GlobalWork.Instance!.player_manager = new GSD1.PlayerManager(); Check(!context.IsCurrent(), "New manager accepted");
});
Case("Canonical role zero and regular full-name request reach native resolution once under suppression", () =>
{
    World(); var context = Context();
    GSD1.Fhuman_c.Resolve = (id, needShort) =>
    { Check(DialogueNative.NameDepth == 1 && id == 0 && !needShort, "Wrong native name contract"); return "Gengen"; };
    Check(context.ResolveName(0) == "Gengen", "Role zero treated as empty");
    Check(GSD1.Fhuman_c.Calls.SequenceEqual(new[] { (0, false) }) && DialogueNative.NameDepth == 0, "Name double-mapped or suppression leaked");
});
Case("Fixed-unassigned, protected and identity roles preserve their source span", () =>
{
    World(); var context = Context();
    Check(context.ShouldReplaceRole!(0) && context.ShouldReplaceRole(28), "Mapped role not replaceable");
    foreach (int role in new[] { 22, 8, 108, 109, -1, 999 }) Check(!context.ShouldReplaceRole(role), "Unchanged role selected: " + role);
    Check(context.ResolveName(8) is null && GSD1.Fhuman_c.Calls.Count == 0, "Protected/unassigned role reached name getter");
});
Case("Ownership loss inside a name lookup rejects the returned value and unwinds suppression", () =>
{
    var runtime = World(); var context = Context();
    GSD1.Fhuman_c.Resolve = (_, _) => { runtime.Epoch++; return "stale donor"; };
    Check(context.ResolveName(28) is null && DialogueNative.NameDepth == 0, "Stale returned name escaped");
});
Case("Throwing name resolution and suppression failures remain presentation-only", () =>
{
    World(); var context = Context(); GSD1.Fhuman_c.Resolve = (_, _) => throw new InvalidOperationException("Native name failed");
    Check(context.ResolveName(28) is null && DialogueNative.NameDepth == 0, "Throwing getter leaked suppression");
    DialogueNative.ThrowSuppress = true; GSD1.Fhuman_c.Calls.Clear();
    Check(context.ResolveName(28) is null && GSD1.Fhuman_c.Calls.Count == 0, "Suppression failure ran native name call");
    DialogueNative.ThrowSuppress = false;
});
Case("S1 uses the current native961 or481 UTF16 limit and fails closed otherwise", () =>
{
    World(); var context = Context();
    foreach (int bound in new[] { 961, 481 }) { GSD1.G1_event_h.Value = bound; Check(context.MaxUtf16Length() == bound, "Wrong native limit"); }
    GSD1.G1_event_h.Value = 999; Check(context.MaxUtf16Length() == 0, "Unproved limit accepted");
    GSD1.G1_event_h.ThrowOnRead = true; Check(context.MaxUtf16Length() == 0, "Failed getter became unbounded");
});
Case("Body, choice preparation and backlog scopes enter Gsd1 and restore nested context", () =>
{
    World(); var a = Prefix(typeof(DialogueS1EventMessagePatch)); var first = DialogueNative.Current;
    var b = Prefix(typeof(DialogueS1EventMessage2Patch)); var second = DialogueNative.Current;
    var c = Prefix(typeof(DialogueS1BacklogPatch));
    Check(DialogueNative.LastGame == ActiveGame.Gsd1 && DialogueNative.Enters == 3 && DialogueNative.Current?.Game == "GSD1", "Wrong bridge entry");
    Finalize(typeof(DialogueS1BacklogPatch), null, c); Check(ReferenceEquals(DialogueNative.Current, second), "Backlog did not restore previous");
    Finalize(typeof(DialogueS1EventMessage2Patch), null, b); Check(ReferenceEquals(DialogueNative.Current, first), "Message2 did not restore previous");
    Finalize(typeof(DialogueS1EventMessagePatch), null, a); Check(DialogueNative.Current is null && DialogueNative.Disposes == 3, "Body scope leaked");
});
Case("Both name-entry methods suppress recursive raw lookup through nested calls", () =>
{
    World(); var a = Prefix(typeof(DialogueS1NamePatch)); var b = Prefix(typeof(DialogueS1NameTextIdPatch));
    Check(DialogueNative.NameDepth == 2, "Nested name lookup not suppressed");
    Finalize(typeof(DialogueS1NameTextIdPatch), null, b); Check(DialogueNative.NameDepth == 1, "Inner scope erased outer suppression");
    Finalize(typeof(DialogueS1NamePatch), null, a); Check(DialogueNative.NameDepth == 0, "Name suppression leaked");
});
Case("Native load and save barriers suppress capture and existing contexts until all nested calls exit", () =>
{
    World(); var context = Context(); var load = Prefix(typeof(DialogueS1LoadBarrierPatch));
    Check(DialogueS1.Capture() is null && !context.IsCurrent(), "Native load admitted text");
    var save = Prefix(typeof(DialogueS1SaveBarrierPatch)); Finalize(typeof(DialogueS1LoadBarrierPatch), null, load);
    Check(DialogueS1.Capture() is null && !context.IsCurrent(), "Nested save barrier lost");
    Finalize(typeof(DialogueS1SaveBarrierPatch), null, save); Check(context.IsCurrent() && DialogueS1.Capture() is not null, "Barrier failed to unwind");
    Finalize(typeof(DialogueS1SaveBarrierPatch), null, save); Check(context.IsCurrent(), "Double dispose damaged depth");
});
Case("Every finalizer preserves the exact original exception even when disposal throws", () =>
{
    World(); var original = new InvalidOperationException("Native original");
    foreach (var type in DialogueS1.PatchTypes)
    {
        var state = Prefix(type); Check(ReferenceEquals(Finalize(type, original, state), original), "Native exception replaced");
        Check(ReferenceEquals(Finalize(type, original, new ThrowingScope()), original), "Cleanup exception replaced original");
        Check(Finalize(type, null, new ThrowingScope()) is null, "Cleanup failed native success");
    }
    Check(DialogueNative.NameDepth == 0 && DialogueS1.Capture() is not null, "Finalizer left suppression behind");
});
Case("Bridge-entry failure never fails a native message prefix", () =>
{
    World(); DialogueNative.ThrowEnter = true; Check(Prefix(typeof(DialogueS1EventMessagePatch)) is null, "Bridge failure escaped");
    DialogueNative.ThrowEnter = false;
});

Console.WriteLine(cases.Count + "/" + cases.Count + " linked S1 dialogue adapter/snapshot checks passed.");
if (args.Length == 1)
    File.WriteAllText(args[0], JsonSerializer.Serialize(new { passed = cases.Count, failed = 0, cases,
        scope = "Actual linked DialogueS1 and AvatarRuntime.Dialogue with managed native/bridge/admission doubles; no Harmony detour execution or native gameplay qualification." },
        new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);

internal sealed class ThrowingScope : IDisposable { public void Dispose() => throw new InvalidOperationException("Cleanup failed"); }
