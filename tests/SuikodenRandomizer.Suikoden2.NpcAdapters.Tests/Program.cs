using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using SuikodenRandomizer.Suikoden2Plugin;

internal static class Program
{
    private static readonly JsonDocument Fixtures = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures/original-small-field-banks.json")));
    private static readonly JsonSerializerOptions Json = new() { IncludeFields = true, Converters = { new SerializedByteBoolean() } };
    private static void Main()
    {
        var tests = new (string, Action)[] {
            ("all three original metadata fixtures create exact4/12 native views", OriginalFixtures),
            ("nested tables, timers, geometry and caches are independently owned", IndependentOwnership),
            ("original timing and mirror flags survive complete pattern copying", TimingAndMirror),
            ("wrong identity, count, selected row, geometry and resource states reject", RejectChangedSources),
            ("unused original extra poses remain inaccessible in the view", ExtraPosesExcluded),
            ("registered adapters pass the actual NPC factory boundary once", FactoryIntegration),
            ("whole custom banks and counterfeit named four-row banks still reject", NoNameBypass),
            ("custom poses, character factories and HD shared banks retain native input", ExistingBoundaries),
            ("one source pointer reuses its view; another source owns a distinct view", Reuse),
            ("campaign turnover retains old renderer resources without table reset", Retirement),
            ("allocation failure remains optional and can retry without source writes", AllocationFailure),
            ("failed readback destroys only private candidate caches and publishes nothing", FailedReadback),
            ("failed cleanup quarantines its candidate without clearing original caches", CleanupFailure),
            ("optional normal atlas shares only its atlas reference and owns both caches", OptionalNormalAtlas),
            ("Unity pointer aliasing cannot destroy a source or a previously verified view", PointerAliasing),
            ("optional warning failure cannot fault native campaign dispatch", WarningFailure),
            ("failed automatic attempts back off per donor/source; success/reset clear only timers", RetryBackoff)
        };
        foreach (var (name, run) in tests)
        {
            UnityEngine.ScriptableObject.CreationHook = null; UnityEngine.ScriptableObject.ThrowCreation = false;
            UnityEngine.Object.BeforeDestroy = null;
            Gsd2Runtime.Current = new(); Gsd2Graphics.Projected.Clear(); Gsd2Graphics.Requests.Clear();
            Gsd2Plugin.PluginLog.Warnings.Clear();
            run(); Console.WriteLine("PASS " + name);
        }
        Console.WriteLine(tests.Length + "/" + tests.Length + " production-linked NPC adapter tests passed.");
    }
    private static OPD_DATA Source(int donor)
    {
        var row = Fixtures.RootElement.GetProperty("rows").EnumerateArray().Single(x => x.GetProperty("donorId").GetInt32() == donor);
        var bank = row.GetProperty("bank");
        var data = JsonSerializer.Deserialize<OPD_DATA>(bank.GetRawText(), Json)!;
        data.name = bank.GetProperty("m_Name").GetString()!;
        data.atlas = new NativeObject(); data.normal_atlas = null;
        data.sprites = new(); data.normal_sprites = new();
        data.sprites.Add("existing-original-cache", new UnityEngine.Sprite());
        data.normal_sprites.Add("existing-original-normal-cache", new UnityEngine.Sprite());
        return data;
    }
    private static OPD_DATA View(int donor, OPD_DATA source) => Gsd2NpcDirectionAdapters.Prepare(donor, source)
        ?? throw new Exception("Qualified original source was rejected: " + string.Join(";", Gsd2Plugin.PluginLog.Warnings));
    private static void True(bool value) { if (!value) throw new Exception("Assertion failed."); }
    private static void Eq<T>(T a, T b) { if (!EqualityComparer<T>.Default.Equals(a, b)) throw new Exception("Expected " + a + ", got " + b); }
    private static void Ptr(NativeObject? a, NativeObject? b) => Eq(a?.Pointer ?? IntPtr.Zero, b?.Pointer ?? IntPtr.Zero);
    private static void OriginalFixtures()
    {
        foreach (int donor in new[] { 1, 48, 79 })
        {
            var source = Source(donor); var view = View(donor, source);
            True(view.Pointer != source.Pointer); True(Gsd2NpcDirectionAdapters.IsVerified(view));
            Eq(view.Pointer, Gsd2NpcDirectionAdapters.Find(donor, source)!.Pointer);
            Eq(4, view.anime_kazu); Eq(4, view.anime_table!.Count); Eq(12, view.pattern_kazu); Eq(12, view.pattern_table!.Count);
            Eq(0, view.sprites!.Count); Eq(0, view.normal_sprites!.Count); Eq(1, source.sprites!.Count); Eq(1, source.normal_sprites!.Count);
            Ptr(source.atlas, view.atlas); True((view.hideFlags & UnityEngine.HideFlags.DontUnloadUnusedAsset) != 0);
            True(!Gsd2NpcDirectionAdapters.IsVerified(source));
        }
    }
    private static void IndependentOwnership()
    {
        var source = Source(48); var view = View(48, source);
        True(source.anime_table!.Pointer != view.anime_table!.Pointer); True(source.pattern_table!.Pointer != view.pattern_table!.Pointer);
        for (int direction = 0; direction < 4; direction++)
        {
            var a = source.anime_table[direction]; var b = view.anime_table[direction];
            True(a.Pointer != b.Pointer); True(a.timer_table!.Pointer != b.timer_table!.Pointer);
            for (int frame = 0; frame < 4; frame++) True(a.timer_table[frame].Pointer != b.timer_table[frame].Pointer);
        }
        for (int pattern = 0; pattern < 12; pattern++)
        {
            var a = source.pattern_table[pattern]; var b = view.pattern_table[pattern];
            True(a.Pointer != b.Pointer); True(a.atari_data.Pointer != b.atari_data.Pointer);
            True(a.obj_table.Pointer != b.obj_table.Pointer); True(a.obj_table[0].Pointer != b.obj_table[0].Pointer);
        }
        view.anime_table[0].timer_table![0].pattern_no = 10;
        view.pattern_table[0].obj_table[0].u = 1234; view.pattern_table[0].atari_data.Add(99);
        view.sprites!.Add("adapter-only", new UnityEngine.Sprite()); view.normal_sprites!.Clear();
        Eq(0, source.anime_table[0].timer_table![0].pattern_no); Eq(0, source.pattern_table[0].obj_table[0].u);
        Eq(0, source.pattern_table[0].atari_data.Count); Eq(1, source.sprites!.Count); Eq(1, source.normal_sprites!.Count);
    }
    private static void TimingAndMirror()
    {
        foreach (int donor in new[] { 1, 48, 79 })
        {
            var source = Source(donor); var view = View(donor, source);
            foreach (var animation in view.anime_table!) foreach (var timer in animation.timer_table!) Eq((byte)(donor == 79 ? 11 : 8), timer.timer);
            Eq((uint)2, view.pattern_table![9].obj_table[0].flag);
            Eq(source.pattern_table![9].obj_table[0].sprite_name, view.pattern_table[9].obj_table[0].sprite_name);
            Eq(source.pattern_table[9].obj_table[0].offset_x, view.pattern_table[9].obj_table[0].offset_x);
        }
    }
    private static void RejectChangedSources()
    {
        var changes = new Action<OPD_DATA>[] {
            x => x.name = "shu-clone", x => x.id = "other", x => x.path = "enemy",
            x => x.anime_kazu++, x => x.pattern_kazu++, x => x.anime_table!.RemoveAt(4),
            x => x.pattern_table!.RemoveAt(12), x => x.anime_table![0].timer_kazu = 3,
            x => x.anime_table![0].timer_table![0].pattern_no = 12,
            x => x.anime_table![0].timer_table![0].timer++, x => x.anime_table![1].abe_flg = 1,
            x => x.anime_table![1].timer_flg = 1, x => x.anime_table![1].loop_flg = 1,
            x => x.pattern_table![0].center_x++, x => x.pattern_table![0].size_y++,
            x => x.pattern_table![0].atari_data.Add(2), x => x.pattern_table![9].obj_table[0].flag = 0,
            x => x.pattern_table![0].obj_table[0].sprite_name = "unproved",
            x => x.pattern_table![0].obj_table[0].tmp_sprite_name = "unproved",
            x => x.pattern_table![0].obj_table[0].offset_y++, x => x.pattern_table![0].obj_table[0].shadow_y++,
            x => x.pattern_table![0].isOverriteBlendMode = true, x => x.atlas = null,
            x => x.sprites = null, x => x.normal_sprites = null, x => x.ThrowName = true
        };
        foreach (var change in changes)
        {
            var source = Source(1); change(source);
            True(Gsd2NpcDirectionAdapters.Prepare(1, source) is null); True(Gsd2NpcDirectionAdapters.Find(1, source) is null);
            True(!Gsd2NpcDirectionAdapters.IsVerified(source));
        }
        foreach (int unsupported in new[] { 0, 2, 49, 50, 72, 74, 80, 83 }) True(Gsd2NpcDirectionAdapters.Prepare(unsupported, Source(1)) is null);
        True(Gsd2NpcDirectionAdapters.Prepare(48, Source(1)) is null);
        True(Gsd2Runtime.Current!.FaultReason is null);
    }
    private static void ExtraPosesExcluded()
    {
        var source = Source(1); source.anime_table![18].timer_table![0].pattern_no = 9876;
        var view = View(1, source); Eq(4, view.anime_kazu); Eq(12, view.pattern_kazu);
        True(view.anime_table!.All(a => a.timer_table!.All(t => t.pattern_no < 12)));
        for (int requested = 4; requested < source.anime_kazu; requested++) True(requested >= view.anime_kazu);
        // Native AnimeChange rejects by anime_kazu before changing its current row.
        True(source.anime_kazu > view.anime_kazu);
    }
    private static EVENT_HUMAN Actor(OPD_DATA source, byte type = 1) => new() { otyp = type, ano = 0, fpno = 83, opd = source, anime = new(), x = 82, y = 78 };
    private static EVENTCON Scene(params EVENT_HUMAN[] actors) => new() { okaz = actors.Length,
        ehuman = Enumerable.Range(0, 8).Select(_ => new EVENT_HUMAN()).Concat(actors).ToList() };
    private static OPD_DATA? Factory(EVENTCON scene, OPD_DATA original, int ano = 0, bool character = false)
    {
        Gsd2NpcSpriteContextPatch.Prefix(scene, out var context);
        OPD_DATA? input = original;
        try
        {
            Gsd2NpcSpriteFactoryPatch.Prefix(ref input, ano, 1, character, out var state);
            try { Gsd2NpcSpriteFactoryPatch.Postfix(ANIME.AnimeCreate(input, ano, 1, character, "test", false), state); }
            finally { Gsd2NpcSpriteFactoryPatch.Finalizer(null, state); }
            return input;
        }
        finally { Gsd2NpcSpriteContextPatch.Finalizer(null, context); }
    }
    private static OPD_DATA Stage()
    {
        var data = Source(48); data.name = "vb02_14"; data.anime_kazu = 4;
        data.anime_table!.RemoveRange(4, data.anime_table.Count - 4); return data;
    }
    private static void FactoryIntegration()
    {
        foreach (int donor in new[] { 1, 48, 79 })
        {
            var view = View(donor, Source(donor)); Gsd2Graphics.Projected[28] = view;
            var stage = Stage(); var actor = Actor(stage); var anime = actor.anime;
            for (int direction = 0; direction < 4; direction++) Ptr(view, Factory(Scene(actor), stage, direction));
            Ptr(stage, actor.opd); Ptr(anime, actor.anime); Eq((ushort)83, actor.fpno); Eq(82, actor.x); Eq(78, actor.y);
        }
        True(Gsd2Graphics.Requests.All(id => id == 28));
    }
    private static void NoNameBypass()
    {
        foreach (int donor in new[] { 1, 48, 79 })
        {
            var whole = Source(donor); Gsd2Graphics.Projected[28] = whole;
            var stage = Stage(); Ptr(stage, Factory(Scene(Actor(stage)), stage));
            whole.anime_kazu = 4; whole.anime_table!.RemoveRange(4, whole.anime_table.Count - 4);
            whole.pattern_kazu = 12; whole.pattern_table!.RemoveRange(12, whole.pattern_table.Count - 12);
            True(!Gsd2NpcDirectionAdapters.IsVerified(whole)); Ptr(stage, Factory(Scene(Actor(stage)), stage));
        }
    }
    private static void ExistingBoundaries()
    {
        var view = View(79, Source(79)); Gsd2Graphics.Projected[28] = view;
        var stage = Stage(); var actor = Actor(stage);
        Ptr(stage, Factory(Scene(actor), stage, 4)); Ptr(stage, Factory(Scene(actor), stage, character: true));
        Ptr(stage, Factory(Scene(actor, Actor(view, 3)), stage));
        Ptr(stage, Factory(Scene(actor, Actor(stage, 4)), stage));
        actor.ano = 4; Ptr(stage, Factory(Scene(actor), stage));
    }
    private static void Reuse()
    {
        var first = Source(48); var view = View(48, first);
        UnityEngine.ScriptableObject.ThrowCreation = true; Ptr(view, View(48, first));
        UnityEngine.ScriptableObject.ThrowCreation = false;
        var second = Source(48); var secondView = View(48, second); True(view.Pointer != secondView.Pointer);
        True(Gsd2NpcDirectionAdapters.Find(1, first) is null);
    }
    private static void Retirement()
    {
        var source = Source(1); var view = View(1, source); var nativeRenderer = new ANIME { data = view };
        Gsd2Runtime.Current = new(); Gsd2Runtime.Current.Profile = new(); Gsd2Graphics.Projected.Clear();
        Ptr(view, nativeRenderer.data); Ptr(view, Gsd2NpcDirectionAdapters.Find(1, source));
        True(!UnityEngine.Object.Destroyed.Contains(view.Pointer)); True(Gsd2NpcDirectionAdapters.IsVerified(view));
    }
    private static void AllocationFailure()
    {
        var source = Source(48); UnityEngine.ScriptableObject.ThrowCreation = true;
        True(Gsd2NpcDirectionAdapters.Prepare(48, source) is null); True(Gsd2NpcDirectionAdapters.Find(48, source) is null);
        Eq(1, source.sprites!.Count); Eq(1, source.normal_sprites!.Count); True(!UnityEngine.Object.Destroyed.Contains(source.Pointer));
        True(Gsd2Runtime.Current!.FaultReason is null); UnityEngine.ScriptableObject.ThrowCreation = false; View(48, source);
    }
    private static void FailedReadback()
    {
        var source = Source(48); OPD_DATA? candidate = null;
        UnityEngine.ScriptableObject.CreationHook = value => { candidate = (OPD_DATA)value; source.id = "external-change-during-allocation"; };
        UnityEngine.Object.BeforeDestroy = value =>
        {
            var rejected = (OPD_DATA)value; True(rejected.sprites!.Pointer != source.sprites!.Pointer);
            True(rejected.normal_sprites!.Pointer != source.normal_sprites!.Pointer);
        };
        True(Gsd2NpcDirectionAdapters.Prepare(48, source) is null);
        True(candidate is not null && UnityEngine.Object.Destroyed.Contains(candidate.Pointer));
        True(!Gsd2NpcDirectionAdapters.IsVerified(candidate)); True(Gsd2NpcDirectionAdapters.Find(48, source) is null);
        Eq(1, source.sprites!.Count); Eq(1, source.normal_sprites!.Count);
    }
    private static void CleanupFailure()
    {
        var source = Source(79); OPD_DATA? candidate = null;
        UnityEngine.ScriptableObject.CreationHook = value => { candidate = (OPD_DATA)value; source.id = "changed"; };
        UnityEngine.Object.BeforeDestroy = _ => throw new InvalidOperationException("injected cleanup failure");
        True(Gsd2NpcDirectionAdapters.Prepare(79, source) is null);
        True(candidate is not null && !Gsd2NpcDirectionAdapters.IsVerified(candidate));
        True(!UnityEngine.Object.Destroyed.Contains(source.Pointer)); Eq(1, source.sprites!.Count); Eq(1, source.normal_sprites!.Count);
        True(Gsd2Runtime.Current!.FaultReason is null);
    }
    private static void OptionalNormalAtlas()
    {
        var source = Source(79); source.normal_atlas = new NativeObject(); var view = View(79, source);
        Ptr(source.normal_atlas, view.normal_atlas); True(source.normal_sprites!.Pointer != view.normal_sprites!.Pointer);
        var absent = Source(79); var withoutNormals = View(79, absent); True(withoutNormals.normal_atlas is null);
        True(withoutNormals.normal_sprites is not null);
    }
    private static void PointerAliasing()
    {
        var original = Source(48); var existing = View(48, original);
        var fresh = Source(48);
        UnityEngine.ScriptableObject.CreationHook = value => value.Pointer = existing.Pointer;
        True(Gsd2NpcDirectionAdapters.Prepare(48, fresh) is null);
        True(!UnityEngine.Object.Destroyed.Contains(existing.Pointer)); True(Gsd2NpcDirectionAdapters.IsVerified(existing));
        Ptr(existing, Gsd2NpcDirectionAdapters.Find(48, original));
        UnityEngine.ScriptableObject.CreationHook = value => value.Pointer = fresh.Pointer;
        True(Gsd2NpcDirectionAdapters.Prepare(48, fresh) is null);
        True(!UnityEngine.Object.Destroyed.Contains(fresh.Pointer)); Eq(1, fresh.sprites!.Count); Eq(1, fresh.normal_sprites!.Count);
    }
    private static void WarningFailure()
    {
        var source = Source(79); source.name = "unproved";
        Gsd2Plugin.PluginLog.ThrowWarnings = true;
        try { True(Gsd2NpcDirectionAdapters.Prepare(79, source) is null); }
        finally { Gsd2Plugin.PluginLog.ThrowWarnings = false; }
        True(Gsd2Runtime.Current!.FaultReason is null); True(Gsd2NpcDirectionAdapters.Find(79, source) is null);
    }
    private static void RetryBackoff()
    {
        var retry = new Gsd2NpcDirectionAdapters.RetryGate();
        var a = new IntPtr(71); var b = new IntPtr(72);
        True(retry.Begin(1, a, 10f)); True(!retry.Begin(1, a, 10.1f)); True(!retry.Begin(1, a, 10.999f));
        True(retry.Begin(1, b, 10.1f)); True(retry.Begin(48, a, 10.1f));
        True(retry.Begin(1, a, 11f)); True(!retry.Begin(1, a, 11.5f));
        retry.Complete(1, a); True(retry.Begin(1, a, 11.5f));
        True(!retry.Begin(48, a, 10.5f)); retry.Clear(); True(retry.Begin(48, a, 10.5f));
        var source = Source(1); var view = View(1, source);
        retry.Clear(); Ptr(view, Gsd2NpcDirectionAdapters.Find(1, source));
        True(Gsd2NpcDirectionAdapters.IsVerified(view)); True(!UnityEngine.Object.Destroyed.Contains(view.Pointer));
    }
    private sealed class SerializedByteBoolean : JsonConverter<bool>
    {
        public override bool Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            if (reader.TokenType is JsonTokenType.True or JsonTokenType.False) return reader.GetBoolean();
            int value = reader.GetInt32();
            if (value is not (0 or 1)) throw new JsonException("Original serialized bool must be0/1.");
            return value != 0;
        }
        public override void Write(Utf8JsonWriter writer, bool value, JsonSerializerOptions options) => writer.WriteBooleanValue(value);
    }
}
