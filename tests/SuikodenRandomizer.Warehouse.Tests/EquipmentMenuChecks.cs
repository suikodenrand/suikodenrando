using System;
using SuikodenRandomizer.AvatarPlugin;

namespace SuikodenRandomizer.AvatarPlugin
{
    internal static class EquipmentOptimizer
    {
        internal static int Mode;
        internal static string SourceLabel => Mode.ToString();
        internal static PLAYER_DATA? Applied;
        internal static void CycleSources(int direction) => Mode += direction;
        internal static string Optimize(PLAYER_DATA target) { Applied = target; return "DEF +7"; }
    }
}

internal static class EquipmentMenuChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var work = G_cmd_c.command_work;
        var originalIndex = work.index;
        var menu = WindowManager.Instance.menuWindow.menuEquip;
        var row = menu.m_charaEquip.cellList[0];
        row.leftText.text = "stock";
        row.m_canvasGroup.alpha = 0.25f;
        work.player_data.Add(new());
        work.player_data.Add(new());
        work.player_kazu = 2;
        work.selected_player = 1;
        work.index = 0;

        bool Input(uint repeat = 0, uint trigger = 0)
        {
            OldSrcBase.sys_work.PadRTrig = repeat;
            OldSrcBase.sys_work.PadTrig = trigger;
            int result = 0;
            bool native = EquipmentOptimizePickerPatch.Prefix(ref result);
            check(native || result == -1, "Handled gear input must keep the native menu open.");
            return native;
        }

        S1QualityOfLife.OptimizeEquipment = false;
        check(Input() && row.leftText.text == "stock", "Disabled Optimize must leave native equipment selection intact.");
        S1QualityOfLife.OptimizeEquipment = true;
        check(Input() && row.gameObject.activeSelf && row.leftText.text == "Optimize", "Optimize row missing.");
        check(!Input(0x1000) && row.Selected && work.index == 0, "Up from the first slot should select Optimize.");
        check(!Input(trigger: 0x2000) && EquipmentOptimizer.Mode == 1, "Right should change the source.");
        check(!Input(trigger: 0x8000) && EquipmentOptimizer.Mode == 0, "Left should change back.");
        check(!Input(trigger: 0x20) && ReferenceEquals(EquipmentOptimizer.Applied, work.player_data[1])
            && G_cmd_c.LastEquipmentRefresh == 1 && menu.Summary == "DEF +7", "Wrong target, refresh or result.");
        check(Input(0x4000) && work.index == 4, "Down from Optimize should let native navigation wrap to headgear.");
        work.index = 4;
        check(!Input(0x4000), "Down from the last slot should reach Optimize.");
        check(Input(trigger: 0x40) && !row.gameObject.activeSelf && !row.Selected && row.leftText.text == "stock"
            && row.m_canvasGroup.alpha == 0.25f && !menu.m_recommendLine.activeSelf,
            "Cancel should restore the native row and return to character selection.");
        check(Input(trigger: 0x20), "Confirm on a normal slot should open the native item list.");
        check(Input() && row.gameObject.activeSelf, "Returning to equipment should restore Optimize.");
        work.selected_player = 2;
        check(Input() && !row.gameObject.activeSelf, "Invalid party selection should remove Optimize.");
        work.player_data.Clear();
        work.player_kazu = work.selected_player = 0;
        work.index = originalIndex;
        OldSrcBase.sys_work.PadRTrig = OldSrcBase.sys_work.PadTrig = 0;
    }
}
