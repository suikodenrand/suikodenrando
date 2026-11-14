extern alias GSD1;

using SuikodenRandomizer.AvatarPlugin;
using System;
using System.Linq;

internal static partial class Program
{
    private static void RunViktorStartChecks()
    {
        Case("Opening and noninitial reconstruction steps leave native state alone", () =>
        {
            var world = FreshViktorWorld();
            ViktorStart.Arm(false);
            ViktorStart.BeforeGameInit("LoadMapData", 0);
            Check(world.Game.party_data.player_kazu == 1, "Opening unexpectedly changed party");
            ViktorStart.Arm(true);
            ViktorStart.BeforeGameInit("LoadMapData", 1);
            Check(world.Game.party_data.player_kazu == 1, "Noninitial step applied the checkpoint");
            ViktorStart.BeforeGameInit("LoadPlayerData", 0);
            Check(world.Game.party_data.player_kazu == 3, "Initial reconstruction did not apply");
        });

        Case("Viktor checkpoint sets story prerequisites once and retains the player's new-game choices", () =>
        {
            var world = FreshViktorWorld();
            var game = world.Game;
            var party = game.party_data;
            var tir = world.Base(8);
            game.playerName = "Test Hero";
            game.difficulty = 2;
            party.mochi_kin = 4567;
            game.tmpEventFlagS[80] = 8;
            game.storyFlagS[12] = 4;
            var profile = world.Runtime.Profile;
            ViktorStart.Arm(true);
            ViktorStart.BeforeGameInit("LoadMapData", 0);
            Check((party.vil_no, party.map_no, party.area_no, party.x, party.y) == (0, 0, 0, 56, 55),
                "Checkpoint map/position is wrong");
            Check(party.player_kazu == 3 && party.narabi.Take(party.player_kazu)
                .Select(slot => party.chara_code[slot]).SequenceEqual(new short[] { 8, 2, 1 }),
                "Checkpoint active order is wrong");
            Check((game.tmpEventFlagS[5] & 0x80) != 0 && game.tmpEventFlagS[6] == 3
                && (game.tmpEventFlagS[24] & 0x40) != 0 && game.member_flag[35] == 0,
                "Recruitment or escape is in the wrong phase");
            Check(game.member_flag[6] == 8 && game.member_flag[108] == 9,
                "Departed Pahn and Ted membership was lost");
            Check(tir.monsyo_data?.monsyo_id == ViktorCheckpointData.SoulEaterRune
                && tir.level == ViktorCheckpointData.TirLevel && tir.exp == ViktorCheckpointData.TirExperience
                && tir.hp == tir.max_hp && tir.noryoku.SequenceEqual(ViktorCheckpointData.TirStats.ToArray()),
                "Tir's progression or Soul Eater was not initialized");
            Check(game.playerName == "Test Hero" && game.difficulty == 2 && party.mochi_kin == 4567
                && game.tmpEventFlagS[80] == 8 && game.storyFlagS[12] == 4
                && game.tmpEventFlagS[170] == 0 && ReferenceEquals(profile, world.Runtime.Profile),
                "Checkpoint replaced unrelated New Game state");

            party.x = 57;
            ViktorStart.BeforeGameInit("LoadPlayerData", 0);
            Check(party.x == 57 && world.Runtime.FaultReason is null, "Second boundary reapplied the checkpoint");
        });

        Case("Cancel, unbound runtime, and a rejected first boundary leave the opening intact", () =>
        {
            var world = FreshViktorWorld();
            ViktorStart.Arm(true);
            ViktorStart.Cancel();
            ViktorStart.BeforeGameInit("LoadMapData", 0);
            Check(world.Game.party_data.player_kazu == 1, "Cancelled checkpoint applied");

            world = FreshViktorWorld(admit: false);
            ViktorStart.Arm(true);
            ViktorStart.BeforeGameInit("LoadMapData", 0);
            Check(world.Game.party_data.player_kazu == 1, "Unbound runtime accepted a checkpoint");

            world = FreshViktorWorld();
            world.Game.member_flag[35] = 9;
            ViktorStart.Arm(true);
            ViktorStart.BeforeGameInit("LoadMapData", 0);
            world.Game.member_flag[35] = 0;
            ViktorStart.BeforeGameInit("LoadPlayerData", 0);
            Check(world.Game.party_data.player_kazu == 1 && world.Game.tmpEventFlagS[6] == 0
                && world.Runtime.FaultReason is null, "Rejected checkpoint wrote or retried");
        });

        Case("A checkpoint write failure restores the opening snapshot", () =>
        {
            var world = FreshViktorWorld();
            var game = world.Game;
            var party = game.party_data;
            var before = world.Runtime.CanonicalLayoutFingerprint();
            var temporary = game.tmpEventFlagS.ToArray();
            party.FailXSetterOnce = true;
            ViktorStart.Arm(true);
            ViktorStart.BeforeGameInit("LoadMapData", 0);
            Check((party.vil_no, party.map_no, party.area_no, party.x, party.y) == (0, 2, 1, 11, 12)
                && world.Runtime.CanonicalLayoutFingerprint() == before && game.tmpEventFlagS.SequenceEqual(temporary)
                && world.Base(8).monsyo_data?.monsyo_id == 0 && world.Runtime.FaultReason is null,
                "Failed checkpoint left partial changes");
        });

        Case("The first native spawn verifies the checkpoint and detects overwritten state", () =>
        {
            var world = FreshViktorWorld();
            ViktorStart.Arm(true);
            ViktorStart.BeforeGameInit("LoadMapData", 0);
            ViktorStart.ObserveSpawn(56, 55);
            Check(world.Runtime.FaultReason is null && AvatarPlugin.PluginLog.Messages.Any(
                message => message.Contains("checkpoint verified at first native spawn", StringComparison.Ordinal)),
                "Expected the native spawn to verify the checkpoint");
            ViktorStart.ObserveSpawn(11, 12);
            Check(world.Runtime.FaultReason is null, "A later spawn was treated as the checkpoint");

            world = FreshViktorWorld();
            ViktorStart.Arm(true);
            ViktorStart.BeforeGameInit("LoadMapData", 0);
            world.Game.tmpEventFlagS[6] = 0;
            ViktorStart.ObserveSpawn(56, 55);
            Check(world.Runtime.FaultReason?.Contains("Viktor checkpoint spawn check failed", StringComparison.Ordinal) == true,
                "The native spawn accepted overwritten story state");
        });
        ViktorStart.Cancel();
    }

    private static World FreshViktorWorld(bool admit = true)
    {
        ViktorStart.Cancel();
        var world = new World(admit);
        world.StageOpening();
        var party = world.Game.party_data;
        (party.vil_no, party.map_no, party.area_no, party.x, party.y) = (0, 2, 1, 11, 12);
        return world;
    }
}
