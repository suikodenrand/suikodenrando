using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace SuikodenRandomizer.Core.Suikoden2;

/// <summary>Game-isolated deterministic S2 profiles. Story bindings remain runtime state.</summary>
public sealed class Gsd2AvatarProfile
{
    public const int CurrentVersion = 1;
    public const int MaximumSeedLength = 128;
    public const string CurrentProfileId = "gsd2-combat-avatar-shuffle-v1";
    public const string IncludeProtagonistProfileId = "gsd2-combat-avatar-shuffle-with-protagonist-v1";
    public const string FixedProfileId = "gsd2-fixed-roster-v1";
    public const string AlgorithmId = "sha256-splitmix64-footprint-fisher-yates-v1";
    public const string StartPolicyId = "gsd2-canonical-role-donor-start-once-v1";
    public const string ProgressionPolicyId = "gsd2-canonical-role-save-bound-v1";
    private readonly IReadOnlyDictionary<int, int> _assignments;

    private Gsd2AvatarProfile(string seed, string mode, bool includesProtagonist, int[] roles, int[] donors, int[] companions)
    {
        Seed = seed; Mode = mode; IncludesProtagonist = includesProtagonist;
        RoleIds = Array.AsReadOnly(roles);
        CompanionIds = Array.AsReadOnly(companions);
        HeroIds = Array.AsReadOnly(mode == "Shuffle" ? Array.Empty<int>() : new[] { Gsd2CharacterCatalog.ProtagonistId }.Concat(companions).ToArray());
        var assignments = new Dictionary<int, int>();
        for (int index = 0; index < roles.Length; index++) assignments.Add(roles[index], donors[index]);
        _assignments = new ReadOnlyDictionary<int, int>(assignments);
        using var sha = SHA256.Create();
        if (mode != "Shuffle")
        {
            string identity = FixedProfileId + "\n" + string.Join(",", HeroIds.Select(id => id.ToString(CultureInfo.InvariantCulture)));
            Fingerprint = Hex(sha.ComputeHash(Encoding.UTF8.GetBytes(identity)));
            return;
        }
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
        {
            writer.Write(ProfileId); writer.Write(CurrentVersion); writer.Write(AlgorithmId);
            writer.Write(Seed); writer.Write(StartPolicy); writer.Write(ProgressionPolicy);
            writer.Write(roles.Length);
            foreach (int role in roles) { writer.Write(role); writer.Write(assignments[role]); }
        }
        Fingerprint = Hex(sha.ComputeHash(stream.ToArray()));
    }

    public string GameId => Gsd2CharacterCatalog.GameId;
    public string Seed { get; }
    public string Mode { get; }
    public bool IncludesProtagonist { get; }
    public string ProfileId => Mode == "Shuffle" ? IncludesProtagonist ? IncludeProtagonistProfileId : CurrentProfileId : FixedProfileId;
    public string Fingerprint { get; }
    public string ShuffleAlgorithm => AlgorithmId;
    public string StartPolicy => StartPolicyId;
    public string ProgressionPolicy => ProgressionPolicyId;
    public IReadOnlyList<int> RoleIds { get; }
    public IReadOnlyList<int> CompanionIds { get; }
    public IReadOnlyList<int> HeroIds { get; }
    public IReadOnlyDictionary<int, int> Assignments => _assignments;
    public bool IsRole(int id) => Mode == "Shuffle" ? _assignments.ContainsKey(id) : Gsd2CharacterCatalog.IsStoryRole(id);
    public bool KeepsCanonicalLoadout(int roleId) => roleId == Gsd2CharacterCatalog.ProtagonistId
        || (Mode == "Shuffle" && GetDonor(roleId) == Gsd2CharacterCatalog.ProtagonistId);

    /// <summary>Fixed rosters require saved runtime role bindings; they are not a static permutation.</summary>
    public int GetDonor(int roleId)
    {
        if (roleId < 0) throw new ArgumentOutOfRangeException(nameof(roleId));
        if (Mode != "Shuffle") throw new InvalidOperationException("Fixed rosters resolve roles through saved runtime bindings.");
        return _assignments.TryGetValue(roleId, out int donor) ? donor : roleId;
    }

    public static Gsd2AvatarProfile Create(string seed, bool includeProtagonist = false)
    {
        string normalized = NormalizeSeed(seed);
        int[] roles = Gsd2CharacterCatalog.NormalCombatIds.Concat(includeProtagonist ? new[] { Gsd2CharacterCatalog.ProtagonistId } : Array.Empty<int>()).OrderBy(id => id).ToArray();
        var random = new SeededShuffle(AlgorithmId, normalized);
        var donorsByRole = new Dictionary<int, int>();
        // Preserve native two-cell formations by shuffling each footprint class independently.
        foreach (var group in roles.GroupBy(Gsd2CharacterCatalog.GetSlotCost).OrderBy(group => group.Key))
        {
            int[] groupRoles = group.ToArray(), donors = groupRoles.ToArray();
            random.Shuffle(donors);
            for (int index = 0; index < groupRoles.Length; index++) donorsByRole.Add(groupRoles[index], donors[index]);
        }
        return new Gsd2AvatarProfile(normalized, "Shuffle", includeProtagonist, roles, roles.Select(id => donorsByRole[id]).ToArray(), Array.Empty<int>());
    }

    public static Gsd2AvatarProfile CreateChosen(string seed, IEnumerable<int> companionIds)
    {
        string normalized = NormalizeSeed(seed);
        if (companionIds is null) throw new ArgumentNullException(nameof(companionIds));
        int[] companions = companionIds.Take(Gsd2CharacterCatalog.CompanionCount + 1).ToArray();
        if (companions.Length != Gsd2CharacterCatalog.CompanionCount || companions.Distinct().Count() != companions.Length
            || companions.Any(id => !Gsd2CharacterCatalog.IsFixedCompanion(id)))
            throw new ArgumentException("Choose five distinct single-slot Suikoden II companions. The protagonist is reserved; two-slot units are Shuffle-only.", nameof(companionIds));
        return new Gsd2AvatarProfile(normalized, "FixedChosen", false, Array.Empty<int>(), Array.Empty<int>(), companions);
    }

    public static Gsd2AvatarProfile CreateRandom(string seed)
    {
        var shuffle = Create(seed);
        int[] companions = shuffle.RoleIds.Where(Gsd2CharacterCatalog.IsFixedCompanion).Take(Gsd2CharacterCatalog.CompanionCount).Select(shuffle.GetDonor).ToArray();
        return new Gsd2AvatarProfile(shuffle.Seed, "FixedRandom", false, Array.Empty<int>(), Array.Empty<int>(), companions);
    }

    public static string NormalizeSeed(string seed)
    {
        if (seed is null) throw new ArgumentNullException(nameof(seed));
        if (seed.Length > MaximumSeedLength) throw new ArgumentException("Seed exceeds 128 characters.", nameof(seed));
        string normalized = seed.Trim().Normalize(NormalizationForm.FormC);
        if (normalized.Length == 0 || normalized.Length > MaximumSeedLength) throw new ArgumentException("Seed must contain1 to128 characters.", nameof(seed));
        if (normalized.Any(char.IsControl)) throw new ArgumentException("Seed cannot contain control characters.", nameof(seed));
        if (normalized.Contains('\\')) throw new ArgumentException("Backslashes are not supported in installer seeds.", nameof(seed));
        return normalized;
    }

    private static string Hex(byte[] bytes) => string.Concat(bytes.Select(value => value.ToString("x2")));
}
