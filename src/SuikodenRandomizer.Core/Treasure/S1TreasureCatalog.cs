using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace SuikodenRandomizer.Core.Treasure;

/// <summary>One ordinary item chest, keyed by its native saved collection bit.</summary>
public sealed class TreasureChest
{
    internal TreasureChest(int flagIndex, byte flagMask, int itemId, string areaName, int itemOperandOffset, byte[] script)
    {
        FlagIndex = flagIndex;
        FlagMask = flagMask;
        OriginalItemId = itemId;
        AreaName = areaName;
        ItemOperandOffset = itemOperandOffset;
        Script = Array.AsReadOnly((byte[])script.Clone());
        LocationId = "s1:tmp-event:" + flagIndex.ToString(CultureInfo.InvariantCulture) + ":" + flagMask.ToString("x2", CultureInfo.InvariantCulture);
    }

    public string LocationId { get; }
    public int FlagIndex { get; }
    public byte FlagMask { get; }
    public int OriginalItemId { get; }
    public string AreaName { get; }
    public int ItemOperandOffset { get; }
    public IReadOnlyList<byte> Script { get; }
    public bool IsShuffleEligible => OriginalItemId != 81 && OriginalItemId != 171 && OriginalItemId != 172 && OriginalItemId != 173;

    public bool MatchesOriginalScript(ReadOnlySpan<byte> script, int operandIndex)
    {
        if (operandIndex != ItemOperandOffset || script.Length != Script.Count) return false;
        for (int index = 0; index < script.Length; index++)
            if (script[index] != Script[index]) return false;
        return true;
    }
}

/// <summary>Original S1 item rewards. Alternate maps share their saved collection bit.</summary>
public static partial class S1TreasureCatalog
{
    private static readonly IReadOnlyDictionary<int, TreasureChest> ByFlag;
    public static IReadOnlyList<TreasureChest> Chests { get; }
    public static string CatalogDigest { get; }

    static S1TreasureCatalog()
    {
        TreasureChest[] chests = CreateChests();
        var byFlag = new Dictionary<int, TreasureChest>();
        if (chests.Length != 136 || chests.Count(chest => chest.IsShuffleEligible) != 132)
            throw new InvalidDataException("S1 treasure catalog differs from the reviewed reward pool.");
        foreach (var chest in chests)
        {
            if (chest.FlagIndex < 0 || chest.FlagIndex >= 240 || chest.FlagMask == 0
                || (chest.FlagMask & (chest.FlagMask - 1)) != 0 || chest.OriginalItemId < 1 || chest.OriginalItemId > 179
                || chest.ItemOperandOffset < 1 || chest.ItemOperandOffset >= chest.Script.Count
                || chest.Script[chest.ItemOperandOffset - 1] != 0x49 || chest.Script[chest.ItemOperandOffset] != chest.OriginalItemId)
                throw new InvalidDataException("S1 treasure catalog contains an invalid chest record.");
            byFlag.Add(Key(chest.FlagIndex, chest.FlagMask), chest);
        }
        Chests = Array.AsReadOnly(chests);
        ByFlag = new ReadOnlyDictionary<int, TreasureChest>(byFlag);
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
        {
            writer.Write("s1-chest-catalog-v1");
            writer.Write(chests.Length);
            foreach (var chest in chests)
            {
                writer.Write(chest.LocationId);
                writer.Write(chest.FlagIndex);
                writer.Write(chest.FlagMask);
                writer.Write(chest.OriginalItemId);
                writer.Write(chest.ItemOperandOffset);
                writer.Write(chest.Script.Count);
                foreach (byte value in chest.Script) writer.Write(value);
            }
        }
        using var sha = SHA256.Create();
        CatalogDigest = Hex(sha.ComputeHash(stream.ToArray()));
    }

    public static bool TryGetChest(int flagIndex, byte flagMask, out TreasureChest chest)
    {
        chest = null!;
        return flagIndex >= 0 && flagIndex < 240 && ByFlag.TryGetValue(Key(flagIndex, flagMask), out chest!);
    }

    private static int Key(int flagIndex, byte mask) => (flagIndex << 8) | mask;
    internal static string Hex(byte[] bytes) => string.Concat(bytes.Select(value => value.ToString("x2", CultureInfo.InvariantCulture)));
}
