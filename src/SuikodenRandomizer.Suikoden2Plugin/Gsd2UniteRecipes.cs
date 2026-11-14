extern alias GSD2;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace SuikodenRandomizer.Suikoden2Plugin;

// Original asset defaults; native battle objects remain mutable.
internal sealed class Gsd2UniteRecipe
{
    internal readonly int Index, Count, NameId, TextId, Info, Attribute, Function, Hosei;
    internal readonly int MonsterStatus, MonsterStatusProbability, PlayerStatus, PlayerStatusProbability;
    internal readonly string Name;
    internal readonly IReadOnlyList<byte> RawMembers;
    internal readonly IReadOnlyList<int> DonorIds;
    internal readonly IReadOnlyList<byte> HighBitFlags;
    internal bool IsDisabled => Index == 0;
    internal bool IsUpgradeOnly => Index == 40;
    internal bool HasNativeSpecialEligibility => Index == 19;
    internal Gsd2UniteRecipe(int index, string name, int nameId, int textId, int info, int attribute, int count,
        byte[] rawMembers, int function, int hosei, int monsterStatus, int monsterStatusProbability, int playerStatus, int playerStatusProbability)
    {
        Index=index; Name=name; NameId=nameId; TextId=textId; Info=info; Attribute=attribute; Count=count;
        Function=function; Hosei=hosei; MonsterStatus=monsterStatus; MonsterStatusProbability=monsterStatusProbability;
        PlayerStatus=playerStatus; PlayerStatusProbability=playerStatusProbability;
        var copied=(byte[])rawMembers.Clone(); var donors=new int[copied.Length]; var flags=new byte[copied.Length];
        for(int i=0;i<copied.Length;i++) { donors[i]=copied[i]&0x7f; flags[i]=(byte)(copied[i]&0x80); }
        RawMembers=Array.AsReadOnly(copied); DonorIds=Array.AsReadOnly(donors); HighBitFlags=Array.AsReadOnly(flags);
        if(index!=0 && count!=copied.Length) throw new InvalidOperationException("S2 unite member count mismatch.");
    }
    internal byte EncodeForRole(int position, int canonicalRole)
    {
        if(canonicalRole<1 || canonicalRole>127) throw new ArgumentOutOfRangeException(nameof(canonicalRole));
        return checked((byte)(HighBitFlags[position]|canonicalRole));
    }
    internal bool MatchesOriginalMetadata(GSD2.UNITE_DATA native)
        => native is not null && native.name==NameId && native.text==TextId && native.info==Info && native.atrb==Attribute
            && native.player_kazu==Count && native.func_no==Function && native.hosei==Hosei
            && native.m_status==MonsterStatus && native.m_status_prob==MonsterStatusProbability
            && native.p_status==PlayerStatus && native.p_status_prob==PlayerStatusProbability;
    // Native UniteNormalFunc derives hosei/m_status/m_status_prob in the loaded table.
    // The loader can reuse this table across battles. Defaults are artifact proof;
    // every runtime projection retains the current native values via stable validation.
    internal bool MatchesStableMetadata(GSD2.UNITE_DATA native)
        => native is not null && native.name==NameId && native.text==TextId && native.info==Info && native.atrb==Attribute
            && native.player_kazu==Count && native.func_no==Function
            && native.p_status==PlayerStatus && native.p_status_prob==PlayerStatusProbability;
    internal bool MatchesStable(GSD2.UNITE_DATA native)
    {
        if(!MatchesStableMetadata(native) || native.player_id is not { } ids || ids.Count!=RawMembers.Count) return false;
        for(int i=0;i<RawMembers.Count;i++) if(ids[i]!=RawMembers[i]) return false;
        return true;
    }
    internal bool MatchesOriginal(GSD2.UNITE_DATA native)
    {
        if(!MatchesOriginalMetadata(native) || native.player_id is not { } ids || ids.Count!=RawMembers.Count) return false;
        for(int i=0;i<RawMembers.Count;i++) if(ids[i]!=RawMembers[i]) return false;
        return true;
    }
}

