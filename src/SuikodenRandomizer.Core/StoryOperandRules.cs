using System;

namespace SuikodenRandomizer.Core;

public enum StoryCharacterCommand { RemovePartyMember, MemberLock, LevelChange }

public sealed class StoryCharacterOperand
{
    internal StoryCharacterOperand(int? position, int width, bool canonicalLoss)
    { Position = position; Width = width; CanonicalLoss = canonicalLoss; }
    public int? Position { get; }
    public int Width { get; }
    public bool CanonicalLoss { get; }
}

/// <summary>Reviewed native operand grammar; the opcode was already consumed.</summary>
public static class StoryOperandRules
{
    public static StoryCharacterCommand? FromOpcode(int opcode) => opcode switch
    {
        0x20 => StoryCharacterCommand.RemovePartyMember,
        0x4A => StoryCharacterCommand.MemberLock,
        0x4B => StoryCharacterCommand.LevelChange,
        _ => (StoryCharacterCommand?)null
    };

    public static StoryCharacterOperand Plan(StoryCharacterCommand command, byte first,
        int operandIndex, int streamCount)
    {
        int width;
        int? position;
        bool loss = false;
        switch (command)
        {
            case StoryCharacterCommand.RemovePartyMember:
                if (first == 0xFF || first == 0xFD)
                { width = 1; position = null; }
                else if (first == 0xFC || first == 0xFE)
                { width = 2; position = checked(operandIndex + 1); loss = first == 0xFE; }
                else { width = 1; position = operandIndex; }
                break;
            case StoryCharacterCommand.MemberLock:
                width = 2; position = checked(operandIndex + 1);
                break;
            case StoryCharacterCommand.LevelChange:
                // Native consumes the second byte in every mode. Only 3 and 4
                // operate on all actual active members without a target base.
                // Modes 0/1/2 additionally consume an amount. Other mode values
                // still look up the second byte's PLAYER_BASE for stat refresh.
                width = first <= 2 ? 3 : 2;
                position = first == 3 || first == 4 ? null : checked(operandIndex + 1);
                break;
            default: throw new ArgumentOutOfRangeException(nameof(command));
        }
        if (operandIndex < 0 || streamCount < 0 || (long)operandIndex + width > streamCount)
            throw new ArgumentOutOfRangeException(nameof(operandIndex), "Native story operand stream is truncated.");
        return new StoryCharacterOperand(position, width, loss);
    }
}
