extern alias GSD1;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace SuikodenRandomizer.AvatarPlugin;

// Generated from SHA-verified original native initializer and reviewed choreography.
// Native facts: 7e08cb6b9e6d1e382d5f281a1bddc27ec8a615b3caabdac2a6e340639302a912
internal sealed class ShuffledUniteRecipe
{
    internal readonly int Id, Function, Family, Type, NameId, HelpId;
    internal readonly string Name;
    internal readonly IReadOnlyList<int> Donors;
    internal readonly IReadOnlyList<int>? OpdNumbers;
    private readonly IReadOnlyList<int>[] _rows;
    internal string WorkGetter => "kmagic" + Family.ToString("00", System.Globalization.CultureInfo.InvariantCulture) + "_work";
    internal ShuffledUniteRecipe(int id, string name, int function, int family, int type, int nameId, int helpId, int[] donors, int[]? opds, int[][] rows)
    {
        Id=id; Name=name; Function=function; Family=family; Type=type; NameId=nameId; HelpId=helpId;
        Donors=Array.AsReadOnly((int[])donors.Clone()); OpdNumbers=opds is null ? null : Array.AsReadOnly((int[])opds.Clone());
        _rows=new IReadOnlyList<int>[rows.Length]; for(int i=0;i<rows.Length;i++) _rows[i]=Array.AsReadOnly((int[])rows[i].Clone());
        if (_rows.Length != Donors.Count) throw new InvalidOperationException("Unite row contract has the wrong count.");
    }
    internal IReadOnlyList<int> Rows(int position) => _rows[position];
    internal bool Matches(GSD1.KYORYOKU_DATA native)
    {
        if (native.player_kazu!=Donors.Count || native.func_no!=Function || native.type!=Type || native.name!=NameId || native.help!=HelpId
            || native.player_id is not { } ids || ids.Count!=Donors.Count) return false;
        for(int i=0;i<Donors.Count;i++) if(ids[i]!=Donors[i]) return false;
        var opds=native.opd_nums;
        if(OpdNumbers is null) return opds is null;
        if(opds is null || opds.Count!=OpdNumbers.Count) return false;
        for(int i=0;i<OpdNumbers.Count;i++) if(opds[i]!=OpdNumbers[i]) return false;
        return true;
    }
}

