extern alias GSD2;

using System;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using NativeBytes = Il2CppSystem.Collections.Generic.List<byte>;
using NativeByteRows = Il2CppSystem.Collections.Generic.List<Il2CppSystem.Collections.Generic.List<byte>>;

namespace SuikodenRandomizer.Suikoden2Plugin;

/// <summary>Detached copies of the original, canonical-indexed GSD2 records.</summary>
internal static class Gsd2NativeData
{
    internal static Il2CppStructArray<byte> Bytes(Il2CppStructArray<byte> source)
    {
        if (source is null) throw new InvalidOperationException("Missing native byte array.");
        var result = new Il2CppStructArray<byte>(source.Count);
        for (int i = 0; i < source.Count; i++) result[i] = source[i];
        return result;
    }
    internal static NativeBytes Row(NativeBytes source)
    {
        if (source is null) throw new InvalidOperationException("Missing native character metadata.");
        var result = new NativeBytes();
        for (int i = 0; i < source.Count; i++) result.Add(source[i]);
        return result;
    }
    internal static GSD2.C_KOTEI_DAT Fixed(GSD2.C_KOTEI_DAT source) => new GSD2.C_KOTEI_DAT
    {
        seicyo_type = Bytes(source.seicyo_type), monsyo_seigen = Bytes(source.monsyo_seigen),
        mon_zoku_aisyo = Bytes(source.mon_zoku_aisyo), mon_kosu_type = Bytes(source.mon_kosu_type),
        soubi_henkou = source.soubi_henkou, soubi_kanou = source.soubi_kanou, okori_panic = source.okori_panic
    };
    internal static GSD2.C_VARIA_DAT Variable(GSD2.C_VARIA_DAT source)
    {
        if (source is null) throw new InvalidOperationException("Missing native character state.");
        var items = new Il2CppReferenceArray<GSD2.PARTY_ITEM>(source.item_eqp.Count);
        for (int i = 0; i < items.Count; i++)
        {
            var item = source.item_eqp[i] ?? throw new InvalidOperationException("Missing equipped item.");
            items[i] = new GSD2.PARTY_ITEM { item_no = item.item_no, use_cnt = item.use_cnt };
        }
        return new GSD2.C_VARIA_DAT
        {
            level = source.level, exp = source.exp, now_hp = source.now_hp, max_hp = source.max_hp,
            mp = Bytes(source.mp), para = Bytes(source.para), buki_lv = source.buki_lv, buki_mon = source.buki_mon,
            mon_eqp = Bytes(source.mon_eqp), bogu_eqp = Bytes(source.bogu_eqp), item_eqp = items, todome = source.todome
        };
    }
    internal static GSD2.CHARA_DATA Characters(GSD2.CHARA_DATA source)
    {
        if (source?.c_kotei_dat is null || source.c_varia_dat is null || source.ckd_info is null
            || source.c_kotei_dat.Count < 84 || source.c_varia_dat.Count < 84 || source.ckd_info.Count < 84)
            throw new InvalidOperationException("Original Suikoden II character tables are incomplete.");
        var fixedRows = new Il2CppReferenceArray<GSD2.C_KOTEI_DAT>(source.c_kotei_dat.Count);
        var variableRows = new Il2CppReferenceArray<GSD2.C_VARIA_DAT>(source.c_varia_dat.Count);
        for (int i = 0; i < fixedRows.Count; i++) fixedRows[i] = Fixed(source.c_kotei_dat[i]);
        for (int i = 0; i < variableRows.Count; i++) variableRows[i] = Variable(source.c_varia_dat[i]);
        var info = new NativeByteRows();
        for (int i = 0; i < source.ckd_info.Count; i++) info.Add(Row(source.ckd_info[i]));
        var friendship = new Il2CppStructArray<sbyte>(source.suki_value.Count);
        for (int i = 0; i < friendship.Count; i++) friendship[i] = source.suki_value[i];
        var result = new GSD2.CHARA_DATA { c_kotei_dat = fixedRows, c_varia_dat = variableRows, ckd_info = info, suki_value = friendship };
        // Serialize an independently owned nested-list mirror; never alias the imported asset.
        result._serialized_ckd_info = new Il2CppSystem.Collections.Generic.List<GSD2.IMPORT_GAME_DATA.NestDataClass<byte>>();
        for (int i = 0; i < info.Count; i++)
            result._serialized_ckd_info.Add(new GSD2.IMPORT_GAME_DATA.NestDataClass<byte> { data = Row(info[i]) });
        return result;
    }
    internal static Il2CppStructArray<int> Names(Il2CppStructArray<int> source)
    {
        var result = new Il2CppStructArray<int>(source.Count);
        for (int i = 0; i < source.Count; i++) result[i] = source[i];
        return result;
    }
    internal static GSD2.ARMS_DATA Arm(GSD2.ARMS_DATA source) => new GSD2.ARMS_DATA
    {
        name_1 = source.name_1, name_2 = source.name_2, name_3 = source.name_3,
        info = Bytes(source.info), mon_no = source.mon_no
    };
    internal static Il2CppReferenceArray<GSD2.ARMS_DATA> Arms(Il2CppReferenceArray<GSD2.ARMS_DATA> source)
    {
        if (source is null || source.Count < 84) throw new InvalidOperationException("Original Suikoden II weapon templates are incomplete.");
        var result = new Il2CppReferenceArray<GSD2.ARMS_DATA>(source.Count);
        for (int i = 0; i < result.Count; i++) result[i] = Arm(source[i]);
        return result;
    }
}
