extern alias GSD1;

using SuikodenRandomizer.Core;
using System;
using System.Collections.Generic;

namespace SuikodenRandomizer.AvatarPlugin;

/// <summary>
/// Presents native eligibility code with unavailable requirements for Unite
/// groups whose choreography does not match their assigned combat avatars.
/// The native code still computes its own result and availability list. No
/// command numbers, return sentinels, table sizes or persistent IDs are changed.
/// </summary>
internal sealed class AvatarUniteTableScope : IDisposable
{
    [ThreadStatic] private static int _depth;
    private readonly List<Action>? _restore;
    private bool _disposed;
    internal int MaskedGroupCount { get; }

    private AvatarUniteTableScope(List<Action>? restore, int maskedGroupCount)
    {
        _restore = restore;
        MaskedGroupCount = maskedGroupCount;
    }

    internal static AvatarUniteTableScope? Begin()
    {
        if (_depth != 0)
        {
            _depth++;
            return new AvatarUniteTableScope(null, 0);
        }
        var runtime = AvatarRuntime.Current;
        if (runtime?.IsActive != true) return null;

        var players = GSD1.BattleBase.battle_work?.p_battle_data
            ?? throw new InvalidOperationException("Unite eligibility has no native battle-player data.");
        var present = new HashSet<int>();
        for (int n = 0; n < players.Count; n++)
        {
            var player = players[n]?.player_data;
            if (player is null) continue;
            int id = player.chara_id;
            if (id < 0 || id > 109)
                throw new InvalidOperationException("Unite eligibility has an invalid battle-player identity.");
            present.Add(id);
        }
        if (present.Count == 0)
            throw new InvalidOperationException("Unite eligibility has no populated native battle players.");
        int absentId = -1;
        foreach (int candidate in CombatCatalog.NormalCombatIds)
            if (!present.Contains(candidate)) { absentId = candidate; break; }
        if (absentId < 0)
            throw new InvalidOperationException("Unite eligibility has no valid absent combat identity.");
        byte unavailableParticipant = checked((byte)absentId);

        var table = GSD1.BattleBase.kyoryoku_data_table
            ?? throw new InvalidOperationException("Unite eligibility has no native combination table.");
        var writes = new List<Action>();
        var restores = new List<Action>();
        var planned = new HashSet<(IntPtr Pointer, int Index)>();
        int maskedGroups = 0;
        // Read and validate every record before changing any shared native data.
        for (int n = 0; n < table.Count; n++)
        {
            var group = table[n];
            if (group is null) continue; // Preserve native unused/sentinel slots.
            int count = group.player_kazu;
            if (count == 0) continue; // Keep native disabled/sentinel records intact.
            var ids = group.player_id;
            if (count > 6 || ids is null || count > ids.Count)
                throw new InvalidOperationException("Unite eligibility contains an invalid participant list.");
            int changedIndex = -1;
            for (int p = 0; p < count; p++)
            {
                int id = ids[p];
                if (id > 109)
                    throw new InvalidOperationException("Unite eligibility contains an invalid participant identity.");
                if (changedIndex < 0 && runtime.Profile.GetDonor(id) != id) changedIndex = p;
            }
            if (changedIndex < 0) continue;
            maskedGroups++;
            int index = changedIndex;
            if (!planned.Add((ids.Pointer, index))) continue;
            byte original = ids[index];
            writes.Add(() => ids[index] = unavailableParticipant);
            restores.Add(() => ids[index] = original);
        }

        int attempted = 0;
        try
        {
            // Include a possibly partially completed setter in rollback.
            foreach (var write in writes) { attempted++; write(); }
        }
        catch (Exception writeError)
        {
            try { Restore(restores, attempted); }
            catch (Exception rollbackError)
            {
                throw new AggregateException("Unite requirement masking and rollback failed.", writeError, rollbackError);
            }
            throw;
        }
        _depth = 1;
        return new AvatarUniteTableScope(restores, maskedGroups);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { if (_restore is not null) Restore(_restore, _restore.Count); }
        finally { _depth--; }
    }

    private static void Restore(List<Action> restores, int count)
    {
        List<Exception>? errors = null;
        for (int n = count - 1; n >= 0; n--)
            try { restores[n](); }
            catch (Exception error) { (errors ??= new List<Exception>()).Add(error); }
        if (errors is not null)
            throw new AggregateException("Could not restore native Unite participant requirements.", errors);
    }
}
