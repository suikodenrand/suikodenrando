using System;

namespace SuikodenRandomizer.S1BattlePreview;

// Canvas-local coordinates, independent of Unity so native-HUD collision rules
// can be exercised without loading game assemblies.
internal readonly record struct CtbStripRect(float X, float Y, float Width, float Height)
{
    internal float Left => X;
    internal float Right => X + Width;
    internal float Bottom => Y;
    internal float Top => Y + Height;
    internal float CenterX => X + Width / 2;
    internal bool HasArea => float.IsFinite(X) && float.IsFinite(Y)
        && float.IsFinite(Width) && float.IsFinite(Height) && Width > 0 && Height > 0;
    internal bool Overlaps(CtbStripRect other) => HasArea && other.HasArea
        && Left < other.Right && Right > other.Left && Bottom < other.Top && Top > other.Bottom;
    internal bool Contains(CtbStripRect other) => HasArea && other.HasArea
        && other.Left >= Left && other.Right <= Right && other.Bottom >= Bottom && other.Top <= Top;
    internal CtbStripRect Inflate(float amount) => new(X - amount, Y - amount, Width + 2 * amount, Height + 2 * amount);
}

internal readonly record struct CtbStripPlacement(CtbStripRect Bounds, int CardCount,
    float PortraitSize, float Gap, float Padding, float LabelHeight, float LabelGap);

internal static class NativeCtbStripLayout
{
    private readonly record struct Interval(float Left, float Right);

    internal static bool TryPanelFootprint(CtbStripRect canvas, CtbStripRect candidate,
        bool nativeFrame, out CtbStripRect footprint)
    {
        footprint = default;
        if (!canvas.HasArea || !candidate.HasArea) return false;
        float left = Math.Max(canvas.Left, candidate.Left), right = Math.Min(canvas.Right, candidate.Right);
        float bottom = Math.Max(canvas.Bottom, candidate.Bottom), top = Math.Min(canvas.Top, candidate.Top);
        if (left >= right || bottom >= top) return false;
        var clipped = new CtbStripRect(left, bottom, right - left, top - bottom);
        // A fullscreen controller/container is not its visible window footprint.
        // An actual frame covering the viewport must still block the CTB strip.
        if (!nativeFrame && clipped.Width >= canvas.Width * 0.9f && clipped.Height >= canvas.Height * 0.9f) return false;
        footprint = clipped;
        return true;
    }

    internal static bool TryPlace(CtbStripRect safe, ReadOnlySpan<CtbStripRect> occupied,
        int requestedCards, float nativePadding, float scale, out CtbStripPlacement placement)
    {
        placement = default;
        if (!safe.HasArea || requestedCards <= 0 || !float.IsFinite(scale) || scale <= 0
            || !float.IsFinite(nativePadding) || nativePadding < 0 || occupied.Length > 64) return false;
        int desired = Math.Min(8, requestedCards);
        int minimum = Math.Min(3, desired);
        float portraitMin = 56 * scale;
        float portraitMax = 64 * scale;
        float label = 20 * scale;
        float labelGap = 4 * scale;
        float gap = 6 * scale;
        float margin = 8 * scale;
        float padding = Math.Max(nativePadding, 8 * scale);
        float reservedHeight = 2 * padding + portraitMax + labelGap + label;
        if (safe.Height < reservedHeight + 2 * margin || safe.Width <= 2 * margin) return false;
        float left = safe.Left + margin;
        float right = safe.Right - margin;
        float bottom = safe.Bottom + margin;
        var band = new CtbStripRect(left, bottom, right - left, reservedHeight);
        Span<Interval> intervals = stackalloc Interval[64];
        int count = 0;
        foreach (var nativePanel in occupied)
        {
            var blocked = nativePanel.Inflate(margin);
            if (!band.Overlaps(blocked)) continue;
            float from = Math.Max(left, blocked.Left);
            float to = Math.Min(right, blocked.Right);
            if (from >= to) continue;
            int index = count;
            while (index > 0 && intervals[index - 1].Left > from)
            {
                intervals[index] = intervals[index - 1];
                index--;
            }
            intervals[index] = new Interval(from, to);
            count++;
        }
        float best = float.NegativeInfinity;
        float cursor = left;
        for (int index = 0; index <= count; index++)
        {
            float end = index < count ? intervals[index].Left : right;
            if (end > cursor)
            {
                float available = end - cursor;
                int cards = Math.Min(desired, Math.Max(0, (int)Math.Floor((available - 2 * padding + gap) / (portraitMin + gap))));
                if (cards >= minimum)
                {
                    float portrait = Math.Min(portraitMax, (available - 2 * padding - (cards - 1) * gap) / cards);
                    float width = 2 * padding + cards * portrait + (cards - 1) * gap;
                    float x = Math.Clamp(safe.CenterX - width / 2, cursor, Math.Max(cursor, end - width));
                    float height = 2 * padding + portrait + labelGap + label;
                    var rect = new CtbStripRect(x, bottom, width, height);
                    float score = cards * 100000 + portrait * 100 - Math.Abs(rect.CenterX - safe.CenterX);
                    if (score > best && safe.Contains(rect))
                    {
                        bool overlap = false;
                        foreach (var panel in occupied) overlap |= rect.Overlaps(panel.Inflate(margin));
                        if (!overlap)
                        {
                            best = score;
                            placement = new CtbStripPlacement(rect, cards, portrait, gap, padding, label, labelGap);
                        }
                    }
                }
            }
            if (index < count) cursor = Math.Max(cursor, intervals[index].Right);
        }
        return placement.CardCount > 0;
    }

    internal static bool CanPlaceNotice(CtbStripRect safe, ReadOnlySpan<CtbStripRect> occupied,
        CtbStripRect strip, float scale, out CtbStripRect notice)
    {
        notice = new CtbStripRect(strip.Left, strip.Top + 4 * scale, strip.Width, 20 * scale);
        if (!safe.Contains(notice)) return false;
        foreach (var panel in occupied)
            if (notice.Overlaps(panel.Inflate(4 * scale))) return false;
        return true;
    }
}
