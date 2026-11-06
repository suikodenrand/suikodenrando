extern alias GSD1;

using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using Il2CppInterop.Runtime.InteropTypes.Arrays;

namespace SuikodenRandomizer.AvatarPlugin;

/// <summary>
/// Detached data preparation for the canonical-role avatar pilot. These helpers
/// never publish a cache entry or replace a persistent GAME_WORK base record.
/// OPD resources are reused read-only; mutable player records and arrays are
/// copied. Native constructor allocation remains experimental when PLAYER_DATA
/// is not a Unity object. Metadata alone does not establish every native combat
/// rule or the lifecycle requirements of the external SOBase implementation.
/// </summary>
internal static class AvatarNativeData
{
    internal static GSD1.PLAYER_BASE FindBase(int characterId)
    {
        if (characterId < 0 || characterId > byte.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(characterId));
        var records = GSD1.OldSrcBase.game_work?.player_base;
        if (records is null)
            throw new InvalidOperationException("Persistent character records are unavailable.");

        GSD1.PLAYER_BASE? found = null;
        for (int i = 0; i < records.Count; i++)
        {
            var candidate = records[i];
            if (candidate is null || candidate.chara_no != characterId) continue;
            if (found is not null)
                throw new InvalidOperationException("Duplicate persistent character identity " + characterId + ".");
            found = candidate;
        }
        if (found is null)
            throw new InvalidOperationException("No persistent character record matches " + characterId + ".");
        ValidateBase(found);
        return found;
    }

