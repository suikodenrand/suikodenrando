extern alias GSD1;

using System;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using SuikodenRandomizer.Shared;
using Battle = GSD1.BattleBase;

namespace SuikodenRandomizer.AvatarPlugin;

// The optional CTB plugin binds the public methods by name. Native S1 uses the same reservations.
public static class SharedBattleItems
{
    private sealed class Stack
    {
        internal int Owner, Token;
        internal ushort Id;
        internal GSD1.PLAYER_BASE Bag = null!;
        internal GSD1.PLAYER_ITEM Item = null!;
        internal IntPtr Actor;
        internal Il2CppReferenceArray<GSD1.PLAYER_ITEM>? ScratchItems;
    }

    private static IntPtr _battle;
    private static readonly Stack?[] Pending = new Stack?[S1BattleItemAddress.PartySlots + 1];
    private static readonly Stack?[] MenuRows = new Stack?[S1BattleItemAddress.AddressCount];
    private static GSD1.PLAYER_DATA? _view;
    private static int _menuActor;
    public static bool Enabled => S1QualityOfLife.SharedBattleItems && AvatarRuntime.Current?.OwnsExecution == true;

    internal static void ClearCommands() => Array.Clear(Pending, 0, Pending.Length);

    internal static void Reset()
    {
        ClearCommands();
        Array.Clear(MenuRows, 0, MenuRows.Length);
        _battle = IntPtr.Zero;
        _menuActor = 0;
        if (_view != null) UnityEngine.Object.Destroy(_view);
        _view = null;
    }

    private static GSD1.BATTLE_WORK? CurrentBattle()
    {
        if (!Enabled || Battle.battle_work is not { } work) return null;
        if (_battle != work.Pointer) { Reset(); _battle = work.Pointer; }
        return work;
    }

    // Unbalanced and sleeping fighters are still present and conscious for inventory purposes.
    private static bool InParty(GSD1.BATTLE_WORK work, int slot) =>
        slot >= 1 && slot <= S1BattleItemAddress.PartySlots && slot <= work.player_kazu
        && work.pm_data is { } rows && slot < rows.Count
        && rows[slot] is { hp: > 0, dead_flg: 0, IsAway: false };

    private static GSD1.PLAYER_BASE? Bag(GSD1.BATTLE_WORK work, int slot) =>
        work.p_battle_data is { } party && slot < party.Count ? party[slot]?.player_data?.player_base : null;

    private static GSD1.ITEM_DATA? Effect(GSD1.PLAYER_ITEM item)
    {
        var table = GSD1.G_item_c_data.item_data_table;
        if (item.item_id == 0 || table == null || item.item_id >= table.Count
            || table[item.item_id] is not { } row || (row.type & 4) == 0) return null;
        return row.TryCast<GSD1.ITEM_DATA>() is { item_func: not null } effect ? effect : null;
    }

    private static int ChargesAvailable(GSD1.BATTLE_WORK work, int actor, GSD1.PLAYER_ITEM item, GSD1.ITEM_DATA effect)
    {
        if (item.data == 0) return effect.times == 0 ? int.MaxValue : 0;
        int remaining = item.data;
        for (int slot = 1; slot < Pending.Length; slot++)
            if (slot != actor && Pending[slot] is { } other && other.Item.Pointer == item.Pointer
                && InParty(work, slot) && work.pm_data[slot].Pointer == other.Actor)
                remaining--;
        return remaining;
    }

    private static Stack Describe(GSD1.BATTLE_WORK work, int actor, int owner, int index,
        GSD1.PLAYER_BASE bag, GSD1.PLAYER_ITEM item) => new Stack
    {
        Owner = owner, Token = S1BattleItemAddress.Encode(actor, owner, index), Id = item.item_id,
        Actor = work.pm_data[actor].Pointer, Bag = bag, Item = item
    };

    // Native del_item shifts references left, then recycles the removed reference outside item_kazu.
    private static int FindStack(GSD1.BATTLE_WORK work, int actor, Stack stack)
    {
        if (!InParty(work, actor) || work.pm_data[actor].Pointer != stack.Actor || !InParty(work, stack.Owner)
            || Bag(work, stack.Owner)?.Pointer != stack.Bag.Pointer || stack.Item.item_id != stack.Id
            || stack.Bag.item is not { } items) return -1;
        for (int i = 0; i < Math.Min(stack.Bag.item_kazu, items.Count); i++)
            if (items[i]?.Pointer == stack.Item.Pointer) return i;
        return -1;
    }

