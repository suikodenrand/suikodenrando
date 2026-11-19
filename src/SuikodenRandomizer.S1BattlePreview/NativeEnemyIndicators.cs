extern alias GSD1;

using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Battle = GSD1::BattleBase;
using Manager = GSD1::UIBattleManager;

namespace SuikodenRandomizer.S1BattlePreview;

/// <summary>Passive identity badges on the existing battle canvas and enemy list.</summary>
internal static class NativeEnemyIndicators
{
    private static readonly Dictionary<(long Encounter, int Id), IndicatorPair> Indicators = new();
    private static RectTransform? _worldRoot;
    private static RectTransform? _listRoot;
    private static RectTransform? _parent;
    private static Canvas? _canvas;
    private static IntPtr _battle;
    private static IntPtr _window;
    private static bool _warned;
    private static Il2CppSystem.Collections.Generic.List<Renderer>? _bodyRenderers;

    internal static void Update()
    {
        if (!BattlePreviewPlugin.CtbEnabled || !CtbPresentation.Visible
            || global::SystemObject.mode != global::SystemObject.Mode.GSD1)
        {
            Hide();
            return;
        }
        try
        {
            var work = Battle.battle_work;
            var canvas = NativeCtbWindow.NativeCanvas;
            var parent = NativeCtbWindow.NativeParent;
            var font = NativeCtbWindow.NativeFontTemplate;
            var frame = NativeCtbWindow.NativeFrameTemplate;
            var background = NativeCtbWindow.NativeBackgroundTemplate;
            var window = Manager.IsInstantiated ? Manager.Instance.battleWindow : null;
            if (work is null || work.pm_data is not { } actors || work.b_chara is not { } models
                || window == null
                || canvas == null || parent == null || font == null || frame == null || background == null)
            {
                Hide();
                return;
            }
            if (_battle != work.Pointer || _window != window.Pointer || _parent == null
                || _parent.Pointer != parent.Pointer || _canvas == null || _canvas.Pointer != canvas.Pointer)
            {
                ResetEncounter();
                _battle = work.Pointer;
                _window = window.Pointer;
                _parent = parent;
                _canvas = canvas;
            }
            if (!parent.gameObject.activeInHierarchy || !window.gameObject.activeInHierarchy || !canvas.enabled)
            {
                Hide();
                return;
            }
            EnsureRoots(parent);
            SetVisible(_worldRoot, true);
            SetVisible(_listRoot, true);
            foreach (var pair in Indicators.Values) pair.Hide();

            Camera? camera = Battle.camera;
            if (camera == null) camera = Manager.Instance.mainCamera;
            if (camera == null) camera = Camera.main;
            var monsterWindow = window.m_monster;
            bool listVisible = monsterWindow != null && monsterWindow.gameObject.activeInHierarchy
                && (monsterWindow.IsOpen || monsterWindow.IsOpening) && !monsterWindow.IsClosing;
            var nativeWindow = Battle.window_work;
            var rowOwners = ReadListRowOwners(work, nativeWindow);
            float symbolSize = Math.Clamp(font.fontSize * 0.65f, 14, 32);
            float listSize = Math.Clamp(font.fontSize * 0.52f, 12, 27);
            var safe = NativeCtbWindow.SafeBounds(parent, canvas, Manager.Instance.uiCamera);

            foreach (var view in CtbPresentation.Enemies)
            {
                int slot = view.Slot;
                if (slot <= work.player_kazu || slot > work.pm_kazu || slot >= actors.Count || slot >= models.Count
                    || view.Identity == IntPtr.Zero || view.Badge.Slot != slot
                    || view.Badge.NativeIdentity != view.Identity.ToInt64()) continue;
                var actor = actors[slot];
                var model = models[slot];
                if (actor is null || actor.Pointer != view.Identity || actor.hp == 0 || actor.dead_flg != 0
                    || Battle.dead_check(slot) || model is null) continue;
                var key = (view.Badge.Encounter, view.Badge.Id);
                if (!Indicators.TryGetValue(key, out var pair))
                {
                    pair = new IndicatorPair(_worldRoot!, _listRoot!, font, frame, background);
                    Indicators.Add(key, pair);
                }

                if (camera != null && TryGetSpriteBounds(model, camera, canvas, parent, out var body))
                    pair.World.ShowAbove(view, symbolSize, body, safe);

                if (!listVisible || monsterWindow!.m_monsterListGS1 is not { } cells) continue;
                int row = FindRow(rowOwners, slot);
                if (row < 0 || row >= cells.Count || cells[row] is not { } cell
                    || !cell.gameObject.activeInHierarchy || cell.rectTransform is not { } rowRect) continue;
                RectTransform textRect = cell.leftText != null ? cell.leftText.rectTransform : rowRect;
                Vector3 edge = textRect.TransformPoint(new Vector3(textRect.rect.xMin, textRect.rect.center.y, 0));
                Vector3 point = parent.InverseTransformPoint(edge);
                pair.List.ShowBeforeText(view, listSize, new Vector2(point.x, point.y), parent.rect);
            }
        }
        catch (Exception error)
        {
            Hide();
            if (_warned) return;
            _warned = true;
            BattlePreviewPlugin.PreviewLog?.LogWarning("Native enemy indicators paused: " + error.Message);
        }
    }