    internal static Il2CppStructArray<byte> CloneBytes(Il2CppStructArray<byte> source)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));
        var copy = new Il2CppStructArray<byte>(source.Count);
        for (int i = 0; i < source.Count; i++) copy[i] = source[i];
        return copy;
    }

    internal static GSD1.PLAYER_BASE CloneBase(GSD1.PLAYER_BASE source)
    {
        ValidateBase(source);
        var sourceItems = source.item;
        var sourceWeapon = source.buki_data;
        var sourceRune = source.monsyo_data;
        var items = new Il2CppReferenceArray<GSD1.PLAYER_ITEM>(sourceItems.Count);
        for (int i = 0; i < sourceItems.Count; i++)
        {
            var item = sourceItems[i];
            if (item is null)
                throw new InvalidOperationException("Persistent inventory contains an uninitialized item record.");
            items[i] = new GSD1.PLAYER_ITEM
            {
                item_id = item.item_id,
                soubi = item.soubi,
                data = item.data
            };
        }
        var weapon = new GSD1.PLAYER_BUKI
        {
            buki_id = sourceWeapon.buki_id,
            level = sourceWeapon.level,
            monsyo = CloneBytes(sourceWeapon.monsyo)
        };
        var rune = new GSD1.PLAYER_MONSYO
        {
            monsyo_id = sourceRune.monsyo_id,
            monsyo_level = sourceRune.monsyo_level,
            monsyo_exp = sourceRune.monsyo_exp
        };
        return new GSD1.PLAYER_BASE
        {
            chara_no = source.chara_no,
            max_hp = source.max_hp,
            hp = source.hp,
            magic_point = CloneBytes(source.magic_point),
            level = source.level,
            exp = source.exp,
            noryoku = CloneBytes(source.noryoku),
            status = source.status,
            seicho_type = CloneBytes(source.seicho_type),
            item_kazu = source.item_kazu,
            item = items,
            buki_data = weapon,
            monsyo_data = rune
        };
    }

    /// <summary>
    /// Prepare independent nested records before touching the destination.
    /// The caller owns transaction rollback if a native setter subsequently
    /// fails. Existing references to the destination PLAYER_BASE remain valid.
    /// </summary>
    internal static void CopyBase(GSD1.PLAYER_BASE from, GSD1.PLAYER_BASE to,
        bool preserveIdentity = true)
    {
        if (to is null) throw new ArgumentNullException(nameof(to));
        if (to.Pointer == IntPtr.Zero)
            throw new InvalidOperationException("The destination character record has no native allocation.");
        int identity = to.chara_no;
        if (preserveIdentity && (identity < 0 || identity > byte.MaxValue))
            throw new InvalidOperationException("The destination character identity is invalid.");
        var copy = CloneBase(from);

        to.max_hp = copy.max_hp;
        to.hp = copy.hp;
        to.magic_point = copy.magic_point;
        to.level = copy.level;
        to.exp = copy.exp;
        to.noryoku = copy.noryoku;
        to.status = copy.status;
        to.seicho_type = copy.seicho_type;
        to.item_kazu = copy.item_kazu;
        to.item = copy.item;
        to.buki_data = copy.buki_data;
        to.monsyo_data = copy.monsyo_data;
        if (!preserveIdentity) to.chara_no = copy.chara_no;
        if (preserveIdentity && to.chara_no != identity)
            throw new InvalidOperationException("Copying character state changed its canonical identity.");
    }

    /// <summary>Fingerprint every declared persistent field, including identity.</summary>
    internal static string Fingerprint(GSD1.PLAYER_BASE source)
    {
        ValidateBase(source);
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
        {
            // A format tag prevents an accidental comparison with another
            // serializer. Array lengths delimit all variable-length content.
            writer.Write("srr-avatar-player-base-v1");
            WriteScalar(writer, source.chara_no);
            WriteScalar(writer, source.max_hp);
            WriteScalar(writer, source.hp);
            WriteBytes(writer, source.magic_point);
            WriteScalar(writer, source.level);
            WriteScalar(writer, source.exp);
            WriteBytes(writer, source.noryoku);
            WriteScalar(writer, source.status);
            WriteBytes(writer, source.seicho_type);
            WriteScalar(writer, source.item_kazu);
            var items = source.item;
            writer.Write(items.Count);
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (item is null)
                    throw new InvalidOperationException("Persistent inventory contains an uninitialized item record.");
                WriteScalar(writer, item.item_id);
                WriteScalar(writer, item.soubi);
                WriteScalar(writer, item.data);
            }
            var weapon = source.buki_data;
            WriteScalar(writer, weapon.buki_id);
            WriteScalar(writer, weapon.level);
            WriteBytes(writer, weapon.monsyo);
            var rune = source.monsyo_data;
            WriteScalar(writer, rune.monsyo_id);
            WriteScalar(writer, rune.monsyo_level);
            WriteScalar(writer, rune.monsyo_exp);
            writer.Flush();
        }
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(stream.ToArray())).ToLowerInvariant();
    }

    internal static GSD1.PLAYER_DATA CreateProxy(GSD1.PLAYER_DATA canonical,
        GSD1.PLAYER_DATA donor, GSD1.PLAYER_BASE canonicalBase)
    {
        if (canonical is null) throw new ArgumentNullException(nameof(canonical));
        if (donor is null) throw new ArgumentNullException(nameof(donor));
        if (canonical.Pointer == IntPtr.Zero || donor.Pointer == IntPtr.Zero)
            throw new InvalidOperationException("Character assets have no native allocation.");
        ValidateBase(canonicalBase);
        if (canonicalBase.chara_no != canonical.chara_id)
            throw new InvalidOperationException("The proxy base does not belong to the canonical role.");

        // Prepare donor metadata before allocating/publishing a PLAYER_DATA.
        // No donor persistent state is read or assigned here.
        var padding = CloneBytes(canonical.n_1);
        var runeTypes = CloneBytes(donor.monsyu_type);
        var growth = CloneBytes(donor.seicho_type);
        if (growth.Count < 7)
            throw new InvalidOperationException("Donor growth metadata is incomplete.");
        var donorField = donor.field_data;
        if (donorField?.opd_data is null)
            throw new InvalidOperationException("Donor field graphics are unavailable.");
        var field = new GSD1.PLAYER_FIELD { opd_data = donorField.opd_data };
        var battle = CloneBattle(donor.battle_data);
        // PLAYER_FACE declares no data fields in this build; portrait assets
        // are resolved separately by the presentation hooks.
        var face = donor.face_data is null ? null : new GSD1.PLAYER_FACE();

        bool isUnityObject = typeof(UnityEngine.Object).IsAssignableFrom(typeof(GSD1.PLAYER_DATA));
        UnityEngine.Object? unityClone = null;
        try
        {
            GSD1.PLAYER_DATA proxy;
            if (isUnityObject)
            {
                // Use the exact non-generic Object overload exposed by the
                // installed Unity interop assembly. Failure never falls through
                // to constructor allocation for a Unity-backed asset.
                var instantiate = FindUnityObjectMethod("Instantiate", typeof(UnityEngine.Object));
                unityClone = instantiate.Invoke(null, new object[] { (UnityEngine.Object)(object)canonical })
                    as UnityEngine.Object;
                if (unityClone is null)
                    throw new InvalidOperationException("Unity did not return a cloned canonical asset.");
                proxy = unityClone.Cast<GSD1.PLAYER_DATA>();
            }
            else
            {
                // This invokes the generated native constructor, not a
                // fabricated asset. SOBase lifecycle and all combat consumers
                // still require validation before broadening the pilot.
                proxy = new GSD1.PLAYER_DATA();
            }

            if (proxy.Pointer == IntPtr.Zero || proxy.Pointer == canonical.Pointer
                || proxy.Pointer == donor.Pointer)
                throw new InvalidOperationException("The avatar proxy is not an independent native object.");
            proxy.chara_id = canonical.chara_id;
            proxy.n_1 = padding;
            // Menus, battle commands and results can read this packed localized
            // name directly instead of calling Fhuman_c. It is display metadata,
            // separate from chara_id and the canonical persistent base. Copy the
            // whole donor key so the native localization/table selection remains
            // intact; leave both shared original assets untouched.
            proxy.chara_name = donor.chara_name;
            proxy.n_2 = canonical.n_2;
            proxy.sex = donor.sex;
            proxy.soubi_type = donor.soubi_type;
            proxy.monsyu_type = runeTypes;
            proxy.seicho_type = growth;
            proxy.player_base = canonicalBase;
            proxy.battle_data = battle;
            proxy.field_data = field;
            proxy.face_data = face;
            if (proxy.chara_name != donor.chara_name)
                throw new InvalidOperationException("The avatar proxy lost its donor display name key.");
            if (proxy.chara_id != canonical.chara_id
                || proxy.player_base?.Pointer != canonicalBase.Pointer)
                throw new InvalidOperationException("The avatar proxy lost its canonical identity or base binding.");
            return proxy;
        }
        catch
        {
            // An unpublished Unity clone would otherwise survive managed GC.
            // Never destroy either original, even if a broken clone API returns
            // an alias. The original failure remains the transaction error.
            if (unityClone is not null && unityClone.Pointer != IntPtr.Zero
                && unityClone.Pointer != canonical.Pointer && unityClone.Pointer != donor.Pointer)
            {
                try
                {
                    FindUnityObjectMethod("Destroy", typeof(void)).Invoke(null,
                        new object[] { unityClone });
                }
                catch { }
            }
            throw;
        }
    }

    private static GSD1.PLAYER_BATTLE_SO CloneBattle(GSD1.PLAYER_BATTLE_SO source)
    {
        if (source is null || source.opd_data is null || source.action_table is null
            || source.action_function_index is null)
            throw new InvalidOperationException("Donor battle metadata is incomplete.");
        var actions = source.action_table;
        var functions = source.action_function_index;
        // These are independent native tables, not parallel arrays. Original
        // Kirkis, Sylvina and Stallion assets have one more ActionCommand than
        // function-index entry. Preserve each table's exact length and contents:
        // padding/truncating or rejecting those original shapes changes valid
        // donor behavior. Both tables are nonempty in every reviewed combat asset.
        if (actions.Count == 0 || functions.Count == 0)
            throw new InvalidOperationException("Donor battle action tables are empty.");
        var table = new Il2CppReferenceArray<GSD1.PLAYER_BATTLE_SO.ActionCommand>(actions.Count);
        for (int i = 0; i < actions.Count; i++)
        {
            var action = actions[i];
            if (action?.action_cmd is null)
                throw new InvalidOperationException("Donor battle action commands are incomplete.");
            var commands = action.action_cmd;
            var copied = new Il2CppStructArray<short>(commands.Count);
            for (int j = 0; j < commands.Count; j++) copied[j] = commands[j];
            table[i] = new GSD1.PLAYER_BATTLE_SO.ActionCommand { action_cmd = copied };
        }
        var indices = new Il2CppStructArray<short>(functions.Count);
        for (int i = 0; i < functions.Count; i++) indices[i] = functions[i];
        return new GSD1.PLAYER_BATTLE_SO
        {
            action_table = table,
            action_function_index = indices,
            opd_data = source.opd_data
        };
    }

    internal static void ValidateBase(GSD1.PLAYER_BASE source)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));
        if (source.Pointer == IntPtr.Zero || source.chara_no < 0 || source.chara_no > byte.MaxValue)
            throw new InvalidOperationException("The persistent character identity/allocation is invalid.");
        if (source.level == 0 || source.max_hp == 0)
            throw new InvalidOperationException("The persistent character level/HP has not been initialized.");
        if (source.magic_point is null || source.magic_point.Count < 5
            || source.noryoku is null || source.noryoku.Count < 6
            || source.seicho_type is null || source.seicho_type.Count < 7
            || source.item is null || source.item.Count < 9 || source.item_kazu > source.item.Count
            || source.buki_data is null || source.buki_data.monsyo is null
            || source.buki_data.monsyo.Count < 6 || source.monsyo_data is null)
            throw new InvalidOperationException("Persistent character stats/equipment are incomplete.");
    }

    private static void WriteScalar(BinaryWriter writer, object value) =>
        writer.Write(Convert.ToInt64(value, CultureInfo.InvariantCulture));

    private static void WriteBytes(BinaryWriter writer, Il2CppStructArray<byte> bytes)
    {
        if (bytes is null) throw new InvalidOperationException("A fingerprint array is missing.");
        writer.Write(bytes.Count);
        for (int i = 0; i < bytes.Count; i++) writer.Write(bytes[i]);
    }

    private static MethodInfo FindUnityObjectMethod(string name, Type returnType)
    {
        MethodInfo? found = null;
        foreach (var method in typeof(UnityEngine.Object).GetMethods(BindingFlags.Public | BindingFlags.Static))
        {
            if (method.Name != name || method.IsGenericMethod || method.ReturnType != returnType) continue;
            var parameters = method.GetParameters();
            if (parameters.Length != 1 || parameters[0].ParameterType != typeof(UnityEngine.Object)) continue;
            if (found is not null)
                throw new AmbiguousMatchException("Multiple exact Unity Object." + name + " overloads.");
            found = method;
        }
        return found ?? throw new MissingMethodException("UnityEngine.Object", name + "(Object)");
    }
}