    internal static int BuildMenu()
    {
        if (CurrentBattle() is not { } work || Battle.window_work is not { } window
            || !InParty(work, window.selected_player)) return -1;
        int actor = window.selected_player;
        if (_view == null)
        {
            _view = UnityEngine.ScriptableObject.CreateInstance<GSD1.PLAYER_DATA>();
            _view.player_base = new GSD1.PLAYER_BASE
            {
                item = new Il2CppReferenceArray<GSD1.PLAYER_ITEM>(S1BattleItemAddress.AddressCount)
            };
        }
        _menuActor = actor;
        Array.Clear(MenuRows, 0, MenuRows.Length);
        if (window.item_window_data == null || window.item_window_data.Count < MenuRows.Length)
            window.item_window_data = new Il2CppStructArray<int>(MenuRows.Length);
        if (window.item_name == null || window.item_name.Count < MenuRows.Length)
            window.item_name = new Il2CppStructArray<int>(MenuRows.Length);
        var map = window.item_window_data;
        var names = window.item_name;
        var viewItems = _view.player_base.item;
        int count = 0;
        void Append(int owner)
        {
            if (!InParty(work, owner) || Bag(work, owner) is not { item: { } items } bag) return;
            int end = Math.Min(S1BattleItemAddress.Pockets, Math.Min(bag.item_kazu, items.Count));
            for (int i = 0; i < end; i++)
            {
                if (items[i] is not { } item || Effect(item) is not { } effect) continue;
                int available = ChargesAvailable(work, actor, item, effect);
                if (available <= 0) continue;
                var stack = Describe(work, actor, owner, i, bag, item);
                MenuRows[stack.Token] = stack;
                var display = viewItems[stack.Token] ?? new GSD1.PLAYER_ITEM();
                display.item_id = item.item_id;
                display.soubi = item.soubi;
                display.data = available == int.MaxValue ? (byte)0 : (byte)available;
                viewItems[stack.Token] = display;
                map[count] = stack.Token;
                names[count++] = unchecked((int)effect.name);
            }
        }
        Append(actor);
        for (int owner = 1; owner <= Math.Min(S1BattleItemAddress.PartySlots, work.player_kazu); owner++)
            if (owner != actor) Append(owner);
        window.item_kazu = count;
        return count;
    }

    internal static GSD1.PLAYER_DATA? BeginWindowView()
    {
        if (!Enabled || _view == null || Battle.window_work is not { } window
            || window.selected_player != _menuActor || window.selected_player_data is not { } previous
            || previous.Pointer == _view.Pointer) return null;
        window.selected_player_data = _view;
        return previous;
    }

    internal static string? OwnerNameForRow(int row)
    {
        if (CurrentBattle() is not { } work || _view == null || Battle.window_work is not { } window
            || window.selected_player_data?.Pointer != _view.Pointer || window.selected_player != _menuActor
            || row < 0 || row >= window.item_kazu || row >= window.item_window_data.Count) return null;
        int token = window.item_window_data[row];
        if ((uint)token >= MenuRows.Length || MenuRows[token] is not { } stack
            || work.pm_data == null || stack.Owner >= work.pm_data.Count) return null;
        return work.pm_data[stack.Owner]?.name;
    }

    internal static void EndWindowView(GSD1.PLAYER_DATA? previous)
    {
        if (previous is not null && Battle.window_work is { } window && window.selected_player_data?.Pointer == _view?.Pointer)
            window.selected_player_data = previous;
    }

    internal static void EnterItemList(bool returning)
    {
        if (CurrentBattle() is null || Battle.window_work is not { } window) return;
        int actor = window.selected_player;
        if (actor < 1 || actor >= Pending.Length) return;
        var prior = Pending[actor];
        ReleaseItem(actor);
        int count = BuildMenu();
        if (!returning || count < 0) return;
        // Native target cancellation sometimes puts c_item (a pocket address) in the visible row.
        int row = Math.Clamp(window.item_hyouji_kaishi + window.window_index, 0, Math.Max(0, count - 1));
        if (prior != null)
            for (int i = 0; i < count; i++)
                if (MenuRows[window.item_window_data[i]]?.Item.Pointer == prior.Item.Pointer) { row = i; break; }
        window.item_hyouji_kaishi = Math.Clamp(window.item_hyouji_kaishi, Math.Max(0, row - 4), Math.Min(row, Math.Max(0, count - 5)));
        window.window_index = row - window.item_hyouji_kaishi;
    }

    internal static bool SelectNativeItem()
    {
        if (CurrentBattle() is not { } work || Battle.window_work is not { } window || _view == null) return true;
        int actor = window.selected_player;
        if (actor != _menuActor || !InParty(work, actor)) return false;
        int token = work.pm_data[actor].c_item;
        if ((uint)token >= MenuRows.Length || MenuRows[token] is not { } stack
            || FindStack(work, actor, stack) < 0 || Effect(stack.Item) is not { } effect
            || ChargesAvailable(work, actor, stack.Item, effect) <= 0)
            return false;
        Pending[actor] = stack;
        return true;
    }

