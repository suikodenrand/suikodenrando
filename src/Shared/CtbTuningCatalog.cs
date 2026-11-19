using System;
using System.Collections.Generic;
using System.Linq;

namespace SuikodenRandomizer.Shared;

internal sealed record CtbTuningStar(int Id, int StarNumber, string Name, int? BaseSpd);
internal sealed record CtbTuningEnemy(string SourceId, string Name, int Level, int BaseSpd, int Hp);

internal static partial class CtbTuningCatalog
{
    internal static IReadOnlyList<CtbTuningStar> Stars { get; } = Array.AsReadOnly(CreateStars());
    internal static IReadOnlyList<CtbTuningEnemy> Enemies { get; } = Array.AsReadOnly(CreateEnemies());

    private static readonly HashSet<int> CombatStars =
        Stars.Where(star => star.BaseSpd.HasValue).Select(star => star.Id).ToHashSet();
    private static readonly HashSet<string> EnemyIds =
        Enemies.Select(enemy => enemy.SourceId).ToHashSet(StringComparer.Ordinal);

    internal static bool HasCombatStar(int id) => CombatStars.Contains(id);
    internal static bool HasEnemy(string id) => EnemyIds.Contains(id);
}
