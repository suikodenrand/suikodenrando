using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public sealed class PLAYER_DATA { }

namespace ShareUI
{
    public sealed class UIWindowListCell
    {
        public readonly Menu.Text leftText = new(), rightText = new();
        public readonly GameObject gameObject = new();
        public readonly CanvasGroup m_canvasGroup = new();
        public bool Selected;
        public void SetSelect(bool selected) => Selected = selected;
    }
    public sealed class UIWindowList
    {
        public readonly List<UIWindowListCell> cellList = Enumerable.Range(0, 6).Select(_ => new UIWindowListCell()).ToList();
    }
}
namespace ShareUI.Menu
{
    public sealed class UIMainEquipMenu
    {
        public IntPtr Pointer = new(3);
        public readonly UIWindowList m_charaEquip = new();
        public readonly GameObject m_recommendLine = new();
        public string Summary = "";
        public void SetRecommendEquipSelectCursor() => m_charaEquip.cellList[0].SetSelect(true);
        public void SetNowItemDescriptionText(string title, string description) => Summary = description;
        public void OpenNowDescription() { }
    }
}
