using System;

namespace SuikodenRandomizer.S1BattlePreview;

internal static class NativeEnemyBadgeLayout
{
    internal static bool TryPlace(CtbStripRect canvas, CtbStripRect projectedSprite,
        float badgeWidth, float badgeHeight, float gap, out CtbStripRect badge)
    {
        badge = default;
        if (!canvas.Overlaps(projectedSprite) || !float.IsFinite(badgeWidth) || badgeWidth <= 0
            || !float.IsFinite(badgeHeight) || badgeHeight <= 0 || !float.IsFinite(gap) || gap < 0
            || badgeWidth > canvas.Width) return false;

        float x = Math.Clamp(projectedSprite.CenterX - badgeWidth / 2, canvas.Left, canvas.Right - badgeWidth);
        var above = new CtbStripRect(x, projectedSprite.Top + gap, badgeWidth, badgeHeight);
        if (!canvas.Contains(above)) return false;
        badge = above;
        return true;
    }
}
