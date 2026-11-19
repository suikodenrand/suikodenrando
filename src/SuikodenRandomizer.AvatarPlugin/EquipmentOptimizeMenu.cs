extern alias GSD1;

using System;
using HarmonyLib;
using ShareUI;
using ShareUI.Menu;

namespace SuikodenRandomizer.AvatarPlugin;

// The shared HD equipment panel reserves cell zero for recommendation. S1's
// native equipment indices 0–4 go to cells 1–5 and must stay in that range.
internal static class EquipmentOptimizeMenu
{
    private const int FirstSlot = 0, LastSlot = 4;
    private const uint Confirm = 0x20, Cancel = 0x40, Up = 0x1000, Right = 0x2000, Down = 0x4000, Left = 0x8000;
    private static UIMainEquipMenu? _menu;
    private static UIWindowListCell? _row;
    private static IntPtr _work;
    private static bool _selected, _oldActive, _oldLine;
    private static float _oldAlpha;
    private static string _oldLeft = "", _oldRight = "";

    internal static bool Run()
    {
        if (!S1QualityOfLife.OptimizeEquipment || AvatarRuntime.Current?.OwnsExecution != true) { Reset(); return true; }
        var work = GSD1.G_cmd_c.command_work;
        var menu = GSD1.WindowManager.Instance?.menuWindow?.menuEquip;
        if (work == null || menu == null || work.player_data == null || work.selected_player < 0
            || work.selected_player >= work.player_kazu || work.selected_player >= work.player_data.Count
            || work.player_data[work.selected_player] is not { } target) { Reset(); return true; }
        if (_row == null || _work != work.Pointer || _menu == null || _menu.Pointer != menu.Pointer)
        {
            Reset();
            var cells = menu.m_charaEquip?.cellList;
            if (cells == null || cells.Count <= LastSlot + 1 || cells[0] is not { } row || row.leftText == null
                || row.rightText == null || row.m_canvasGroup == null || menu.m_recommendLine == null) return true;
            _row = row;
            _menu = menu;
            _work = work.Pointer;
            _oldActive = row.gameObject.activeSelf;
            _oldAlpha = row.m_canvasGroup.alpha;
            _oldLine = menu.m_recommendLine.activeSelf;
            _oldLeft = row.leftText.text;
            _oldRight = row.rightText.text;
            row.gameObject.SetActive(true);
            row.m_canvasGroup.alpha = 1f;
            row.leftText.text = "Optimize";
            row.rightText.text = "";
            menu.m_recommendLine.SetActive(true);
        }

        if (work.step != 1) return true;
        var pad = GSD1.OldSrcBase.sys_work;
        if ((pad.PadTrig & Confirm) != 0)
        {
            if (!_selected) { Reset(); return true; }
            string result = EquipmentOptimizer.Optimize(target);
            // Rebuild the native equipment index cache, item names and total DEF.
            GSD1.G_cmd_c.make_soubi_window(work.selected_player);
            Show(menu, result);
            GSD1.P_sound_c.Qsd_call(0x503, false);
            return false;
        }
        if ((pad.PadTrig & Cancel) != 0) { Reset(); return true; }
        uint vertical = pad.PadRTrig & (Up | Down);
        if (_selected)
        {
            if (vertical == Up || vertical == Down)
            {
                // Native navigation wraps and updates the inventory description.
                work.index = vertical == Down ? LastSlot : FirstSlot;
                _selected = false;
                return true;
            }
            uint horizontal = pad.PadTrig & (Left | Right);
            if (horizontal == Left || horizontal == Right)
            {
                EquipmentOptimizer.CycleSources(horizontal == Right ? 1 : -1);
                Show(menu);
                GSD1.P_sound_c.Qsd_call(0x502, false);
            }
            return false;
        }
        if (!(vertical == Up && work.index == FirstSlot || vertical == Down && work.index == LastSlot)) return true;
        _selected = true;
        Show(menu);
        GSD1.P_sound_c.Qsd_call(0x502, false);
        return false;
    }

    private static void Show(UIMainEquipMenu menu, string? result = null)
    {
        menu.SetRecommendEquipSelectCursor();
        menu.SetNowItemDescriptionText("Optimize", result ?? $"Gear: {EquipmentOptimizer.SourceLabel}. Left/right to change.");
        menu.OpenNowDescription();
    }

    internal static void Reset()
    {
        if (_row != null)
        {
            if (_selected) _row.SetSelect(false);
            _row.leftText.text = _oldLeft;
            _row.rightText.text = _oldRight;
            _row.m_canvasGroup.alpha = _oldAlpha;
            _row.gameObject.SetActive(_oldActive);
            if (_menu != null && _menu.m_recommendLine != null) _menu.m_recommendLine.SetActive(_oldLine);
        }
        _work = IntPtr.Zero;
        _menu = null;
        _row = null;
        _selected = false;
    }
}

[HarmonyPatch(typeof(GSD1.G_cmd_c), nameof(GSD1.G_cmd_c.command_soubi02))]
internal static class EquipmentOptimizePickerPatch
{
    [HarmonyPrefix] internal static bool Prefix(ref int __result)
    {
        if (EquipmentOptimizeMenu.Run()) return true;
        __result = -1;
        return false;
    }
}

[HarmonyPatch(typeof(GSD1.G_cmd_c), nameof(GSD1.G_cmd_c.field_command_exit))]
internal static class EquipmentOptimizeExitPatch
{
    [HarmonyPrefix] internal static void Prefix() => EquipmentOptimizeMenu.Reset();
}
