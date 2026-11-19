using System;

namespace SuikodenRandomizer.S1BattlePreview;

internal enum NativeHeroMenuInputKind { PassNative, Consume, ConfirmParty, Cancel, Move }

internal readonly record struct NativeHeroMenuInput(NativeHeroMenuInputKind Kind, int Row, int PartyCommand = 0);

/// <summary>
/// Routes the extended native command rows without sending party commands through
/// the stock four-row hero handler. Command 3 remains Auto for ATB and Wait for CTB;
/// the owning battle mode interprets it after the native menu child returns.
/// </summary>
internal static class NativeHeroMenuInputPolicy
{
    // A hero can act twice in a row. Matching their slot during the first
    // animation does not authorize a Wait or escape chosen for the second turn.
    internal static bool OwnsTurn(int menuSlot, int pendingSlot, bool queued, bool activeAtbInput)
        => !queued && (activeAtbInput ? pendingSlot == 0 : pendingSlot == menuSlot);

    internal static NativeHeroMenuInput Evaluate(int row, ReadOnlySpan<bool> heroEnabled,
        bool confirm, bool cancel, bool up, bool down)
    {
        if (heroEnabled.IsEmpty) throw new ArgumentException("The native hero command rows are required.", nameof(heroEnabled));
        int heroRows = heroEnabled.Length;
        int rowCount = heroRows + 3; // Run/Let Go, Bribe, and Auto/Wait.
        if ((uint)row >= (uint)rowCount) throw new ArgumentOutOfRangeException(nameof(row));
        if (confirm)
            return row < heroRows
                ? new NativeHeroMenuInput(NativeHeroMenuInputKind.PassNative, row)
                : new NativeHeroMenuInput(NativeHeroMenuInputKind.ConfirmParty, row, row - heroRows + 1);
        if (cancel) return new NativeHeroMenuInput(NativeHeroMenuInputKind.Cancel, row);
        if (up != down)
        {
            int direction = up ? -1 : 1;
            do { row = (row + direction + rowCount) % rowCount; }
            while (row < heroRows && !heroEnabled[row]);
            return new NativeHeroMenuInput(NativeHeroMenuInputKind.Move, row);
        }
        return new NativeHeroMenuInput(row < heroRows ? NativeHeroMenuInputKind.PassNative : NativeHeroMenuInputKind.Consume, row);
    }
}
