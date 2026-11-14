using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using SuikodenRandomizer.AvatarPlugin;
using SuikodenRandomizer.Core.Treasure;

var prefix = typeof(AvatarTreasureItemPatch).GetMethod("Prefix", BindingFlags.NonPublic | BindingFlags.Static)!;
var finalizer = typeof(AvatarTreasureItemPatch).GetMethod("Finalizer", BindingFlags.NonPublic | BindingFlags.Static)!;
var profile = TreasureShuffleProfile.Create("treasure-runtime-regression");
var chest = S1TreasureCatalog.Chests.First(candidate => candidate.IsShuffleEligible
    && profile.TryGetReward(candidate.LocationId, out int itemId) && itemId != candidate.OriginalItemId);
int passed = 0;

void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

void Case(string title, Action test)
{
    AvatarRuntime.Current = new AvatarRuntime { TreasureProfile = profile };
    test();
    Console.WriteLine("PASS " + title);
    passed++;
}

Il2CppStructArray<byte> Native(IReadOnlyList<byte> bytes)
{
    var result = new Il2CppStructArray<byte>(bytes.Count);
    for (int index = 0; index < bytes.Count; index++) result[index] = bytes[index];
    return result;
}

Event_c Event(TreasureChest? selected = null)
{
    var source = selected ?? chest;
    return new Event_c
    {
        eventWK = new EVENT_WK { flag1 = 0x20, eventPut = Native(new byte[] { 8, (byte)source.FlagIndex, source.FlagMask, 6, 0, 6 }) },
        eventComTMP = Native(source.Script),
        eventComTMPindex = source.ItemOperandOffset
    };
}

object? Begin(Event_c native)
{
    object?[] arguments = { native, null };
    Check((bool)prefix.Invoke(null, arguments)!, "The patch must preserve the native grant call.");
    return arguments[1];
}

Exception? Finish(Event_c native, object? state, Exception? error = null)
    => (Exception?)finalizer.Invoke(null, new[] { (object?)native, state, error });

Case("Approved chest substitutes only the operand and restores the original script after native consumption", () =>
{
    var native = Event();
    var original = native.eventComTMP!;
    var state = Begin(native);
    Check(state is not null && native.eventComTMP!.Pointer != original.Pointer, "The command must use a detached temporary array.");
    profile.TryGetReward(chest.LocationId, out int reward);
    for (int index = 0; index < original.Count; index++)
    {
        Check(original[index] == chest.Script[index], "The shared original script was modified.");
        Check(native.eventComTMP![index] == (index == chest.ItemOperandOffset ? reward : original[index]), "A non-reward byte changed.");
    }
    native.eventComTMPindex++;
    native.eventKettei = 1;
    Check(Finish(native, state) is null, "Successful native calls must retain their null exception.");
    Check(native.eventComTMP!.Pointer == original.Pointer, "The original pointer must be restored.");
    Check(native.eventComTMPindex == chest.ItemOperandOffset + 1, "The native program counter must remain consumed.");
    Check(AvatarRuntime.Current!.Messages.Single().Contains("result=granted", StringComparison.Ordinal), "The grant should be logged.");
});

Case("Special finalization operand 0xff remains native", () =>
{
    var native = Event();
    native.eventComTMP![chest.ItemOperandOffset] = 0xFF;
    var original = native.eventComTMP;
    Check(Begin(native) is null && ReferenceEquals(original, native.eventComTMP), "The finalization operation was intercepted.");
});

Case("Protected recruitment rewards and Blinking Mirror retain native scripts", () =>
{
    foreach (var source in S1TreasureCatalog.Chests.Where(candidate => !candidate.IsShuffleEligible))
    {
        var native = Event(source);
        var original = native.eventComTMP;
        Check(Begin(native) is null && ReferenceEquals(original, native.eventComTMP), "A protected chest was changed.");
    }
});