internal static class Gsd2UniteRecipes
{
    internal const int OriginalCount=51;
    internal const string OriginalAssetObjectSha256="559287a88885f2df783452d354482463d149c96d7702104e3e827c47dbe4c85a";
    internal const string CatalogSha256="fadd6f3774bf8a909ea58b37be345c788f7aebaf66d9f9a53fb8b2d0e91a28bc";
    internal static readonly IReadOnlyList<Gsd2UniteRecipe> All = new ReadOnlyCollection<Gsd2UniteRecipe>(new Gsd2UniteRecipe[]
    {
        new Gsd2UniteRecipe(0, "(disabled)", 0, 0, 0, 0, 0, new byte[] { 0, 0, 0, 0, 0 }, 0, 0, 0, 0, 0, 0),
        new Gsd2UniteRecipe(1, "Cross Attack", 1234, 1235, 32, 0, 2, new byte[] { 3, 2 }, 86, 150, 7, 30, 0, 0),
        new Gsd2UniteRecipe(2, "Knight Attack", 1236, 1237, 32, 0, 2, new byte[] { 33, 32 }, 87, 200, 0, 0, 7, 30),
        new Gsd2UniteRecipe(3, "Kobold Attack", 1238, 1239, 32, 0, 2, new byte[] { 156, 29 }, 88, 100, 0, 0, 7, 100),
        new Gsd2UniteRecipe(4, "Manly Attack", 1240, 1241, 160, 0, 2, new byte[] { 16, 37 }, 89, 200, 0, 0, 0, 0),
        new Gsd2UniteRecipe(5, "Double Monster Attack", 1242, 1243, 32, 0, 2, new byte[] { 72, 50 }, 90, 300, 0, 0, 7, 100),
        new Gsd2UniteRecipe(6, "Rivals Attack", 1244, 1245, 32, 0, 2, new byte[] { 12, 167 }, 91, 300, 0, 0, 7, 100),
        new Gsd2UniteRecipe(7, "Loyal Dog Attack", 1246, 1247, 32, 0, 2, new byte[] { 30, 26 }, 92, 150, 0, 0, 0, 0),
        new Gsd2UniteRecipe(8, "Bow Wow Attack", 1248, 1249, 32, 0, 2, new byte[] { 28, 26 }, 93, 150, 0, 0, 0, 0),
        new Gsd2UniteRecipe(9, "Bow Wow Attack", 1248, 1249, 32, 0, 2, new byte[] { 29, 26 }, 94, 150, 0, 0, 0, 0),
        new Gsd2UniteRecipe(10, "Head Up!", 1250, 1251, 32, 0, 2, new byte[] { 132, 49 }, 95, 200, 0, 0, 7, 100),
        new Gsd2UniteRecipe(11, "Head Up!", 1250, 1251, 32, 0, 2, new byte[] { 132, 74 }, 96, 200, 0, 0, 7, 100),
        new Gsd2UniteRecipe(12, "Bow Attack", 1252, 1253, 96, 0, 2, new byte[] { 30, 43 }, 97, 50, 0, 0, 0, 0),
        new Gsd2UniteRecipe(13, "Bow Attack", 1252, 1253, 96, 0, 2, new byte[] { 30, 67 }, 98, 50, 0, 0, 0, 0),
        new Gsd2UniteRecipe(14, "Bow Attack", 1252, 1253, 96, 0, 2, new byte[] { 43, 67 }, 99, 50, 0, 0, 0, 0),
        new Gsd2UniteRecipe(15, "Trick Attack", 1254, 1255, 96, 0, 2, new byte[] { 41, 42 }, 100, 100, 0, 0, 0, 0),
        new Gsd2UniteRecipe(16, "Narcissists Attack", 1256, 1253, 96, 0, 2, new byte[] { 64, 65 }, 101, 50, 0, 0, 0, 0),
        new Gsd2UniteRecipe(17, "Winger Attack", 1257, 1258, 32, 0, 2, new byte[] { 54, 56 }, 102, 150, 32, 30, 0, 0),
        new Gsd2UniteRecipe(18, "Dad & Daughter Attack", 1259, 1260, 48, 0, 2, new byte[] { 52, 17 }, 103, 100, 0, 0, 0, 0),
        new Gsd2UniteRecipe(19, "Twin Fighter Attack", 1261, 1262, 32, 0, 2, new byte[] { 68, 69 }, 104, 300, 0, 0, 0, 0),
        new Gsd2UniteRecipe(20, "Buddy Attack", 1263, 1264, 96, 0, 2, new byte[] { 1, 80 }, 105, 100, 0, 0, 0, 0),
        new Gsd2UniteRecipe(21, "Ninja Attack", 1265, 1235, 32, 0, 2, new byte[] { 73, 63 }, 106, 150, 7, 30, 0, 0),
        new Gsd2UniteRecipe(22, "Husband & Wife Attack", 1266, 1267, 32, 0, 2, new byte[] { 163, 57 }, 107, 200, 0, 0, 7, 100),
        new Gsd2UniteRecipe(23, "Double Kraken Attack", 1268, 1260, 224, 0, 2, new byte[] { 49, 74 }, 108, 100, 0, 0, 0, 0),
        new Gsd2UniteRecipe(24, "Swordsman Attack", 1269, 1270, 96, 0, 2, new byte[] { 15, 47 }, 109, 50, 25, 20, 0, 0),
        new Gsd2UniteRecipe(25, "Copycat Attack", 1271, 1272, 32, 0, 2, new byte[] { 1, 71 }, 110, 100, 0, 0, 0, 0),
        new Gsd2UniteRecipe(26, "Double Leader Attack", 1273, 1274, 96, 0, 2, new byte[] { 1, 82 }, 111, 75, 0, 0, 0, 0),
        new Gsd2UniteRecipe(27, "Circus Attack", 1275, 1276, 32, 0, 3, new byte[] { 147, 148, 21 }, 112, 200, 0, 0, 7, 100),
        new Gsd2UniteRecipe(28, "Flash Attack", 1277, 1278, 96, 0, 3, new byte[] { 21, 70, 69 }, 113, 150, 0, 0, 0, 0),
        new Gsd2UniteRecipe(29, "Beauty Attack", 1279, 1280, 96, 0, 3, new byte[] { 14, 20, 25 }, 114, 25, 10, 30, 0, 0),
        new Gsd2UniteRecipe(30, "Warriors' Village Attack", 1281, 1282, 32, 0, 2, new byte[] { 7, 8 }, 115, 200, 0, 0, 0, 0),
        new Gsd2UniteRecipe(31, "Beauty Attack", 1279, 1280, 96, 0, 3, new byte[] { 14, 20, 73 }, 116, 25, 10, 30, 0, 0),
        new Gsd2UniteRecipe(32, "Groupie Attack", 1283, 1284, 32, 0, 2, new byte[] { 2, 55 }, 125, 250, 0, 0, 0, 0),
        new Gsd2UniteRecipe(33, "Beauty Attack", 1279, 1280, 96, 0, 3, new byte[] { 14, 25, 73 }, 118, 25, 10, 30, 0, 0),
        new Gsd2UniteRecipe(34, "Beauty Attack", 1279, 1280, 96, 0, 3, new byte[] { 20, 25, 73 }, 119, 25, 10, 30, 0, 0),
        new Gsd2UniteRecipe(35, "Tackle Attack", 1285, 1286, 96, 0, 2, new byte[] { 23, 46 }, 120, 75, 0, 0, 7, 100),
        new Gsd2UniteRecipe(36, "Family Attack", 1287, 1288, 32, 0, 2, new byte[] { 129, 18 }, 121, 200, 0, 0, 7, 100),
        new Gsd2UniteRecipe(37, "Pretty Boy Attack", 1289, 1290, 96, 0, 3, new byte[] { 9, 62, 53 }, 122, 100, 0, 0, 23, 100),
        new Gsd2UniteRecipe(38, "Pretty Girl Attack", 1291, 1253, 96, 0, 3, new byte[] { 8, 24, 41 }, 123, 50, 0, 0, 0, 0),
        new Gsd2UniteRecipe(39, "Pretty Girl Attack", 1291, 1253, 96, 0, 3, new byte[] { 8, 83, 41 }, 124, 50, 0, 0, 0, 0),
        new Gsd2UniteRecipe(40, "100 Kobolds Attack", 1292, 1239, 96, 0, 2, new byte[] { 28, 29 }, 121, 300, 0, 0, 7, 100),
        new Gsd2UniteRecipe(41, "Fancy Lad Attack", 1293, 1294, 32, 0, 3, new byte[] { 2, 33, 32 }, 126, 150, 25, 25, 0, 0),
        new Gsd2UniteRecipe(42, "Bandit Attack", 1295, 1235, 32, 0, 3, new byte[] { 58, 59, 60 }, 127, 150, 7, 30, 0, 0),
        new Gsd2UniteRecipe(43, "Underling Attack", 1296, 1297, 32, 0, 3, new byte[] { 45, 40, 21 }, 128, 400, 0, 0, 0, 0),
        new Gsd2UniteRecipe(44, "True Beauty Attack", 1298, 1299, 96, 0, 4, new byte[] { 14, 20, 25, 73 }, 129, 25, 10, 70, 0, 0),
        new Gsd2UniteRecipe(45, "Five Squirrel Attack", 1300, 1301, 32, 0, 5, new byte[] { 48, 75, 76, 77, 78 }, 130, 0, 19, 90, 0, 0),
        new Gsd2UniteRecipe(46, "Buddy Attack", 1263, 1264, 96, 0, 2, new byte[] { 1, 81 }, 131, 100, 0, 0, 0, 0),
        new Gsd2UniteRecipe(47, "Beastmaster Attack", 1302, 1303, 32, 0, 3, new byte[] { 179, 49, 74 }, 132, 150, 0, 0, 11, 100),
        new Gsd2UniteRecipe(48, "Beastmaster Attack", 1302, 1303, 32, 0, 3, new byte[] { 179, 49, 72 }, 133, 150, 0, 0, 11, 100),
        new Gsd2UniteRecipe(49, "Beastmaster Attack", 1302, 1303, 32, 0, 3, new byte[] { 179, 49, 50 }, 134, 150, 0, 0, 11, 100),
        new Gsd2UniteRecipe(50, "Beastmaster Attack", 1302, 1303, 32, 0, 3, new byte[] { 179, 72, 50 }, 135, 150, 0, 0, 11, 100),
    });
    internal static bool TryGet(int index, out Gsd2UniteRecipe? recipe)
    {
        if(index>=0 && index<OriginalCount) { recipe=All[index]; return recipe.Index==index; }
        recipe=null; return false;
    }
}
