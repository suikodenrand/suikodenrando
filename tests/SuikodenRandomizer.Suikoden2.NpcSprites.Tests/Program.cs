using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using SuikodenRandomizer.Suikoden2Plugin;

internal static class Program
{
    private static void Main()
    {
        var tests = new (string Name, Action Run)[]
        {
            ("actual production aliases identify art, never raw fpno", AliasAndActorIdentity),
            ("visible Ryube temporary Millie uses role24 donor independently of hidden normal Millie", MillieSceneVariant),
            ("Millie scene alias does not admit pristine temporary or unrelated guest resources", ProtectedVariants),
            ("inactive/unbound/ineligible/missing donor retain native input", Admission),
            ("only actual NPC range and factory type/character context project", FactoryContext),
            ("original and donor require four valid complete animation banks", Banks),
            ("unready donor atlases/caches retain canonical sprites without initialization", DonorCaches),
            ("only proved pristine donor direction banks are accepted", DonorWitnesses),
            ("invalid/current custom poses retain native input", Poses),
            ("HD3/4 sharing a bank blocks that pointer throughout the context", SharedHd),
            ("ambiguous pointer aliases cannot select a guessed canonical role", AmbiguousPointer),
            ("nested factories do not permute an already projected OPD again", NestedFactory),
            ("nested and unsupported contexts restore their outer scope", NestedContexts),
            ("scope and factory finalizers restore state without suppressing exceptions", Exceptions),
            ("stale runtime/profile scopes cannot apply to a new campaign", CampaignBinding),
            ("native result pointer is required before a once-per-binding success log", Confirmation),
            ("ineffective native ref argument safely retains canonical result and warns once", CanonicalFallback),
            ("null or third native resource fails closed without actor/cache writes", UnexpectedResource),
            ("failed context inspection keeps canonical input without partial publication", BrokenContext),
            ("thread-local context cannot leak into another thread", ThreadIsolation),
            ("different donor pattern counts pass whole donor OPD without index copying", CompleteDonorBank)
        };
        foreach (var test in tests)
        {
            Reset(); test.Run(); Console.WriteLine("PASS " + test.Name);
        }
        Console.WriteLine(tests.Length + "/" + tests.Length + " production-linked NPC sprite tests passed.");
    }

