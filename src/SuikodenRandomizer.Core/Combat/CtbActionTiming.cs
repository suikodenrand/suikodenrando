using System;

namespace SuikodenRandomizer.Core.Combat;

public enum CtbActionKind { Wait, Defend, Attack, Item, Rune, EnemyAttack }

/// <summary>A command's logical recovery cost, independent of playback duration.</summary>
public readonly struct CtbActionCost
{
    internal CtbActionCost(CtbActionKind kind, int rank, double multiplier, string label)
    {
        Kind = kind;
        Rank = rank;
        Multiplier = multiplier;
        Label = label;
    }

    public CtbActionKind Kind { get; }
    /// <summary>Rune rank 1..4, or zero when not applicable or not safely known.</summary>
    public int Rank { get; }
    public double Multiplier { get; }
    public string Label { get; }
}

/// <summary>
/// Initial CTB command balance. A native adapter supplies the command and a proven
/// rune rank; unknown ranks use an explicit fallback instead of guessing from IDs.
/// </summary>
public static class CtbActionTiming
{
    public static CtbActionCost Describe(CtbActionKind kind, int runeRank = 0)
    {
        switch (kind)
        {
            case CtbActionKind.Wait: return new CtbActionCost(kind, 0, 0.5, "Wait");
            case CtbActionKind.Defend: return new CtbActionCost(kind, 0, 0.75, "Defend");
            case CtbActionKind.Attack: return new CtbActionCost(kind, 0, 1, "Attack");
            case CtbActionKind.Item: return new CtbActionCost(kind, 0, 0.85, "Item");
            case CtbActionKind.EnemyAttack: return new CtbActionCost(kind, 0, 1, "Enemy attack");
            case CtbActionKind.Rune:
                switch (runeRank)
                {
                    case 1: return new CtbActionCost(kind, 1, 1.15, "Rune I");
                    case 2: return new CtbActionCost(kind, 2, 1.35, "Rune II");
                    case 3: return new CtbActionCost(kind, 3, 1.55, "Rune III");
                    case 4: return new CtbActionCost(kind, 4, 1.75, "Rune IV");
                    default: return new CtbActionCost(kind, 0, 1.35, "Rune");
                }
            default: throw new ArgumentOutOfRangeException(nameof(kind));
        }
    }

    public static double RecoveryMultiplier(CtbActionKind kind, int runeRank = 0)
        => Describe(kind, runeRank).Multiplier;
}
