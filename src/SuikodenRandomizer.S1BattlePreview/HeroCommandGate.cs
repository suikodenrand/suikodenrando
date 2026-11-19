using System;

namespace SuikodenRandomizer.S1BattlePreview;

internal enum HeroCommandKind { None, Attack, Defend, Rune, Item }

internal readonly record struct HeroCommandResource(int Index, int Id, int RuneId = -1, IntPtr ItemStack = default);

internal readonly record struct HeroCommandTarget(int Slot, IntPtr Identity, string Name)
{
    internal static HeroCommandTarget SelectAttackTarget(ReadOnlySpan<HeroCommandTarget> inRange,
        int preferredSlot, IntPtr preferredIdentity)
    {
        foreach (var target in inRange)
            if (target.Slot == preferredSlot && target.Identity == preferredIdentity) return target;
        return inRange.IsEmpty ? default : inRange[0];
    }
}

internal readonly record struct HeroCommand(HeroCommandKind Kind, int TargetSlot, IntPtr TargetIdentity,
    HeroCommandResource? Resource = null);

internal sealed record HeroCommandPrompt(int Slot, IntPtr Identity, string Name, HeroCommandTarget[] Targets,
    HeroResourceOption[] Runes, HeroResourceOption[] Items);

internal sealed record HeroResourceOption(HeroCommandResource Resource, string Name, int? Uses, HeroCommandTarget[] Targets);

internal struct HeroCommandGate
{
    internal int Slot { get; private set; }
    internal IntPtr Identity { get; private set; }
    internal HeroCommand Choice { get; private set; }

    internal bool Open(int slot, IntPtr identity)
    {
        if (Slot != 0 || slot <= 0 || identity == IntPtr.Zero) return false;
        Slot = slot;
        Identity = identity;
        return true;
    }

    internal bool Choose(int slot, IntPtr identity, HeroCommand command)
    {
        if (Slot == 0 || Slot != slot || Identity != identity || Choice.Kind != HeroCommandKind.None) return false;
        bool targeted = command.TargetSlot > 0 && command.TargetIdentity != IntPtr.Zero;
        bool untargeted = command.TargetSlot == 0 && command.TargetIdentity == IntPtr.Zero;
        bool valid = command.Kind switch
        {
            HeroCommandKind.Attack => targeted && command.Resource is null,
            HeroCommandKind.Defend => untargeted && command.Resource is null,
            HeroCommandKind.Rune => (targeted || untargeted)
                && command.Resource is { Index: >= 0 and <= sbyte.MaxValue, Id: >= 0, RuneId: >= 0 },
            HeroCommandKind.Item => (targeted || untargeted)
                && command.Resource is { Index: >= 0 and <= sbyte.MaxValue, Id: > 0, RuneId: -1 },
            _ => false
        };
        if (!valid) return false;
        Choice = command;
        return true;
    }

    internal void Reset() => this = default;
}
