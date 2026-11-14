using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace SuikodenRandomizer.Core.Avatars;

/// <summary>An immutable seed-defined bijection of the eligible combat roles.</summary>
public sealed class AvatarShuffleProfile
{
    public const int CurrentVersion = 1;
    public const string CurrentProfileId = "combat-avatar-shuffle-v1";
    public const string IncludeTirProfileId = "combat-avatar-shuffle-with-tir-v1";
    public const string AlgorithmId = "sha256-splitmix64-fisher-yates-v1";
    public const int MaximumSeedLength = 128;
    private readonly IReadOnlyDictionary<int, int> _assignments;

    private AvatarShuffleProfile(string seed, int[] roles, int[] donors, bool includeTir)
    {
        Seed = seed;
        IncludesTir = includeTir;
        RoleIds = Array.AsReadOnly(roles);
        var assignments = new Dictionary<int, int>();
        for (int n = 0; n < roles.Length; n++) assignments.Add(roles[n], donors[n]);
        _assignments = new ReadOnlyDictionary<int, int>(assignments);
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
        {
            writer.Write(ProfileId); writer.Write(CurrentVersion); writer.Write(AlgorithmId);
            writer.Write(Seed); writer.Write(StartPolicy); writer.Write(ProgressionPolicy);
            writer.Write(roles.Length);
            foreach (int role in roles) { writer.Write(role); writer.Write(assignments[role]); }
        }
        using var sha = SHA256.Create();
        Fingerprint = Hex(sha.ComputeHash(stream.ToArray()));
    }

    public string ProfileId => IncludesTir ? IncludeTirProfileId : CurrentProfileId;
    public bool IncludesTir { get; }
    public string Seed { get; }
    public string Fingerprint { get; }
    public string ShuffleAlgorithm => AlgorithmId;
    public string StartPolicy => RoleAvatarProgressionPolicy.StartPolicyId;
    public string ProgressionPolicy => RoleAvatarProgressionPolicy.PolicyId;
    public IReadOnlyList<int> RoleIds { get; }
    public IReadOnlyDictionary<int, int> Assignments => _assignments;
    public bool IsRole(int roleId) => _assignments.ContainsKey(roleId);

    /// <summary>Keep Tir's story rune, equipment and progression local to his canonical role.</summary>
    public bool KeepsCanonicalLoadout(int roleId) =>
        IncludesTir && (roleId == CombatCatalog.TirId || GetDonor(roleId) == CombatCatalog.TirId);

    /// <summary>Protected valid identities remain their own identity in all display lookups.</summary>
    public int GetDonor(int roleId)
    {
        if (roleId < 0 || roleId > 109) throw new ArgumentOutOfRangeException(nameof(roleId));
        return _assignments.TryGetValue(roleId, out int donor) ? donor : roleId;
    }

    public static AvatarShuffleProfile Create(string seed, bool includeTir = false)
    {
        if (seed is null) throw new ArgumentNullException(nameof(seed));
        if (seed.Length > MaximumSeedLength) throw new ArgumentException("Seed exceeds 128 characters.", nameof(seed));
        string normalized = seed.Trim().Normalize(NormalizationForm.FormC);
        if (normalized.Length == 0 || normalized.Length > MaximumSeedLength)
            throw new ArgumentException("Seed must contain 1 to 128 characters.", nameof(seed));
        if (normalized.Any(char.IsControl)) throw new ArgumentException("Seed cannot contain control characters.", nameof(seed));
        int[] roles = CombatCatalog.NormalCombatIds.OrderBy(id => id).ToArray();
        if (roles.Length != 75 || roles.Distinct().Count() != 75
            || roles.Any(id => id == CombatCatalog.TirId || id == CombatCatalog.TedId || id == CombatCatalog.OdessaId))
            throw new InvalidOperationException("The reviewed avatar shuffle requires exactly 75 distinct normal combat roles.");
        if (includeTir) roles = roles.Append(CombatCatalog.TirId).OrderBy(id => id).ToArray();
        int[] donors = (int[])roles.Clone();
        var random = new StableRandom(normalized);
        for (int n = donors.Length - 1; n > 0; n--)
        {
            int other = random.Next(n + 1);
            int value = donors[n]; donors[n] = donors[other]; donors[other] = value;
        }
        return new AvatarShuffleProfile(normalized, roles, donors, includeTir);
    }

    internal static string Hex(byte[] bytes) => string.Concat(bytes.Select(value => value.ToString("x2")));

    private sealed class StableRandom
    {
        private ulong _state;
        internal StableRandom(string seed)
        {
            using var sha = SHA256.Create();
            byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(AlgorithmId + "\n" + seed));
            for (int n = 0; n < 8; n++) _state |= (ulong)hash[n] << (8 * n);
        }
        private ulong NextUInt64()
        {
            unchecked
            {
                ulong value = (_state += 0x9e3779b97f4a7c15UL);
                value = (value ^ (value >> 30)) * 0xbf58476d1ce4e5b9UL;
                value = (value ^ (value >> 27)) * 0x94d049bb133111ebUL;
                return value ^ (value >> 31);
            }
        }
        internal int Next(int exclusiveMaximum)
        {
            ulong bound = (ulong)exclusiveMaximum;
            ulong threshold = unchecked(0UL - bound) % bound;
            ulong value;
            do { value = NextUInt64(); } while (value < threshold);
            return (int)(value % bound);
        }
    }
}
