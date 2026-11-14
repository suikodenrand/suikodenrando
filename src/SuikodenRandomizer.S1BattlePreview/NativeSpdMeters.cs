extern alias GSD1;

using System;
using Il2CppInterop.Runtime;
using UnityEngine;
using UnityEngine.UI;
using Battle = GSD1::BattleBase;
using Manager = GSD1::UIBattleManager;

namespace SuikodenRandomizer.S1BattlePreview;

internal static class NativeSpdMeters
{
    private static readonly Color Border = new(0.04f, 0.05f, 0.08f, 0.92f);
    private static readonly Color Track = new(0.20f, 0.23f, 0.29f, 0.94f);
    private static readonly Color Hero = new(0.25f, 0.80f, 0.93f);
    private static readonly Color Enemy = new(0.92f, 0.42f, 0.29f);
    private static readonly Color Ready = new(1f, 0.82f, 0.35f);
    private static readonly Meter?[] Meters = new Meter[BattlePreviewState.SlotCount];

    private static RectTransform? _root;
    private static GameObject? _canvasObject;
    private static IntPtr _battle;
    private static bool _reported;
    private static double _missingSince = -1;

    internal static bool UsingFallback { get; private set; }

    internal static void Update()
    {
        if (!BattlePreviewPlugin.NativeSpdMetersEnabled
            || global::SystemObject.mode != global::SystemObject.Mode.GSD1)
        {
            if (_battle != IntPtr.Zero) ResetEncounter();
            return;
        }

        try
        {
            var work = Battle.battle_work;
            if (work is null)
            {
                if (_battle != IntPtr.Zero) ResetEncounter();
                return;
            }
            if (_battle != work.Pointer)
            {
                ResetEncounter();
                _battle = work.Pointer;
            }
            if (UsingFallback) return;
            if (!BattlePreviewState.Visible || !BattlePreviewState.RepresentsBattle(_battle)
                || work.battle_phase_no is < 2 or > 4)
            {
                SetVisible(_root, false);
                _missingSince = -1;
                return;
            }

            if (Draw(work, out int heroes, out int enemies, out string unavailable))
            {
                _missingSince = -1;
                if (!_reported && heroes > 0 && enemies > 0)
                {
                    _reported = true;
                    Trace($"Native SPD meters attached to {heroes} heroes and {enemies} enemies.");
                }
                return;
            }

            if (!_reported && work.battle_phase_no == 4)
            {
                double now = Time.realtimeSinceStartupAsDouble;
                if (_missingSince < 0) _missingSince = now;
                if (now - _missingSince >= 1)
                    UseFallback(unavailable);
            }
        }
        catch (Exception error) { UseFallback(error.Message); }
    }

    private static bool Draw(GSD1.BATTLE_WORK work, out int heroes, out int enemies, out string unavailable)
    {
        heroes = enemies = 0;
        unavailable = "the native battle models were unavailable";
        if (work.b_chara is not { } models)
        {
            SetVisible(_root, false);
            return false;
        }
        var worldCamera = Battle.camera;
        var managerCamera = Manager.IsInstantiated ? Manager.Instance.mainCamera : null;
        if (worldCamera == null) worldCamera = managerCamera;
        if (worldCamera == null) worldCamera = Camera.main;
        if (worldCamera == null)
        {
            unavailable = "the battle camera was unavailable";
            SetVisible(_root, false);
            return false;
        }

        var root = GetRoot();
        var bounds = root.rect;
        if (bounds.width <= 0 || bounds.height <= 0)
        {
            unavailable = "the SPD canvas had no drawable area";
            SetVisible(root, false);
            return false;
        }
        float scale = bounds.height / 720f;
        var size = new Vector2(44f * scale, 6f * scale);
        int anchored = 0;
        SetVisible(root, true);
        for (int slot = 1; slot < Meters.Length; slot++)
        {
            var actor = BattlePreviewState.GetCombatant(slot);
            var model = slot <= work.pm_kazu && slot < models.Count && actor.Alive ? models[slot] : null;
            if (model is null)
            {
                SetVisible(Meters[slot]?.Frame, false);
                continue;
            }

            anchored++;
            var anchor = GetAnchor(model);
            if (!TryProject(worldCamera, managerCamera, bounds, anchor, out var local)
                && !TryProject(worldCamera, managerCamera, bounds, model.pos.ToVector3(), out local))
            {
                SetVisible(Meters[slot]?.Frame, false);
                continue;
            }

            var meter = Meters[slot] ??= new Meter(root, slot);
            local.y -= 13f * scale;
            local.x = Mathf.Clamp(local.x, bounds.xMin + size.x / 2, bounds.xMax - size.x / 2);
            local.y = Mathf.Clamp(local.y, bounds.yMin + size.y / 2, bounds.yMax - size.y / 2);
            meter.Show(local, size, actor.PartyMember, (float)BattlePreviewState.GetGauge(slot));
            if (actor.PartyMember) heroes++;
            else enemies++;
        }
        if (heroes + enemies > 0) return true;
        unavailable = anchored == 0 ? "no live actor anchors were available"
            : $"camera '{worldCamera.name}' could not project any of the {anchored} live actor anchors";
        return false;
    }

