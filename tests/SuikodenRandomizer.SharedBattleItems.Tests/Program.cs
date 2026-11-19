using SuikodenRandomizer.AvatarPlugin;
using SuikodenRandomizer.Shared;

internal static class Program
{
    private const ushort Medicine = 25, MegaMedicine = 72, Gear = 10;
    private static BATTLE_WORK _battle = null!;
    private static BattleBase.WINDOW_WORK _window = null!;
    private static PLAYER_BASE Bag(int slot) => _battle.p_battle_data[slot].player_data.player_base;
    private static PM_DATA Hero(int slot) => _battle.pm_data[slot];
    private static int Token(int actor, int owner, int pocket) => S1BattleItemAddress.Encode(actor, owner, pocket);

    private static void Check(bool ok, string message)
    {
        if (!ok) throw new InvalidOperationException(message);
    }

    private static void SetUp(int count)
    {
        SharedBattleItems.Reset();
        _battle = new BATTLE_WORK { player_kazu = count, pm_kazu = count + 1 };
        BattleBase.battle_work = _battle;
        for (int slot = 1; slot <= 6; slot++)
        {
            _battle.pm_data[slot] = new PM_DATA { name = "Hero " + slot };
            _battle.p_battle_data[slot] = new P_BATTLE_DATA();
            Bag(slot).chara_no = 10 + slot;
        }
        _window = new BattleBase.WINDOW_WORK { selected_player = 1, selected_player_data = _battle.p_battle_data[1].player_data };
        BattleBase.window_work = _window;
        G_item_c_data.item_data_table[Medicine] = new ITEM_DATA { type = 4, name = 100, item_func = target => target, times = 6 };
        G_item_c_data.item_data_table[MegaMedicine] = new ITEM_DATA { type = 4, name = 101, item_func = target => target, times = 3 };
        G_item_c_data.item_data_table[Gear] = new ITEM_BASE { type = 0 };
    }

    private static PLAYER_ITEM Add(int owner, ushort id, byte charges)
    {
        var item = new PLAYER_ITEM { item_id = id, data = charges };
        var bag = Bag(owner);
        bag.item[bag.item_kazu++] = item;
        return item;
    }

    private static void CommitBorrowed(int actor, int token, int owner, int nativeResult)
    {
        var pockets = Bag(actor).item;
        int count = Bag(actor).item_kazu;
        Hero(actor).c_item = checked((sbyte)token);
        Hero(actor).c_mokuhyo = 3;
        int result = 0;
        bool run = SharedBattleItems.BeginUse(actor, out var use, ref result);
        Check(run && use?.Owner == owner, "Native action lost its owner");
        var visible = Bag(actor).item[Hero(actor).c_item];
        Check(visible.item_id == Medicine && visible.data == 0, "Native effect received the wrong item or could debit the wrong pocket");
        Check(Hero(actor).c_mokuhyo == 3 && Bag(actor).item_kazu == count, "Using a borrowed item changed the target or inventory count");
        SharedBattleItems.EndUse(use, nativeResult, null);
        Check(ReferenceEquals(Bag(actor).item, pockets), "Temporary item view leaked into the actor's bag");
    }

