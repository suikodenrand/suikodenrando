extern alias GSD1;

using HarmonyLib;
using SuikodenRandomizer.Core;
using System;

namespace SuikodenRandomizer.AvatarPlugin;

internal static class ViktorStart
{
    private enum Phase { Off, Armed, Applied }
    private static Phase _phase;

    internal static void Arm(bool requested)
    {
        _phase = requested ? Phase.Armed : Phase.Off;
        if (requested) AvatarRuntime.Current?.Trace("VIKTOR START armed for New Game.");
    }

    internal static void Cancel() => _phase = Phase.Off;

    internal static void BeforeGameInit(string boundary, int stepLevel)
    {
        if (_phase != Phase.Armed) return;
        var runtime = AvatarRuntime.Current;
        if (runtime?.IsAdmitted != true) { Cancel(); return; }
        try
        {
            if (GSD1.ChapterManager.GetGameStepSeq(stepLevel) != 0) return;
            // Take the first reconstruction boundary only. Retrying at a later
            // boundary could leave native map and party data out of sync.
            Cancel();
            Apply(GSD1.OldSrcBase.game_work ?? throw new InvalidOperationException("No native campaign state."));
            _phase = Phase.Applied;
            runtime.Trace("VIKTOR START applied before " + boundary + " step 0.");
            TraceState("after checkpoint application");
        }
        catch (AggregateException exception)
        {
            runtime.Fail("Viktor checkpoint rollback failed: " + exception.Message);
        }
        catch (Exception exception)
        {
            Cancel();
            runtime.Trace("VIKTOR START using Opening: " + exception.Message);
        }
    }

    internal static void Apply(GSD1.GAME_WORK game)
    {
        var party = game.party_data ?? throw new InvalidOperationException("No native party data.");
        var codes = party.chara_code;
        var order = party.narabi;
        var temporary = game.tmpEventFlagS;
        var members = game.member_flag;
        if (codes?.Count != ViktorCheckpointData.CharacterCodes.Length
            || order?.Count != ViktorCheckpointData.PartyOrder.Length
            || temporary?.Count != ViktorCheckpointData.TemporaryFlagCount
            || members?.Count != ViktorCheckpointData.MemberFlagCount
            || game.storyFlagS?.Count != ViktorCheckpointData.StoryFlagCount)
            throw new InvalidOperationException("Native campaign layout does not match the checkpoint.");

        if (party.player_kazu != 1 || order[0] < 0 || order[0] >= codes.Count
            || codes[order[0]] != CombatCatalog.TirId || temporary[6] != 0 || (temporary[5] & 0x80) != 0
            || members[35] != 0)
            throw new InvalidOperationException("Expected a fresh Tir-only campaign.");

        var tir = AvatarNativeData.FindBase(CombatCatalog.TirId);
        var rune = tir.monsyo_data ?? throw new InvalidOperationException("Tir's rune data is unavailable.");
        var stats = tir.noryoku;
        if (rune.monsyo_id != 0 || stats?.Count != ViktorCheckpointData.TirStats.Length)
            throw new InvalidOperationException("Tir's starting progression does not match the expected layout.");

        var previousLocation = (party.vil_no, party.map_no, party.area_no, party.x, party.y, party.player_kazu);
        var previousCodes = new short[codes.Count];
        var previousOrder = new sbyte[order.Count];
        var previousStats = new byte[stats.Count];
        for (int i = 0; i < codes.Count; i++) { previousCodes[i] = codes[i]; previousOrder[i] = order[i]; }
        for (int i = 0; i < stats.Count; i++) previousStats[i] = stats[i];
        var previousTemporary = new byte[ViktorCheckpointData.TemporaryFlags.Length];
        var previousMembers = new byte[ViktorCheckpointData.MemberFlags.Length];
        for (int i = 0; i < previousTemporary.Length; i++) previousTemporary[i] = temporary[ViktorCheckpointData.TemporaryFlags[i].Index];
        for (int i = 0; i < previousMembers.Length; i++) previousMembers[i] = members[ViktorCheckpointData.MemberFlags[i].Index];
        var previousRune = (rune.monsyo_id, rune.monsyo_level, rune.monsyo_exp);
        var previousTir = (tir.level, tir.exp, tir.hp, tir.max_hp);

        try
        {
            foreach (var (index, value) in ViktorCheckpointData.TemporaryFlags) temporary[index] |= value;
            foreach (var (index, value) in ViktorCheckpointData.MemberFlags) members[index] |= value;
            for (int i = 0; i < codes.Count; i++)
            {
                codes[i] = ViktorCheckpointData.CharacterCodes[i];
                order[i] = ViktorCheckpointData.PartyOrder[i];
            }
            party.vil_no = ViktorCheckpointData.Village;
            party.map_no = ViktorCheckpointData.Map;
            party.area_no = ViktorCheckpointData.Area;
            party.x = ViktorCheckpointData.X;
            party.y = ViktorCheckpointData.Y;
            party.player_kazu = ViktorCheckpointData.ActiveCount;
            rune.monsyo_id = ViktorCheckpointData.SoulEaterRune;
            rune.monsyo_level = 0;
            rune.monsyo_exp = 0;
            tir.level = ViktorCheckpointData.TirLevel;
            tir.exp = ViktorCheckpointData.TirExperience;
            tir.max_hp = ViktorCheckpointData.TirHitPoints;
            tir.hp = ViktorCheckpointData.TirHitPoints;
            for (int i = 0; i < stats.Count; i++) stats[i] = ViktorCheckpointData.TirStats[i];
        }
        catch (Exception writeFailure)
        {
            try
            {
                (party.vil_no, party.map_no, party.area_no, party.x, party.y, party.player_kazu) = previousLocation;
                (tir.level, tir.exp, tir.max_hp, tir.hp) = (previousTir.level, previousTir.exp, previousTir.max_hp, previousTir.hp);
                (rune.monsyo_id, rune.monsyo_level, rune.monsyo_exp) = previousRune;
                for (int i = 0; i < stats.Count; i++) stats[i] = previousStats[i];
                for (int i = 0; i < codes.Count; i++) { codes[i] = previousCodes[i]; order[i] = previousOrder[i]; }
                for (int i = 0; i < previousTemporary.Length; i++) temporary[ViktorCheckpointData.TemporaryFlags[i].Index] = previousTemporary[i];
                for (int i = 0; i < previousMembers.Length; i++) members[ViktorCheckpointData.MemberFlags[i].Index] = previousMembers[i];
            }
            catch (Exception rollbackFailure)
            {
                throw new AggregateException(writeFailure, rollbackFailure);
            }
            throw;
        }
    }