    private static int CheckPocket(int actorSlot, int token, bool reserve)
    {
        if (CurrentBattle() is not { } work || !InParty(work, actorSlot)
            || !S1BattleItemAddress.TryDecode(actorSlot, token, out int owner, out int index) || !InParty(work, owner)
            || Bag(work, owner) is not { item: { } items } bag || index >= bag.item_kazu || index >= items.Count
            || items[index] is not { } item || Effect(item) is not { } effect) return 0;
        int remaining = Math.Max(0, ChargesAvailable(work, actorSlot, item, effect));
        if (reserve && remaining > 0) Pending[actorSlot] = Describe(work, actorSlot, owner, index, bag, item);
        return remaining;
    }

    public static bool ReserveOverlayItem(int actorSlot, int token) => CheckPocket(actorSlot, token, reserve: true) > 0;
    public static int AvailableUses(int actorSlot, int token) => CheckPocket(actorSlot, token, reserve: false);

    public static int ReservedItemPocket(int actorSlot, int token)
    {
        if (CurrentBattle() is not { } work || actorSlot < 1 || actorSlot >= Pending.Length
            || Pending[actorSlot] is not { } stack || stack.Token != token) return -1;
        int index = FindStack(work, actorSlot, stack);
        return index >= 0 && Effect(stack.Item) is { } effect && ChargesAvailable(work, actorSlot, stack.Item, effect) > 0
            ? index : -1;
    }

    public static bool IsItemPending(int actorSlot) => CurrentBattle() != null && actorSlot > 0
        && actorSlot < Pending.Length && Pending[actorSlot] != null;
    public static void ReleaseItem(int actorSlot)
    {
        if (actorSlot > 0 && actorSlot < Pending.Length) Pending[actorSlot] = null;
    }

    internal sealed class Use
    {
        internal GSD1.PM_DATA Actor = null!;
        internal GSD1.PLAYER_BASE ActingBag = null!;
        internal Il2CppReferenceArray<GSD1.PLAYER_ITEM>? OriginalItems;
        internal int Slot, Owner, Token, Index;
        internal GSD1.PLAYER_BASE OwnerBag = null!;
        internal GSD1.PLAYER_ITEM Item = null!;
        internal ushort Id;
        internal byte Before;
    }

    internal static bool BeginUse(int slot, out Use? use, ref int result)
    {
        use = null;
        if (CurrentBattle() is not { } work || slot < 1 || slot >= Pending.Length
            || slot > work.player_kazu || work.pm_data[slot] is not { } actor) return true;
        int token = actor.c_item;
        var stack = Pending[slot];
        if (stack == null || token != stack.Token || actor.Pointer != stack.Actor)
        {
            if (token >= 0 && token < S1BattleItemAddress.Pockets) return true; // Native auto or scripted command.
            result = -1;
            return false;
        }
        int index = ReservedItemPocket(slot, token);
        if (index < 0 || Bag(work, slot) is not { } actingBag)
        {
            ReleaseItem(slot);
            result = -1;
            return false;
        }
        use = new Use { Actor = actor, Slot = slot, Owner = stack.Owner, Index = index, Token = token,
            ActingBag = actingBag, OwnerBag = stack.Bag, Item = stack.Item, Id = stack.Id, Before = stack.Item.data };
        if (slot == stack.Owner) { actor.c_item = (sbyte)index; return true; }

        // player_item_sub first checks animation/target readiness, then reads c_item, debits a
        // nonzero charge and starts the effect on the ACTING hero. A scratch entry with zero
        // charges tells that routine to perform its native effect without touching any inventory.
        // The original nine pockets, their records and the native item count stay intact.
        var original = actingBag.item;
        int scratchCount = Math.Max(original.Count, token + 1);
        var scratch = stack.ScratchItems;
        if (scratch == null || scratch.Count != scratchCount)
        {
            scratch = new Il2CppReferenceArray<GSD1.PLAYER_ITEM>(scratchCount);
            scratch[token] = new GSD1.PLAYER_ITEM { item_id = stack.Id };
            stack.ScratchItems = scratch;
        }
        for (int i = 0; i < original.Count; i++) scratch[i] = original[i];
        use.OriginalItems = original;
        actingBag.item = scratch;
        return true;
    }

    internal static void EndUse(Use? use, int result, Exception? failure)
    {
        if (use == null) return;
        if (use.OriginalItems != null) use.ActingBag.item = use.OriginalItems;
        use.Actor.c_item = (sbyte)use.Token;
        if (result == 0 && failure == null) return; // Native animation has not admitted this action yet.
        ReleaseItem(use.Slot);
        if (failure != null || result != 1) return;
        if (use.Owner != use.Slot && use.Before > 0)
        {
            use.Item.data--;
            if (use.Item.data == 0) GSD1.G_item_c.del_item_base(use.OwnerBag, use.Index);
        }
        AvatarPlugin.PluginLog.LogInfo($"S1 battle item used: actor slot {use.Slot}, owner slot {use.Owner}, item {use.Id}, charges {use.Before} -> {use.Item.data}.");
    }
}
