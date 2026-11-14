using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace SuikodenRandomizer.Suikoden2Plugin;

/// <summary>Detached ordinary-direction views of three proved extra-animation field banks.</summary>
internal static class Gsd2NpcDirectionAdapters
{
    internal sealed class RetryGate
    {
        private readonly Dictionary<(int, IntPtr), float> _next = new();
        internal bool Begin(int donor, IntPtr source, float now)
        {
            var key = (donor, source);
            if (_next.TryGetValue(key, out float next) && now < next) return false;
            _next[key] = now + 1f;
            return true;
        }
        internal void Complete(int donor, IntPtr source) => _next.Remove((donor, source));
        internal void Clear() => _next.Clear();
    }

    private sealed class Spec
    {
        internal int Donor, Animations, Patterns;
        internal string Name = "", SourceHash = "", AdapterHash = "";
    }
    private sealed class Entry
    {
        internal int Donor;
        internal global::OPD_DATA Source = null!, Adapter = null!;
    }
    private static readonly Dictionary<IntPtr, Entry> Entries = new();
    private static readonly HashSet<IntPtr> Verified = new();
    private static readonly HashSet<string> Warned = new(StringComparer.Ordinal);
    // Native ANIME/OPDSprite objects can outlive the profile that created them.
    // Do not release these views or their source dependencies on campaign reset.
    private static readonly List<global::OPD_DATA> Retained = new();

    internal static bool IsSupportedDonor(int donor) => donor == 1 || donor == 48 || donor == 79;
    internal static bool IsVerified(global::OPD_DATA? data) => data is not null
        && data.Pointer != IntPtr.Zero && Verified.Contains(data.Pointer);
    internal static global::OPD_DATA? Find(int donor, global::OPD_DATA? source)
    {
        if (source is null || !Entries.TryGetValue(source.Pointer, out var entry) || entry.Donor != donor) return null;
        return entry.Adapter;
    }

    internal static global::OPD_DATA? Prepare(int donor, global::OPD_DATA? source)
    {
        var spec = Specification(donor);
        if (spec is null || source is null || source.Pointer == IntPtr.Zero) return null;
        if (Entries.TryGetValue(source.Pointer, out var existing)) return existing.Donor == donor ? existing.Adapter : null;
        global::OPD_DATA? adapter = null;
        try
        {
            if (source.name != spec.Name || source.anime_kazu != spec.Animations || source.pattern_kazu != spec.Patterns
                || source.anime_table is null || source.anime_table.Count != spec.Animations
                || source.pattern_table is null || source.pattern_table.Count != spec.Patterns
                || source.atlas is null || source.atlas.Pointer == IntPtr.Zero
                || source.sprites is null || source.normal_sprites is null
                || Fingerprint(source) != spec.SourceHash)
                throw new InvalidOperationException("Original bank identity, rows, geometry or ready resources differ.");
            var sourceSprites = source.sprites; var sourceNormals = source.normal_sprites;
            int spriteCount = sourceSprites.Count, normalCount = sourceNormals.Count;
            adapter = Create(source);
            if (Fingerprint(adapter) != spec.AdapterHash || Fingerprint(source) != spec.SourceHash
                || source.sprites.Pointer != sourceSprites.Pointer || source.normal_sprites.Pointer != sourceNormals.Pointer
                || source.sprites.Count != spriteCount || source.normal_sprites.Count != normalCount)
                throw new InvalidOperationException("Detached direction view failed readback or changed its source.");
            VerifyDetached(source, adapter);
            Retained.Add(source); Retained.Add(adapter);
            Entries.Add(source.Pointer, new Entry { Donor = donor, Source = source, Adapter = adapter });
            Verified.Add(adapter.Pointer);
            return adapter;
        }
        catch (Exception error)
        {
            Discard(adapter, source);
            if (Warned.Add(donor + ":" + source.Pointer))
                try { Gsd2Plugin.PluginLog.LogWarning("Ordinary NPC direction adapter skipped for donor " + donor
                    + "; canonical NPC sprite retained: " + error.Message); } catch { }
            return null;
        }
    }

    private static Spec? Specification(int donor) => donor switch
    {
        1 => new Spec { Donor = 1, Name = "shu", Animations = 19, Patterns = 28, SourceHash = "74852184370578e976f916fb37950272fcf5b94794ee28dc5a408ecd0244fc5f", AdapterHash = "9f320d33cf154abbf31657f989f3a31d21ec0f01d8c6b17387ca29cf123aebaf" },
        48 => new Spec { Donor = 48, Name = "muk", Animations = 7, Patterns = 17, SourceHash = "7c51c6cccf8a1059aab1e416c0bfd9e89266a1e76afb43e07105e0a9abac318b", AdapterHash = "01c0ec514440bd6b54fe9db0369064254781a7264ed66ca223798f325cdf5e1a" },
        79 => new Spec { Donor = 79, Name = "kkl", Animations = 8, Patterns = 19, SourceHash = "6e86654dc7962bc4e7db17994980ca24f170aaeacb4c9443a52d7ae4eb30d08e", AdapterHash = "e60a83d0bc7549f2b8e027419b12469ad476f8d82ec12576cf5de0de317f56f2" },
        _ => null
    };

