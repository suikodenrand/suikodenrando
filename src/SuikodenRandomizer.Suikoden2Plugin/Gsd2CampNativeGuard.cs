extern alias GSD2;

using System;

namespace SuikodenRandomizer.Suikoden2Plugin;

/// <summary>Read-only checks for the captured camp start and its first native field initialization.</summary>
internal static class Gsd2CampNativeGuard
{
    private const int CampArea = 1;
    private const int CampTown = 10;
    private const int CampMap = 2;
    private const int CampMemoryType = 1;
    private const int CampPlayerX = 167;
    private const int CampPlayerY = 206;
    private const int CampMapX = CampPlayerX * 8;
    private const int CampMapY = CampPlayerY * 8;

    internal static void ValidateFresh(GSD2.GAME_WORK game)
    {
        if (game is null) throw new InvalidOperationException("Camp start requires native game state.");
        var sys = game.sys_work ?? throw new InvalidOperationException("Camp start requires native system state.");
        if (sys.game_mode != 1)
            throw new InvalidOperationException($"Camp start requires native map mode 1; observed {sys.game_mode}.");
        var steps = sys.game_step;
        if (steps is null || steps.Count != 13)
            throw new InvalidOperationException("Camp start requires the fresh native array of 13 chapter steps.");
        if (steps[1] != 0)
            throw new InvalidOperationException($"Camp start requires map step 0 before field initialization; observed {steps[1]}.");
        // Reject an existing controller conservatively; never clear or repurpose native transient state.
        if (sys.mcon is not null)
            throw new InvalidOperationException("Camp start requires field initialization to remain pending; an existing map controller is present.");
    }

    internal static void ValidateInitialMap(GSD2.MACHICON mcon)
    {
        ValidateTarget(mcon);
        // Native MachiInit copies GAME_DATA.deru_pos directly into these pixel coordinates.
        if (mcon.px != CampMapX || mcon.py != CampMapY)
            throw new InvalidOperationException($"Camp map initialization requires pixel position {CampMapX},{CampMapY}; observed {mcon.px},{mcon.py}.");
    }

    internal static bool TryVerifyPlayer(GSD2.MACHICON mcon)
    {
        if (mcon is null) throw new InvalidOperationException("Camp player verification requires a native map controller.");
        var econ = mcon.econ;
        if (econ is null) return false;
        ValidateTarget(mcon);
        // EventInit divides the initial map pixels by 8 before returning. SaveFile reads these fields.
        if (econ.px != CampPlayerX || econ.py != CampPlayerY)
            throw new InvalidOperationException($"Camp player initialization requires save position {CampPlayerX},{CampPlayerY}; observed {econ.px},{econ.py}.");
        return true;
    }

    private static void ValidateTarget(GSD2.MACHICON mcon)
    {
        if (mcon is null) throw new InvalidOperationException("Camp map verification requires a native map controller.");
        if (mcon.ano != CampArea || mcon.vno != CampTown || mcon.mno != CampMap || mcon.memtyp != CampMemoryType)
            throw new InvalidOperationException($"Camp map initialization requires area/town/map/memory {CampArea}/{CampTown}/{CampMap}/{CampMemoryType}; observed {mcon.ano}/{mcon.vno}/{mcon.mno}/{mcon.memtyp}.");
    }
}
