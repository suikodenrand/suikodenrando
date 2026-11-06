using System;
using System.Collections.Generic;
using System.Linq;

namespace SuikodenRandomizer.Core;

/// <summary>
/// Versioned profiles. The optional profile retains its exact prior contract.
/// The experimental profile is the user's complete blank-role list, not a
/// claim that every story role, duel, or ending is supported.
/// </summary>
public static class SeedProfiles
{
    public const string OptionalRecruitsV1 = "optional-recruits-v1";

    public const string AllPlayableExperimentalV1 = "all-playable-experimental-v1";
    private static readonly int[] AllPlayableIds = { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 25, 26, 28, 29, 30, 31, 32, 33, 35, 36, 37, 38, 39, 40, 41, 42, 43, 45, 47, 48, 49, 51, 52, 53, 54, 55, 56, 59, 61, 62, 63, 64, 65, 66, 67, 68, 69, 70, 71, 72, 73, 74, 75, 76, 80, 81, 91, 92, 95, 101, 102, 107, 108, 109 };
    private static readonly HashSet<int> AllPlayableSet = new HashSet<int>(AllPlayableIds);
    public static IReadOnlyList<int> AllPlayableRecruitIds { get; } = Array.AsReadOnly(AllPlayableIds);
    public static IReadOnlyList<int> ExperimentalStarterIds { get; } = Array.AsReadOnly(new[] { 1, 2, 6, 8, 108 });
    public static bool IsAllPlayable(int id) => AllPlayableSet.Contains(id);
    public static bool IsExperimental(SeedManifest? seed) => seed?.Profile == AllPlayableExperimentalV1;
    public static IReadOnlyList<int> GetIds(string profile) => profile == OptionalRecruitsV1
        ? OptionalRecruitIds : profile == AllPlayableExperimentalV1 ? AllPlayableRecruitIds
        : throw new ArgumentException("Unsupported recruitment profile: " + profile, nameof(profile));

    private static readonly int[] OptionalIds =
    {
        47, 51, 52, 53, 54, 55, 56, 59, 61, 64, 65, 66, 68,
        71, 73, 74, 76, 80, 81, 91, 92, 95, 101, 102, 107
    };
    private static readonly HashSet<int> OptionalSet = new HashSet<int>(OptionalIds);

    public static IReadOnlyList<int> OptionalRecruitIds { get; } = Array.AsReadOnly(OptionalIds);
    public static IReadOnlyList<int> ProtectedCombatIds { get; } = Array.AsReadOnly(
        CombatCatalog.NormalCombatIds.Where(id => !OptionalSet.Contains(id)).ToArray());

    public static bool IsOptionalRecruit(int characterId) => OptionalSet.Contains(characterId);
}