Case("Unknown chest flags retain the native reward", () =>
{
    var native = Event();
    native.eventWK!.eventPut![1] = 239;
    native.eventWK.eventPut[2] = 128;
    var original = native.eventComTMP;
    Check(Begin(native) is null && ReferenceEquals(original, native.eventComTMP), "An unknown location was admitted.");
});

Case("A changed script is preserved and reported without skipping native initialization", () =>
{
    var native = Event();
    native.eventComTMP![0] ^= 1;
    var original = native.eventComTMP;
    Check(Begin(native) is null && ReferenceEquals(original, native.eventComTMP), "An unreviewed script was changed.");
    Check(AvatarRuntime.Current!.Messages.Single().Contains("TREASURE SKIPPED", StringComparison.Ordinal), "A script mismatch must be reported.");
    Check(AvatarRuntime.Current.FaultReason is null, "A preserved unsupported script should retain native play.");
});

Case("An unrelated item operand and ordinary NPC event retain native handling", () =>
{
    var native = Event();
    native.eventComTMPindex++;
    var original = native.eventComTMP;
    Check(Begin(native) is null && ReferenceEquals(original, native.eventComTMP), "An unrelated operand was intercepted.");
    native = Event();
    native.eventWK!.flag1 = 7;
    original = native.eventComTMP;
    Check(Begin(native) is null && ReferenceEquals(original, native.eventComTMP), "An NPC reward was intercepted.");
});

Case("Restoration does not overwrite a replacement script installed by native code", () =>
{
    var native = Event();
    var state = Begin(native);
    var nextScript = Native(new byte[] { 0xFD });
    native.eventComTMP = nextScript;
    native.eventKettei = 1;
    Finish(native, state);
    Check(ReferenceEquals(nextScript, native.eventComTMP), "Restoration overwrote a later native script.");
});

Case("Native exceptions retain their identity while the original script is restored and saving faults", () =>
{
    var native = Event();
    var original = native.eventComTMP;
    var state = Begin(native);
    var error = new InvalidOperationException("native test failure");
    Check(ReferenceEquals(error, Finish(native, state, error)), "The native exception was swallowed or replaced.");
    Check(ReferenceEquals(original, native.eventComTMP), "The script was not restored after an exception.");
    Check(AvatarRuntime.Current!.FaultReason?.Contains("Native treasure grant failed", StringComparison.Ordinal) == true,
        "A failed native grant should fault campaign saving.");
    Check(AvatarRuntime.Current.Messages.Count == 0, "A failed grant was logged as successful.");
});

Case("Full inventory keeps native result and command position for the discard menu", () =>
{
    var native = Event();
    var original = native.eventComTMP;
    var state = Begin(native);
    native.eventComTMPindex++;
    native.eventKettei = 0;
    Finish(native, state);
    Check(ReferenceEquals(original, native.eventComTMP) && native.eventKettei == 0,
        "Full-inventory state was changed before the native discard flow.");
    Check(native.eventComTMPindex == chest.ItemOperandOffset + 1, "The full-inventory command was replayed.");
    Check(AvatarRuntime.Current!.Messages.Single().Contains("result=inventory-full", StringComparison.Ordinal), "The full-inventory result must be identified.");
});

Case("Disabled treasure campaigns leave item grants untouched", () =>
{
    AvatarRuntime.Current!.Enabled = false;
    var native = Event();
    var original = native.eventComTMP;
    Check(Begin(native) is null && ReferenceEquals(original, native.eventComTMP), "A disabled campaign changed a reward.");
});

Case("An unexpected native grant result restores the script and faults saving", () =>
{
    var native = Event();
    var original = native.eventComTMP;
    var state = Begin(native);
    native.eventKettei = 2;
    Check(Finish(native, state) is null, "The patch invented a native exception.");
    Check(ReferenceEquals(original, native.eventComTMP), "The script was not restored before result validation.");
    Check(AvatarRuntime.Current!.FaultReason is not null, "An unknown grant result must block saving.");
});

Console.WriteLine(passed + " treasure runtime adapter checks passed.");
