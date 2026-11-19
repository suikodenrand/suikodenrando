extern alias GSD1;

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using SuikodenRandomizer.Shared;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using SuikodenRandomizer.Core.Combat;
using UnityEngine;
using UnityEngine.UI;

namespace SuikodenRandomizer.S1BattlePreview;

internal static class CtbVisualAssets
{
    private static CtbPortraitIndex? _index;
    private static readonly Dictionary<string, Sprite> Loaded = new(StringComparer.Ordinal);
    private static readonly Dictionary<IntPtr, Sprite> NativeEnemyFrames = new();
    private static readonly List<Texture2D> Textures = new();
    private static readonly HashSet<string> Missing = new(StringComparer.Ordinal);
    private static readonly Dictionary<CtbEnemyShape, Sprite> Shapes = new();
    private static readonly List<Texture2D> ShapeTextures = new();
    private static bool _readIndex;
    private static string _assetRoot = "";

    internal static Color BadgeColor(CtbEnemyBadge badge) => new(badge.Red / 255f, badge.Green / 255f, badge.Blue / 255f, 1);

    internal static Sprite? GetHeroSprite(int slot)
    {
        var work = GSD1.BattleBase.battle_work;
        var window = GSD1.BattleBase.window_work;
        if (work is null || slot < 1 || slot > work.player_kazu || window?.player_window_data is not { } map
            || !GSD1.UIBattleManager.IsInstantiated) return null;
        var portraits = GSD1.UIBattleManager.Instance.battleWindow?.m_party?.m_playrStatusList;
        if (portraits is null) return null;
        for (int index = 0; index < work.player_kazu && index < map.Count && index < portraits.Count; index++)
            if (map[index] == slot && portraits[index]?.m_face is { } face)
                return face.overrideSprite != null ? face.overrideSprite : face.sprite;
        return null;
    }

    internal static Sprite? GetEnemySprite(CtbTurnView view)
    {
        ReadIndex();
        if (_index is not null && GetExtractedSprite(view.EnemySourceId) is { } extracted) return extracted;

        // Borrow a complete frame from the game's loaded battle atlas. Multipart or
        // prefab enemies keep the generated badge rather than showing a detached limb.
        var work = GSD1.BattleBase.battle_work;
        if (work?.b_chara is not { } models || work.pm_data is not { } actors || view.Slot <= work.player_kazu
            || view.Slot >= actors.Count || view.Slot >= models.Count || actors[view.Slot]?.Pointer != view.Identity
            || models[view.Slot]?.anime_sp?.opd_data is not { } data) return null;
        if (NativeEnemyFrames.TryGetValue(data.Pointer, out var cached) && cached != null) return cached;
        var first = data.anime_table;
        if (first == null || first.Count == 0 || first[0]?.timer_table is not { } timers || timers.Count == 0
            || timers[0] is not { } frame || data.pattern_table is not { } patterns
            || (uint)frame.pattern_no >= (uint)patterns.Count || patterns[frame.pattern_no]?.obj_table is not { } parts
            || parts.Count != 1 || parts[0] is not { } part || string.IsNullOrEmpty(part.sprite_name)
            || data.sprites is not { } sprites || !sprites.TryGetValue(part.sprite_name, out var sprite)
            || sprite == null) return null;
        NativeEnemyFrames[data.Pointer] = sprite;
        return sprite;
    }

