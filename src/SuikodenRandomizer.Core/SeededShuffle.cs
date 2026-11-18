using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace SuikodenRandomizer.Core;

/// <summary>Deterministic Fisher–Yates shuffle. Each feature supplies its versioned seed domain.</summary>
internal sealed class SeededShuffle
{
    private ulong _state;

    internal SeededShuffle(string algorithmId, string seed)
    {
        using var sha = SHA256.Create();
        byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(algorithmId + "\n" + seed));
        _state = BinaryPrimitives.ReadUInt64LittleEndian(hash);
    }

    internal void Shuffle(int[] values)
    {
        for (int index = values.Length - 1; index > 0; index--)
        {
            int other = Next(index + 1);
            (values[index], values[other]) = (values[other], values[index]);
        }
    }

    private int Next(int exclusiveMaximum)
    {
        ulong bound = (ulong)exclusiveMaximum;
        ulong threshold = unchecked(0UL - bound) % bound;
        ulong value;
        do { value = NextUInt64(); } while (value < threshold);
        return (int)(value % bound);
    }

    private ulong NextUInt64()
    {
        unchecked
        {
            ulong value = (_state += 0x9e3779b97f4a7c15UL);
            value = (value ^ (value >> 30)) * 0xbf58476d1ce4e5b9UL;
            value = (value ^ (value >> 27)) * 0x94d049bb133111ebUL;
            return value ^ (value >> 31);
        }
    }
}
