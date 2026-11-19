using System;
using SuikodenRandomizer.Core.Combat;

namespace SuikodenRandomizer.S1BattlePreview;

internal readonly record struct CtbTurnView(int Slot, IntPtr Identity, string Name, bool IsHero, bool Current,
    string EnemySourceId, CtbEnemyBadge? Badge, float BreakFraction, bool IsBroken, bool Targeted,
    bool ConditionalReturn = false);

internal readonly record struct CtbEnemyView(int Slot, IntPtr Identity, string Name, string EnemySourceId,
    CtbEnemyBadge Badge, float BreakFraction, bool IsBroken, bool Targeted);

// Read-only presentation data for the game's battle canvas. Commands are accepted
// exclusively by the native battle menu, never by the timeline window.
internal static class CtbPresentation
{
    internal static bool Visible { get; set; }
    internal static string Support { get; set; } = "";
    internal static string PreviewLabel { get; set; } = "";
    internal static CtbTurnView[] Turns { get; set; } = Array.Empty<CtbTurnView>();
    internal static CtbEnemyView[] Enemies { get; set; } = Array.Empty<CtbEnemyView>();
}