    private static void Main()
    {
        SetUp(5);
        S1QualityOfLife.SharedBattleItems = false;
        Check(!SharedBattleItems.Enabled && SharedBattleItems.BuildMenu() < 0
            && !SharedBattleItems.ReserveOverlayItem(1, Token(1, 2, 0)), "Disabled sharing must use native per-fighter items for both menus");
        S1QualityOfLife.SharedBattleItems = true;
        Hero(2).unbalance_cnt = 2;
        Hero(3).hp = 0;
        Hero(4).dead_flg = 1;
        Hero(5).IsAway = true;
        var one = Add(2, Medicine, 1);
        Add(2, MegaMedicine, 0);
        Add(2, Gear, 5);
        foreach (int owner in new[] { 3, 4, 5, 6 }) Add(owner, Medicine, 3);
        int borrowed = Token(1, 2, 0);
        Check(SharedBattleItems.BuildMenu() == 1 && _window.item_window_data[0] == borrowed,
            "A living Unbalanced ally should supply medicine to an actor with empty pockets");
        var oldView = SharedBattleItems.BeginWindowView();
        Check(_window.selected_player_data!.player_base.item[borrowed].data == 1, "The picker did not display the owner's remaining charge");
        Check(SharedBattleItems.OwnerNameForRow(0) == "Hero 2", "The picker showed the wrong carrier");
        SharedBattleItems.EndWindowView(oldView);
        Check(ReferenceEquals(oldView, _window.selected_player_data), "The preview did not restore the actor");
        Check(SharedBattleItems.OwnerNameForRow(0) == null, "Shared item labels leaked outside their window");
        Hero(1).c_item = (sbyte)borrowed;
        Check(SharedBattleItems.SelectNativeItem(), "Native confirmation refused the available dose");
        Check(SharedBattleItems.IsItemPending(1), "Native confirmation missed its reservation");
        Check(SharedBattleItems.AvailableUses(2, Token(2, 2, 0)) == 0, "Queued last dose still showed in the owner's fallback menu");
        Check(!SharedBattleItems.ReserveOverlayItem(2, Token(2, 2, 0)), "The owner double-booked the last dose");
        CommitBorrowed(1, borrowed, 2, 0);
        Check(one.data == 1 && SharedBattleItems.IsItemPending(1), "Waiting for an animation consumed or released the dose");
        CommitBorrowed(1, borrowed, 2, 1);
        Check(one.data == 0 && Bag(2).item_kazu == 2 && Bag(1).item_kazu == 0 && !SharedBattleItems.IsItemPending(1),
            "Consumption did not debit exactly one dose from the owner");

        SetUp(3);
        var first = Add(2, Medicine, 1);
        var second = Add(2, Medicine, 2);
        borrowed = Token(1, 2, 1);
        Check(SharedBattleItems.ReserveOverlayItem(1, borrowed), "Could not reserve second stack");
        G_item_c.del_item_base(Bag(2), 0);
        Check(ReferenceEquals(second, Bag(2).item[0]), "Fixture failed to compact the pocket");
        Check(SharedBattleItems.ReservedItemPocket(1, borrowed) == 0
            && SharedBattleItems.ReservedItemPocket(1, Token(1, 2, 0)) < 0,
            "CTB could not resolve the original reservation to its moved stack");
        CommitBorrowed(1, borrowed, 2, 1);
        Check(first.data == 1 && second.data == 1 && Bag(2).item_kazu == 1, "Depleted the wrong identical stack after a pocket moved");

        borrowed = Token(1, 2, 0);
        Check(SharedBattleItems.ReserveOverlayItem(1, borrowed), "Could not reserve the last dose");
        Hero(2).hp = 0;
        Check(SharedBattleItems.ReservedItemPocket(1, borrowed) < 0, "CTB could still dispatch a knocked-out carrier's item");
        Hero(1).c_item = (sbyte)borrowed;
        int result = 0;
        Check(!SharedBattleItems.BeginUse(1, out _, ref result) && second.data == 1,
            "Used a dose after its owner was knocked out");
        Hero(2).hp = 50;
        Check(SharedBattleItems.ReserveOverlayItem(1, borrowed), "Revived carrier did not contribute again");
        SharedBattleItems.ReleaseItem(1);
        Check(SharedBattleItems.ReserveOverlayItem(3, Token(3, 2, 0)), "Cancelling a command did not release its dose");

        SetUp(3);
        var lastDose = Add(2, Medicine, 1);
        borrowed = Token(1, 2, 0);
        Check(SharedBattleItems.BuildMenu() == 1, "Expected one available stack for the native picker");
        Check(SharedBattleItems.ReserveOverlayItem(3, Token(3, 2, 0)), "First confirmed choice did not acquire the last dose");
        Hero(1).c_item = (sbyte)borrowed;
        Check(!SharedBattleItems.SelectNativeItem() && !SharedBattleItems.IsItemPending(1) && SharedBattleItems.IsItemPending(3),
            "A stale menu selection took an earlier reservation");
        CommitBorrowed(3, Token(3, 2, 0), 2, 1);
        Check(lastDose.data == 0 && Bag(2).item_kazu == 0, "Last dose was not consumed by the first confirmed choice");

        SetUp(3);
        var ownDose = Add(1, Medicine, 2);
        Hero(1).c_item = 0;
        Check(SharedBattleItems.ReserveOverlayItem(1, 0), "Failed to select the acting fighter's own stack");
        Check(SharedBattleItems.BeginUse(1, out var ownUse, ref result)
            && ownUse?.OriginalItems == null && ReferenceEquals(Bag(1).item[Hero(1).c_item], ownDose),
            "Own item did not take the ordinary native inventory path");
        ownDose.data--; // The real native routine debits this stack before returning success.
        SharedBattleItems.EndUse(ownUse, 1, null);
        Check(ownDose.data == 1, "The adapter charged the acting fighter a second time");

        var itemEffect = (ITEM_DATA)G_item_c_data.item_data_table[Medicine];
        itemEffect.times = 0; // Native unlimited-use battle item.
        var reusable = Add(2, Medicine, 0);
        borrowed = Token(1, 2, 0);
        Check(SharedBattleItems.ReserveOverlayItem(1, borrowed)
            && SharedBattleItems.ReserveOverlayItem(3, Token(3, 2, 0)), "Multiple heroes could not use a reusable item");
        CommitBorrowed(1, borrowed, 2, 1);
        Check(Bag(2).item_kazu == 1 && reusable.data == 0, "Reusable item disappeared from its carrier");
        SharedBattleItems.ClearCommands();
        Check(!SharedBattleItems.IsItemPending(3), "Skipped action kept its old reservation after native command reset");

        SetUp(6);
        for (int slot = 1; slot <= 6; slot++)
            for (int pocket = 0; pocket < 9; pocket++) Add(slot, Medicine, 2);
        Check(SharedBattleItems.BuildMenu() == 54, "A full party list was truncated");
        Check(Enumerable.Range(0, 54).Select(i => _window.item_window_data[i]).Distinct().Count() == 54,
            "Two carried stacks share a menu address");
        oldView = SharedBattleItems.BeginWindowView();
        Check(SharedBattleItems.OwnerNameForRow(50) == "Hero 6", "Scrolled item page lost its owner labels");
        SharedBattleItems.EndWindowView(oldView);
        Check(!SharedBattleItems.IsItemPending(3), "A reservation survived into a new battle");
        Console.WriteLine("Shared inventory checks passed for party eligibility, 54 stacks, reservations, consumption, compaction, cancellation and battle reset.");
    }
}
