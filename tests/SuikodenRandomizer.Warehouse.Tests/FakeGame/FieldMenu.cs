using System;
using System.Collections.Generic;
using System.Linq;

namespace UnityEngine
{
    public sealed class CanvasGroup { public float alpha = 1; }
    public sealed class Transform
    {
        private readonly List<Transform> _children;
        public Transform(List<Transform> children) { _children = children; children.Add(this); }
        public int GetSiblingIndex() => _children.IndexOf(this);
        public void SetSiblingIndex(int index) { _children.Remove(this); _children.Insert(index, this); }
    }
    public sealed class GameObject
    {
        public bool Active { get; private set; }
        public bool activeSelf => Active;
        public void SetActive(bool active) => Active = active;
        private readonly Dictionary<Type, object> _components = new();
        public T? GetComponent<T>() where T : class => _components.GetValueOrDefault(typeof(T)) as T;
        public T AddComponent<T>() where T : new() { var item = new T(); _components.Add(typeof(T), item!); return item; }
    }
}
namespace ShareUI.Menu
{
    using UnityEngine;
    public sealed class Text { public string text = ""; }
    public sealed class Row(List<Transform> children)
    {
        public readonly Text leftText = new();
        public readonly Transform transform = new(children);
        public readonly GameObject gameObject = new();
        public bool Selected { get; private set; }
        public void SetSelect(bool selected) => Selected = selected;
    }
    public sealed class UIMainItemMenu
    {
        public enum WindowType { PartyList, ToolList }
        public readonly List<Row> partyItemList;
        public int Cursor, CloseCount;
        public UIMainItemMenu()
        {
            var children = new List<Transform>();
            var party = new Row(children);
            partyItemList = Enumerable.Range(0, 6).Select(_ => new Row(children)).ToList();
            partyItemList.Add(party);
            partyItemList.Add(new Row(children));
        }
        public void CloseWindow(WindowType type) => CloseCount++;
        public void SetMemberSelectCursor(int row) => Cursor = row;
    }
    public sealed class MainMenu
    {
        public readonly GameObject gameObject = new();
        public readonly UIMainItemMenu menuTool = new();
        public readonly UIMainEquipMenu menuEquip = new();
    }
}
public static class G_cmd_c
{
    public static COMMAND_WORK command_work = new();
    public static int LastEquipmentRefresh = -1;
    public static int make_soubi_window(int selected) { LastEquipmentRefresh = selected; return -1; }
    public static int command_soubi02() => -1;
    public static void field_command_exit() { }
    public sealed class COMMAND_WORK
    {
        public IntPtr Pointer = new(1);
        public int step = 1, index = 6, cancel_cnt, exit_kinshi_flg;
        public int selected_player, player_kazu;
        public readonly List<PLAYER_DATA> player_data = new();
    }
}
public sealed class Event_c { public D_azukar_c.UIAzukari? uiAzukari; }
public sealed class GlobalWork { public Event_c event_c = new(); }
public sealed class GameWork { public byte[] member_flag = new byte[108]; public SHIRO_DATA shiro_data = new(); }
public sealed class PadWork { public uint PadRTrig, PadTrig; }
public static class OldSrcBase
{
    public static GlobalWork global_work = new();
    public static GameWork game_work = new();
    public static PadWork sys_work = new();
}
public sealed class WindowManager
{
    public static WindowManager Instance = new();
    public readonly ShareUI.Menu.MainMenu menuWindow = new();
}
public static class P_sound_c { public static void Qsd_call(int sound, bool repeat) { } }
public delegate (bool, SHIRO_FUNC_DELEGATE?) SHIRO_FUNC_DELEGATE();
public static class D_azukar_c
{
    public static int GetSyohinNum() => OldSrcBase.game_work.shiro_data.azukari_kantei
        .Skip(1).Take(178).Sum(amount => amount[0] + amount[1]);
    public sealed class UIAzukari
    {
        public IntPtr Pointer = new(2);
        public void Terminate() { }
    }
    public static bool FailNext;
    public static int Opened;
    public static (bool, SHIRO_FUNC_DELEGATE) azukari_demo()
    {
        Opened++;
        OldSrcBase.global_work.event_c.uiAzukari = new();
        return (true, () =>
        {
            if (FailNext) { FailNext = false; throw new InvalidOperationException("Injected warehouse error"); }
            OldSrcBase.global_work.event_c.uiAzukari = null;
            return (true, null);
        });
    }
}