    private static Sprite? GetExtractedSprite(string sourceIdOrOpdName)
    {
        if (string.IsNullOrEmpty(sourceIdOrOpdName)) return null;
        if (!_index!.Aliases.TryGetValue(sourceIdOrOpdName, out var asset)) return null;
        if (Loaded.TryGetValue(asset.File, out var cached) && cached != null) return cached;
        if (Missing.Contains(asset.File)) return null;
        Texture2D? texture = null;
        try
        {
            string path = Path.GetFullPath(Path.Combine(_assetRoot, asset.File));
            if (!path.StartsWith(_assetRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Thumbnail path escaped the asset directory.");
            byte[] bytes = File.ReadAllBytes(path);
            using var sha = SHA256.Create();
            if (!Convert.ToHexString(sha.ComputeHash(bytes)).Equals(asset.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Thumbnail checksum mismatch.");
            texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!ImageConversion.LoadImage(texture, bytes, false) || texture.width != 96 || texture.height != 96)
                throw new InvalidDataException("Expected a 96x96 thumbnail.");
            texture.name = "CTB enemy " + sourceIdOrOpdName;
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;
            var sprite = Sprite.Create(texture, new Rect(0, 0, 96, 96), new Vector2(0.5f, 0.5f), 96);
            sprite.name = texture.name;
            Loaded[asset.File] = sprite;
            Textures.Add(texture);
            return sprite;
        }
        catch (Exception error)
        {
            if (texture != null) UnityEngine.Object.Destroy(texture);
            WarnOnce(asset.File, "Enemy thumbnail unavailable: " + error.Message);
            return null;
        }
    }

    private static void ReadIndex()
    {
        if (_readIndex) return;
        _readIndex = true;
        try
        {
            _assetRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(typeof(CtbVisualAssets).Assembly.Location)!, "CTBAssets", "Enemies"));
            if (File.Exists(Path.Combine(_assetRoot, "index.json"))) _index = CtbPortraitIndex.Read(_assetRoot);
        }
        catch (Exception error) { WarnOnce("index", "CTB thumbnail index unavailable: " + error.Message); }
    }

    internal static Sprite GetBadgeSprite(CtbEnemyShape shape)
    {
        if (Shapes.TryGetValue(shape, out var cached) && cached != null) return cached;
        const int size = 32;
        var pixels = new Il2CppStructArray<Color32>(size * size);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                double dx = (x + 0.5 - size / 2.0) / 13, dy = (y + 0.5 - size / 2.0) / 13;
                bool inside = shape switch
                {
                    CtbEnemyShape.Circle => dx * dx + dy * dy <= 1,
                    CtbEnemyShape.Triangle => dy >= -0.85 && dy <= 1 && Math.Abs(dx) <= (1 - dy) * 0.54,
                    CtbEnemyShape.Square => Math.Abs(dx) <= 0.9 && Math.Abs(dy) <= 0.9,
                    CtbEnemyShape.Diamond => Math.Abs(dx) + Math.Abs(dy) <= 1.15,
                    CtbEnemyShape.Cross => Math.Abs(dx) <= 1 && Math.Abs(dy) <= 1 && (Math.Abs(dx) < 0.30 || Math.Abs(dy) < 0.30),
                    CtbEnemyShape.Star => InStar(dx, dy),
                    _ => false
                };
                pixels[y * size + x] = new Color32(255, 255, 255, inside ? (byte)255 : (byte)0);
            }
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        texture.name = "CTB badge " + shape;
        texture.filterMode = FilterMode.Bilinear;
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.SetPixels32(pixels);
        texture.Apply(false, false);
        var sprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        Shapes[shape] = sprite;
        ShapeTextures.Add(texture);
        return sprite;
    }

    private static bool InStar(double x, double y)
    {
        bool inside = false;
        double previousX = 0, previousY = 1;
        for (int index = 1; index <= 10; index++)
        {
            double angle = Math.PI / 2 + index * Math.PI / 5;
            double radius = index % 2 == 0 ? 1 : 0.44;
            double nextX = Math.Cos(angle) * radius, nextY = Math.Sin(angle) * radius;
            if ((previousY > y) != (nextY > y)
                && x < (nextX - previousX) * (y - previousY) / (nextY - previousY) + previousX) inside = !inside;
            previousX = nextX;
            previousY = nextY;
        }
        return inside;
    }

    internal static void ResetEncounter()
    {
        foreach (var sprite in Loaded.Values) if (sprite != null) UnityEngine.Object.Destroy(sprite);
        foreach (var texture in Textures) if (texture != null) UnityEngine.Object.Destroy(texture);
        Loaded.Clear();
        NativeEnemyFrames.Clear(); // Borrowed sprites are owned by the game.
        Textures.Clear();
        // The six small badge textures are shared by later encounters.
    }

    private static void WarnOnce(string key, string message)
    {
        if (Missing.Add(key)) BattlePreviewPlugin.PreviewLog?.LogWarning("[S1 CTB] " + message);
    }
}
