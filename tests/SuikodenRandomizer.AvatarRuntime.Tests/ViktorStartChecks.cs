extern alias GSD1;

using SuikodenRandomizer.AvatarPlugin;
using System;
using System.Linq;

internal static partial class Program
{
    private static void RunViktorStartChecks()
    {
        Case("Disabling companion leveling preserves the Viktor checkpoint with its original party levels", () =>
        {
            var world = FreshViktorWorld();
            try
            {
                S1QualityOfLife.MatchViktorStarterLevels = false;
                ViktorStart.Arm(true);
                ViktorStart.BeforeGameInit("LoadMapData", 0);
                var levels = new[] { world.Base(1).level, world.Base(2).level };
                ViktorStart.ObserveSpawn(56, 55);
                Check(world.Game.party_data.player_kazu == 3 && world.Base(8).level == 7
                    && !ViktorStart.WaitingForCompanions && world.Base(1).level == levels[0] && world.Base(2).level == levels[1],
                    "Disabled companion leveling changed the checkpoint or companions");
            }
            finally { S1QualityOfLife.MatchViktorStarterLevels = true; }
        });

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

        Case("Viktor skip waits for both shuffled companions and grows their assigned fighters once", () =>
        {
            var world = FreshViktorWorld();
            var roles = new[] { 2, 1 };
            var starts = roles.Select(role => AvatarNativeData.CloneBase(world.Base(world.Runtime.Profile.GetDonor(role)))).ToArray();
            // One recruit has already gained two levels. Only the remaining levels are due.
            world.Base(world.Runtime.Profile.GetDonor(2)).level = starts[0].level = 3;
            ViktorStart.Arm(true);
            ViktorStart.BeforeGameInit("LoadMapData", 0);
            world.Game.player_data[3] = null;
            ViktorStart.ObserveSpawn(56, 55);
            Check(world.Runtime.FaultReason is null && world.Base(2).level < 7 && world.Base(1).level < 7,
                "The checkpoint grew a partial party or faulted before native reconstruction finished");

            world.Game.player_data[3] = world.Originals[1];
            world.Ready();
            for (int i = 0; i < roles.Length; i++)
            {
                var current = world.Base(roles[i]);
                Check(current.Equipment == starts[i].Equipment && current.Rune == starts[i].Rune
                    && current.Items.SequenceEqual(starts[i].Items) && current.exp == starts[i].exp,
                    "Starting growth changed the assigned fighter's loadout or experience");
                CheckViktorGrowth(current, starts[i], 7);
            }

            string[] after = roles.Select(role => AvatarNativeData.Fingerprint(world.Base(role))).ToArray();
            world.Base(8).level = 9;
            ViktorStart.ObserveSpawn(56, 55);
            world.Game.party_data.chara_code[2] = 35;
            world.Game.party_data.narabi[3] = 2;
            world.Game.party_data.player_kazu = 4;
            world.Game.player_data[2] = world.Originals[35];
            byte viktorLevel = world.Base(35).level;
            world.Ready();
            Check(roles.Select(role => AvatarNativeData.Fingerprint(world.Base(role))).SequenceEqual(after)
                && world.Base(35).level == viktorLevel, "A repeat callback or later recruit received checkpoint growth");
        });

        Case("Viktor skip levels the two assigned fixed-roster companions after a Tir-only projection", () =>
        {
            var world = FreshViktorWorld(admit: false);
            AvatarPlugin.ConfiguredMode = "FixedChosen";
            AvatarPlugin.ConfiguredCompanions = "11,12,13,14,15";
            world.Runtime.BeginNewGame();
            world.Ready();
            var starts = new[] { 11, 12, 13, 14, 15 }.ToDictionary(id => id, id => AvatarNativeData.CloneBase(world.Base(id)));
            ViktorStart.Arm(true);
            ViktorStart.BeforeGameInit("LoadMapData", 0);
            ViktorStart.ObserveSpawn(56, 55);
            world.Ready();
            foreach (int role in new[] { 2, 1 })
            {
                int fighter = world.Runtime.MapDisplayId(role);
                Check(starts.ContainsKey(fighter), "A fixed starter did not use a selected fighter");
                CheckViktorGrowth(world.Base(role), starts[fighter], world.Base(8).level);
            }
            Check(starts.All(entry => AvatarNativeData.Fingerprint(entry.Value) == AvatarNativeData.Fingerprint(world.Base(entry.Key))),
                "Leveling fixed companions also changed the fighters' separate story-role records");
        });

        Case("The regular opening leaves Gremio and Cleo at their assigned starting levels", () =>
        {
            var world = FreshViktorWorld();
            ViktorStart.Arm(false);
            ViktorStart.BeforeGameInit("LoadMapData", 0);
            world.Game.party_data.narabi[1] = 1;
            world.Game.party_data.narabi[2] = 3;
            world.Game.party_data.player_kazu = 3;
            world.Base(8).level = 7;
            ViktorStart.ObserveSpawn(56, 55);
            world.Ready();
            Check(world.Base(2).level == 1 && world.Base(1).level == 1, "The normal opening received checkpoint levels");
        });

        Case("An early save waits for the first spawn and captures the live Tir level", () =>
        {
            var world = FreshViktorWorld();
            ViktorStart.Arm(true);
            ViktorStart.BeforeGameInit("LoadMapData", 0);
            world.Ready();
            object?[] args = { true, null, null };
            Check(!(bool)InvokePatch(typeof(AvatarSavePreparationPatch), "Prefix", args)!
                && !(bool)args[0]! && args[2] is null && world.Runtime.FaultReason is null,
                "A save was admitted before the starting fighters could be leveled");

            world.Base(8).level = 8;
            world.Base(1).level = 9;
            string higherFighter = AvatarNativeData.Fingerprint(world.Base(1));
            ViktorStart.ObserveSpawn(56, 55);
            var call = EnterSave();
            try
            {
                Check(world.Base(2).level == 8 && AvatarNativeData.Fingerprint(world.Base(1)) == higherFighter,
                    "Ready save used a fixed level or reset a companion already above Tir");
                InvokePatch(typeof(AvatarSaveCapturePatch), "Postfix", new GSD1.SaveDataManager.SaveData(), true);
                Check(!call.BindingFailed && StorageAllowed(), "The adjusted party could not finish a bound save");
            }
            finally { ExitSave(call); }
        });

        Case("A failed checkpoint growth step leaves both projected fighters intact", () =>
        {
            var world = FreshViktorWorld();
            ViktorStart.Arm(true);
            ViktorStart.BeforeGameInit("LoadMapData", 0);
            world.Ready();
            string[] before = new[] { 2, 1 }.Select(role => AvatarNativeData.Fingerprint(world.Base(role))).ToArray();
            GSD1.BattleBase.FailLevelUpRoleOnce = 1;
            try
            {
                ViktorStart.ObserveSpawn(56, 55);
                Check(GSD1.BattleBase.FailLevelUpRoleOnce is null && world.Runtime.FaultReason is not null,
                    "The native growth failure was not observed");
                Check(new[] { 2, 1 }.Select(role => AvatarNativeData.Fingerprint(world.Base(role))).SequenceEqual(before),
                    "The failed catch-up left part of the party leveled");
            }
            finally { GSD1.BattleBase.FailLevelUpRoleOnce = null; }
        });
        ViktorStart.Cancel();
    }