    private static int[] ReadListRowOwners(GSD1.BATTLE_WORK work, Battle.WINDOW_WORK? window)
    {
        var owners = new int[6];
        if (window?.monster_window_data is not { } slots || window.monster_position_data is not { } positions
            || work.pm_data is not { } actors) return owners;
        int count = Math.Min(ActiveBattleState.CtbEnemyListCount(work, window), Math.Min(slots.Count, positions.Count));
        for (int index = 0; index < count; index++)
        {
            int slot = slots[index];
            int row = positions[index] - 1;
            if (row < 0 || row >= owners.Length || slot <= work.player_kazu || slot > work.pm_kazu
                || slot >= actors.Count || actors[slot] is not { hp: > 0, dead_flg: 0 } actor
                || actor.kougeki_position != row + 1) continue;
            // Native MakeMonsterWindow writes by formation position, not list index.
            if (owners[row] == 0 || owners[row] == slot) owners[row] = slot;
            else owners[row] = -1; // An ambiguous native row must not display another actor's badge.
        }
        return owners;
    }

    private static int FindRow(int[] owners, int slot)
    {
        int found = -1;
        for (int row = 0; row < owners.Length; row++)
            if (owners[row] == slot)
            {
                if (found >= 0) return -1;
                found = row;
            }
        return found;
    }

    private static bool TryGetSpriteBounds(GSD1.BATTLE_CHARA model, Camera camera, Canvas canvas,
        RectTransform parent, out CtbStripRect body)
    {
        body = default;
        if (!model.disp_flg || !model.be_flg) return false;
        Vector2 min = new(float.PositiveInfinity, float.PositiveInfinity);
        Vector2 max = new(float.NegativeInfinity, float.NegativeInfinity);
        var sprite = model.anime_sp?.opd_sprite;
        if (sprite != null && sprite.gameObject.activeInHierarchy)
        {
            // Classic OPD enemies can draw several independently transformed mesh pieces.
            var parts = sprite.objList;
            if (parts != null)
                for (int i = 0; i < parts.Count; i++)
                {
                    var part = parts[i];
                    if (part != null) IncludeRenderer(part._meshRenderer, camera, ref min, ref max);
                }
            if (min.x > max.x) IncludeBodyRenderers(sprite.gameObject, camera, ref min, ref max);
        }
        var prefab = model.spawnedObj?.spawnedObject;
        if (prefab != null && prefab.activeInHierarchy)
            IncludeBodyRenderers(prefab, camera, ref min, ref max);
        if (min.x >= max.x || min.y >= max.y || max.x <= 0 || min.x >= 1 || max.y <= 0 || min.y >= 1
            || !TryViewportToCanvas(camera, canvas, parent, min, out var lower)
            || !TryViewportToCanvas(camera, canvas, parent, max, out var upper)) return false;
        body = new CtbStripRect(lower.x, lower.y, upper.x - lower.x, upper.y - lower.y);
        return body.HasArea;
    }

    private static void IncludeBodyRenderers(GameObject root, Camera camera, ref Vector2 min, ref Vector2 max)
    {
        _bodyRenderers ??= new Il2CppSystem.Collections.Generic.List<Renderer>();
        root.GetComponentsInChildren<Renderer>(false, _bodyRenderers);
        for (int i = 0; i < _bodyRenderers.Count; i++)
        {
            var renderer = _bodyRenderers[i];
            if (renderer != null && (renderer.TryCast<MeshRenderer>() != null
                || renderer.TryCast<SkinnedMeshRenderer>() != null || renderer.TryCast<SpriteRenderer>() != null))
                IncludeRenderer(renderer, camera, ref min, ref max);
        }
        _bodyRenderers.Clear();
    }