    private static void Reset()
    {
        Gsd2Runtime.Current = new Gsd2Runtime();
        Gsd2Graphics.Projected.Clear(); Gsd2Graphics.Requests.Clear(); Gsd2Plugin.PluginLog.Warnings.Clear();
        Gsd2Graphics.Projected[28] = Bank("frk", 16);
        Gsd2Graphics.Projected[22] = Bank("rol");
    }
    private static Gsd2Runtime Runtime => Gsd2Runtime.Current!;
    private static OPD_DATA Bank(string name, int patterns = 12)
    {
        var result = new OPD_DATA { name = name, anime_kazu = 4, pattern_kazu = patterns,
            anime_table = new(), pattern_table = new(Enumerable.Range(0, patterns).Select(_ => new OPD_DATA.PatternTable())),
            atlas = new NativeObject(), sprites = new() };
        for (int i = 0; i < 4; i++) result.anime_table.Add(new OPD_DATA.AnimeTable
        { timer_kazu = 1, timer_table = new() { new OPD_DATA.TimerTable { pattern_no = i * patterns / 4, timer = 8 } } });
        return result;
    }
    private static EVENT_HUMAN Npc(OPD_DATA? bank = null, byte type = 1, int animation = 0) => new()
    { otyp = type, ano = animation, fpno = 173, opd = bank ?? Bank("vb02_14"), anime = new ANIME(), x = 167, y = 206, motion = 23, flags = 0x541 };
    private static EVENTCON Scene(params EVENT_HUMAN[] npcs) => new()
    { okaz = npcs.Length, ehuman = Enumerable.Range(0, 8).Select(_ => new EVENT_HUMAN()).Concat(npcs).ToList() };
    private static void Context(EVENTCON owner, Action body)
    {
        Gsd2NpcSpriteContextPatch.Prefix(owner, out var state);
        Exception? exception = null;
        try { body(); }
        catch (Exception error) { exception = error; throw; }
        finally { Same(exception, Gsd2NpcSpriteContextPatch.Finalizer(exception, state)); }
    }
    private static (OPD_DATA? Input, ANIME? Result) Factory(OPD_DATA? original, int animation = 0, int type = 1, bool character = false,
        Func<OPD_DATA?, ANIME?>? native = null)
    {
        var input = original;
        Gsd2NpcSpriteFactoryPatch.Prefix(ref input, animation, type, character, out var state);
        Exception? exception = null;
        try
        {
            var result = native is null ? ANIME.AnimeCreate(input, animation, type, character, "actor", false) : native(input);
            Gsd2NpcSpriteFactoryPatch.Postfix(result, state);
            return (input, result);
        }
        catch (Exception error) { exception = error; throw; }
        finally { Same(exception, Gsd2NpcSpriteFactoryPatch.Finalizer(exception, state)); }
    }
    private static void Eq<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception("Expected " + expected + ", got " + actual); }
    private static void Same(object? expected, object? actual) { if (!ReferenceEquals(expected, actual)) throw new Exception("Reference identity changed."); }
    private static void True(bool condition) { if (!condition) throw new Exception("Assertion failed."); }

    private static void AliasAndActorIdentity()
    {
        True(Gsd2NpcSpriteAliases.TryResolve("vb02_14", out int role)); Eq(28, role);
        var actor = Npc(); var original = actor.opd; var anime = actor.anime;
        Context(Scene(actor), () =>
        {
            var result = Factory(original); Same(Gsd2Graphics.Projected[28], result.Input); Same(result.Input, result.Result!.data);
        });
        Same(original, actor.opd); Same(anime, actor.anime); Eq((ushort)173, actor.fpno); Eq(0, actor.ano);
        Eq(167, actor.x); Eq(206, actor.y); Eq(23, actor.motion); Eq(0x541, actor.flags);
        Eq(28, Gsd2Graphics.Requests.Single());
        var unknown = Npc(Bank("unverified-scene-bank")); unknown.fpno = 28;
        Context(Scene(unknown), () => Same(unknown.opd, Factory(unknown.opd).Input));
    }
    private static void Admission()
    {
        var actor = Npc(); Runtime.Active = false;
        Context(Scene(actor), () => Same(actor.opd, Factory(actor.opd).Input));
        Eq(0, Gsd2Graphics.Requests.Count);
        Runtime.Active = true; Runtime.Profile = null;
        Context(Scene(actor), () => Same(actor.opd, Factory(actor.opd).Input));
        Runtime.Profile = new(); Runtime.Roles.Remove(28);
        Context(Scene(actor), () => Same(actor.opd, Factory(actor.opd).Input));
        Runtime.Roles.Add(28); Gsd2Graphics.Projected.Remove(28);
        Context(Scene(actor), () => Same(actor.opd, Factory(actor.opd).Input));
    }
    private static void MillieSceneVariant()
    {
        True(Gsd2NpcSpriteAliases.TryResolve("vb05_13", out int role)); Eq(24, role);
        True(Gsd2NpcSpriteAliases.TryResolve("vb05_14", out role)); Eq(24, role);
        Runtime.Roles.Add(24); True(!Runtime.IsRole(83));
        var donor = Bank("sro"); Gsd2Graphics.Projected[24] = donor;
        var hiddenNormal = Npc(Bank("vb05_14")); hiddenNormal.fpno = 24;
        hiddenNormal.x = 0; hiddenNormal.y = 0;
        var visibleVariant = Npc(Bank("vb05_13"), animation: 3); visibleVariant.fpno = 83;
        visibleVariant.x = 82; visibleVariant.y = 78;
        var normalOpd = hiddenNormal.opd; var variantOpd = visibleVariant.opd;
        var normalAnime = hiddenNormal.anime; var variantAnime = visibleVariant.anime;
        True(normalOpd!.Pointer != variantOpd!.Pointer);
        Context(Scene(hiddenNormal, visibleVariant), () =>
        {
            var hidden = Factory(normalOpd); Same(donor, hidden.Input); Same(donor, hidden.Result!.data);
            var visible = Factory(variantOpd, animation: 3); Same(donor, visible.Input); Same(donor, visible.Result!.data);
        });
        Same(normalOpd, hiddenNormal.opd); Same(variantOpd, visibleVariant.opd);
        Same(normalAnime, hiddenNormal.anime); Same(variantAnime, visibleVariant.anime);
        Eq((ushort)24, hiddenNormal.fpno); Eq((ushort)83, visibleVariant.fpno);
        Eq(0, hiddenNormal.ano); Eq(3, visibleVariant.ano);
        Eq(0, hiddenNormal.x); Eq(0, hiddenNormal.y); Eq(82, visibleVariant.x); Eq(78, visibleVariant.y);
        Eq((byte)1, visibleVariant.otyp); Eq(23, visibleVariant.motion); Eq(0x541, visibleVariant.flags);
        Eq(2, Gsd2Graphics.Requests.Count); True(Gsd2Graphics.Requests.All(x => x == 24));
        Eq(1, Runtime.Traces.Count); True(Runtime.Traces.Single().Contains("role 24"));
        True(!Runtime.IsRole(83)); True(!Gsd2Graphics.Projected.ContainsKey(83));
    }
    private static void ProtectedVariants()
    {
        Runtime.Roles.Add(24); Gsd2Graphics.Projected[24] = Bank("sro");
        foreach (string name in new[] { "mi2", "joi", "jo2", "zsh", "vb05_13-extra" })
        {
            True(!Gsd2NpcSpriteAliases.TryResolve(name, out _));
            var actor = Npc(Bank(name)); actor.fpno = 83;
            var original = actor.opd; var anime = actor.anime;
            Context(Scene(actor), () => Same(original, Factory(original).Input));
            Same(original, actor.opd); Same(anime, actor.anime); Eq((ushort)83, actor.fpno);
        }
        var party = Npc(Bank("mi2")); party.fpno = 83;
        Context(Scene(party), () => Same(party.opd, Factory(party.opd, character: true).Input));
        True(!Runtime.IsRole(83)); Eq(0, Gsd2Graphics.Requests.Count); Eq(0, Runtime.Traces.Count);
    }
    private static void FactoryContext()
    {
        var actor = Npc(); Same(actor.opd, Factory(actor.opd).Input);
        var scene = Scene(); scene.ehuman![0] = actor;
        Context(scene, () => Same(actor.opd, Factory(actor.opd).Input));
        scene = Scene(); scene.ehuman!.Add(actor); // Beyond native8+okaz bound.
        Context(scene, () => Same(actor.opd, Factory(actor.opd).Input));
        Context(Scene(actor), () =>
        {
            Same(actor.opd, Factory(actor.opd, type: 0).Input);
            Same(actor.opd, Factory(actor.opd, character: true).Input);
            Same(Gsd2Graphics.Projected[28], Factory(actor.opd).Input);
        });
        var type2 = Npc(type: 2); Context(Scene(type2), () => Same(Gsd2Graphics.Projected[28], Factory(type2.opd).Input));
    }
    private static void Banks()
    {
        var corruptions = new Action<OPD_DATA>[]
        {
            b => b.anime_kazu = 5, b => b.anime_table!.RemoveAt(3), b => b.anime_table = null,
            b => b.pattern_kazu++, b => b.pattern_table = null, b => b.anime_table![0] = null!,
            b => b.anime_table![0].timer_table = null, b => b.anime_table![0].timer_kazu = 0,
            b => b.anime_table![0].timer_table!.Add(new OPD_DATA.TimerTable()),
            b => b.anime_table![0].timer_table![0].pattern_no = -1,
            b => b.anime_table![0].timer_table![0].pattern_no = b.pattern_kazu,
            b => b.pattern_table![0] = null!
        };
        foreach (var corrupt in corruptions)
        {
            var original = Bank("vb02_14"); corrupt(original); var actor = Npc(original);
            Context(Scene(actor), () => Same(original, Factory(original).Input));
            original = Bank("vb02_14"); var donor = Bank("frk", 16); corrupt(donor); Gsd2Graphics.Projected[28] = donor; actor = Npc(original);
            Context(Scene(actor), () => Same(original, Factory(original).Input));
            Gsd2Graphics.Projected[28] = Bank("frk", 16);
        }
        var same = Bank("vb02_14"); Gsd2Graphics.Projected[28] = same;
        Context(Scene(Npc(same)), () => Same(same, Factory(same).Input));
    }
    private static void Poses()
    {
        foreach (int animation in new[] { -1, 4, 9 })
        {
            var actor = Npc(animation: animation);
            Context(Scene(actor), () => Same(actor.opd, Factory(actor.opd).Input));
        }
        var normal = Npc(); Context(Scene(normal), () =>
        {
            Same(normal.opd, Factory(normal.opd, -1).Input); Same(normal.opd, Factory(normal.opd, 4).Input);
            for (int i = 0; i < 4; i++) Same(Gsd2Graphics.Projected[28], Factory(normal.opd, i).Input);
        });
    }
    private static void DonorCaches()
    {
        var corruptions = new Action<OPD_DATA>[]
        {
            b => b.atlas = null, b => b.atlas!.Pointer = IntPtr.Zero, b => b.sprites = null,
            b => { b.normal_atlas = new NativeObject(); b.normal_sprites = null; }
        };
        foreach (var corrupt in corruptions)
        {
            var actor = Npc(); var donor = Bank("frk", 16); corrupt(donor); Gsd2Graphics.Projected[28] = donor;
            var atlas = donor.atlas; var sprites = donor.sprites; var normals = donor.normal_sprites;
            Context(Scene(actor), () => Same(actor.opd, Factory(actor.opd).Input));
            Same(atlas, donor.atlas); Same(sprites, donor.sprites); Same(normals, donor.normal_sprites);
        }
        var ready = Bank("frk", 16); ready.normal_atlas = new NativeObject(); ready.normal_sprites = new();
        Gsd2Graphics.Projected[28] = ready; var npc = Npc();
        Context(Scene(npc), () => Same(ready, Factory(npc.opd).Input));
        Eq(0, ready.sprites!.Count); Eq(0, ready.normal_sprites.Count);
    }
    private static void SharedHd()
    {
        foreach (byte type in new byte[] { 3, 4 })
        {
            var bank = Bank("vb02_14"); var actor = Npc(bank); var hd = Npc(bank, type);
            Context(Scene(actor, hd), () => Same(bank, Factory(bank).Input));
            var scene = Scene(actor); scene.ehuman![1] = hd;
            Context(scene, () => Same(bank, Factory(bank).Input));
            hd = Npc(Gsd2Graphics.Projected[28], type);
            Context(Scene(actor, hd), () => Same(bank, Factory(bank).Input));
        }
    }
    private static void AmbiguousPointer()
    {
        var a = Bank("vb02_14"); var b = Bank("vb11_05"); b.Pointer = a.Pointer;
        Context(Scene(Npc(a), Npc(b), Npc(a)), () => Same(a, Factory(a).Input));
    }
    private static void NestedFactory()
    {
        var a = Bank("vb02_14"); var b = Bank("tou"); Gsd2Graphics.Projected[28] = b;
        Context(Scene(Npc(a), Npc(b)), () =>
        {
            var outer = Factory(a, native: projected =>
            {
                Same(b, projected); Same(b, Factory(projected).Input); return new ANIME { data = projected };
            });
            Same(b, outer.Input); Same(Gsd2Graphics.Projected[22], Factory(b).Input);
        });
    }
    private static void NestedContexts()
    {
        var a = Npc(); var b = Npc(Bank("vb11_05"));
        Context(Scene(a), () =>
        {
            Context(Scene(b), () => { Same(a.opd, Factory(a.opd).Input); Same(Gsd2Graphics.Projected[22], Factory(b.opd).Input); });
            Context(Scene(), () => Same(a.opd, Factory(a.opd).Input));
            Same(Gsd2Graphics.Projected[28], Factory(a.opd).Input);
        });
        Same(a.opd, Factory(a.opd).Input);
    }
    private static void Exceptions()
    {
        var actor = Npc(); var expected = new InvalidOperationException("native factory failure");
        try { Context(Scene(actor), () => Factory(actor.opd, native: _ => throw expected)); throw new Exception("Expected native exception"); }
        catch (InvalidOperationException actual) { Same(expected, actual); }
        Same(actor.opd, Factory(actor.opd).Input);
        Context(Scene(actor), () => Same(Gsd2Graphics.Projected[28], Factory(actor.opd).Input));
    }
    private static void CampaignBinding()
    {
        var actor = Npc(); var old = Runtime;
        Context(Scene(actor), () =>
        {
            Gsd2Runtime.Current = new Gsd2Runtime(); Same(actor.opd, Factory(actor.opd).Input);
            Gsd2Runtime.Current = old; old.Profile = new(); Same(actor.opd, Factory(actor.opd).Input);
        });
        Context(Scene(actor), () => Same(Gsd2Graphics.Projected[28], Factory(actor.opd).Input));
    }
    private static void Confirmation()
    {
        var actor = Npc(); Context(Scene(actor), () =>
        {
            OPD_DATA? input = actor.opd;
            Gsd2NpcSpriteFactoryPatch.Prefix(ref input, 0, 1, false, out var state);
            Eq(0, Runtime.Traces.Count);
            var wrapper = Bank("separate-managed-wrapper"); wrapper.Pointer = input!.Pointer;
            Gsd2NpcSpriteFactoryPatch.Postfix(new ANIME { data = wrapper }, state);
            Eq(1, Runtime.Traces.Count);
            Gsd2NpcSpriteFactoryPatch.Finalizer(null, state);
            Factory(actor.opd); Eq(1, Runtime.Traces.Count);
        });
        Runtime.Profile = new(); Context(Scene(actor), () => Factory(actor.opd)); Eq(2, Runtime.Traces.Count);
    }
    private static void CanonicalFallback()
    {
        var actor = Npc(); Context(Scene(actor), () =>
        {
            for (int i = 0; i < 2; i++) Same(actor.opd, Factory(actor.opd, native: _ => new ANIME { data = actor.opd }).Result!.data);
        });
        Eq(0, Runtime.Traces.Count); Eq(1, Gsd2Plugin.PluginLog.Warnings.Count); True(Runtime.FaultReason is null); True(Runtime.IsActive);
    }
    private static void UnexpectedResource()
    {
        foreach (bool missing in new[] { false, true })
        {
            Reset(); var actor = Npc(); var original = actor.opd;
            Context(Scene(actor), () => Factory(original, native: _ => missing ? null : new ANIME { data = Bank("foreign") }));
            True(Runtime.FaultReason is not null); Eq(0, Runtime.Traces.Count); Eq(1, Gsd2Plugin.PluginLog.Warnings.Count);
            Same(original, actor.opd); Eq((ushort)173, actor.fpno);
        }
    }
    private static void BrokenContext()
    {
        var good = Npc(); var broken = Npc(); broken.opd!.ThrowName = true;
        Context(Scene(good, broken), () => Same(good.opd, Factory(good.opd).Input));
        Eq(1, Gsd2Plugin.PluginLog.Warnings.Count);
        foreach (var scene in new[] { new EVENTCON { okaz = -1 }, new EVENTCON { okaz = 256 }, new EVENTCON { okaz = 1, ehuman = new() } })
            Context(scene, () => Same(good.opd, Factory(good.opd).Input));
    }
    private static void ThreadIsolation()
    {
        var actor = Npc(); Exception? error = null;
        Context(Scene(actor), () =>
        {
            var thread = new Thread(() => { try { Same(actor.opd, Factory(actor.opd).Input); } catch (Exception ex) { error = ex; } });
            thread.Start(); thread.Join(); if (error is not null) throw error;
            Same(Gsd2Graphics.Projected[28], Factory(actor.opd).Input);
        });
    }
    private static void CompleteDonorBank()
    {
        var actor = Npc(); var donor = Gsd2Graphics.Projected[28]; Eq(16, donor.pattern_kazu);
        Context(Scene(actor), () =>
        {
            var result = Factory(actor.opd, 3); Same(donor, result.Result!.data);
            Same(donor.anime_table!, result.Result.data!.anime_table!); Same(donor.pattern_table!, result.Result.data.pattern_table!);
        });
        Eq(12, actor.opd!.pattern_kazu); Eq(16, donor.pattern_kazu);
    }
    private static void DonorWitnesses()
    {
        True(Gsd2NpcSpriteAliases.SupportsNormalDirections("frk"));
        True(Gsd2NpcSpriteAliases.SupportsNormalDirections("rol"));
        True(!Gsd2NpcSpriteAliases.SupportsNormalDirections("vb02_14"));
        True(!Gsd2NpcSpriteAliases.SupportsNormalDirections("unverified-donor"));
        True(!Gsd2NpcSpriteAliases.SupportsNormalDirections(1));
        foreach (string name in new[] { "unverified-donor", "vb02_14" })
        {
            var actor = Npc(); Gsd2Graphics.Projected[28] = Bank(name);
            Context(Scene(actor), () => Same(actor.opd, Factory(actor.opd).Input));
        }
    }
}
