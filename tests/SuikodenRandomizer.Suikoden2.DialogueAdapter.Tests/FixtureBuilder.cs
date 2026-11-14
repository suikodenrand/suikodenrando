using System;
using System.Linq;
using Il2CppInterop.Runtime.InteropTypes.Arrays;

internal static class FixtureBuilder
{
    private static Il2CppStructArray<byte> Bytes(int count, int seed)
    { var a = new Il2CppStructArray<byte>(count); for (int i=0;i<count;i++) a[i]=(byte)((seed+i)%250); return a; }
    internal static GAME_WORK Fixture(params byte[] roles)
    {
        const int count=128;
        var game=new GAME_WORK { chara_data=new CHARA_DATA {
            c_kotei_dat=new Il2CppReferenceArray<C_KOTEI_DAT>(count), c_varia_dat=new Il2CppReferenceArray<C_VARIA_DAT>(count),
            ckd_info=new Il2CppSystem.Collections.Generic.List<Il2CppSystem.Collections.Generic.List<byte>>(),
            _serialized_ckd_info=new Il2CppSystem.Collections.Generic.List<IMPORT_GAME_DATA.NestDataClass<byte>>(),
            suki_value=new Il2CppStructArray<sbyte>(count) },
            party_data=new PARTY_DATA {party_cha_no=Bytes(8,0)}, c_teg_dat=new C_TEG_DAT {name=new Il2CppStructArray<int>(count)},
            arms_data=new Il2CppReferenceArray<ARMS_DATA>(count),chara_flag=Enumerable.Range(0,count).Select(i=>(byte)(i%7)).ToArray(),story_flag=new byte[] {3,17,91,128} };
        for(int i=0;i<count;i++) {
            game.chara_data.c_kotei_dat[i]=new C_KOTEI_DAT {seicyo_type=Bytes(8,i),monsyo_seigen=Bytes(8,i+1),mon_zoku_aisyo=Bytes(8,i+2),mon_kosu_type=Bytes(8,i+3),soubi_henkou=(byte)i,soubi_kanou=(byte)(i+1),okori_panic=(byte)(i+2)};
            var items=new Il2CppReferenceArray<PARTY_ITEM>(3);for(int n=0;n<3;n++)items[n]=new PARTY_ITEM {item_no=(byte)(i+n),use_cnt=(byte)(n+1)};
            game.chara_data.c_varia_dat[i]=new C_VARIA_DAT {level=(byte)(1+i%70),exp=(ushort)(i*101),now_hp=(ushort)(100+i),max_hp=(ushort)(200+i),mp=Bytes(4,i),para=Bytes(7,i+1),buki_lv=(byte)(1+i%16),buki_mon=(byte)(i%8),mon_eqp=Bytes(3,i+2),bogu_eqp=Bytes(3,i+3),item_eqp=items,todome=(ushort)(i+4)};
            var row=new Il2CppSystem.Collections.Generic.List<byte>();row.Add((byte)i);row.Add((byte)(new[] {49,50,72,74}.Contains(i)?0x60:i==26?6:0));row.Add((byte)(i+2));
            if(i==26)game.chara_data.c_varia_dat[i].buki_lv=0; // Original natural-weapon class6 legitimately uses level0.
            game.chara_data.ckd_info.Add(row);var mirror=new Il2CppSystem.Collections.Generic.List<byte>();mirror.AddRange(row);game.chara_data._serialized_ckd_info.Add(new IMPORT_GAME_DATA.NestDataClass<byte>{data=mirror});
            game.c_teg_dat.name[i]=10000+i;game.chara_data.suki_value[i]=(sbyte)(i%100);
            game.arms_data[i]=new ARMS_DATA {name_1=20000+i,name_2=21000+i,name_3=22000+i,info=Bytes(17,i),mon_no=(byte)(i%10)};
        }
        SetParty(game,roles.Length==0?new byte[]{1,80}:roles);
        return game;
    }
    private static void SetParty(GAME_WORK game, params byte[] roles)
    { for(int i=0;i<8;i++)game.party_data.party_cha_no[i]=i<roles.Length?roles[i]:(byte)0; }
}
