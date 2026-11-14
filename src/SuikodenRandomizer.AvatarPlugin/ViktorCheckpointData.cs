using System;

namespace SuikodenRandomizer.AvatarPlugin;

/// <summary>Canonical Gregminster state after the inn escape and before Viktor's first join.</summary>
internal static class ViktorCheckpointData
{
    internal const byte Village = 0;
    internal const byte Map = 0;
    internal const byte Area = 0;
    internal const byte X = 56;
    internal const byte Y = 55;
    internal const byte ActiveCount = 3;
    internal const byte SoulEaterRune = 1;
    internal const byte TirLevel = 7;
    internal const ushort TirExperience = 356;
    internal const ushort TirHitPoints = 82;
    private static readonly byte[] CapturedTirStats = { 33, 42, 35, 38, 30, 35 };
    internal static ReadOnlySpan<byte> TirStats => CapturedTirStats;

    internal const int TemporaryFlagCount = 240;
    internal const int StoryFlagCount = 20;
    internal const int MemberFlagCount = 128;

    private static readonly short[] CapturedCharacterCodes = { 8, 2, -1, 1, -1, -1 };
    private static readonly sbyte[] CapturedPartyOrder = { 0, 1, 3, 3, 4, -1 };

    // The first ActiveCount entries in the order select Tir, Gremio and Cleo.
    // Slot 2 is empty here and is filled by the native Viktor event.
    internal static ReadOnlySpan<short> CharacterCodes => CapturedCharacterCodes;
    internal static ReadOnlySpan<sbyte> PartyOrder => CapturedPartyOrder;

    // Verified opening and chase flags. The optional Rockland fight and opened
    // chest flags are deliberately omitted. In byte 6, bits 0 and 1 record the
    // inn sequence, while bits 2 and 3 leave Viktor's join and the gate escape pending.
    private static readonly (int Index, byte Value)[] CapturedTemporaryFlags =
    {
        (0, 0xF8), (1, 0xF3), (2, 0xF7), (3, 0xFF), (4, 0x4B),
        (5, 0xED), (6, 0x03), (20, 0x34), (24, 0xC0), (35, 0x10), (37, 0x40)
    };

    // Pahn and Ted are absent from the active party. Their recruitment bits
    // still matter to the native story, independently of active slots.
    private static readonly (int Index, byte Value)[] CapturedMemberFlags =
    {
        (1, 0x09), (2, 0x09), (6, 0x08), (8, 0x09), (108, 0x09)
    };

    internal static ReadOnlySpan<(int Index, byte Value)> TemporaryFlags => CapturedTemporaryFlags;
    internal static ReadOnlySpan<(int Index, byte Value)> MemberFlags => CapturedMemberFlags;
}