    // Canonical binary encoding includes every field copied below. The constants
    // are generated from original asset metadata, independently of this decoder.
    private static string Fingerprint(global::OPD_DATA data)
    {
        using var bytes = new MemoryStream();
        using var writer = new BinaryWriter(bytes, Encoding.UTF8, true);
        writer.Write(data.name); writer.Write(data.id); writer.Write(data.path);
        writer.Write(data.anime_kazu); writer.Write(data.pattern_kazu);
        if (data.anime_table is null || data.anime_table.Count < 4 || data.pattern_table is null || data.pattern_table.Count < 12)
            throw new InvalidOperationException("Normal direction tables are incomplete.");
        for (int direction = 0; direction < 4; direction++)
        {
            var animation = data.anime_table[direction];
            if (animation is null || animation.timer_kazu != 4 || animation.timer_table is null || animation.timer_table.Count != 4)
                throw new InvalidOperationException("Normal direction timing is incomplete.");
            writer.Write((byte)animation.timer_kazu); writer.Write(animation.abe_flg);
            writer.Write(animation.timer_flg); writer.Write(animation.loop_flg); writer.Write(animation.timer_table.Count);
            foreach (var timer in animation.timer_table)
            {
                if (timer is null || timer.pattern_no < 0 || timer.pattern_no >= 12)
                    throw new InvalidOperationException("Normal direction references an extra pose.");
                writer.Write(timer.pattern_no); writer.Write(timer.timer);
            }
        }
        for (int index = 0; index < 12; index++)
        {
            var pattern = data.pattern_table[index];
            if (pattern is null || pattern.atari_data is null || pattern.atari_data.Count != 0
                || pattern.obj_table is null || pattern.obj_table.Count != 1)
                throw new InvalidOperationException("Normal pattern geometry is unsupported.");
            writer.Write(pattern.flag); writer.Write(pattern.center_x); writer.Write(pattern.center_y);
            writer.Write(pattern.size_x); writer.Write(pattern.size_y); writer.Write(pattern.padding0); writer.Write(pattern.padding1);
            writer.Write(pattern.atari_data.Count); writer.Write(pattern.obj_table.Count);
            foreach (var obj in pattern.obj_table)
            {
                if (obj is null) throw new InvalidOperationException("Normal pattern object is absent.");
                writer.Write(obj.flag); writer.Write(obj.offset_x); writer.Write(obj.offset_y);
                writer.Write(obj.size_x); writer.Write(obj.size_y); writer.Write(obj.u); writer.Write(obj.v);
                writer.Write(obj.sprite_name); writer.Write(obj.tmp_sprite_name);
                writer.Write(obj.overwriteBlendMode); writer.Write(obj.shadow_y);
            }
            writer.Write(pattern.isOverriteBlendMode);
        }
        writer.Flush();
        return Convert.ToHexString(SHA256.HashData(bytes.ToArray())).ToLowerInvariant();
    }

