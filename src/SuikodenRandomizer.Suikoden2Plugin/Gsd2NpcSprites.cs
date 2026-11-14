extern alias GSD2;

using System;
using System.Collections.Generic;
using HarmonyLib;

namespace SuikodenRandomizer.Suikoden2Plugin;

/// <summary>Projects only proven ordinary NPC sprite factory inputs; story actors stay native.</summary>
internal static class Gsd2NpcSprites
{
    [ThreadStatic] private static Scope? _current;
    [ThreadStatic] private static int _factoryDepth;
    private static readonly object LogLock = new();
    private static Gsd2Runtime? _logRuntime;
    private static object? _logBinding;
    private static readonly HashSet<int> Confirmed = new();
    private static readonly HashSet<string> Warnings = new(StringComparer.Ordinal);

    internal sealed class Scope
    {
        internal Scope? Previous;
        internal Gsd2Runtime? Runtime;
        internal object? Binding;
        internal Dictionary<IntPtr, Projection> Candidates = new();
    }

    internal sealed class Projection
    {
        internal int Canonical;
        internal global::OPD_DATA Original = null!;
        internal global::OPD_DATA Donor = null!;
    }

    internal sealed class FactoryScope
    {
        internal int PreviousDepth;
        internal Scope? Context;
        internal Projection? Projection;
    }

    internal static Scope Enter(GSD2.EVENTCON owner)
    {
        // Even an unsupported nested call creates a barrier to the outer context.
        var scope = new Scope { Previous = _current };
        _current = scope;
        var runtime = Gsd2Runtime.Current;
        if (runtime?.IsActive != true) return scope;
        scope.Runtime = runtime;
        scope.Binding = runtime.Profile;
        if (scope.Binding is null) return scope;
        try
        {
            var actors = owner.ehuman;
            int count = owner.okaz; // Original EVENTCON+0x2c; native loop ends at8+okaz.
            if (actors is null || count < 0 || count > 255 || actors.Count < 8 + count) return scope;
            int end = 8 + count;
            var blocked = new HashSet<IntPtr>();
            for (int index = 0; index < end; index++)
            {
                var actor = actors[index];
                if (actor is not null && (actor.otyp == 3 || actor.otyp == 4) && actor.opd is not null)
                    blocked.Add(actor.opd.Pointer);
            }
            var candidates = new Dictionary<IntPtr, Projection>();
            for (int index = 8; index < end; index++)
            {
                var actor = actors[index];
                if (actor is null || (actor.otyp != 1 && actor.otyp != 2) || actor.ano < 0 || actor.ano > 3) continue;
                var original = actor.opd;
                if (original is null || original.Pointer == IntPtr.Zero || blocked.Contains(original.Pointer)
                    || !FourDirections(original)
                    || !Gsd2NpcSpriteAliases.TryResolve(original.name, out int canonical)
                    || !runtime.IsRole(canonical)) continue;
                // This accessor returns the canonical role's ALREADY projected field bank.
                // Looking up the donor ID again would apply a permutation twice.
                var donor = Gsd2Graphics.NpcFieldOpd(canonical);
                if (donor is null || donor.Pointer == IntPtr.Zero || donor.Pointer == original.Pointer
                    || blocked.Contains(donor.Pointer) || (!Gsd2NpcSpriteAliases.SupportsNormalDirections(donor.name)
                        && !Gsd2NpcDirectionAdapters.IsVerified(donor))
                    || !FourDirections(donor) || !DonorReady(donor)) continue;
                if (candidates.TryGetValue(original.Pointer, out var prior)
                    && (prior.Canonical != canonical || prior.Donor.Pointer != donor.Pointer))
                {
                    candidates.Remove(original.Pointer);
                    blocked.Add(original.Pointer);
                    continue;
                }
                candidates[original.Pointer] = new Projection { Canonical = canonical, Original = original, Donor = donor };
            }
            // Publish only after the complete context was checked. No actor fields change.
            scope.Candidates = candidates;
        }
        catch (Exception error)
        {
            scope.Candidates.Clear();
            Warn(scope, "context", "NPC sprite context could not be qualified; canonical sprites retained: " + error.Message);
        }
        return scope;
    }

    internal static void Exit(Scope? scope)
    {
        if (scope is not null) _current = scope.Previous;
    }

    private static bool FourDirections(global::OPD_DATA data)
    {
        var animations = data.anime_table;
        var patterns = data.pattern_table;
        if (data.anime_kazu != 4 || animations is null || animations.Count != 4
            || data.pattern_kazu < 1 || patterns is null || patterns.Count != data.pattern_kazu) return false;
        for (int direction = 0; direction < 4; direction++)
        {
            var animation = animations[direction];
            var timers = animation?.timer_table;
            if (animation is null || timers is null || animation.timer_kazu < 1 || timers.Count != animation.timer_kazu) return false;
            for (int frame = 0; frame < timers.Count; frame++)
            {
                var timer = timers[frame];
                if (timer is null || timer.pattern_no < 0 || timer.pattern_no >= patterns.Count
                    || patterns[timer.pattern_no] is null) return false;
            }
        }
        return true;
    }

