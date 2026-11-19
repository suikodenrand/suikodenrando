extern alias GSD1;

using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using SuikodenRandomizer.Shared;

namespace SuikodenRandomizer.S1BattlePreview;

internal static partial class ActiveBattleState
{
    private static void RecordCtbPartyReference(GSD1.BATTLE_WORK work)
    {
        try
        {
            var party = new Dictionary<int, int>();
            for (int slot = 1; slot <= work.player_kazu; slot++)
            {
                int speed = ReadSpeed(work.pm_data[slot]);
                if (speed <= 0) continue;
                int id = ReadCtbStarId(work, slot);
                if (CtbTuningCatalog.HasCombatStar(id)) party.TryAdd(id, speed);
            }
            // The editor uses this reference to suggest a starting value. Capture native SPD even during an override.
            CtbTuningLastParty.Write(Path.Combine(Paths.CachePath, CtbTuningLastParty.FileName), party);
        }
        catch (Exception error)
        {
            BattlePreviewPlugin.PreviewLog?.LogWarning("CTB editor could not record the opening party SPD: " + error.Message);
        }
    }
}