    private static global::OPD_DATA Create(global::OPD_DATA source)
    {
        var sourceAnimations = source.anime_table ?? throw new InvalidOperationException("Source animation table disappeared.");
        var sourcePatterns = source.pattern_table ?? throw new InvalidOperationException("Source pattern table disappeared.");
        var animations = new Il2CppSystem.Collections.Generic.List<global::OPD_DATA.AnimeTable>();
        for (int direction = 0; direction < 4; direction++)
        {
            var original = sourceAnimations[direction];
            var timers = new Il2CppSystem.Collections.Generic.List<global::OPD_DATA.AnimeTable.Timer>();
            foreach (var timer in original.timer_table ?? throw new InvalidOperationException("Source timer table disappeared."))
                timers.Add(new global::OPD_DATA.AnimeTable.Timer { pattern_no = timer.pattern_no, timer = timer.timer });
            animations.Add(new global::OPD_DATA.AnimeTable { timer_kazu = original.timer_kazu,
                abe_flg = original.abe_flg, timer_flg = original.timer_flg, loop_flg = original.loop_flg, timer_table = timers });
        }
        var patterns = new Il2CppSystem.Collections.Generic.List<global::OPD_DATA.PatternTable>();
        for (int index = 0; index < 12; index++)
        {
            var original = sourcePatterns[index];
            var collision = new Il2CppSystem.Collections.Generic.List<int>();
            foreach (int value in original.atari_data) collision.Add(value);
            var objects = new Il2CppSystem.Collections.Generic.List<global::OPD_DATA.PatternTable.Obj>();
            foreach (var obj in original.obj_table)
                objects.Add(new global::OPD_DATA.PatternTable.Obj { flag = obj.flag, offset_x = obj.offset_x,
                    offset_y = obj.offset_y, size_x = obj.size_x, size_y = obj.size_y, u = obj.u, v = obj.v,
                    sprite_name = obj.sprite_name, tmp_sprite_name = obj.tmp_sprite_name,
                    overwriteBlendMode = obj.overwriteBlendMode, shadow_y = obj.shadow_y });
            patterns.Add(new global::OPD_DATA.PatternTable { flag = original.flag,
                center_x = original.center_x, center_y = original.center_y, size_x = original.size_x, size_y = original.size_y,
                padding0 = original.padding0, padding1 = original.padding1, atari_data = collision, obj_table = objects,
                isOverriteBlendMode = original.isOverriteBlendMode });
        }
        global::OPD_DATA? adapter = null;
        try
        {
            adapter = ScriptableObject.CreateInstance<global::OPD_DATA>();
            if (adapter is null || adapter.Pointer == IntPtr.Zero || adapter.Pointer == source.Pointer || IsVerified(adapter))
                throw new InvalidOperationException("Unity did not create a detached OPD.");
            adapter.sprites = new Il2CppSystem.Collections.Generic.Dictionary<string, Sprite>();
            adapter.normal_sprites = new Il2CppSystem.Collections.Generic.Dictionary<string, Sprite>();
            adapter.id = source.id; adapter.path = source.path; adapter.name = source.name;
            adapter.anime_kazu = 4; adapter.anime_table = animations;
            adapter.pattern_kazu = 12; adapter.pattern_table = patterns;
            adapter.atlas = source.atlas; adapter.normal_atlas = source.normal_atlas;
            adapter.hideFlags |= HideFlags.DontUnloadUnusedAsset;
            return adapter;
        }
        catch { Discard(adapter, source); throw; }
    }

    private static void VerifyDetached(global::OPD_DATA source, global::OPD_DATA view)
    {
        var originalAnimations = source.anime_table; var animations = view.anime_table;
        var originalPatterns = source.pattern_table; var patterns = view.pattern_table;
        var originalSprites = source.sprites; var sprites = view.sprites;
        var originalNormals = source.normal_sprites; var normals = view.normal_sprites;
        if (view.Pointer == source.Pointer || view.anime_kazu != 4 || animations is null || animations.Count != 4
            || originalAnimations is null || originalPatterns is null || view.pattern_kazu != 12 || patterns is null || patterns.Count != 12
            || animations.Pointer == originalAnimations.Pointer || patterns.Pointer == originalPatterns.Pointer
            || sprites is null || normals is null || originalSprites is null || originalNormals is null
            || sprites.Pointer == originalSprites.Pointer || normals.Pointer == originalNormals.Pointer
            || sprites.Count != 0 || normals.Count != 0 || view.atlas is null || source.atlas is null || view.atlas.Pointer != source.atlas.Pointer
            || (view.normal_atlas?.Pointer ?? IntPtr.Zero) != (source.normal_atlas?.Pointer ?? IntPtr.Zero))
            throw new InvalidOperationException("Direction view aliases original tables or caches.");
        for (int direction = 0; direction < 4; direction++)
        {
            var original = originalAnimations[direction]; var copy = animations[direction];
            var originalTimers = original.timer_table; var timers = copy.timer_table;
            if (copy.Pointer == original.Pointer || timers is null || originalTimers is null || timers.Pointer == originalTimers.Pointer)
                throw new InvalidOperationException("Direction timing rows alias the source.");
            for (int frame = 0; frame < 4; frame++)
                if (timers[frame].Pointer == originalTimers[frame].Pointer)
                    throw new InvalidOperationException("Direction timer aliases the source.");
        }
        for (int index = 0; index < 12; index++)
        {
            var original = originalPatterns[index]; var copy = patterns[index];
            if (copy.Pointer == original.Pointer || copy.obj_table.Pointer == original.obj_table.Pointer
                || copy.atari_data.Pointer == original.atari_data.Pointer || copy.obj_table[0].Pointer == original.obj_table[0].Pointer)
                throw new InvalidOperationException("Direction geometry aliases the source.");
        }
    }

    private static void Discard(global::OPD_DATA? adapter, global::OPD_DATA source)
    {
        if (adapter is null || adapter.Pointer == IntPtr.Zero || adapter.Pointer == source.Pointer || IsVerified(adapter)) return;
        try
        {
            // OnDestroy clears both caches. Install fresh private dictionaries
            // before discarding even a partially initialized candidate.
            adapter.sprites = new Il2CppSystem.Collections.Generic.Dictionary<string, Sprite>();
            adapter.normal_sprites = new Il2CppSystem.Collections.Generic.Dictionary<string, Sprite>();
            UnityEngine.Object.Destroy(adapter);
        }
        catch { Retained.Add(adapter); } // Quarantine rather than touch a source cache.
    }
}