    private static bool DonorReady(global::OPD_DATA data)
    {
        // Native GetSprite requires these existing caches and the atlas. First
        // draw creates renderer patterns; this check never initializes resources.
        var atlas = data.atlas;
        if (atlas is null || atlas.Pointer == IntPtr.Zero || data.sprites is null) return false;
        var normal = data.normal_atlas;
        return normal is null || normal.Pointer == IntPtr.Zero || data.normal_sprites is not null;
    }

    private static bool Current(Scope? scope) => scope?.Runtime is not null && scope.Binding is not null
        && ReferenceEquals(Gsd2Runtime.Current, scope.Runtime) && scope.Runtime.IsActive
        && ReferenceEquals(scope.Runtime.Profile, scope.Binding);

    internal static FactoryScope BeginFactory(ref global::OPD_DATA? data, int animation, int type, bool isCharacter)
    {
        var state = new FactoryScope { PreviousDepth = _factoryDepth, Context = _current };
        _factoryDepth++;
        if (state.PreviousDepth != 0 || !Current(state.Context) || type != 1 || isCharacter
            || animation < 0 || animation > 3 || data is null) return state;
        try
        {
            if (state.Context!.Candidates.TryGetValue(data.Pointer, out var projection))
            {
                state.Projection = projection;
                data = projection.Donor;
            }
        }
        catch (Exception error)
        {
            if (state.Projection is not null) data = state.Projection.Original;
            state.Projection = null;
            Warn(state.Context!, "factory-prefix", "NPC sprite projection skipped; canonical factory input retained: " + error.Message);
        }
        return state;
    }

    internal static void VerifyFactory(FactoryScope? state, GSD2.ANIME? result)
    {
        var projection = state?.Projection;
        if (projection is null || !Current(state!.Context)) return;
        var context = state.Context!;
        try
        {
            var actual = result?.data;
            if (actual is not null && actual.Pointer == projection.Donor.Pointer)
            {
                bool first;
                lock (LogLock) { LogBinding(context); first = Confirmed.Add(projection.Canonical); }
                if (first) context.Runtime!.Trace("NPC SPRITE native factory confirmed canonical role " + projection.Canonical + "; story actor references retained.");
                return;
            }
            Warn(context, "factory-result-" + projection.Canonical,
                "NPC sprite native factory did not confirm the projected OPD for canonical role " + projection.Canonical + ".");
            // If ref-argument interception is ineffective, the native canonical result
            // is already the safe fallback. Never rewrite actor/data/cache references.
            if (actual is null || actual.Pointer != projection.Original.Pointer)
                context.Runtime!.Fail("NPC sprite factory returned an unexpected resource; further native dispatch is held.");
        }
        catch (Exception error)
        {
            Warn(context, "factory-verification", "NPC sprite factory verification failed: " + error.Message);
            context.Runtime!.Fail("NPC sprite factory resource could not be verified; further native dispatch is held.");
        }
    }

    internal static void EndFactory(FactoryScope? state)
    {
        if (state is not null) _factoryDepth = state.PreviousDepth;
    }

    private static void LogBinding(Scope context)
    {
        if (ReferenceEquals(_logRuntime, context.Runtime) && ReferenceEquals(_logBinding, context.Binding)) return;
        _logRuntime = context.Runtime; _logBinding = context.Binding;
        Confirmed.Clear(); Warnings.Clear();
    }

    private static void Warn(Scope context, string key, string message)
    {
        bool first;
        lock (LogLock) { LogBinding(context); first = Warnings.Add(key); }
        if (first) Gsd2Plugin.PluginLog.LogWarning(message);
    }
}

[HarmonyPatch(typeof(GSD2.EVENTCON), "EventHitoAnimeCreate", new Type[] { })]
internal static class Gsd2NpcSpriteContextPatch
{
    [HarmonyPrefix] internal static void Prefix(GSD2.EVENTCON __instance, out Gsd2NpcSprites.Scope __state) =>
        __state = Gsd2NpcSprites.Enter(__instance);
    [HarmonyFinalizer] internal static Exception? Finalizer(Exception? __exception, Gsd2NpcSprites.Scope? __state)
    { Gsd2NpcSprites.Exit(__state); return __exception; }
}

[HarmonyPatch(typeof(GSD2.ANIME), "AnimeCreate", new Type[] { typeof(global::OPD_DATA), typeof(int), typeof(int), typeof(bool), typeof(string), typeof(bool) })]
internal static class Gsd2NpcSpriteFactoryPatch
{
    [HarmonyPrefix] internal static void Prefix(ref global::OPD_DATA? __0, int __1, int __2, bool __3, out Gsd2NpcSprites.FactoryScope __state) =>
        __state = Gsd2NpcSprites.BeginFactory(ref __0, __1, __2, __3);
    [HarmonyPostfix] internal static void Postfix(GSD2.ANIME? __result, Gsd2NpcSprites.FactoryScope? __state) =>
        Gsd2NpcSprites.VerifyFactory(__state, __result);
    [HarmonyFinalizer] internal static Exception? Finalizer(Exception? __exception, Gsd2NpcSprites.FactoryScope? __state)
    { Gsd2NpcSprites.EndFactory(__state); return __exception; }
}
