extern alias GSD1;

using System;
using SuikodenRandomizer.AvatarPlugin;
using SuikodenRandomizer.Core.Treasure;

internal static partial class Program
{
    private static void RunTreasureLifecycleChecks()
    {
        Case("Global treasure pause retains the campaign mapping across save and reload", () =>
        {
            var world = new World(false);
            AvatarPlugin.ConfiguredTreasureShuffle = true;
            world.Runtime.BeginNewGame();
            world.Ready();
            string fingerprint = world.Runtime.TreasureProfile!.Fingerprint;
            AvatarPlugin.ConfiguredTreasureEnabled = false;
            Check(!world.Runtime.TreasureActive && world.Runtime.TreasureProfile.Fingerprint == fingerprint,
                "Pausing treasure discarded or applied the saved mapping");
            var save = new GSD1.SaveDataManager.SaveData { Identity = new string('5', 64) };
            world.Runtime.WriteCampaignBinding(save, save.Identity);
            world.Runtime.WriteTreasureBinding(save.Identity);
            world.Runtime.PrepareLoad(save);
            world.Ready();
            Check(!world.Runtime.TreasureActive && world.Runtime.TreasureProfile?.Fingerprint == fingerprint,
                "Reload lost the paused mapping or enabled it");
            AvatarPlugin.ConfiguredTreasureEnabled = true;
            Check(world.Runtime.TreasureActive && world.Runtime.TreasureProfile?.Fingerprint == fingerprint,
                "Resuming treasure did not restore the same mapping");
        });

        Case("Treasure save restores its mapping after config and seed change", () =>
        {
            var world = new World(false);
            AvatarPlugin.ConfiguredTreasureShuffle = true;
            world.Runtime.BeginNewGame();
            world.Ready();
            string fingerprint = world.Runtime.TreasureProfile!.Fingerprint;
            var save = new GSD1.SaveDataManager.SaveData { Identity = new string('1', 64) };
            world.Runtime.WriteCampaignBinding(save, save.Identity);
            world.Runtime.WriteTreasureBinding(save.Identity);
            AvatarPlugin.ConfiguredTreasureShuffle = false;
            AvatarPlugin.ConfiguredSeed = "another campaign";
            world.Runtime.PrepareLoad(save);
            world.Ready();
            Check(world.Runtime.TreasureActive && world.Runtime.TreasureProfile!.Fingerprint == fingerprint,
                "Saved treasure mapping changed with the configuration");
        });

        Case("Legacy bound save adopts treasure using the saved character campaign seed", () =>
        {
            var world = new World();
            world.Ready();
            string savedSeed = world.Runtime.Profile.Seed;
            var save = new GSD1.SaveDataManager.SaveData { Identity = new string('2', 64) };
            world.Runtime.WriteCampaignBinding(save, save.Identity);
            AvatarPlugin.ConfiguredSeed = "future seed";
            AvatarPlugin.ConfiguredTreasureShuffle = true;
            world.Runtime.PrepareLoad(save);
            world.Ready();
            Check(world.Runtime.TreasureProfile?.Fingerprint == TreasureShuffleProfile.Create(savedSeed).Fingerprint,
                "Treasure adoption used the configured future seed");
        });

        Case("An explicitly disabled saved treasure policy stays disabled", () =>
        {
            var world = new World();
            world.Ready();
            var save = new GSD1.SaveDataManager.SaveData { Identity = new string('3', 64) };
            world.Runtime.WriteCampaignBinding(save, save.Identity);
            world.Runtime.WriteTreasureBinding(save.Identity);
            AvatarPlugin.ConfiguredTreasureShuffle = true;
            world.Runtime.PrepareLoad(save);
            world.Ready();
            Check(!world.Runtime.TreasureActive && world.Runtime.TreasureProfile is null,
                "Disabled saved policy adopted the new configuration");
        });

        Case("Loading an unbound save clears an earlier treasure campaign", () =>
        {
            var world = new World(false);
            AvatarPlugin.ConfiguredTreasureShuffle = true;
            world.Runtime.BeginNewGame();
            world.Ready();
            Check(world.Runtime.TreasureActive, "Treasure campaign was not enabled");
            world.Runtime.PrepareLoad(new GSD1.SaveDataManager.SaveData { Identity = new string('4', 64) });
            Check(!world.Runtime.TreasureActive && world.Runtime.TreasureProfile is null,
                "Unbound save inherited the previous treasure profile");
        });

        Case("Starting a later disabled campaign clears earlier treasure settings", () =>
        {
            var world = new World(false);
            AvatarPlugin.ConfiguredTreasureShuffle = true;
            world.Runtime.BeginNewGame();
            Check(world.Runtime.TreasureActive, "Treasure campaign was not admitted");
            AvatarPlugin.ConfiguredTreasureShuffle = false;
            world.Runtime.BeginNewGame();
            Check(!world.Runtime.TreasureActive && world.Runtime.TreasureProfile is null,
                "New disabled campaign kept the previous profile");
        });
    }
}
