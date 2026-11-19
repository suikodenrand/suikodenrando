extern alias GSD1;

using System;
using HarmonyLib;
using ShareUI.Menu;
using UnityEngine;

namespace SuikodenRandomizer.AvatarPlugin;

// The S1 HD Items picker uses native row 6 for Party Items, rows 0–5 for
// fighters, and leaves its second special-item row (7) unused. Keep the
// native command index in its original range even when row 7 has focus.
internal static class WarehouseMenu
{
    private const int Roc = 85, PartyItems = 6, Warehouse = 7;
    private const uint Confirm = 0x20, Cancel = 0x40, Up = 0x1000, Down = 0x4000;
    private static UIMainItemMenu? _menu;
    private static IntPtr _owner;
    private static int _originalSibling;
    private static bool _selected;
    private static GSD1.SHIRO_FUNC_DELEGATE? _warehouseStep;
    private static GSD1.Event_c? _events;
    private static GSD1.D_azukar_c.UIAzukari? _nameWindow;
    private static CanvasGroup? _fieldCanvas;
    private static float _fieldAlpha;
    private static int _previousExitLock;

    internal static bool RocRecruited
    {
        get
        {
            var flags = GSD1.OldSrcBase.game_work?.member_flag;
            return flags != null && flags.Length > Roc && (flags[Roc] & 0x08) != 0;
        }
    }

    internal static void EnterItems()
    {
        Reset();
        if (!S1QualityOfLife.WarehouseMenu || AvatarRuntime.Current?.OwnsExecution != true || !RocRecruited) return;
        var menu = GSD1.WindowManager.Instance?.menuWindow?.menuTool;
        var work = GSD1.G_cmd_c.command_work;
        if (menu == null || work == null || menu.partyItemList.Count <= Warehouse) return;

        var row = menu.partyItemList[Warehouse];
        var party = menu.partyItemList[PartyItems];
        _originalSibling = row.transform.GetSiblingIndex();
        _menu = menu;
        _owner = work.Pointer;
        row.leftText.text = "Roc's Warehouse";
        row.transform.SetSiblingIndex(party.transform.GetSiblingIndex() + 1);
        row.gameObject.SetActive(true);
    }

    internal static bool RunPicker()
    {
        if (_menu == null) return true;
        var work = GSD1.G_cmd_c.command_work;
        if (work == null || work.Pointer != _owner || AvatarRuntime.Current?.OwnsExecution != true)
        {
            Reset();
            return true;
        }
        if (_warehouseStep != null)
        {
            try
            {
                _warehouseStep = _warehouseStep.Invoke().Item2;
                if (_warehouseStep == null) ReturnToItems(work);
            }
            catch (Exception error)
            {
                ReleaseWarehouse(work);
                AvatarRuntime.Current?.Trace("WAREHOUSE: native session interrupted: " + error.Message);
            }
            return false;
        }
        if (work.step != 1) return true;
        var pad = GSD1.OldSrcBase.sys_work;
        if ((pad.PadTrig & Confirm) != 0)
        {
            if (!_selected) return true;
            BeginWarehouse(work);
            return false;
        }
        if ((pad.PadTrig & Cancel) != 0)
        {
            if (_selected) _menu.partyItemList[Warehouse].SetSelect(false);
            _selected = false;
            return true;
        }
        if (work.cancel_cnt > 0) return true;
        uint move = pad.PadRTrig & (Up | Down);
        if (_selected)
        {
            if (move != Down && move != Up) return false;
            // Let native navigation populate its own preview and consume the input.
            work.index = move == Down ? PartyItems : 0;
            _menu.partyItemList[Warehouse].SetSelect(false);
            _selected = false;
            return true;
        }
        if (!((move == Down && work.index == PartyItems) || (move == Up && work.index == 0))) return true;
        _selected = true;
        _menu.CloseWindow(UIMainItemMenu.WindowType.ToolList);
        SelectWarehouseRow(_menu);
        GSD1.P_sound_c.Qsd_call(0x502, false);
        return false;
    }

