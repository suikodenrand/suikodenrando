using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using StarsOfDestiny.Launcher.Core;

namespace StarsOfDestiny.Launcher;

internal sealed partial class MainForm
{
    private Control BuildTreasure()
    {
        if (GameId != "s1") return Note("Chest randomization is currently available for Suikoden I. Choose Suikoden I above to configure it.");
        var page = Grid(1, 3);
        page.RowStyles.Add(new RowStyle(SizeType.Absolute, 180));
        page.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        page.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        var options = Group("Treasure chest shuffle");
        var optionsStack = Stack();
        optionsStack.Controls.Add(Check("s1.chests", "Enable chest shuffle for new campaigns"));
        optionsStack.Controls.Add(Note("Existing saves keep their own chest policy. Use the controls below to change a saved campaign."));
        optionsStack.Controls.Add(Note("132 ordinary item locations are shuffled. Recruitment items, Blinking Mirror, potch, and the special Dwarves' Vault chest keep their original rewards."));
        options.Controls.Add(optionsStack);
        page.Controls.Add(options, 0, 0);

        var campaigns = Group("Saved campaign chest settings");
        var layout = Grid(1, 3);
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 37));
        layout.Controls.Add(Note("Choose checked (shuffled) or unchecked (original) above, Apply settings, then select a save and reset its chest settings. The next load adopts that policy. Items and opened chests are kept."), 0, 0);
        var list = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false };
        list.Columns.Add("Seed", 260);
        list.Columns.Add("Chest policy", 110);
        list.Columns.Add("Saved", 170);
        list.Columns.Add("Save identity", 300);
        TreasureCampaignManager? manager = null;
        void RefreshCampaigns()
        {
            try
            {
                manager = TreasureCampaignManager.Load(_workspace!.GameFolder, () => GameInstallation.IsGameRunning(_workspace.GameFolder));
                list.Items.Clear();
                foreach (var campaign in manager.Campaigns.OrderByDescending(item => item.LastWriteTimeUtc))
                {
                    var row = new ListViewItem(campaign.Seed) { Tag = campaign };
                    row.SubItems.Add(campaign.Enabled ? "Shuffled" : "Original rewards");
                    row.SubItems.Add(campaign.LastWriteTimeUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"));
                    row.SubItems.Add(campaign.SaveIdentity);
                    list.Items.Add(row);
                }
                if (manager.Issues.Count != 0) _status.Text = string.Join(" ", manager.Issues);
            }
            catch (Exception error) { _status.Text = error.Message; }
        }
        layout.Controls.Add(list, 0, 1);
        var reset = Button("Reset selected save's chest settings");
        reset.Click += (_, _) =>
        {
            if (list.SelectedItems.Count == 0 || manager is null) return;
            try
            {
                if (_workspace!.IsDirty || !_workspace.GetBoolean("s1.treasureEnabled"))
                { _status.Text = "Apply your chest settings first, then reset the selected save."; return; }
                string backup = manager.Reset((TreasureCampaignInfo)list.SelectedItems[0].Tag!);
                RefreshCampaigns();
                _status.Text = "Saved chest policy backed up to " + backup + ". Load that save to adopt the current settings.";
            }
            catch (Exception error) { ShowError(error); }
        };
        var refresh = Button("Refresh list");
        refresh.Click += (_, _) => RefreshCampaigns();
        layout.Controls.Add(Buttons(reset, refresh), 0, 2);
        campaigns.Controls.Add(layout);
        page.Controls.Add(campaigns, 0, 1);
        page.Controls.Add(Note("The seed stored in an existing character campaign determines its chest mapping. Editing the seed above is for your next new game."), 0, 2);
        RefreshCampaigns();
        return page;
    }

    private Control BuildBattle()
    {
        var page = Grid(2, 3);
        page.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        page.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        page.RowStyles.Add(new RowStyle(SizeType.Absolute, 105));
        page.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
        page.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
        var unites = Group("Shuffled-character Unite attacks");
        var uniteStack = Stack();
        var uniteSwitch = Check(GameId + ".unites", "Enable shuffled-character Unite attacks");
        uniteSwitch.Enabled = SupportsHost(new Version(0, 14, 1));
        uniteStack.Controls.Add(uniteSwitch);
        uniteStack.Controls.Add(Note("Unites follow the actual fighters in your party and require standard turn-based combat. Turn off S1 shuffled Unites before enabling ATB. Requires plug-in 0.14.1 or later."));
        unites.Controls.Add(uniteStack);
        page.Controls.Add(unites, 0, 0);
        page.SetColumnSpan(unites, 2);
        var atb = Group("Suikoden I experimental ATB");
        var stack = Stack();
        var state = GameInstallation.Inspect(_workspace!.GameFolder);
        stack.Controls.Add(Note(state.AtbActive ? "ATB preview " + state.AtbVersion + " is installed." : state.AtbDormantPath is not null
            ? "ATB preview " + state.AtbVersion + " is available but its plug-in is inactive." : "ATB is not installed. Select Experimental S1 ATB in setup to add it; it starts disabled."));
        if (!state.AtbActive && state.AtbDormantPath is not null)
        {
            var activate = Button("Activate ATB preview");
            activate.Click += (_, _) =>
            {
                try
                {
                    _workspace.SaveChanges();
                    string path = GameInstallation.ActivateAtb(_workspace.GameFolder);
                    RebuildPages();
                    _status.Text = "ATB preview activated at " + path;
                }
                catch (Exception error) { ShowError(error); }
            };
            stack.Controls.Add(activate);
        }
        foreach (var option in new[] { ("Enabled", "Enable ATB preview"), ("ActiveAtb", "Schedule actions with SPD meters"),
            ("RepeatTurns", "Allow repeated actions when a meter refills"), ("ReadyCommands", "Choose a hero's command when ready"),
            ("NativeReadyMenus", "Use native menus and targeting"), ("NativeSpdMeters", "Show SPD meters in the battle UI") })
        {
            var control = Check("atb." + option.Item1, option.Item2);
            control.Enabled = state.AtbActive;
            stack.Controls.Add(control);
        }
        atb.Controls.Add(stack);
        atb.Enabled = GameId == "s1";
        page.Controls.Add(atb, 0, 1);
        page.SetRowSpan(atb, 2);

        var timing = Group("ATB timing and input");
        var timingStack = Stack();
        timingStack.Controls.Add(Check("atb.ActiveInput", "Keep selecting commands during attacks"));
        timingStack.Controls.Add(Check("atb.EnemyDexCharge", "Use DEX to speed up enemy charge"));
        timingStack.Controls.Add(Check("atb.OpeningCharge", "Randomize starting meters using SPD and DEX"));
        timingStack.Controls.Add(Check("atb.OneShotAttack", "Legacy one-action test"));
        timingStack.Controls.Add(Note("These settings belong to the experimental S1 preview. The one-action test is used when full ATB is disabled."));
        timing.Controls.Add(timingStack);
        timing.Enabled = GameId == "s1" && state.AtbActive;
        page.Controls.Add(timing, 1, 1);

        var battle = Group("Battle conveniences • Suikoden Fix");
        var battleStack = Stack();
        battleStack.Controls.Add(Check("fix.RememberBattleSpeed", "Remember battle speed"));
        battleStack.Controls.Add(Check("fix.RecoverHpAfterBattle", "Restore HP after battles"));
        battleStack.Controls.Add(Check("fix.RecoverMpAfterBattle", "Restore MP after battles"));
        battleStack.Controls.Add(Number("fix.EncounterRateMultiplier", "Encounter rate multiplier", 0, 100, 1));
        battle.Controls.Add(battleStack);
        page.Controls.Add(battle, 1, 2);
        return page;
    }

    private Control BuildQualityOfLife()
    {
        var page = Grid(3, 2);
        for (int n = 0; n < 3; n++) page.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333F));
        page.RowStyles.Add(new RowStyle(SizeType.Percent, 65));
        page.RowStyles.Add(new RowStyle(SizeType.Percent, 35));
        var movement = Group("Movement and menus");
        var a = Stack();
        a.Controls.Add(Check("fix.ToggleDash", "Toggle dash"));
        a.Controls.Add(Check("fix.DisableDiagonalMovements", "Disable diagonal movement"));
        a.Controls.Add(Check("fix.StallionBoons", "Stallion's movement benefits"));
        a.Controls.Add(Check("fix.InstantMessage", "Instant dialogue text"));
        a.Controls.Add(Check("fix.InstantShop", "Instant shop menus"));
        a.Controls.Add(Check("fix.SkipSplashscreens", "Skip splash screens"));
        a.Controls.Add(Check("fix.SkipMovies", "Skip opening movies"));
        movement.Controls.Add(a);
        page.Controls.Add(movement, 0, 0);

        var saves = Group("Saving and controls");
        var b = Stack();
        b.Controls.Add(Number("fix.SaveAnywhere", "Save Anywhere slot", -1, 99));
        b.Controls.Add(Note("F1 / controller Select. −1 disables it, 0 uses the last slot, and a positive number chooses a slot."));
        b.Controls.Add(Number("fix.SaveSlots", "Save slots", -1, 99));
        b.Controls.Add(Note("−1 uses the game's default. Custom values must be at least 16."));
        b.Controls.Add(Check("fix.ExitApplication", "Enable exit shortcut"));
        b.Controls.Add(Check("fix.ResetGame", "Enable title-screen reset shortcut"));
        b.Controls.Add(Check("fix.PauseGame", "Enable pause shortcut"));
        saves.Controls.Add(b);
        page.Controls.Add(saves, 1, 0);

        var services = Group("Shops and headquarters");
        var c = Stack();
        c.Controls.Add(Check("fix.UncapMoney", "Uncap money"));
        c.Controls.Add(Check("fix.RareFindsAlwaysInStock", "Keep rare finds in stock"));
        c.Controls.Add(Check("fix.EasyMinigames", "Easy minigames"));
        c.Controls.Add(Check("fix.BetterSanchez", "S1 • Ted and Odessa in Sanchez's menu"));
        c.Controls.Add(Check("fix.BetterLeona", "S2 • expanded Leona party choices"));
        c.Controls.Add(Check("fix.MoreTeleportLocations", "S2 • extra visited teleport locations"));
        c.Controls.Add(Check("fix.InstantRichmondInvestigation", "S2 • instant Richmond investigations"));
        services.Controls.Add(c);
        page.Controls.Add(services, 2, 0);

        var multipliers = Group("Battle and reward multipliers");
        var numbers = Grid(3, 2);
        for (int n = 0; n < 3; n++) numbers.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333F));
        var fields = new[] { ("PlayerDamageMultiplier", "Hero damage"), ("MonsterDamageMultiplier", "Enemy damage"), ("MonsterHealthMultiplier", "Enemy HP"),
            ("ExperienceMultiplier", "Experience"), ("MoneyMultiplier", "Potch"), ("LootMultiplier", "Loot") };
        for (int n = 0; n < fields.Length; n++) numbers.Controls.Add(Number("fix." + fields[n].Item1, fields[n].Item2, 0, 100, 1), n % 3, n / 3);
        multipliers.Controls.Add(numbers);
        page.Controls.Add(multipliers, 0, 1);
        page.SetColumnSpan(multipliers, 3);
        return page;
    }

    private Control BuildDisplay()
    {
        var page = Grid(2, 2);
        page.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        page.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        page.RowStyles.Add(new RowStyle(SizeType.Percent, 60));
        page.RowStyles.Add(new RowStyle(SizeType.Percent, 40));
        var display = Group("Display • Suikoden Fix");
        var a = Stack();
        a.Controls.Add(Number("fix.FPS", "FPS limit", -1, 360));
        a.Controls.Add(Number("fix.Vsync", "VSync", -1, 1));
        a.Controls.Add(Number("fix.Width", "Resolution width", -1, 16384));
        a.Controls.Add(Number("fix.Height", "Resolution height", -1, 16384));
        a.Controls.Add(Number("fix.Fullscreen", "Fullscreen mode", -1, 3));
        a.Controls.Add(Note("−1 keeps the game's default. FPS 0 removes the limit. VSync 0 is off, 1 is on."));
        a.Controls.Add(Check("fix.NoFrameSkip", "Disable frame skipping"));
        display.Controls.Add(a);
        page.Controls.Add(display, 0, 0);
        var visuals = Group("Visual effects");
        var b = Stack();
        b.Controls.Add(Check("fix.DisableVignette", "Disable vignette"));
        b.Controls.Add(Check("fix.DisableMaskedVignette", "Disable masked vignette"));
        b.Controls.Add(Check("fix.DisableDepthOfField", "Disable depth of field"));
        b.Controls.Add(Check("fix.SmoothSprites", "Smooth sprites"));
        b.Controls.Add(Number("fix.BloomMultiplier", "Bloom multiplier", 0, 10, 1));
        visuals.Controls.Add(b);
        page.Controls.Add(visuals, 1, 0);
        var sound = Group("Audio and speed");
        var d = Stack();
        d.Controls.Add(Number("fix.SpedUpMusic", "Music speed mode", -1, 3));
        d.Controls.Add(Number("fix.SpedUpSoundEffect", "Sound effect speed mode", -1, 1));
        d.Controls.Add(Check("fix.DisableMessageWindowSound", "Mute message window sound"));
        d.Controls.Add(Check("fix.DisableStartledSound", "Mute startled sound"));
        sound.Controls.Add(d);
        page.Controls.Add(sound, 0, 1);
        var speed = Group("Game speed");
        var e = Stack();
        e.Controls.Add(Number("fix.SpeedHackFactor", "Speed multiplier", 1, 100));
        e.Controls.Add(Note("Additional font, transition, audio, and visual options from the installed config are available in Advanced."));
        speed.Controls.Add(e);
        page.Controls.Add(speed, 1, 1);
        return page;
    }

    private Control BuildInterface()
    {
        var page = Grid(1, 3);
        page.RowStyles.Add(new RowStyle(SizeType.Absolute, 170));
        page.RowStyles.Add(new RowStyle(SizeType.Absolute, 145));
        page.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var dialogue = Group("Shuffled character names");
        var a = Stack();
        a.Controls.Add(Check("shared.dialogue", "Use shuffled character names in supported dialogue"));
        a.Controls.Add(Note("English dialogue replacement for both games uses the active campaign's saved character mapping."));
        dialogue.Controls.Add(a);
        page.Controls.Add(dialogue, 0, 0);
        var ui = Group("Shared in-game panel");
        var b = Stack();
        b.Controls.Add(Choice("shared.view", "Initial F6 panel", new[] { "Full", "Compact", "Hidden" }));
        b.Controls.Add(Check("fix.DisableAutoSaveNotification", "Hide autosave notification"));
        ui.Controls.Add(b);
        page.Controls.Add(ui, 0, 1);
        page.Controls.Add(Note("The panel is text only. F6 cycles Full, Compact and Hidden for the current session; the selected initial view is used again next launch."), 0, 2);
        return page;
    }
}
