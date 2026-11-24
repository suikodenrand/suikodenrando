extern alias GSD1;

using SuikodenRandomizer.AvatarPlugin;
using SuikodenRandomizer.Core;
using SuikodenRandomizer.Core.Avatars;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

internal static class Program
{
    private static AvatarRuntime Runtime = null!;
    private static AvatarShuffleProfile Profile => Runtime.Profile;
    private static int Role => Profile.RoleIds.First(id => Profile.GetDonor(id) != id);
    private static int Donor => Profile.GetDonor(Role);
    private static short Face(int id) => checked((short)(id + 1000));

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
    private static GSD1.PLAYER_DATA Asset(int id, int offset = 10000) => new GSD1.PLAYER_DATA
    {
        field_data = new GSD1.PLAYER_FIELD { opd_data = new GSD1.OPD_DATA { Pointer = new IntPtr(id + offset) } }
    };
    private static void Setup()
    {
        Check(!AvatarPresentation.ResolvingNames && !AvatarPresentation.ResolvingFaces, "A prior test leaked a presentation scope.");
        Runtime = new AvatarRuntime(AvatarShuffleProfile.Create("presentation-regression-080"));
        AvatarRuntime.Current = Runtime;
        foreach (int id in Profile.RoleIds)
        {
            Runtime.Originals[id] = Asset(id);
            // A deliberately different asset at cache[donor] represents the
            // donor role's own assigned avatar after full-roster publication.
            Runtime.ShuffledCache[id] = Asset(Profile.GetDonor(id), 20000);
        }
        GSD1.Ws_face_c.Converter = id => Face(id);
        GSD1.Ws_face_c.StaticCalls = GSD1.Ws_face_c.InstanceCalls = 0;
        GSD1.AnimeSpriteManager.DestroyCalls = 0;
        GSD1.AnimeSpriteManager.ThrowOnDestroy = false;
        AvatarPresentation.Reset();
    }
    private static (int Id, bool Entered) NamePrefix(Type patch, int value)
    {
        object?[] args = { value, null };
        patch.GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, args);
        return ((int)args[0]!, (bool)args[1]!);
    }
    private static (ushort Id, bool Entered) FacePrefix(Type patch, ushort value)
    {
        object?[] args = { value, null };
        patch.GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, args);
        return ((ushort)args[0]!, (bool)args[1]!);
    }
    private static void FacePostfix(Type patch, ushort mappedId, short result, bool entered) =>
        patch.GetMethod("Postfix", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, new object?[] { mappedId, result, entered });
    private static short ConvertFace(Type patch, ushort value)
    {
        var call = FacePrefix(patch, value);
        try
        {
            short result = patch == typeof(AvatarStaticFaceCharacterPatch)
                ? GSD1.Ws_face_c.CharacterIDConvertToFaceIDStatic(call.Id)
                : new GSD1.Ws_face_c().CharacterIDConvertToFaceID(call.Id);
            FacePostfix(patch, call.Id, result, call.Entered);
            return result;
        }
        finally { Finish(patch, call.Entered); }
    }
    private static Exception? Finish(Type patch, bool entered, Exception? exception = null) =>
        (Exception?)patch.GetMethod("Finalizer", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, new object?[] { exception, entered });
    private static GSD1.AnimeSPRITE OriginalResult() => GSD1.Village_c.ValidSprite(new GSD1.OPD_DATA { Pointer = new IntPtr(90000) }, 0);

    private static int Main()
    {
        var tests = new (string Name, Action Run)[]
        {
            ("GetCharacterName and nested GetCharacterNameTextID project a seeded cycle exactly once", () => {
                int caller = Role;
                var outer = NamePrefix(typeof(AvatarNamePatch), caller);
                Check(outer.Entered && outer.Id == Donor && caller == Role, "Root name projection or caller isolation failed.");
                var inner = NamePrefix(typeof(AvatarNameTextIdPatch), outer.Id);
                Check(inner.Entered && inner.Id == Donor && inner.Id != Profile.GetDonor(Donor), "Nested lookup applied the permutation twice.");
                Finish(typeof(AvatarNameTextIdPatch), inner.Entered);
                Check(AvatarPresentation.ResolvingNames, "Inner finalizer ended the outer name scope.");
                Finish(typeof(AvatarNamePatch), outer.Entered);
                Check(!AvatarPresentation.ResolvingNames, "Outer finalizer leaked name depth.");
            }),
            ("Standalone GetCharacterNameTextID root projects normally", () => {
                var call = NamePrefix(typeof(AvatarNameTextIdPatch), Role);
                Check(call.Id == Donor, "Direct text-ID name lookup was not projected.");
                Finish(typeof(AvatarNameTextIdPatch), call.Entered);
            }),
            ("Name projection exposes original-data lookup scope before resolving a donor", () => {
                var call = NamePrefix(typeof(AvatarNamePatch), Role);
                Check(Runtime.ObservedNameScope && AvatarPresentation.ResolvingNames, "Original-data lookup scope was absent during projection.");
                Finish(typeof(AvatarNamePatch), call.Entered);
            }),
            ("Name finalizers retain native exception and unwind nested depth", () => {
                var outer = NamePrefix(typeof(AvatarNamePatch), Role);
                var inner = NamePrefix(typeof(AvatarNameTextIdPatch), outer.Id);
                var failure = new InvalidOperationException("simulated native lookup failure");
                Check(ReferenceEquals(Finish(typeof(AvatarNameTextIdPatch), inner.Entered, failure), failure), "Inner finalizer replaced the native exception.");
                Check(ReferenceEquals(Finish(typeof(AvatarNamePatch), outer.Entered, failure), failure), "Outer finalizer replaced the native exception.");
                Check(!AvatarPresentation.ResolvingNames, "Exceptional unwind leaked name depth.");
                var next = NamePrefix(typeof(AvatarNamePatch), Role);
                Check(next.Id == Donor, "Next lookup remained suppressed after an exception.");
                Finish(typeof(AvatarNamePatch), next.Entered);
            }),
            ("Protected Tir Ted Odessa and noncombat name inputs remain canonical", () => {
                foreach (int id in new[] { 8, 108, 109, 24, 27, 34, 44 })
                {
                    Check(!Profile.IsRole(id), "Protected test identity is unexpectedly eligible.");
                    var call = NamePrefix(typeof(AvatarNamePatch), id);
                    Check(call.Id == id, "A protected character name was projected.");
                    var nested = NamePrefix(typeof(AvatarNameTextIdPatch), call.Id);
                    Check(nested.Id == id, "A protected nested name was projected.");
                    Finish(typeof(AvatarNameTextIdPatch), nested.Entered);
                    Finish(typeof(AvatarNamePatch), call.Entered);
                }
            }),
            ("Opt-in Tir gets the assigned face while his story name stays native", () => {
                var profile = AvatarShuffleProfile.Create("presentation-regression-080", includeTir: true);
                Runtime = new AvatarRuntime(profile);
                AvatarRuntime.Current = Runtime;
                const ushort hero = CombatCatalog.TirId;
                int donor = profile.GetDonor(hero);
                Check(donor != hero, "Opt-in fixture requires a different Tir avatar.");
                var call = NamePrefix(typeof(AvatarNamePatch), hero);
                Check(call.Entered && call.Id == hero, "Protagonist name lookup lost the native hero ID.");
                Finish(typeof(AvatarNamePatch), call.Entered);
                Check(ConvertFace(typeof(AvatarStaticFaceCharacterPatch), hero) == Face(donor),
                    "Protagonist portrait did not follow the assigned avatar.");
            }),
            ("Negative encoded and out-of-range name inputs are never masked into roles", () => {
                foreach (int id in new[] { -1, int.MinValue, int.MaxValue, 110, 65537 })
                {
                    var call = NamePrefix(typeof(AvatarNamePatch), id);
                    Check(call.Id == id, "A sentinel or encoded name input was guessed as a character.");
                    Finish(typeof(AvatarNamePatch), call.Entered);
                }
            }),
            ("Inactive and missing runtimes do not enter name scopes", () => {
                Runtime.IsActive = false;
                var inactive = NamePrefix(typeof(AvatarNamePatch), Role);
                Check(!inactive.Entered && inactive.Id == Role && !AvatarPresentation.ResolvingNames, "Inactive name projection changed state.");
                Finish(typeof(AvatarNamePatch), inactive.Entered);
                AvatarRuntime.Current = null;
                var missing = NamePrefix(typeof(AvatarNameTextIdPatch), Role);
                Check(!missing.Entered && missing.Id == Role, "Missing runtime projected a name.");
                Finish(typeof(AvatarNameTextIdPatch), missing.Entered);
            }),
            ("Static portrait conversion projects without any sprite lookup", () => {
                ushort caller = checked((ushort)Role);
                short result = ConvertFace(typeof(AvatarStaticFaceCharacterPatch), caller);
                Check(result == Face(Donor) && caller == Role, "Direct native converter retained the canonical face or changed its caller.");
                Check(GSD1.Ws_face_c.StaticCalls == 1 && GSD1.Ws_face_c.InstanceCalls == 0, "Direct conversion did not run once.");
            }),
            ("Instance portrait conversion projects without any sprite lookup", () => {
                short result = ConvertFace(typeof(AvatarFaceCharacterPatch), checked((ushort)Role));
                Check(result == Face(Donor) && GSD1.Ws_face_c.InstanceCalls == 1 && GSD1.Ws_face_c.StaticCalls == 0,
                    "Instance conversion did not produce the assigned donor face.");
            }),
            ("Static outer and instance inner converters apply the permutation once", () => {
                var outer = FacePrefix(typeof(AvatarStaticFaceCharacterPatch), checked((ushort)Role));
                var inner = FacePrefix(typeof(AvatarFaceCharacterPatch), outer.Id);
                Check(outer.Id == Donor && inner.Id == Donor && inner.Id != Profile.GetDonor(Donor), "Nested converters applied the cycle twice.");
                short nativeResult = new GSD1.Ws_face_c().CharacterIDConvertToFaceID(inner.Id);
                FacePostfix(typeof(AvatarFaceCharacterPatch), inner.Id, nativeResult, inner.Entered);
                Finish(typeof(AvatarFaceCharacterPatch), inner.Entered);
                Check(AvatarPresentation.ResolvingFaces, "Inner finalizer released outer conversion scope.");
                FacePostfix(typeof(AvatarStaticFaceCharacterPatch), outer.Id, nativeResult, outer.Entered);
                Finish(typeof(AvatarStaticFaceCharacterPatch), outer.Entered);
                Check(nativeResult == Face(Donor) && !AvatarPresentation.ResolvingFaces, "Nested converter result or unwind is wrong.");
            }),
            ("Instance outer and static inner converters apply the permutation once", () => {
                var outer = FacePrefix(typeof(AvatarFaceCharacterPatch), checked((ushort)Role));
                var inner = FacePrefix(typeof(AvatarStaticFaceCharacterPatch), outer.Id);
                Check(outer.Id == Donor && inner.Id == Donor, "Reverse nesting applied two projections.");
                short result = GSD1.Ws_face_c.CharacterIDConvertToFaceIDStatic(inner.Id);
                FacePostfix(typeof(AvatarStaticFaceCharacterPatch), inner.Id, result, inner.Entered);
                Finish(typeof(AvatarStaticFaceCharacterPatch), inner.Entered);
                FacePostfix(typeof(AvatarFaceCharacterPatch), outer.Id, result, outer.Entered);
                Finish(typeof(AvatarFaceCharacterPatch), outer.Entered);
                Check(result == Face(Donor) && !AvatarPresentation.ResolvingFaces, "Reverse converter nesting leaked scope.");
            }),
            ("Portrait scope selects pristine donor data throughout native conversion", () => {
                GSD1.Ws_face_c.Converter = id => {
                    Check(AvatarPresentation.ResolvingFaces, "Native conversion lost its original-data lookup scope.");
                    Check(Runtime.TryGetOriginal(id, out var original), "Pristine donor is unavailable.");
                    Check(original!.field_data.opd_data.Pointer != Runtime.ShuffledCache[id].field_data.opd_data.Pointer,
                        "Test does not distinguish pristine donor from shuffled cache.");
                    return Face(id);
                };
                Check(ConvertFace(typeof(AvatarStaticFaceCharacterPatch), checked((ushort)Role)) == Face(Donor)
                    && Runtime.ObservedFaceScope && Runtime.OriginalReads == 1 && !AvatarPresentation.ResolvingFaces,
                    "Portrait conversion did not select pristine donor in scope.");
            }),
            ("Portrait conversion performs no full-roster precomputation", () => {
                for (int n = 0; n < 4; n++)
                    Check(ConvertFace(typeof(AvatarStaticFaceCharacterPatch), checked((ushort)Role)) == Face(Donor), "Repeated root conversion changed assignment.");
                Check(GSD1.Ws_face_c.StaticCalls == 4 && GSD1.Ws_face_c.InstanceCalls == 0, "Face conversion scanned/precomputed the roster.");
            }),
            ("Protected character portrait inputs remain canonical", () => {
                foreach (int id in new[] { 8, 108, 109, 24, 27, 34, 44 })
                {
                    Check(ConvertFace(typeof(AvatarStaticFaceCharacterPatch), checked((ushort)id)) == Face(id), "Protected static portrait changed.");
                    Check(ConvertFace(typeof(AvatarFaceCharacterPatch), checked((ushort)id)) == Face(id), "Protected instance portrait changed.");
                }
            }),
            ("Encoded and sentinel UInt16 portrait inputs are not masked into heroes", () => {
                GSD1.Ws_face_c.Converter = id => unchecked((short)id);
                foreach (ushort id in new ushort[] { 110, 0x8001, 0xffff, 0x7fff })
                    foreach (Type patch in new[] { typeof(AvatarStaticFaceCharacterPatch), typeof(AvatarFaceCharacterPatch) })
                        Check(ConvertFace(patch, id) == unchecked((short)id), "Encoded portrait input was guessed as a character.");
            }),
            ("Shared original portrait assets do not confuse protected and eligible roles", () => {
                GSD1.Ws_face_c.Converter = id => id == Role || id == 8 ? (short)30000 : Face(id);
                Check(ConvertFace(typeof(AvatarStaticFaceCharacterPatch), 8) == 30000, "Shared asset changed Tir's face.");
                Check(ConvertFace(typeof(AvatarStaticFaceCharacterPatch), checked((ushort)Role)) == Face(Donor),
                    "Known source character was suppressed because an original face asset is shared.");
            }),
            ("Native face IDs are returned unchanged after character projection", () => {
                GSD1.Ws_face_c.Converter = id => (short)12345;
                Check(ConvertFace(typeof(AvatarStaticFaceCharacterPatch), checked((ushort)Role)) == 12345,
                    "Converted face ID was rewritten or inferred after native conversion.");
            }),
            ("Missing donor expression result retains native fallback value", () => {
                GSD1.Ws_face_c.Converter = id => (short)-1;
                Check(ConvertFace(typeof(AvatarFaceCharacterPatch), checked((ushort)Role)) == -1 && !AvatarPresentation.ResolvingFaces,
                    "Native missing-expression result or scope changed.");
            }),
            ("Nested portrait finalizers retain exceptions and permit the next conversion", () => {
                var outer = FacePrefix(typeof(AvatarFaceCharacterPatch), checked((ushort)Role));
                var inner = FacePrefix(typeof(AvatarStaticFaceCharacterPatch), outer.Id);
                var failure = new InvalidOperationException("simulated native converter failure");
                Check(ReferenceEquals(Finish(typeof(AvatarStaticFaceCharacterPatch), inner.Entered, failure), failure), "Inner portrait finalizer replaced exception.");
                Check(AvatarPresentation.ResolvingFaces, "Inner exception unwind released outer scope.");
                Check(ReferenceEquals(Finish(typeof(AvatarFaceCharacterPatch), outer.Entered, failure), failure), "Outer portrait finalizer replaced exception.");
                Check(!AvatarPresentation.ResolvingFaces, "Exceptional portrait unwind leaked depth.");
                Check(ConvertFace(typeof(AvatarStaticFaceCharacterPatch), checked((ushort)Role)) == Face(Donor), "Next root portrait remained suppressed.");
            }),
            ("Native converter exception unwinds the original-data scope", () => {
                GSD1.Ws_face_c.Converter = id => throw new InvalidOperationException("native conversion error");
                bool rejected = false;
                try { ConvertFace(typeof(AvatarStaticFaceCharacterPatch), checked((ushort)Role)); }
                catch (InvalidOperationException) { rejected = true; }
                Check(rejected && !AvatarPresentation.ResolvingFaces, "Native error was hidden or scope leaked.");
                GSD1.Ws_face_c.Converter = id => Face(id);
                Check(ConvertFace(typeof(AvatarFaceCharacterPatch), checked((ushort)Role)) == Face(Donor), "Conversion did not recover after unwind.");
            }),
            ("Name and portrait projection scopes unwind independently", () => {
                var name = NamePrefix(typeof(AvatarNamePatch), Role);
                var face = FacePrefix(typeof(AvatarStaticFaceCharacterPatch), checked((ushort)Role));
                Check(name.Id == Donor && face.Id == Donor && AvatarPresentation.ResolvingNames && AvatarPresentation.ResolvingFaces,
                    "Independent display domains reused a depth guard.");
                Finish(typeof(AvatarStaticFaceCharacterPatch), face.Entered);
                Check(AvatarPresentation.ResolvingNames && !AvatarPresentation.ResolvingFaces, "Face unwind altered name scope.");
                Finish(typeof(AvatarNamePatch), name.Entered);
            }),
            ("A newly loaded profile immediately controls portrait assignments", () => {
                var oldProfile = Profile;
                Check(ConvertFace(typeof(AvatarStaticFaceCharacterPatch), checked((ushort)Role)) == Face(Donor), "Initial profile did not project.");
                Runtime = new AvatarRuntime(AvatarShuffleProfile.Create("different-presentation-seed"));
                AvatarRuntime.Current = Runtime;
                int changed = Profile.RoleIds.First(id => Profile.GetDonor(id) != oldProfile.GetDonor(id));
                Check(ConvertFace(typeof(AvatarFaceCharacterPatch), checked((ushort)changed)) == Face(Profile.GetDonor(changed)), "Portrait retained a prior campaign map.");
            }),
            ("Self-assigned eligible roles keep their own native portrait", () => {
                AvatarShuffleProfile? selfProfile = null; int selfId = -1;
                for (int n = 0; n < 256 && selfProfile is null; n++)
                {
                    var candidate = AvatarShuffleProfile.Create("self-portrait-" + n);
                    int[] fixedIds = candidate.RoleIds.Where(id => candidate.GetDonor(id) == id).ToArray();
                    if (fixedIds.Length > 0) { selfProfile = candidate; selfId = fixedIds[0]; }
                }
                Check(selfProfile is not null, "Fixture could not find a self assignment.");
                Runtime = new AvatarRuntime(selfProfile!); AvatarRuntime.Current = Runtime;
                Check(ConvertFace(typeof(AvatarStaticFaceCharacterPatch), checked((ushort)selfId)) == Face(selfId), "Self-assigned role changed face.");
            }),
            ("Inactive and missing runtimes do not enter portrait scopes", () => {
                Runtime.IsActive = false;
                var inactive = FacePrefix(typeof(AvatarStaticFaceCharacterPatch), checked((ushort)Role));
                Check(!inactive.Entered && inactive.Id == Role && !AvatarPresentation.ResolvingFaces, "Inactive portrait altered input/scope.");
                Finish(typeof(AvatarStaticFaceCharacterPatch), inactive.Entered);
                AvatarRuntime.Current = null;
                var missing = FacePrefix(typeof(AvatarFaceCharacterPatch), checked((ushort)Role));
                Check(!missing.Entered && missing.Id == Role && !AvatarPresentation.ResolvingFaces, "Missing runtime projected a portrait.");
                Finish(typeof(AvatarFaceCharacterPatch), missing.Entered);
            }),
            ("Portrait hooks target only character converters and never rewrite sprite IDs", () => {
                var targets = typeof(AvatarPresentation).Assembly.GetTypes()
                    .SelectMany(type => type.GetCustomAttributes<HarmonyLib.HarmonyPatch>()).Select(attribute => attribute.TargetName).ToArray();
                Check(targets.Contains("CharacterIDConvertToFaceIDStatic") && targets.Contains("CharacterIDConvertToFaceID"), "Known-hit native converters are not both hooked.");
                Check(!targets.Contains("GetFaceSprite"), "A sprite-boundary remap can apply the permutation twice.");
            }),
            ("Field sprite factory receives pristine donor OPD instead of shuffled cache OPD", () => {
                var village = new GSD1.Village_c(); var result = OriginalResult();
                var pristine = Runtime.Originals[Donor].field_data.opd_data;
                var shuffled = Runtime.ShuffledCache[Donor].field_data.opd_data;
                Check(pristine.Pointer != shuffled.Pointer, "Test data does not distinguish original and shuffled assets.");
                Check(AvatarPresentation.TryCreateFieldSprite(village, Role, 2, ref result), "Field projection failed.");
                Check(ReferenceEquals(village.LastOpd, pristine) && village.LastOpd.Pointer != shuffled.Pointer
                    && result.opd_data.Pointer == pristine.Pointer && Runtime.OriginalReads == 1,
                    "Field projection applied the permutation twice or bypassed pristine originals.");
                Check(village.LastDirection == 2 && GSD1.AnimeSpriteManager.DestroyCalls == 0, "Transferred sprite ownership or direction is wrong.");
            }),
            ("Unsupported field poses retain native sprite without reading donor assets", () => {
                foreach (int direction in new[] { -1, 4, 127 })
                {
                    var village = new GSD1.Village_c(); var result = OriginalResult(); var before = result;
                    Check(!AvatarPresentation.TryCreateFieldSprite(village, Role, direction, ref result), "Unsupported pose was projected.");
                    Check(ReferenceEquals(result, before) && village.Calls == 0 && Runtime.OriginalReads == 0, "Unsupported pose touched sprite ownership or original assets.");
                }
            }),
            ("Gregminster ending lets the native factory create Hix and Gen", () => {
                var sprites = new GSD1.OPD_DATA[13];
                sprites[7] = new GSD1.OPD_DATA { id = "vac_map_31_07" };
                sprites[10] = new GSD1.OPD_DATA { id = "vac_map_31_10" };
                var village = new GSD1.Village_c { mapVillage = new GSD1.MAP { opd_table = sprites } };
                var prefix = typeof(AvatarFieldSpritePatch).GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic)!;

                foreach (int actor in new[] { 63, 20 })
                {
                    Check(Profile.GetDonor(actor) != actor, "Ending fixture needs a shuffled actor.");
                    var result = OriginalResult();
                    object?[] args = { village, actor, 2, result };
                    Check((bool)prefix.Invoke(null, args)! && ReferenceEquals(args[3], result),
                        "Ending choreography was given a replacement field sprite.");
                }
                Check(village.Calls == 0 && Runtime.OriginalReads == 0, "The ending attempted donor construction.");

                const int tengaar = 33;
                var otherResult = OriginalResult();
                Check(AvatarPresentation.TryCreateFieldSprite(village, tengaar, 2, ref otherResult),
                    "Tengaar lost her field shuffle.");
            }),
            ("Hix and Gen use their shuffled sprites on other maps", () => {
                var sprites = new GSD1.OPD_DATA[8];
                sprites[7] = new GSD1.OPD_DATA { id = "ordinary_field" };
                var village = new GSD1.Village_c { mapVillage = new GSD1.MAP { opd_table = sprites } };

                foreach (int actor in new[] { 63, 20 })
                {
                    var result = OriginalResult();
                    Check(AvatarPresentation.TryCreateFieldSprite(village, actor, 2, ref result)
                        && ReferenceEquals(result.opd_data, Runtime.Originals[Profile.GetDonor(actor)].field_data.opd_data),
                        "The ending guard affected an ordinary field sprite.");
                }
            }),
            ("Protected field identities do not enter donor factory", () => {
                foreach (int id in new[] { 8, 108, 109, 24, -1, 65537 })
                {
                    var village = new GSD1.Village_c(); var result = OriginalResult(); var before = result;
                    Check(!AvatarPresentation.TryCreateFieldSprite(village, id, 0, ref result), "Protected field identity was projected.");
                    Check(ReferenceEquals(result, before) && village.Calls == 0, "Protected field factory was called.");
                }
            }),
            ("Unavailable original donor asset retains native field sprite", () => {
                Runtime.Originals.Remove(Donor);
                var village = new GSD1.Village_c(); var result = OriginalResult(); var before = result;
                Check(!AvatarPresentation.TryCreateFieldSprite(village, Role, 0, ref result), "Missing donor original did not fall back.");
                Check(ReferenceEquals(before, result) && village.Calls == 0, "Missing original used the shuffled cache or changed result.");
            }),
            ("Rejected factory sprite is destroyed and native result remains owned by caller", () => {
                var village = new GSD1.Village_c { Factory = (opd, direction) => {
                    var sprite = GSD1.Village_c.ValidSprite(opd, direction); sprite.pattern_kazu = 0; return sprite;
                } };
                var result = OriginalResult(); var before = result;
                Check(!AvatarPresentation.TryCreateFieldSprite(village, Role, 0, ref result), "Invalid factory sprite was published.");
                Check(ReferenceEquals(result, before) && GSD1.AnimeSpriteManager.DestroyCalls == 1, "Rejected sprite cleanup or original ownership failed.");
            }),
            ("Factory exception retains native result", () => {
                var village = new GSD1.Village_c { Factory = (opd, direction) => throw new InvalidOperationException("test factory failure") };
                var result = OriginalResult(); var before = result;
                Check(!AvatarPresentation.TryCreateFieldSprite(village, Role, 0, ref result), "Factory exception escaped fallback.");
                Check(ReferenceEquals(result, before), "Factory failure changed native result.");
            }),
            ("Cleanup exception does not publish invalid sprite or escape fallback", () => {
                GSD1.AnimeSpriteManager.ThrowOnDestroy = true;
                var village = new GSD1.Village_c { Factory = (opd, direction) => {
                    var sprite = GSD1.Village_c.ValidSprite(opd, direction); sprite.pattern_no = 2; return sprite;
                } };
                var result = OriginalResult(); var before = result;
                Check(!AvatarPresentation.TryCreateFieldSprite(village, Role, 0, ref result), "Cleanup exception escaped fallback.");
                Check(ReferenceEquals(result, before) && GSD1.AnimeSpriteManager.DestroyCalls == 1
                    && Runtime.Traces.Any(x => x.Contains("cleanup", StringComparison.Ordinal)), "Cleanup failure was not recorded safely.");
            }),
            ("Field Harmony prefix skips original only after a verified replacement", () => {
                var village = new GSD1.Village_c(); var result = OriginalResult();
                object?[] args = { village, Role, 1, result };
                bool runNative = (bool)typeof(AvatarFieldSpritePatch).GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, args)!;
                Check(!runNative && ((GSD1.AnimeSPRITE)args[3]!).opd_data.Pointer == Runtime.Originals[Donor].field_data.opd_data.Pointer,
                    "Field patch did not skip native construction after replacement.");
                args = new object?[] { village, 8, 1, result };
                runNative = (bool)typeof(AvatarFieldSpritePatch).GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, args)!;
                Check(runNative, "Field patch skipped native construction for a protected role.");
            })
        };

        int passed = 0;
        foreach (var test in tests)
        {
            try { Setup(); test.Run(); passed++; Console.WriteLine("PASS " + test.Name); }
            catch (Exception exception) { Console.Error.WriteLine("FAIL " + test.Name + ": " + exception); return 1; }
        }
        Check(!AvatarPresentation.ResolvingNames && !AvatarPresentation.ResolvingFaces, "Final test leaked a presentation scope.");
        Console.WriteLine(passed + "/" + tests.Length + " production presentation adapter checks passed.");
        Console.WriteLine("Scope: linked production AvatarPresentation.cs with explicitly labeled test doubles; no native detours, real external wrappers or full plugin compilation verified.");
        return 0;
    }
}
