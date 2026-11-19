extern alias GSD1;

using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using ShareUI;
using SuikodenRandomizer.Core.Combat;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Battle = GSD1::BattleBase;
using Manager = GSD1::UIBattleManager;

namespace SuikodenRandomizer.S1BattlePreview;

// The turn strip uses the game's battle canvas. The native menus handle input.
internal static class NativeCtbWindow
{
    private static readonly PortraitCard?[] Cards = new PortraitCard[8];
    private static readonly Dictionary<IntPtr, CtbStripRect> ReservedPanels = new();
    private static readonly Dictionary<IntPtr, RectTransform> PanelFrames = new();
    private static readonly CtbStripRect[] Occupied = new CtbStripRect[64];
    private static Il2CppStructArray<Vector3>? _corners;
    private static RectTransform? _root, _parent;
    private static Canvas? _canvas;
    private static readonly Color ReturnPreviewColor = new(0.25f, 0.95f, 0.95f, 1);
    private static TextMeshProUGUI? _fontTemplate, _support, _preview;
    private static Image? _frameTemplate, _backgroundTemplate;
    private static string? _supportText, _previewText;
    private static IntPtr _battle, _window;
    private static float _framePadding;
    private static float _scale = 1;
    private static Vector2 _parentSize;
    private static double _retryAfter;
    private static bool _reported, _warned;

    internal static Canvas? NativeCanvas => _canvas;
    internal static RectTransform? NativeParent => _parent;
    internal static TextMeshProUGUI? NativeFontTemplate => _fontTemplate;
    internal static Image? NativeFrameTemplate => _frameTemplate;
    internal static Image? NativeBackgroundTemplate => _backgroundTemplate;

