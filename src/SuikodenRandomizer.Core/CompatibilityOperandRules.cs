using System;
using System.Collections.Generic;

namespace SuikodenRandomizer.Core;

/// <summary>Reviewed operations whose operands are canonical story-role IDs.</summary>
public enum CompatibilityOperandCommand
{
    CustomResidentCheck,
    ScriptedPartyOrder,
    SpecialItem
}

/// <summary>
/// Native grammar for the three reviewed compatibility adapters. Positions refer
/// to the current operand cursor, not the preceding command opcode. This does
/// not advance the cursor or interpret any other command's integers as IDs.
/// </summary>
public static class CompatibilityOperandRules
{
    public static int[] GetCharacterPositions(CompatibilityOperandCommand command,
        IReadOnlyList<byte> commands, int operandIndex, int villagePartyCount = 0)
    {
        if (commands is null) throw new ArgumentNullException(nameof(commands));
        Require(commands, operandIndex, 1);
        switch (command)
        {
            case CompatibilityOperandCommand.CustomResidentCheck:
                // Turugai partymemberchk consumes exactly one character byte.
                return new[] { operandIndex };
            case CompatibilityOperandCommand.SpecialItem:
                // Modes 0 and 1 consume ITEM IDs, not character IDs. Other
                // modes do not consume a second byte in the reviewed native body.
                if (commands[operandIndex] != 2) return Array.Empty<int>();
                Require(commands, operandIndex, 2);
                return new[] { operandIndex + 1 };
            case CompatibilityOperandCommand.ScriptedPartyOrder:
                if (villagePartyCount < 0 || villagePartyCount > 6)
                    throw new ArgumentOutOfRangeException(nameof(villagePartyCount), "Native village party count must be 0 through 6.");
                // seiretuPartyMember loops fm_party_data.player_kazu, consuming
                // a new ID only while loopIndex < the encoded count. Extra IDs
                // beyond the current party size are NOT consumed by native code.
                int consumedIds = Math.Min(commands[operandIndex], villagePartyCount);
                Require(commands, operandIndex, 1 + consumedIds);
                var positions = new int[consumedIds];
                for (int i = 0; i < positions.Length; i++) positions[i] = operandIndex + 1 + i;
                return positions;
            default:
                throw new ArgumentOutOfRangeException(nameof(command));
        }
    }

    public static OperandRewrite[] BuildProjection(CompatibilityOperandCommand command,
        IReadOnlyList<byte> commands, int operandIndex, Func<int, int> resolveSource,
        int villagePartyCount = 0)
    {
        if (resolveSource is null) throw new ArgumentNullException(nameof(resolveSource));
        int[] positions = GetCharacterPositions(command, commands, operandIndex, villagePartyCount);
        var result = new List<OperandRewrite>();
        // Compute every assignment against the original snapshot before any
        // writes. Cycles in the permutation must be projected simultaneously.
        foreach (int position in positions)
        {
            byte original = commands[position];
            int actual = resolveSource(original);
            if (actual < 0 || actual > byte.MaxValue)
                throw new InvalidOperationException("Resolved character ID is outside the native byte range.");
            if (actual != original) result.Add(new OperandRewrite(position, original, (byte)actual));
        }
        return result.ToArray();
    }

    private static void Require(IReadOnlyList<byte> commands, int start, int width)
    {
        if (start < 0 || (long)start + width > commands.Count)
            throw new ArgumentOutOfRangeException(nameof(start), "Native operand stream is truncated or its cursor is invalid.");
    }
}

public readonly struct OperandRewrite
{
    public OperandRewrite(int position, byte original, byte replacement)
    { Position = position; Original = original; Replacement = replacement; }
    public int Position { get; }
    public byte Original { get; }
    public byte Replacement { get; }
}
