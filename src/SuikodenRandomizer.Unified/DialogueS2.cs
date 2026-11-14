extern alias GSD2;

using System;
using HarmonyLib;
using SuikodenRandomizer.Shared;
using SuikodenRandomizer.Suikoden2Plugin;

namespace SuikodenRandomizer.Unified;

/// <summary>Admits only proved S2 dialogue callers and the exact live name publication.</summary>
internal static class DialogueS2
{
    internal static Type[] PatchTypes { get; } =
    {
        typeof(DialogueS2WindowStepPatch), typeof(DialogueS2BacklogPatch),
        typeof(DialogueS2NamePatch), typeof(DialogueS2NameIdPatch)
    };

    internal static NativeDialogueContext? Capture()
    {
        try
        {
            var runtime = Gsd2Runtime.Current;
            // Full graphics readiness is checked once on entry. Later probes
            // need the name publication, not a rebuilt graphics signature.
            if (runtime?.CanUseDialogueNames != true || !runtime.IsActive) return null;
            var profile = runtime.Profile;
            if (profile is null) return null;
            long epoch = runtime.ExecutionEpoch;
            string fingerprint = profile.Fingerprint;
            var publishedGame = runtime.DialoguePublishedGame;
            var publishedCharacters = runtime.DialoguePublishedCharacters;
            var publishedNames = runtime.DialoguePublishedNames;
            var probe = new Snapshot(runtime, epoch, profile, fingerprint,
                publishedGame, publishedCharacters, IntPtr.Zero, publishedNames);
            if (!probe.ReadNativeIdentity(out var owner)) return null;
            var snapshot = new Snapshot(runtime, epoch, profile, fingerprint,
                publishedGame, publishedCharacters, owner, publishedNames);
            if (!snapshot.IsCurrent()) return null;
            return new NativeDialogueContext("GSD2", snapshot.IsCurrent, snapshot.ResolveName,
                () => null, snapshot.ShouldReplaceRole);
        }
        catch { return null; }
    }

    internal static IDisposable? BeginScope()
    {
        try { return DialogueNative.Enter(ActiveGame.Gsd2, Capture); }
        catch { return null; }
    }

    internal static IDisposable? BeginNameSuppression()
    {
        try { return DialogueNative.SuppressNames(); }
        catch { return null; }
    }

    internal static Exception? Finish(Exception? original, IDisposable? state)
    {
        // This presentation adapter never replaces an original native exception.
        try { state?.Dispose(); } catch { }
        return original;
    }

    private sealed class Snapshot
    {
        private readonly Gsd2Runtime _runtime;
        private readonly long _epoch;
        private readonly object _profile;
        private readonly string _fingerprint;
        private readonly IntPtr _game, _characters, _nameOwner, _names;

        internal Snapshot(Gsd2Runtime runtime, long epoch, object profile, string fingerprint,
            IntPtr game, IntPtr characters, IntPtr nameOwner, IntPtr names)
        {
            _runtime = runtime; _epoch = epoch; _profile = profile; _fingerprint = fingerprint;
            _game = game; _characters = characters; _nameOwner = nameOwner; _names = names;
        }

        private bool ManagedCurrent()
        {
            if (!ReferenceEquals(Gsd2Runtime.Current, _runtime) || !_runtime.CanUseDialogueNames
                || _runtime.ExecutionEpoch != _epoch || !ReferenceEquals(_runtime.Profile, _profile)) return false;
            return string.Equals(_runtime.Profile.Fingerprint, _fingerprint, StringComparison.Ordinal)
                && _game != IntPtr.Zero && _characters != IntPtr.Zero && _names != IntPtr.Zero
                && _runtime.DialoguePublishedGame == _game && _runtime.DialoguePublishedCharacters == _characters
                && _runtime.DialoguePublishedNames == _names;
        }

        internal bool ReadNativeIdentity(out IntPtr nameOwner)
        {
            nameOwner = IntPtr.Zero;
            try
            {
                if (!ManagedCurrent()) return false;
                var game = GSD2.OldSrcBase.game_work;
                if (!ManagedCurrent() || game is null || game.Pointer != _game) return false;
                var characters = game.chara_data;
                if (!ManagedCurrent() || characters is null || characters.Pointer != _characters) return false;
                var owner = game.c_teg_dat;
                if (!ManagedCurrent() || owner is null || owner.Pointer == IntPtr.Zero) return false;
                var table = owner.name;
                if (!ManagedCurrent() || table is null || table.Pointer != _names) return false;
                nameOwner = owner.Pointer;
                return ManagedCurrent();
            }
            catch { return false; }
        }

        internal bool IsCurrent() => ReadNativeIdentity(out var owner) && owner == _nameOwner;

        internal bool ShouldReplaceRole(int role)
        {
            try
            {
                // Source hero/guest names remain protected. An ordinary role
                // whose donor is the hero still resolves the current native label.
                if (role < 2 || role > 79 || !IsCurrent() || !_runtime.IsRole(role)
                    || _runtime.MapDisplayId(role) == role) return false;
                return IsCurrent();
            }
            catch { return false; }
        }

        internal string? ResolveName(int role)
        {
            try
            {
                if (!ShouldReplaceRole(role)) return null;
                string? result;
                using (DialogueNative.SuppressNames())
                {
                    if (!IsCurrent()) return null;
                    // The live table already contains the donor at this role.
                    // Mapping role to donor here would apply a shuffle twice.
                    result = GSD2.G2_SYS.G2_name_get(role);
                }
                return IsCurrent() ? result : null;
            }
            catch { return null; }
        }
    }
}

[HarmonyPatch(typeof(GSD2.EVENTCON.___WindowCreate_d__413), nameof(GSD2.EVENTCON.___WindowCreate_d__413.MoveNext))]
internal static class DialogueS2WindowStepPatch
{
    [HarmonyPrefix] private static void Prefix(out IDisposable? __state) => __state = DialogueS2.BeginScope();
    [HarmonyFinalizer] private static Exception? Finalizer(Exception? __exception, IDisposable? __state)
        => DialogueS2.Finish(__exception, __state);
}

[HarmonyPatch(typeof(GSD2.WindowManager), nameof(GSD2.WindowManager.ConvertTagToStringFromLogData), new Type[] { typeof(string) })]
internal static class DialogueS2BacklogPatch
{
    [HarmonyPrefix] private static void Prefix(out IDisposable? __state) => __state = DialogueS2.BeginScope();
    [HarmonyFinalizer] private static Exception? Finalizer(Exception? __exception, IDisposable? __state)
        => DialogueS2.Finish(__exception, __state);
}

[HarmonyPatch(typeof(GSD2.G2_SYS), nameof(GSD2.G2_SYS.G2_name_get), new Type[] { typeof(int) })]
internal static class DialogueS2NamePatch
{
    [HarmonyPrefix] private static void Prefix(out IDisposable? __state) => __state = DialogueS2.BeginNameSuppression();
    [HarmonyFinalizer] private static Exception? Finalizer(Exception? __exception, IDisposable? __state)
        => DialogueS2.Finish(__exception, __state);
}

[HarmonyPatch(typeof(GSD2.G2_SYS), nameof(GSD2.G2_SYS.G2_name_get_org), new Type[] { typeof(int) })]
internal static class DialogueS2NameIdPatch
{
    [HarmonyPrefix] private static void Prefix(out IDisposable? __state) => __state = DialogueS2.BeginNameSuppression();
    [HarmonyFinalizer] private static Exception? Finalizer(Exception? __exception, IDisposable? __state)
        => DialogueS2.Finish(__exception, __state);
}