    internal static void Update()
    {
        if (!BattlePreviewPlugin.CtbEnabled || global::SystemObject.mode != global::SystemObject.Mode.GSD1)
        {
            if (_battle != IntPtr.Zero || _root != null) ResetEncounter();
            return;
        }
        try
        {
            var work = Battle.battle_work;
            if (work is null || !Manager.IsInstantiated) { ResetEncounter(); return; }
            var manager = Manager.Instance;
            var window = manager.battleWindow;
            if (window == null) { ResetEncounter(); return; }
            var canvas = window.GetComponentInParent<Canvas>();
            var parent = canvas == null ? null : canvas.GetComponent<RectTransform>();
            if (canvas == null || parent == null || parent.rect.width <= 0 || parent.rect.height <= 0) { Hide(); return; }
            if (_battle != work.Pointer || _window != window.Pointer || _canvas == null
                || _canvas.Pointer != canvas.Pointer || _parent == null || _parent.Pointer != parent.Pointer)
            {
                ResetEncounter();
                _battle = work.Pointer;
                _window = window.Pointer;
                _canvas = canvas;
                _parent = parent;
            }
            if (!CtbPresentation.Visible || work.battle_phase_no is < 2 or > 4
                || !canvas.isActiveAndEnabled || !window.gameObject.activeInHierarchy) { Hide(); return; }
            if (_parentSize != parent.rect.size)
            {
                ReservedPanels.Clear();
                _parentSize = parent.rect.size;
            }
            if (_root == null)
            {
                if (Time.realtimeSinceStartupAsDouble < _retryAfter) return;
                var command = window.m_command;
                var text = command?.m_cellOrigin?.leftText;
                if (command == null || text == null || text.font == null)
                { Unavailable("stock command-window text has not initialized"); return; }
                var frame = FindCommandFrame(command.transform, command.m_contents, command.m_cellOrigin?.transform);
                var background = frame == null ? null : frame.transform.parent?.Find("Img_BG")?.GetComponent<Image>();
                if (frame == null || background == null)
                { Unavailable("the stock command-window frame/background has not initialized"); return; }
                Create(parent, text, frame, background);
            }
            int count = 0;
            // Main HUD windows keep their space even while briefly closed.
            AddMainPanel(window.m_party, parent, ref count);
            // The stock Front/Back party visuals extend beyond Win_Party's
            // controller rectangle. Reserve each displayed status background
            // and face as well, including their actual runtime transforms.
            var statuses = window.m_party?.m_playrStatusList;
            if (statuses != null)
                for (int index = 0; index < statuses.Count && index < work.player_kazu; index++)
                {
                    var status = statuses[index];
                    if (status == null) continue;
                    AddReservedPanel(PartyBackground(status), parent, ref count, IsStable(window.m_party));
                    AddReservedPanel(status.m_face == null ? null : status.m_face.rectTransform,
                        parent, ref count, IsStable(window.m_party));
                }
            AddReservedPanel(_frameTemplate?.rectTransform, parent, ref count, IsStable(window.m_command));
            var monsters = window.m_monster;
            if (monsters != null)
            {
                // Win_Enemy's outer RectTransform has zero prefab size. Its
                // real frame and GS1 cells describe the occupied list area.
                var enemyFrame = monsters.transform.Find("Img_Flame")?.GetComponent<RectTransform>();
                AddReservedPanel(enemyFrame, parent, ref count, IsStable(monsters));
                var cells = monsters.m_monsterListGS1;
                if (cells != null)
                    for (int index = 0; index < cells.Count && index < 6; index++)
                        AddReservedPanel(cells[index]?.rectTransform, parent, ref count, IsStable(monsters));
            }
            // Reserve open submenus that extend into the bottom strip or its captions.
            AddOpenPanel(window.m_common, parent, ref count);
            AddOpenPanel(window.m_emblem, parent, ref count);
            AddOpenPanel(window.m_magic, parent, ref count);
            AddOpenPanel(window.m_item, parent, ref count);
            AddOpenPanel(window.m_bag, parent, ref count);
            AddOpenPanel(window.m_unite, parent, ref count);
            AddOpenPanel(window.m_summaryEmblem, parent, ref count);
            AddOpenPanel(window.m_summaryMagic, parent, ref count);
            AddOpenPanel(window.m_summaryItem, parent, ref count);
            AddOpenPanel(window.m_summaryBag, parent, ref count);
            AddOpenPanel(window.m_summaryUnite, parent, ref count);
            var safe = SafeBounds(parent, canvas, manager.uiCamera);
            var turns = CtbPresentation.Turns;
            if (!NativeCtbStripLayout.TryPlace(safe, Occupied.AsSpan(0, count), turns.Length,
                _framePadding, _scale, out var layout)) { Hide(); return; }
            PlaceRoot(layout.Bounds, parent);
            float x = layout.Padding;
            for (int index = 0; index < Cards.Length; index++)
            {
                var card = Cards[index]!;
                bool visible = index < layout.CardCount && MatchesActor(work, turns[index]);
                SetVisible(card.Root, visible);
                if (!visible) continue;
                card.Show(turns[index], layout, x, _scale);
                x += layout.PortraitSize + layout.Gap;
            }
            bool previewRequested = !string.IsNullOrEmpty(CtbPresentation.PreviewLabel);
            CtbStripRect previewRect = default;
            bool showPreview = previewRequested
                && NativeCtbStripLayout.CanPlaceNotice(safe, Occupied.AsSpan(0, count), layout.Bounds, _scale, out previewRect);
            SetVisible(_preview?.rectTransform, showPreview);
            if (showPreview)
            {
                PlaceNotice(_preview!, previewRect, layout.Bounds);
                SetText(_preview!, CtbPresentation.PreviewLabel, ref _previewText);
            }
            // Preview has the first clear row. Rally may use the next row but
            // cannot replace a blocked preview or overlap a native HUD window.
            CtbStripRect supportRect = default;
            bool showSupport = !string.IsNullOrEmpty(CtbPresentation.Support) && (!previewRequested || showPreview)
                && NativeCtbStripLayout.CanPlaceNotice(safe, Occupied.AsSpan(0, count),
                    showPreview ? previewRect : layout.Bounds, _scale, out supportRect);
            SetVisible(_support?.rectTransform, showSupport);
            if (showSupport)
            {
                PlaceNotice(_support!, supportRect, layout.Bounds);
                SetText(_support!, CtbPresentation.Support, ref _supportText);
            }
            SetVisible(_root, true);
            if (!_reported)
            {
                _reported = true;
                Trace("Compact native CTB portrait strip ready; native party, command and enemy windows reserve their space.");
            }
        }
        catch (Exception error)
        {
            DestroyGeneratedWindow();
            _retryAfter = Time.realtimeSinceStartupAsDouble + 1;
            Unavailable(error.Message);
        }
    }

    private static bool MatchesActor(GSD1.BATTLE_WORK work, CtbTurnView view)
        => view.Identity != IntPtr.Zero && view.Slot > 0 && view.Slot <= work.pm_kazu
            && work.pm_data is { } rows && view.Slot < rows.Count && rows[view.Slot] is { } actor
            && actor.Pointer == view.Identity && actor.hp > 0 && actor.dead_flg == 0;

