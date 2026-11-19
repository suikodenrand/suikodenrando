using System;

namespace SuikodenRandomizer.Shared;

internal static class S1BattleItemAddress
{
    internal const int Pockets = 9;
    internal const int PartySlots = 6;
    internal const int AddressCount = (PartySlots + 1) * Pockets;

    // Preserve the game's own-pocket indices. The remaining values fit in PM_DATA.c_item (sbyte).
    internal static int Encode(int actorSlot, int ownerSlot, int pocket)
    {
        if (actorSlot is < 1 or > PartySlots) throw new ArgumentOutOfRangeException(nameof(actorSlot));
        if (ownerSlot is < 1 or > PartySlots) throw new ArgumentOutOfRangeException(nameof(ownerSlot));
        if (pocket is < 0 or >= Pockets) throw new ArgumentOutOfRangeException(nameof(pocket));
        return actorSlot == ownerSlot ? pocket : ownerSlot * Pockets + pocket;
    }

    internal static bool TryDecode(int actorSlot, int token, out int ownerSlot, out int pocket)
    {
        ownerSlot = 0;
        pocket = -1;
        if (actorSlot is < 1 or > PartySlots || token is < 0 or >= AddressCount) return false;
        int owner = token < Pockets ? actorSlot : token / Pockets;
        if (token >= Pockets && owner == actorSlot) return false;
        ownerSlot = owner;
        pocket = token % Pockets;
        return true;
    }
}