    private static void BeginWarehouse(GSD1.G_cmd_c.COMMAND_WORK work)
    {
        var events = GSD1.OldSrcBase.global_work?.event_c;
        if (events == null || events.uiAzukari != null)
        {
            GSD1.P_sound_c.Qsd_call(0x505, false);
            return;
        }
        _previousExitLock = work.exit_kinshi_flg;
        try
        {
            // Drive only the warehouse's own init/main/exit delegates. The castle
            // NPC's outer event also changes village and event-script state.
            _warehouseStep = GSD1.D_azukar_c.azukari_demo().Item2;
            _events = events;
            _nameWindow = events.uiAzukari;
            if (_warehouseStep == null) throw new InvalidOperationException("Missing native warehouse handler.");
            var root = GSD1.WindowManager.Instance.menuWindow.gameObject;
            _fieldCanvas = root.GetComponent<CanvasGroup>() ?? root.AddComponent<CanvasGroup>();
            _fieldAlpha = _fieldCanvas.alpha;
            _fieldCanvas.alpha = 0;
            work.exit_kinshi_flg = 1;
            GSD1.P_sound_c.Qsd_call(0x503, false);
            AvatarRuntime.Current?.Trace("WAREHOUSE: opened Roc's Warehouse from Items.");
        }
        catch (Exception error)
        {
            ReleaseWarehouse(work);
            AvatarRuntime.Current?.Trace("WAREHOUSE: could not open native warehouse: " + error.Message);
        }
    }

    private static void ReleaseWarehouse(GSD1.G_cmd_c.COMMAND_WORK work)
    {
        // demo_exit normally releases the temporary party-name/UI helper.
        try
        {
            if (_nameWindow != null && _events?.uiAzukari?.Pointer == _nameWindow.Pointer)
            {
                try { _nameWindow.Terminate(); }
                finally { _events.uiAzukari = null; }
            }
        }
        catch (Exception error) { AvatarRuntime.Current?.Trace("WAREHOUSE: cleanup interrupted: " + error.Message); }
        finally { ReturnToItems(work); }
    }

    private static void ReturnToItems(GSD1.G_cmd_c.COMMAND_WORK work)
    {
        _warehouseStep = null;
        _events = null;
        _nameWindow = null;
        if (_fieldCanvas != null) _fieldCanvas.alpha = _fieldAlpha;
        _fieldCanvas = null;
        work.exit_kinshi_flg = _previousExitLock;
        if (_menu != null) SelectWarehouseRow(_menu);
        AvatarRuntime.Current?.Trace("WAREHOUSE: returned to the Items menu.");
    }

    private static void SelectWarehouseRow(UIMainItemMenu menu)
    {
        menu.SetMemberSelectCursor(Warehouse);
        // The stock cursor can skip its normally hidden row on short parties.
        menu.partyItemList[Warehouse].SetSelect(true);
    }

    internal static void Reset()
    {
        if (_menu != null)
        {
            if (_warehouseStep != null && GSD1.G_cmd_c.command_work is { } work && work.Pointer == _owner)
                ReleaseWarehouse(work);
            var row = _menu.partyItemList[Warehouse];
            row.SetSelect(false);
            row.gameObject.SetActive(false);
            row.transform.SetSiblingIndex(_originalSibling);
        }
        if (_fieldCanvas != null) _fieldCanvas.alpha = _fieldAlpha;
        _menu = null;
        _owner = IntPtr.Zero;
        _warehouseStep = null;
        _events = null;
        _nameWindow = null;
        _fieldCanvas = null;
        _selected = false;
    }
}

[HarmonyPatch(typeof(GSD1.G_cmd_c), "command_dougu00")]
internal static class WarehouseItemsEntryPatch
{
    [HarmonyPostfix] internal static void Postfix() => WarehouseMenu.EnterItems();
}

[HarmonyPatch(typeof(GSD1.G_cmd_c), "command_dougu01")]
internal static class WarehouseItemsPickerPatch
{
    [HarmonyPrefix] internal static bool Prefix(ref int __result)
    {
        if (WarehouseMenu.RunPicker()) return true;
        __result = -1;
        return false;
    }
}

[HarmonyPatch(typeof(GSD1.G_cmd_c), "field_command_exit")]
internal static class WarehouseFieldExitPatch
{
    [HarmonyPrefix] internal static void Prefix() => WarehouseMenu.Reset();
}