    private static Vector3 GetAnchor(GSD1.BATTLE_CHARA model)
    {
        var sprite = model.anime_sp?.opd_sprite;
        if (sprite != null && sprite.gameObject.activeInHierarchy)
            return sprite.transform.position;
        var prefab = model.spawnedObj?.spawnedObject;
        if (prefab != null && prefab.activeInHierarchy)
            return prefab.transform.position;
        return model.pos.ToVector3();
    }

    private static bool TryProject(Camera primary, Camera? alternate, Rect bounds, Vector3 position, out Vector2 local)
        => TryProject(primary, bounds, position, out local)
            || (alternate != null && alternate != primary && TryProject(alternate, bounds, position, out local));

    private static bool TryProject(Camera camera, Rect bounds, Vector3 position, out Vector2 local)
    {
        var point = camera.WorldToViewportPoint(position);
        if (!(point.z > 0 && point.x is >= 0 and <= 1 && point.y is >= 0 and <= 1))
        {
            local = default;
            return false;
        }

        // Normalized viewport coordinates also work when the game camera renders to a texture.
        var viewport = camera.rect;
        local = new Vector2(bounds.xMin + bounds.width * (viewport.x + point.x * viewport.width),
            bounds.yMin + bounds.height * (viewport.y + point.y * viewport.height));
        return true;
    }

    private static RectTransform GetRoot()
    {
        if (_root != null) return _root;
        DestroyHud();
        // The stock command and target panels can disable their canvases between turns.
        var canvasObject = new GameObject("S1 ATB Combatant Canvas", Il2CppType.Of<RectTransform>(), Il2CppType.Of<Canvas>());
        _canvasObject = canvasObject;
        var canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        var root = CreateRect("S1 ATB Combatant Meters", canvasObject.transform);
        _root = root;
        Stretch(root, Vector2.zero);
        return root;
    }

    private sealed class Meter
    {
        internal RectTransform Frame { get; }
        private readonly RectTransform _track;
        private readonly Image _fill;
        private Vector2 _size;
        private float _charge = -1;
        private Color _color;

        internal Meter(RectTransform root, int slot)
        {
            Frame = CreateRect("SPD " + slot, root);
            Frame.anchorMin = Frame.anchorMax = new Vector2(0.5f, 0.5f);
            AddImage(Frame, Border);
            _track = CreateRect("Track", Frame);
            AddImage(_track, Track);
            var fill = CreateRect("Charge", _track);
            Stretch(fill, Vector2.zero);
            _fill = AddImage(fill, Color.white);
        }

        internal void Show(Vector2 position, Vector2 size, bool hero, float gauge)
        {
            Frame.anchoredPosition = position;
            if (_size != size)
            {
                _size = size;
                Frame.sizeDelta = size;
                float inset = size.y / 6f;
                Stretch(_track, new Vector2(inset, inset));
            }
            if (_charge != gauge)
            {
                _charge = gauge;
                _fill.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(gauge), 1f);
                _fill.enabled = gauge > 0;
            }
            var color = gauge >= 1 ? Ready : hero ? Hero : Enemy;
            if (_color != color) _fill.color = _color = color;
            SetVisible(Frame, true);
        }
    }

    private static RectTransform CreateRect(string name, Transform parent)
    {
        var child = new GameObject(name, Il2CppType.Of<RectTransform>());
        child.layer = parent.gameObject.layer;
        var rect = child.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        return rect;
    }

    private static void Stretch(RectTransform rect, Vector2 inset)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = inset;
        rect.offsetMax = -inset;
    }

    private static Image AddImage(RectTransform rect, Color color)
    {
        var image = rect.gameObject.AddComponent<Image>();
        image.raycastTarget = false;
        image.color = color;
        return image;
    }

    private static void SetVisible(RectTransform? rect, bool visible)
    {
        if (rect == null) return;
        var obj = rect.gameObject;
        if (obj.activeSelf != visible) obj.SetActive(visible);
    }

    private static void UseFallback(string reason)
    {
        UsingFallback = true;
        DestroyHud();
        Trace("Native SPD meters switched to the preview panel because " + reason + ".");
    }

    private static void DestroyHud()
    {
        var previous = _canvasObject;
        _root = null;
        _canvasObject = null;
        Array.Clear(Meters);
        if (previous == null) return;
        try
        {
            previous.SetActive(false);
            UnityEngine.Object.Destroy(previous);
        }
        catch (Exception error) { Trace("Native SPD meter cleanup failed: " + error.Message); }
    }

    internal static void ResetEncounter()
    {
        DestroyHud();
        _battle = IntPtr.Zero;
        _reported = false;
        _missingSince = -1;
        UsingFallback = false;
    }

    private static void Trace(string message) => BattlePreviewPlugin.PreviewLog?.LogInfo("[S1 ATB] " + message);
}