    private static void CheckViktorGrowth(GSD1.PLAYER_BASE current, GSD1.PLAYER_BASE start, int target)
    {
        int gains = target - start.level;
        int hpGain = (2 + start.seicho_type[0]) * gains;
        Check(current.level == target && current.max_hp == start.max_hp + hpGain && current.hp == start.hp + hpGain,
            "The fighter's level or native HP gain does not match Tir's level");
        Check(current.noryoku.SequenceEqual(start.noryoku.Select((value, i) => (byte)(value + gains * start.seicho_type[i]))),
            "The fighter did not use its own growth for each missing level");
        Check(current.magic_point[1] > 0 && current.magic_point[0] == 0 && Enumerable.Range(1, 4)
            .All(tier => current.magic_point[tier] == Math.Clamp(current.noryoku[4] / 8 - (tier - 1) * 2, 0, 9)),
            "Rune charges were not refreshed from the fighter's final MAG");
    }

    private static World FreshViktorWorld(bool admit = true)
    {
        ViktorStart.Cancel();
        var world = new World(admit);
        foreach (var entry in world.Bases)
        {
            entry.Value.level = 1;
            entry.Value.noryoku = Enumerable.Range(0, 6).Select(i => (byte)(10 + (entry.Key + i) % 13)).ToList();
            byte[] growth = Enumerable.Range(0, 7).Select(i => (byte)(1 + (entry.Key + i) % 5)).ToArray();
            entry.Value.seicho_type = (byte[])growth.Clone();
            world.Originals[entry.Key].seicho_type = growth;
        }
        world.StageOpening();
        var party = world.Game.party_data;
        (party.vil_no, party.map_no, party.area_no, party.x, party.y) = (0, 2, 1, 11, 12);
        return world;
    }
}