internal static class AvatarUniteRecipes
{
    internal const string NativeFactsSha256 = "7e08cb6b9e6d1e382d5f281a1bddc27ec8a615b3caabdac2a6e340639302a912";
    internal static readonly IReadOnlyList<ShuffledUniteRecipe> All = new ReadOnlyCollection<ShuffledUniteRecipe>(new ShuffledUniteRecipe[]
    {
        new ShuffledUniteRecipe(1, "Guardian Attack", 1, 1, 3, 67892, 1261, new[] { 6, 2 }, null, new int[][] { new[] { 9 }, new[] { 9, 15 } }),
        new ShuffledUniteRecipe(2, "Fisherman Attack", 2, 2, 3, 67893, 1262, new[] { 32, 38 }, null, new int[][] { new[] { 9 }, new[] { 9, 15 } }),
        new ShuffledUniteRecipe(3, "Wild Arrow Attack", 3, 3, 1, 67894, 1264, new[] { 4, 9 }, null, new int[][] { new[] { 10, 15 }, new[] { 10 } }),
        new ShuffledUniteRecipe(4, "Elf Attack", 4, 4, 3, 67895, 1276, new[] { 4, 75, 9 }, null, new int[][] { new[] { 11, 15 }, new[] { 11 }, new[] { 11 } }),
        new ShuffledUniteRecipe(5, "Pirate Attack", 5, 5, 3, 67896, 1275, new[] { 11, 43, 41 }, null, new int[][] { new[] { 9, 15 }, new[] { 9 }, new[] { 9 } }),
        new ShuffledUniteRecipe(6, "Blacksmith Attack", 6, 6, 3, 67897, 1285, new[] { 70, 72, 69, 71 }, null, new int[][] { new[] { 9, 15 }, new[] { 9, 15 }, new[] { 9, 15 }, new[] { 9, 15 } }),
        new ShuffledUniteRecipe(7, "Opposites Attrack", 7, 7, 3, 67898, 1270, new[] { 15, 21 }, null, new int[][] { new[] { 10, 11, 15 }, new[] { 9 } }),
        new ShuffledUniteRecipe(8, "Fancy Lad Attack", 16, 16, 3, 67899, 1278, new[] { 17, 48, 49 }, null, new int[][] { new[] { 9, 15 }, new[] { 9, 15 }, new[] { 9, 15 } }),
        new ShuffledUniteRecipe(9, "Pretty Girl Attack", 22, 22, 3, 67900, 1281, new[] { 3, 33, 23 }, null, new int[][] { Array.Empty<int>(), new[] { 11, 12 }, new[] { 16 } }),
        new ShuffledUniteRecipe(10, "Beauty Attack", 17, 17, 1, 67901, 1280, new[] { 1, 0, 36 }, new[] { 1, 3, 7 }, new int[][] { new[] { 10, 15 }, new[] { 10 }, new[] { 10 } }),
        new ShuffledUniteRecipe(11, "Flash Attack", 15, 15, 3, 67902, 1284, new[] { 7, 18, 91 }, null, new int[][] { new[] { 9 }, new[] { 9 }, new[] { 9, 15 } }),
        new ShuffledUniteRecipe(12, "Kobold Attack", 12, 1, 3, 67903, 1268, new[] { 14, 80 }, null, new int[][] { new[] { 9 }, new[] { 9, 15 } }),
        new ShuffledUniteRecipe(13, "Dragon Knight Attack", 11, 1, 3, 67904, 1269, new[] { 19, 29 }, null, new int[][] { new[] { 9 }, new[] { 9, 15 } }),
        new ShuffledUniteRecipe(14, "Stuck With You Attack", 13, 13, 3, 67905, 1271, new[] { 20, 67 }, null, new int[][] { new[] { 9, 15 }, new[] { 9, 15 } }),
        new ShuffledUniteRecipe(15, "Trickster Attack", 10, 10, 1, 67906, 1272, new[] { 76, 39 }, null, new int[][] { new[] { 11 }, new[] { 11, 12 } }),
        new ShuffledUniteRecipe(16, "Warrior Attack", 8, 8, 3, 67907, 1265, new[] { 33, 63 }, new[] { 1 }, new int[][] { new[] { 10, 15 }, new[] { 9 } }),
        new ShuffledUniteRecipe(17, "Couple Attack", 9, 9, 3, 67908, 1266, new[] { 0, 30 }, new[] { 1 }, new int[][] { new[] { 11, 16 }, new[] { 9, 10 } }),
        new ShuffledUniteRecipe(18, "Bandit Attack", 18, 18, 3, 67909, 1267, new[] { 12, 31 }, null, new int[][] { new[] { 9, 10 }, new[] { 10, 15 } }),
        new ShuffledUniteRecipe(19, "Carpenter Attack", 14, 1, 3, 67910, 1273, new[] { 20, 95 }, null, new int[][] { new[] { 9 }, new[] { 9, 15 } }),
        new ShuffledUniteRecipe(20, "Wild Arrow Attack", 3, 3, 1, 67894, 1264, new[] { 4, 51 }, null, new int[][] { new[] { 10, 15 }, new[] { 10 } }),
        new ShuffledUniteRecipe(21, "Wild Arrow Attack", 3, 3, 1, 67894, 1264, new[] { 4, 75 }, null, new int[][] { new[] { 10, 15 }, new[] { 10 } }),
        new ShuffledUniteRecipe(22, "Blacksmith Attack", 6, 6, 3, 67897, 1285, new[] { 70, 72, 69, 5 }, null, new int[][] { new[] { 9, 15 }, new[] { 9, 15 }, new[] { 9, 15 }, new[] { 9, 15 } }),
        new ShuffledUniteRecipe(23, "Blacksmith Attack", 6, 6, 3, 67897, 1285, new[] { 5, 72, 69, 71 }, null, new int[][] { new[] { 9, 15 }, new[] { 9, 15 }, new[] { 9, 15 }, new[] { 9, 15 } }),
        new ShuffledUniteRecipe(24, "Blacksmith Attack", 6, 6, 3, 67897, 1285, new[] { 70, 5, 69, 71 }, null, new int[][] { new[] { 9, 15 }, new[] { 9, 15 }, new[] { 9, 15 }, new[] { 9, 15 } }),
        new ShuffledUniteRecipe(25, "Blacksmith Attack", 6, 6, 3, 67897, 1285, new[] { 70, 72, 5, 71 }, null, new int[][] { new[] { 9, 15 }, new[] { 9, 15 }, new[] { 9, 15 }, new[] { 9, 15 } }),
        new ShuffledUniteRecipe(26, "Beauty Attack", 17, 17, 1, 67901, 1280, new[] { 1, 0, 10 }, new[] { 1, 3, 7 }, new int[][] { new[] { 10, 15 }, new[] { 10 }, new[] { 10 } }),
        new ShuffledUniteRecipe(27, "Kobolds & Pal Attack", 19, 19, 3, 67911, 1282, new[] { 14, 65, 80 }, null, new int[][] { new[] { 9, 15 }, new[] { 9, 15 }, new[] { 9 } }),
        new ShuffledUniteRecipe(28, "Beat 'Em Up Attack", 20, 20, 3, 67912, 1263, new[] { 6, 13 }, null, new int[][] { new[] { 10, 15 }, new[] { 9, 10, 11, 12 } }),
        new ShuffledUniteRecipe(29, "Ninja Attack", 21, 21, 3, 67913, 1277, new[] { 23, 55, 22 }, null, new int[][] { new[] { 15, 16 }, new[] { 10, 11 }, new[] { 10, 11 } }),
        new ShuffledUniteRecipe(30, "Martial Arts Attack", 23, 23, 1, 67914, 1283, new[] { 66, 6, 52 }, null, new int[][] { new[] { 9, 10, 11, 12 }, new[] { 11 }, new[] { 9 } }),
        new ShuffledUniteRecipe(31, "Lepant Family Attack", 24, 24, 3, 67915, 1279, new[] { 30, 0, 62 }, null, new int[][] { new[] { 11, 15 }, new[] { 12, 15 }, new[] { 15 } }),
        new ShuffledUniteRecipe(32, "Master & Pupil Attack", 25, 25, 1, 67916, 1274, new[] { 8, 91 }, null, new int[][] { new[] { 10, 11, 15 }, new[] { 10, 11 } }),
    });
    internal static bool TryGet(int id, out ShuffledUniteRecipe? recipe)
    {
        if(id>=1 && id<=All.Count) { recipe=All[id-1]; return recipe.Id==id; }
        recipe=null; return false;
    }
}
