extern alias GSD1;

using System;
using System.Linq;
using SuikodenRandomizer.AvatarPlugin;
using SuikodenRandomizer.Core;

internal static partial class Program
{
    private static void RunTirShuffleChecks()
    {
        Case("Tir opt-in projects two independent avatars without copying their canonical loadouts", () =>
        {
            const int tir = CombatCatalog.TirId;
            var world = new World(false);
            world.StageOpening();
            AvatarPlugin.ConfiguredIncludeTir = true;
            world.Runtime.BeginNewGame();
            var profile = world.Runtime.Profile;
            int heroDonor = profile.GetDonor(tir);
            int tirRole = profile.Assignments.Single(pair => pair.Value == tir).Key;
            Check(heroDonor != tir && tirRole != tir, "Seed requires a non-self Tir mapping.");
            world.Game.playerName = "Chosen Hero";
            world.Base(tir).monsyo_data!.monsyo_id = ViktorCheckpointData.SoulEaterRune;
            world.Base(tir).Items = new[] { 245, 246, 247 };
            string heroBase = AvatarNativeData.Fingerprint(world.Base(tir));
            string otherBase = AvatarNativeData.Fingerprint(world.Base(tirRole));
            string layout = world.Runtime.CanonicalLayoutFingerprint();

            world.Ready();
            var heroProxy = world.Runtime.ProxyFor(tir);
            var otherProxy = world.Runtime.ProxyFor(tirRole);
            Check(heroProxy?.chara_id == tir && heroProxy.chara_name == world.Originals[tir].chara_name
                && heroProxy.player_base == world.Base(tir) && heroProxy.field_data == world.Originals[heroDonor].field_data
                && heroProxy.battle_data == world.Originals[heroDonor].battle_data, "Hero proxy lost native identity or donor avatar.");
            Check(otherProxy?.chara_id == tirRole && otherProxy.player_base == world.Base(tirRole)
                && otherProxy.field_data == world.Originals[tir].field_data, "Tir donor role has wrong identity or visuals.");
            Check(heroBase == AvatarNativeData.Fingerprint(world.Base(tir))
                && otherBase == AvatarNativeData.Fingerprint(world.Base(tirRole)), "Tir-related loadout or Soul Eater was copied.");
            Check(layout == world.Runtime.CanonicalLayoutFingerprint() && world.Game.playerName == "Chosen Hero",
                "Canonical membership or chosen hero name changed.");

            var save = new GSD1.SaveDataManager.SaveData { Identity = new string('d', 64) };
            world.Runtime.WriteCampaignBinding(save);
            AvatarPlugin.ConfiguredIncludeTir = false;
            world.Runtime.PrepareLoad(save);
            world.Ready();
            Check(world.Runtime.Profile.IncludesTir && world.Runtime.Profile.Fingerprint == profile.Fingerprint,
                "Loading used the current config instead of the bound Tir profile.");
            Check(heroBase == AvatarNativeData.Fingerprint(world.Base(tir))
                && otherBase == AvatarNativeData.Fingerprint(world.Base(tirRole)), "Reload changed protected progression.");
        });

        Case("An existing 75-role save remains Tir-free with opt-in selected", () =>
        {
            var world = new World();
            world.Ready();
            var save = new GSD1.SaveDataManager.SaveData { Identity = new string('e', 64) };
            world.Runtime.WriteCampaignBinding(save);
            AvatarPlugin.ConfiguredIncludeTir = true;
            world.Runtime.PrepareLoad(save);
            world.Ready();
            Check(!world.Runtime.Profile.IncludesTir && world.Runtime.ProxyFor(CombatCatalog.TirId) is null,
                "A legacy campaign adopted the current Tir preference.");
        });
    }
}