    internal static void ObserveSpawn(int x, int y)
    {
        if (AvatarRuntime.Current?.IsAdmitted != true) return;
        if (_phase != Phase.Applied) return;
        Cancel();
        TraceState("first native player spawn (" + x + "," + y + ")");
        var runtime = AvatarRuntime.Current;
        try
        {
            var game = GSD1.OldSrcBase.game_work ?? throw new InvalidOperationException("No native campaign state.");
            var party = game.party_data;
            if (party.vil_no != ViktorCheckpointData.Village || party.map_no != ViktorCheckpointData.Map
                || party.area_no != ViktorCheckpointData.Area || x != ViktorCheckpointData.X || y != ViktorCheckpointData.Y
                || party.player_kazu != ViktorCheckpointData.ActiveCount
                || (game.tmpEventFlagS[5] & 0x80) == 0 || (game.tmpEventFlagS[6] & 0x0F) != 0x03
                || (game.tmpEventFlagS[24] & 0x40) == 0
                || game.member_flag[35] != 0
                || AvatarNativeData.FindBase(CombatCatalog.TirId).monsyo_data?.monsyo_id != ViktorCheckpointData.SoulEaterRune)
                throw new InvalidOperationException("Native initialization did not retain the pre-Viktor state.");
            for (int i = 0; i < party.player_kazu; i++)
            {
                int slot = party.narabi[i];
                if (slot < 0 || slot >= party.chara_code.Count
                    || party.chara_code[slot] != ViktorCheckpointData.CharacterCodes[ViktorCheckpointData.PartyOrder[i]])
                    throw new InvalidOperationException("Native initialization did not retain the pre-Viktor party.");
            }
            runtime?.Trace("VIKTOR START checkpoint verified at first native spawn.");
        }
        catch (Exception exception)
        {
            runtime?.Fail("Viktor checkpoint spawn check failed: " + exception.Message);
        }
    }

    internal static void TraceState(string checkpoint)
    {
        if (AvatarRuntime.Current?.IsAdmitted != true) return;
        try
        {
            var game = GSD1.OldSrcBase.game_work;
            var party = game?.party_data;
            if (game is null || party is null) return;
            AvatarRuntime.Current?.Trace("VIKTOR START " + checkpoint + "; map=" + party.vil_no + "/" + party.map_no
                + "/" + party.area_no + "; position=" + party.x + "," + party.y + "; party=" + party.player_kazu
                + "; flags[5,6,24]=" + game.tmpEventFlagS[5] + "," + game.tmpEventFlagS[6] + "," + game.tmpEventFlagS[24]
                + "; Viktor membership=" + game.member_flag[35] + "; name set=" + !string.IsNullOrWhiteSpace(game.playerName));
        }
        catch (Exception exception)
        {
            AvatarRuntime.Current?.Trace("VIKTOR START diagnostic unavailable: " + exception.Message);
        }
    }
}

[HarmonyPatch(typeof(GSD1.GameInit), "LoadMapData")]
internal static class ViktorMapInitPatch
{
    [HarmonyPrefix]
    private static void Prefix(int __0) => ViktorStart.BeforeGameInit("LoadMapData", __0);
}

[HarmonyPatch(typeof(GSD1.Village_c), "InitPlayer")]
internal static class ViktorSpawnPatch
{
    [HarmonyPostfix]
    private static void Postfix(int __0, int __1) => ViktorStart.ObserveSpawn(__0, __1);
}