    private static void IncludeRenderer(Renderer? renderer, Camera camera, ref Vector2 min, ref Vector2 max)
    {
        if (renderer == null || !renderer.enabled || renderer.forceRenderingOff
            || renderer.shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly) return;
        if (!renderer.gameObject.activeInHierarchy) return;
        var bounds = renderer.localBounds;
        var lo = bounds.min;
        var hi = bounds.max;
        if (lo == hi) return;
        var transform = renderer.localToWorldMatrix;
        // Project the actual oriented bounds before combining parts, including corners outside the viewport.
        // A flat sprite has only four distinct corners.
        for (int corner = 0; corner < (lo.z == hi.z ? 4 : 8); corner++)
        {
            var vertex = new Vector3((corner & 1) == 0 ? lo.x : hi.x,
                (corner & 2) == 0 ? lo.y : hi.y, (corner & 4) == 0 ? lo.z : hi.z);
            var point = camera.WorldToViewportPoint(transform.MultiplyPoint3x4(vertex));
            if (!(point.z > 0) || !float.IsFinite(point.x) || !float.IsFinite(point.y)) continue;
            min.x = Math.Min(min.x, point.x);
            min.y = Math.Min(min.y, point.y);
            max.x = Math.Max(max.x, point.x);
            max.y = Math.Max(max.y, point.y);
        }
    }