    internal static CtbStripRect SafeBounds(RectTransform parent, Canvas canvas, Camera? uiCamera)
    {
        var rect = parent.rect;
        var result = new CtbStripRect(rect.xMin, rect.yMin, rect.width, rect.height);
        Camera? camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        if (camera == null && canvas.renderMode != RenderMode.ScreenSpaceOverlay) camera = uiCamera;
        var safe = Screen.safeArea;
        if (canvas.renderMode != RenderMode.ScreenSpaceOverlay && camera == null) return result;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, new Vector2(safe.xMin, safe.yMin), camera, out var min)
            || !RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, new Vector2(safe.xMax, safe.yMax), camera, out var max)) return result;
        float left = Math.Max(rect.xMin, Math.Min(min.x, max.x));
        float right = Math.Min(rect.xMax, Math.Max(min.x, max.x));
        float bottom = Math.Max(rect.yMin, Math.Min(min.y, max.y));
        float top = Math.Min(rect.yMax, Math.Max(min.y, max.y));
        return new CtbStripRect(left, bottom, Math.Max(0, right - left), Math.Max(0, top - bottom));
    }

    private static void AddOpenPanel(UIWindowBasis? panel, RectTransform parent, ref int count)
    {
        if (panel == null || !(panel.IsOpen || panel.IsOpening || panel.IsClosing)) return;
        AddPanelBounds(PanelFrame(panel), parent, retain: false, stable: false, ref count);
    }

    private static bool IsStable(UIWindowBasis? panel)
        => panel != null && panel.IsOpen && !panel.IsOpening && !panel.IsClosing;

    private static RectTransform? PanelFrame(UIWindowBasis panel)
    {
        if (PanelFrames.TryGetValue(panel.Pointer, out var cached) && cached != null) return cached;
        var frame = panel.transform.Find("Img_Flame")?.GetComponent<RectTransform>();
        if (frame == null)
            foreach (var image in panel.GetComponentsInChildren<Image>(true))
                if (image != null && image.name.Equals("Img_Flame", StringComparison.Ordinal))
                { frame = image.rectTransform; break; }
        frame ??= panel.GetComponent<RectTransform>();
        if (frame != null) PanelFrames[panel.Pointer] = frame;
        return frame;
    }

    private static RectTransform? PartyBackground(ShareUI.Battle.UIBattlePlayerStatus status)
    {
        if (PanelFrames.TryGetValue(status.Pointer, out var cached) && cached != null) return cached;
        foreach (var image in status.GetComponentsInChildren<Image>(true))
            if (image != null && image.name.Equals("Img_BG", StringComparison.Ordinal))
            {
                var rect = image.rectTransform;
                PanelFrames[status.Pointer] = rect;
                return rect;
            }
        return null;
    }

    private static void AddMainPanel(UIWindowBasis? panel, RectTransform parent, ref int count)
    {
        if (panel != null) AddReservedPanel(PanelFrame(panel), parent, ref count, IsStable(panel));
    }

    private static void AddReservedPanel(RectTransform? rect, RectTransform parent, ref int count, bool stable)
        => AddPanelBounds(rect, parent, retain: true, stable, ref count);

    private static void AddPanelBounds(RectTransform? rect, RectTransform parent, bool retain, bool stable, ref int count)
    {
        if (rect == null || count >= Occupied.Length) return;
        var key = rect.Pointer;
        CtbStripRect value = default;
        if (rect.rect.width > 0 && rect.rect.height > 0)
        {
            _corners ??= new Il2CppStructArray<Vector3>(4);
            rect.GetWorldCorners(_corners);
            float left = float.PositiveInfinity, right = float.NegativeInfinity;
            float bottom = float.PositiveInfinity, top = float.NegativeInfinity;
            for (int index = 0; index < 4; index++)
            {
                var point = parent.InverseTransformPoint(_corners[index]);
                left = Math.Min(left, point.x);
                right = Math.Max(right, point.x);
                bottom = Math.Min(bottom, point.y);
                top = Math.Max(top, point.y);
            }
            // Only visible frame/cell footprints reserve space; a fullscreen
            // controller RectTransform is not the shape of its rendered HUD.
            var bounds = parent.rect;
            NativeCtbStripLayout.TryPanelFootprint(new CtbStripRect(bounds.xMin, bounds.yMin, bounds.width, bounds.height),
                new CtbStripRect(left, bottom, right - left, top - bottom),
                rect.name.Equals("Img_Flame", StringComparison.Ordinal), out value);
        }
        if (retain && value.HasArea && stable)
        {
            // Replace a settled footprint. Unioning every animation position
            // would eventually reserve the whole path across the screen.
            ReservedPanels[key] = value;
        }
        else if (retain && ReservedPanels.TryGetValue(key, out var previous))
        {
            if (previous.HasArea) Occupied[count++] = previous;
            if (previous.Equals(value)) return;
        }
        else if (retain && value.HasArea) ReservedPanels[key] = value;
        if (value.HasArea && count < Occupied.Length) Occupied[count++] = value;
    }

    private static Image? FindCommandFrame(Transform command, RectTransform? contents, Transform? cellOrigin)
    {
        Image? selected = null;
        float best = 0;
        foreach (var image in command.GetComponentsInChildren<Image>(true))
        {
            if (image == null || image.sprite == null || (contents != null && image.transform.IsChildOf(contents))
                || (cellOrigin != null && image.transform.IsChildOf(cellOrigin))) continue;
            string name = image.name;
            if (name.Contains("cursor", StringComparison.OrdinalIgnoreCase) || name.Contains("icon", StringComparison.OrdinalIgnoreCase)
                || name.Contains("arrow", StringComparison.OrdinalIgnoreCase) || name.Contains("gray", StringComparison.OrdinalIgnoreCase)
                || name.Contains("selection", StringComparison.OrdinalIgnoreCase) || name.Contains("mask", StringComparison.OrdinalIgnoreCase)) continue;
            Vector4 border = image.sprite.border;
            if (image.type != Image.Type.Sliced && border.x + border.y + border.z + border.w <= 0) continue;
            if (name.Equals("Img_Flame", StringComparison.Ordinal)) return image;
            var rect = image.rectTransform.rect;
            float area = rect.width * rect.height;
            if (rect.width <= 0 || rect.height <= 0 || area <= best) continue;
            best = area;
            selected = image;
        }
        return selected;
    }

    private static void Create(RectTransform parent, TextMeshProUGUI font, Image frame, Image background)
    {
        _fontTemplate = font;
        _frameTemplate = frame;
        _backgroundTemplate = background;
        _scale = Math.Clamp(font.fontSize / 36, 0.5f, 2);
        var border = frame.sprite.border;
        float borderPixels = Math.Max(Math.Max(border.x, border.z), Math.Max(border.y, border.w));
        _framePadding = borderPixels / Math.Max(0.01f, frame.pixelsPerUnit * frame.pixelsPerUnitMultiplier) + 4 * _scale;
        _root = CreateRect("CTB portrait turn order", parent);
        CopyImage(_root, background, normalizeAlpha: false);
        var edge = CreateRect("Native command frame", _root);
        Stretch(edge);
        CopyImage(edge, frame, normalizeAlpha: true);
        for (int index = 0; index < Cards.Length; index++) Cards[index] = new PortraitCard(_root, index, font, frame, background);
        _preview = CreateText("Action recovery preview", _root, font);
        _preview.color = ReturnPreviewColor;
        SetVisible(_preview.rectTransform, false);
        _support = CreateText("Reserve Rally", _root, font);
        SetVisible(_support.rectTransform, false);
    }

    private sealed class PortraitCard
    {
        internal RectTransform Root { get; }
        private readonly Image _portrait, _focus, _badge, _breakFill;
        private readonly TextMeshProUGUI _badgeLabel, _name;
        private readonly RectTransform _breakTrack;
        private readonly RectTransform _returnFrame;
        private readonly RectTransform[] _returnEdges = new RectTransform[4];
        private readonly Color _nativeFrameColor;
        private readonly Color _nativeNameColor;
        private string? _nameText, _badgeText;
        private IntPtr _portraitSprite;
        private int _badgeId = -1;
        private string _badgeName = "";

        internal PortraitCard(RectTransform parent, int index, TextMeshProUGUI font, Image frame, Image background)
        {
            Root = CreateRect("Turn " + (index + 1), parent);
            _portrait = AddImage(CreateRect("Native portrait", Root));
            _portrait.preserveAspect = true;
            _focus = CopyImage(CreateRect("Current or targeted turn", Root), frame, normalizeAlpha: true);
            _focus.pixelsPerUnitMultiplier = Math.Max(0.01f, frame.pixelsPerUnitMultiplier * 8);
            _nativeFrameColor = _focus.color;
            _returnFrame = CreateRect("Conditional next turn", Root);
            for (int edge = 0; edge < _returnEdges.Length; edge++)
            {
                var line = _returnEdges[edge] = CreateRect("Return outline " + edge, _returnFrame);
                AddImage(line).color = ReturnPreviewColor;
            }
            _badge = AddImage(CreateRect("Enemy badge shape", Root));
            _badge.preserveAspect = true;
            _badgeLabel = CreateText("Enemy badge label", Root, font);
            _breakTrack = CreateRect("Break track", Root);
            var track = CopyImage(_breakTrack, background, normalizeAlpha: false);
            track.sprite = null;
            track.type = Image.Type.Simple;
            _breakFill = AddImage(CreateRect("Break progress", _breakTrack));
            Stretch(_breakFill.rectTransform);
            _name = CreateText("Name", Root, font);
            _nativeNameColor = _name.color;
        }

        internal void Show(CtbTurnView view, CtbStripPlacement layout, float x, float scale)
        {
            float size = layout.PortraitSize;
            Position(Root, x, -layout.Padding, size, size + layout.LabelGap + layout.LabelHeight);
            Position(_portrait.rectTransform, 0, 0, size, size);
            var sprite = view.IsHero ? CtbVisualAssets.GetHeroSprite(view.Slot) : CtbVisualAssets.GetEnemySprite(view);
            IntPtr spritePointer = sprite == null ? IntPtr.Zero : sprite.Pointer;
            if (spritePointer != _portraitSprite) { _portraitSprite = spritePointer; _portrait.sprite = sprite; }
            _portrait.enabled = sprite != null;
            bool hasBadge = !view.IsHero && view.Badge is CtbEnemyBadge identityBadge
                && identityBadge.Slot == view.Slot && identityBadge.NativeIdentity == view.Identity.ToInt64();
            var color = hasBadge ? CtbVisualAssets.BadgeColor(view.Badge!.Value) : _nativeFrameColor;
            if (hasBadge)
            {
                var badge = view.Badge!.Value;
                _badge.sprite = CtbVisualAssets.GetBadgeSprite(badge.Shape);
                _badge.color = color;
                float badgeSize = sprite == null ? size * 0.52f : 18 * scale;
                Position(_badge.rectTransform, sprite == null ? (size - badgeSize) / 2 : size - badgeSize,
                    sprite == null ? -(size - badgeSize) / 2 : 0, badgeSize, badgeSize);
                if (_badgeId != badge.Id) { _badgeId = badge.Id; _badgeName = badge.Label; }
                PlaceText(_badgeLabel, 0, 0, size - 20 * scale, 18 * scale, 14 * scale);
                SetText(_badgeLabel, _badgeName, ref _badgeText);
                _badgeLabel.color = color;
            }
            SetVisible(_badge.rectTransform, hasBadge);
            SetVisible(_badgeLabel.rectTransform, hasBadge);
            _focus.enabled = view.Current || view.Targeted;
            _focus.color = view.Targeted && hasBadge ? color : _nativeFrameColor;
            Position(_focus.rectTransform, -2 * scale, 2 * scale, size + 4 * scale, size + 4 * scale);
            bool conditionalReturn = view.IsHero && view.ConditionalReturn && !view.Current;
            bool showReturnOutline = conditionalReturn && !view.Targeted;
            SetVisible(_returnFrame, showReturnOutline);
            if (showReturnOutline)
            {
                float width = size + 4 * scale, thickness = 2 * scale;
                Position(_returnFrame, -2 * scale, 2 * scale, width, width);
                Position(_returnEdges[0], 0, 0, width, thickness);
                Position(_returnEdges[1], 0, -width + thickness, width, thickness);
                Position(_returnEdges[2], 0, 0, thickness, width);
                Position(_returnEdges[3], width - thickness, 0, thickness, width);
            }
            bool showBreak = !view.IsHero && BattlePreviewPlugin.CtbBreakEnabled;
            SetVisible(_breakTrack, showBreak);
            if (showBreak)
            {
                Position(_breakTrack, 0, -size + 4 * scale, size, 4 * scale);
                float progress = view.IsBroken ? 1 : float.IsFinite(view.BreakFraction) ? Math.Clamp(view.BreakFraction, 0, 1) : 0;
                _breakFill.rectTransform.anchorMax = new Vector2(progress, 1);
                _breakFill.enabled = progress > 0;
                _breakFill.color = color;
            }
            PlaceText(_name, 0, -size - layout.LabelGap, size, layout.LabelHeight, 17 * scale);
            _name.fontStyle = view.Current || conditionalReturn ? FontStyles.Bold : FontStyles.Normal;
            _name.color = showReturnOutline ? ReturnPreviewColor : _nativeNameColor;
            SetText(_name, conditionalReturn ? "Next" : view.Name, ref _nameText);
        }
    }

    private static TextMeshProUGUI CreateText(string name, Transform parent, TextMeshProUGUI style)
    {
        var text = CreateRect(name, parent).gameObject.AddComponent<TextMeshProUGUI>();
        text.raycastTarget = false;
        text.font = style.font;
        text.fontSharedMaterial = style.fontSharedMaterial;
        text.fontStyle = style.fontStyle;
        text.fontWeight = style.fontWeight;
        Color color = style.color;
        color.a = 1;
        text.color = color;
        text.alignment = TextAlignmentOptions.Center;
        text.enableAutoSizing = false;
        text.enableWordWrapping = false;
        text.overflowMode = TextOverflowModes.Ellipsis;
        text.richText = false;
        text.margin = Vector4.zero;
        return text;
    }

    private static void SetText(TextMeshProUGUI text, string value, ref string? previous)
    {
        if (string.Equals(previous, value, StringComparison.Ordinal)) return;
        text.text = value;
        previous = value;
    }

    private static void PlaceText(TextMeshProUGUI text, float x, float y, float width, float height, float size)
    {
        Position(text.rectTransform, x, y, width, height);
        if (text.fontSize != size) text.fontSize = size;
    }

    private static void PlaceNotice(TextMeshProUGUI text, CtbStripRect notice, CtbStripRect strip)
        => PlaceText(text, notice.Left - strip.Left, notice.Top - strip.Top, notice.Width, notice.Height, 16 * _scale);

    private static RectTransform CreateRect(string name, Transform parent)
    {
        var child = new GameObject(name, Il2CppType.Of<RectTransform>());
        child.layer = parent.gameObject.layer;
        var rect = child.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        return rect;
    }

    private static Image AddImage(RectTransform rect)
    {
        var image = rect.gameObject.AddComponent<Image>();
        image.raycastTarget = false;
        return image;
    }

    private static Image CopyImage(RectTransform rect, Image style, bool normalizeAlpha)
    {
        var image = AddImage(rect);
        image.sprite = style.sprite;
        image.type = style.type;
        image.fillCenter = style.fillCenter;
        image.material = style.material;
        image.pixelsPerUnitMultiplier = style.pixelsPerUnitMultiplier;
        Color color = style.color;
        if (normalizeAlpha) color.a = 1;
        image.color = color;
        return image;
    }

    private static void Position(RectTransform rect, float x, float y, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(x, y);
        rect.sizeDelta = new Vector2(width, height);
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }

    private static void PlaceRoot(CtbStripRect rect, RectTransform parent)
    {
        var bounds = parent.rect;
        Position(_root!, rect.Left - bounds.xMin, rect.Top - bounds.yMax, rect.Width, rect.Height);
    }

    private static void SetVisible(RectTransform? rect, bool visible)
    {
        if (rect != null && rect.gameObject.activeSelf != visible) rect.gameObject.SetActive(visible);
    }

    private static void Hide() => SetVisible(_root, false);

    private static void Unavailable(string reason)
    {
        Hide();
        if (_warned) return;
        _warned = true;
        BattlePreviewPlugin.PreviewLog?.LogWarning("Native CTB display unavailable: " + reason + ". Battle scheduling is unchanged.");
    }

    internal static void ResetEncounter()
    {
        DestroyGeneratedWindow();
        _parent = null;
        _canvas = null;
        _fontTemplate = null;
        _frameTemplate = _backgroundTemplate = null;
        ReservedPanels.Clear();
        PanelFrames.Clear();
        _parentSize = default;
        _battle = _window = IntPtr.Zero;
        _reported = _warned = false;
        _retryAfter = 0;
    }

    private static void DestroyGeneratedWindow()
    {
        var previous = _root;
        _root = null;
        _support = null;
        _supportText = null;
        _preview = null;
        _previewText = null;
        Array.Clear(Cards);
        if (previous == null) return;
        try { previous.gameObject.SetActive(false); UnityEngine.Object.Destroy(previous.gameObject); }
        catch (Exception error) { Trace("Native CTB display cleanup: " + error.Message); }
    }

    private static void Trace(string message) => BattlePreviewPlugin.PreviewLog?.LogInfo("[S1 CTB] " + message);
}