    private static bool TryViewportToCanvas(Camera worldCamera, Canvas canvas, RectTransform parent,
        Vector2 viewport, out Vector2 local)
    {
        local = default;
        Camera? uiCamera = canvas.worldCamera;
        if (uiCamera == null && Manager.IsInstantiated) uiCamera = Manager.Instance.uiCamera;
        if (worldCamera.targetTexture != null)
        {
            // The world may render into a low-resolution texture. Map its viewport
            // into the existing UI camera plane rather than treating texture pixels as screen pixels.
            Rect cameraRect = worldCamera.rect;
            float x = cameraRect.x + viewport.x * cameraRect.width;
            float y = cameraRect.y + viewport.y * cameraRect.height;
            if (canvas.renderMode == RenderMode.ScreenSpaceOverlay)
            {
                Rect bounds = parent.rect;
                local = new Vector2(bounds.xMin + x * bounds.width, bounds.yMin + y * bounds.height);
            }
            else if (uiCamera != null)
            {
                float depth = uiCamera.WorldToViewportPoint(parent.position).z;
                if (depth <= 0) return false;
                Vector3 world = uiCamera.ViewportToWorldPoint(new Vector3(x, y, depth));
                Vector3 relative = parent.InverseTransformPoint(world);
                local = new Vector2(relative.x, relative.y);
            }
            else return false;
            return float.IsFinite(local.x) && float.IsFinite(local.y);
        }
        Vector3 screen = worldCamera.ViewportToScreenPoint(new Vector3(viewport.x, viewport.y, 0));
        return RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, new Vector2(screen.x, screen.y),
            canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : uiCamera, out local)
            && float.IsFinite(local.x) && float.IsFinite(local.y);
    }

    private static void EnsureRoots(RectTransform parent)
    {
        if (_worldRoot == null)
        {
            _worldRoot = CreateRect("CTB enemy identities in battle", parent);
            Stretch(_worldRoot, parent.pivot);
            // Stock windows and the timeline occlude world badges naturally.
            _worldRoot.SetAsFirstSibling();
        }
        if (_listRoot == null)
        {
            _listRoot = CreateRect("CTB enemy identities in target list", parent);
            Stretch(_listRoot, parent.pivot);
            _listRoot.SetAsLastSibling();
        }
    }

    internal static void ResetEncounter()
    {
        Destroy(_worldRoot);
        Destroy(_listRoot);
        _worldRoot = null;
        _listRoot = null;
        _parent = null;
        _canvas = null;
        _battle = IntPtr.Zero;
        _window = IntPtr.Zero;
        _warned = false;
        _bodyRenderers?.Clear();
        Indicators.Clear();
    }

    private static void Hide()
    {
        SetVisible(_worldRoot, false);
        SetVisible(_listRoot, false);
    }

    private sealed class IndicatorPair
    {
        internal IndicatorPair(RectTransform worldRoot, RectTransform listRoot, TextMeshProUGUI font,
            Image frame, Image background)
        {
            World = new Indicator("World badge", worldRoot, font, frame, background);
            List = new Indicator("List badge", listRoot, font, frame, background);
        }
        internal Indicator World { get; }
        internal Indicator List { get; }
        internal void Hide() { World.Hide(); List.Hide(); }
    }

    private sealed class Indicator
    {
        private readonly RectTransform _rect;
        private readonly RectTransform _haloRect;
        private readonly RectTransform _symbolRect;
        private readonly Image _halo;
        private readonly Image _symbol;
        private readonly TextMeshProUGUI _number;

        internal Indicator(string name, RectTransform parent, TextMeshProUGUI font, Image frame, Image background)
        {
            _rect = CreateRect(name, parent);
            _rect.anchorMin = _rect.anchorMax = parent.pivot;
            _rect.pivot = new Vector2(0.5f, 0.5f);
            var back = _rect.gameObject.AddComponent<Image>();
            back.raycastTarget = false;
            back.sprite = background.sprite;
            back.type = background.type;
            back.color = background.color;
            back.material = background.material;
            _haloRect = CreateRect("Selected target outline", _rect);
            _halo = _haloRect.gameObject.AddComponent<Image>();
            _halo.raycastTarget = false;
            _halo.preserveAspect = true;
            Color bright = frame.color;
            bright.a = 1;
            _halo.color = bright;
            _symbolRect = CreateRect("Enemy colour and shape", _rect);
            _symbol = _symbolRect.gameObject.AddComponent<Image>();
            _symbol.raycastTarget = false;
            _symbol.preserveAspect = true;
            var labelRect = CreateRect("Enemy identity number", _rect);
            _number = labelRect.gameObject.AddComponent<TextMeshProUGUI>();
            _number.raycastTarget = false;
            _number.font = font.font;
            _number.fontSharedMaterial = font.fontSharedMaterial;
            _number.fontStyle = font.fontStyle;
            _number.fontWeight = font.fontWeight;
            _number.color = Color.white;
            _number.enableWordWrapping = false;
            _number.overflowMode = TextOverflowModes.Truncate;
            _number.alignment = TextAlignmentOptions.MidlineLeft;
            _number.richText = false;
            _number.margin = Vector4.zero;
            Hide();
        }

        private static Vector2 Measure(float size, string number)
        {
            float labelWidth = size * 0.68f * (0.75f * number.Length + 0.4f);
            return new Vector2(size + labelWidth + 5, size + 6);
        }

        internal void ShowAbove(CtbEnemyView view, float size, CtbStripRect body, CtbStripRect safe)
        {
            string number = view.Badge.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var dimensions = Measure(size, number);
            if (NativeEnemyBadgeLayout.TryPlace(safe, body, dimensions.x, dimensions.y,
                Math.Max(4, size * 0.25f), out var badge))
                Show(view, new Vector2(badge.CenterX, badge.Bottom + badge.Height / 2), size, dimensions, number);
        }

        internal void ShowBeforeText(CtbEnemyView view, float size, Vector2 edge, Rect bounds)
        {
            string number = view.Badge.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var dimensions = Measure(size, number);
            var half = dimensions * 0.5f;
            var point = new Vector2(Mathf.Clamp(edge.x - half.x - 4, bounds.xMin + half.x + 2, bounds.xMax - half.x - 2),
                Mathf.Clamp(edge.y, bounds.yMin + half.y + 2, bounds.yMax - half.y - 2));
            Show(view, point, size, dimensions, number);
        }

        private void Show(CtbEnemyView view, Vector2 point, float size, Vector2 dimensions, string number)
        {
            float width = dimensions.x, height = dimensions.y;
            _rect.anchoredPosition = point;
            _rect.sizeDelta = dimensions;
            var symbol = CtbVisualAssets.GetBadgeSprite(view.Badge.Shape);
            _symbol.sprite = symbol;
            _halo.sprite = symbol;
            _symbol.color = CtbVisualAssets.BadgeColor(view.Badge);
            Place(_symbolRect, new Vector2(-width * 0.5f + size * 0.5f + 3, 0), new Vector2(size, size));
            Place(_haloRect, _symbolRect.anchoredPosition, new Vector2(size + 5, size + 5));
            SetVisible(_haloRect, view.Targeted);
            Place(_number.rectTransform, new Vector2(size * 0.5f + 2, 0), new Vector2(width - size - 5, height));
            _number.fontSize = size * 0.68f;
            if (_number.text != number) _number.text = number;
            SetVisible(_rect, true);
        }

        internal void Hide() => SetVisible(_rect, false);
    }

    private static RectTransform CreateRect(string name, Transform parent)
    {
        var child = new GameObject(name, Il2CppType.Of<RectTransform>());
        child.layer = parent.gameObject.layer;
        var rect = child.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.localScale = Vector3.one;
        return rect;
    }

    private static void Stretch(RectTransform rect, Vector2 pivot)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        rect.pivot = pivot;
    }

    private static void Place(RectTransform rect, Vector2 position, Vector2 size)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    private static void SetVisible(RectTransform? rect, bool visible)
    {
        if (rect != null && rect.gameObject.activeSelf != visible) rect.gameObject.SetActive(visible);
    }

    private static void Destroy(RectTransform? rect)
    {
        if (rect != null) UnityEngine.Object.Destroy(rect.gameObject);
    }
}
