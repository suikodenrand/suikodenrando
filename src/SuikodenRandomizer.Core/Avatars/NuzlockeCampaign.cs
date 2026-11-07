using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace SuikodenRandomizer.Core.Avatars;

/// <summary>Finite fighter identities assigned to native story roles across a Nuzlocke run.</summary>
public sealed class NuzlockeCampaign
{
    public const string CurrentProfileId = "nuzlocke-avatar-v1";
    public const int CurrentVersion = 2;
    private static readonly int[] Fighters = CombatCatalog.NormalCombatIds.OrderBy(id => id).ToArray();
    private static readonly int[] Roles = Fighters.Concat(new[] { CombatCatalog.TirId, CombatCatalog.TedId, CombatCatalog.OdessaId }).OrderBy(id => id).ToArray();
    private static readonly HashSet<int> FighterSet = new HashSet<int>(Fighters);
    private static readonly HashSet<int> RoleSet = new HashSet<int>(Roles);
    private readonly Dictionary<int, int> _assignments;
    private readonly HashSet<int> _fielded;
    private readonly HashSet<int> _retired;
    private readonly int[] _active;
    private readonly int[] _reserveOrder;
    private readonly int[] _lastKnockouts;

    private NuzlockeCampaign(string seed, bool includesTir, string initialFingerprint,
        Dictionary<int, int> assignments, HashSet<int> fielded, HashSet<int> retired,
        int[] active, int[] reserveOrder, int reservePosition, long lastBattleId, int[] lastKnockouts)
    {
        Seed = seed;
        IncludesTir = includesTir;
        InitialFingerprint = initialFingerprint;
        _assignments = assignments;
        _fielded = fielded;
        _retired = retired;
        _active = active;
        _reserveOrder = reserveOrder;
        _lastKnockouts = lastKnockouts;
        ReservePosition = reservePosition;
        LastBattleId = lastBattleId;
        Assignments = new ReadOnlyDictionary<int, int>(assignments);
        ActiveRoleIds = Array.AsReadOnly(active);
        FieldedHeroIds = Array.AsReadOnly(fielded.OrderBy(id => id).ToArray());
        RetiredHeroIds = Array.AsReadOnly(retired.OrderBy(id => id).ToArray());
        ReserveOrder = Array.AsReadOnly(reserveOrder);
    }

    public string Seed { get; }
    public bool IncludesTir { get; }
    public string InitialFingerprint { get; }
    public IReadOnlyList<int> RoleIds => Array.AsReadOnly(Roles);
    public IReadOnlyDictionary<int, int> Assignments { get; }
    public IReadOnlyList<int> ActiveRoleIds { get; }
    public IReadOnlyList<int> FieldedHeroIds { get; }
    public IReadOnlyList<int> RetiredHeroIds { get; }
    public IReadOnlyList<int> ReserveOrder { get; }
    public int ReservePosition { get; }
    public long LastBattleId { get; }
    /// <summary>Living companion fighters; protected Tir does not consume a life.</summary>
    public int LivingHeroCount => Fighters.Length - _retired.Count;
    public int RemainingReserves => Fighters.Length - _fielded.Count;
    /// <summary>All companion fighters are retired; Tir remains assigned to his native role.</summary>
    public bool IsExhausted => LivingHeroCount == 0;
    public bool IsRole(int roleId) => RoleSet.Contains(roleId);
    public bool IsRetired(int fighterId) => _retired.Contains(fighterId);

    /// <summary>A playable role with no remaining fighter returns -1.</summary>
    public int GetDonor(int roleId)
    {
        if (roleId < 0 || roleId > CombatCatalog.OdessaId) throw new ArgumentOutOfRangeException(nameof(roleId));
        return _assignments.TryGetValue(roleId, out int fighter) ? fighter : IsRole(roleId) ? -1 : roleId;
    }

    public static NuzlockeCampaign Create(string seed, bool includeTir = false)
    {
        // Tir must remain the canonical story leader. Nuzlocke deliberately ignores
        // the ordinary shuffle's optional Tir setting.
        var initial = AvatarShuffleProfile.Create(seed, false);
        var assignments = initial.Assignments.ToDictionary(pair => pair.Key, pair => pair.Value);
        assignments.Add(CombatCatalog.TirId, CombatCatalog.TirId);
        return new NuzlockeCampaign(initial.Seed, false, initial.Fingerprint, assignments,
            new HashSet<int>(), new HashSet<int>(), Array.Empty<int>(), BuildReserveOrder(initial.Seed), 0, 0, Array.Empty<int>());
    }

    /// <summary>Mark all present fighters first, then draw only for active vacancies.</summary>
    public NuzlockeCampaign ReconcileActive(IEnumerable<int> roleIds)
    {
        int[] active = ReadRoles(roleIds);
        var assignments = new Dictionary<int, int>(_assignments);
        var fielded = new HashSet<int>(_fielded);
        foreach (int role in active)
            if (role != CombatCatalog.TirId && assignments.TryGetValue(role, out int fighter)) fielded.Add(fighter);
        int position = FillActiveVacancies(assignments, fielded, active, ReservePosition);
        if (_active.SequenceEqual(active) && fielded.SetEquals(_fielded)
            && position == ReservePosition && SameAssignments(assignments, _assignments)) return this;
        return Next(assignments, fielded, new HashSet<int>(_retired), active, position, LastBattleId, _lastKnockouts);
    }

    /// <summary>Commit the actual fighters latched as KO during one completed battle.</summary>
    public NuzlockeCampaign CommitBattle(long battleId, IEnumerable<int> knockedOutHeroIds)
    {
        if (battleId <= 0) throw new ArgumentOutOfRangeException(nameof(battleId));
        if (knockedOutHeroIds is null) throw new ArgumentNullException(nameof(knockedOutHeroIds));
        int[] reported = knockedOutHeroIds.Take(AvatarPartyLayout.ResidentCapacity + 1).ToArray();
        if (reported.Length > AvatarPartyLayout.ResidentCapacity || reported.Any(id => id != CombatCatalog.TirId && !FighterSet.Contains(id)))
            throw new ArgumentException("Battle knockouts contain too many fighters or an invalid identity.", nameof(knockedOutHeroIds));
        int[] knockouts = reported.Where(id => id != CombatCatalog.TirId).Distinct().OrderBy(id => id).ToArray();
        if (battleId == LastBattleId)
        {
            if (!_lastKnockouts.SequenceEqual(knockouts)) throw new InvalidOperationException("A committed battle has a different knockout result.");
            return this;
        }
        if (battleId < LastBattleId) throw new InvalidOperationException("Battle results cannot be committed out of order.");

        var assignments = new Dictionary<int, int>(_assignments);
        var retired = new HashSet<int>(_retired);
        foreach (int fighter in knockouts)
        {
            if (retired.Contains(fighter)) continue;
            var owners = assignments.Where(pair => pair.Value == fighter).Select(pair => pair.Key).ToArray();
            if (!_fielded.Contains(fighter) || owners.Length != 1 || !_active.Contains(owners[0]))
                throw new InvalidOperationException("A knockout must belong to a fielded fighter in the active party.");
            assignments.Remove(owners[0]);
            retired.Add(fighter);
        }
        var fielded = new HashSet<int>(_fielded);
        int position = FillActiveVacancies(assignments, fielded, _active, ReservePosition);
        return Next(assignments, fielded, retired, _active, position, battleId, knockouts);
    }

    public NuzlockeSnapshot Capture() => new NuzlockeSnapshot
    {
        ProfileId = CurrentProfileId,
        Version = CurrentVersion,
        Seed = Seed,
        IncludesTir = IncludesTir,
        InitialFingerprint = InitialFingerprint,
        Assignments = new Dictionary<int, int>(_assignments),
        ActiveRoleIds = (int[])_active.Clone(),
        FieldedHeroIds = FieldedHeroIds.ToArray(),
        RetiredHeroIds = RetiredHeroIds.ToArray(),
        ReserveOrder = (int[])_reserveOrder.Clone(),
        ReservePosition = ReservePosition,
        LastBattleId = LastBattleId,
        LastKnockoutHeroIds = (int[])_lastKnockouts.Clone()
    };

    public static NuzlockeCampaign Restore(NuzlockeSnapshot snapshot)
    {
        if (snapshot is null) throw new ArgumentNullException(nameof(snapshot));
        if (snapshot.ProfileId == CurrentProfileId && snapshot.Version == 1)
            throw new ArgumentException("Nuzlocke state from 0.8.8 is incompatible with protected Tir. Start a new Nuzlocke run.", nameof(snapshot));
        if (snapshot.ProfileId != CurrentProfileId || snapshot.Version != CurrentVersion)
            throw new ArgumentException("Unsupported Nuzlocke state.", nameof(snapshot));
        if (snapshot.IncludesTir)
            throw new ArgumentException("Nuzlocke state cannot include Tir in the companion shuffle.", nameof(snapshot));
        var initial = Create(snapshot.Seed, snapshot.IncludesTir);
        if (initial.Seed != snapshot.Seed || initial.InitialFingerprint != snapshot.InitialFingerprint)
            throw new ArgumentException("Nuzlocke seed identity does not match.", nameof(snapshot));
        if (snapshot.Assignments is null || snapshot.Assignments.Count > Roles.Length
            || !snapshot.Assignments.TryGetValue(CombatCatalog.TirId, out int tir) || tir != CombatCatalog.TirId
            || snapshot.Assignments.Any(pair => !RoleSet.Contains(pair.Key)
                || (pair.Key == CombatCatalog.TirId ? pair.Value != CombatCatalog.TirId : !FighterSet.Contains(pair.Value)))
            || snapshot.Assignments.Values.Distinct().Count() != snapshot.Assignments.Count)
            throw new ArgumentException("Nuzlocke assignments contain an invalid or duplicate identity.", nameof(snapshot));
        var fielded = ReadUniqueFighters(snapshot.FieldedHeroIds, "fielded");
        var retired = ReadUniqueFighters(snapshot.RetiredHeroIds, "retired");
        if (!retired.IsSubsetOf(fielded) || snapshot.Assignments.Values.Any(retired.Contains)
            || !snapshot.Assignments.Values.Where(id => id != CombatCatalog.TirId).Concat(retired).ToHashSet().SetEquals(Fighters))
            throw new ArgumentException("Nuzlocke living and retired identities are inconsistent.", nameof(snapshot));
        int[] active = ReadRoles(snapshot.ActiveRoleIds);
        if (!active.SequenceEqual(snapshot.ActiveRoleIds)
            || active.Any(role => role != CombatCatalog.TirId
                && snapshot.Assignments.TryGetValue(role, out int fighter) && !fielded.Contains(fighter))
            || (active.Any(role => !snapshot.Assignments.ContainsKey(role))
                && snapshot.Assignments.Keys.Any(role => role != CombatCatalog.TirId && !active.Contains(role))))
            throw new ArgumentException("Nuzlocke active fighters have not been fielded.", nameof(snapshot));
        if (snapshot.ReserveOrder is null || !snapshot.ReserveOrder.SequenceEqual(initial._reserveOrder)
            || snapshot.ReservePosition < 0 || snapshot.ReservePosition > Fighters.Length
            || snapshot.ReserveOrder.Take(snapshot.ReservePosition).Any(id => !fielded.Contains(id)))
            throw new ArgumentException("Nuzlocke reserve order is inconsistent.", nameof(snapshot));
        var lastKnockouts = ReadUniqueFighters(snapshot.LastKnockoutHeroIds, "last battle").OrderBy(id => id).ToArray();
        if (snapshot.LastBattleId < 0 || lastKnockouts.Length > AvatarPartyLayout.ResidentCapacity
            || (snapshot.LastBattleId == 0 && lastKnockouts.Length != 0)
            || lastKnockouts.Any(id => !retired.Contains(id)))
            throw new ArgumentException("Nuzlocke battle history is inconsistent.", nameof(snapshot));
        return new NuzlockeCampaign(initial.Seed, initial.IncludesTir, initial.InitialFingerprint,
            new Dictionary<int, int>(snapshot.Assignments), fielded, retired, active,
            (int[])snapshot.ReserveOrder.Clone(), snapshot.ReservePosition, snapshot.LastBattleId, lastKnockouts);
    }

    private NuzlockeCampaign Next(Dictionary<int, int> assignments, HashSet<int> fielded, HashSet<int> retired,
        int[] active, int position, long lastBattleId, int[] lastKnockouts) =>
        new NuzlockeCampaign(Seed, IncludesTir, InitialFingerprint, assignments, fielded, retired,
            (int[])active.Clone(), _reserveOrder, position, lastBattleId, (int[])lastKnockouts.Clone());

    private int FillActiveVacancies(Dictionary<int, int> assignments, HashSet<int> fielded, int[] active, int position)
    {
        // Protected Tir is never replaced or transferred. Draw only for companion roles.
        int[] fillOrder = active.Where(role => role != CombatCatalog.TirId).OrderBy(role => role).ToArray();
        foreach (int role in fillOrder)
        {
            if (assignments.ContainsKey(role)) continue;
            while (position < _reserveOrder.Length && fielded.Contains(_reserveOrder[position])) position++;
            if (position == _reserveOrder.Length) break;
            int fighter = _reserveOrder[position++];
            int previousRole = assignments.Single(pair => pair.Value == fighter).Key;
            if (active.Contains(previousRole)) throw new InvalidOperationException("An active fighter cannot be a reserve.");
            assignments.Remove(previousRole);
            assignments.Add(role, fighter);
            fielded.Add(fighter);
        }
        // Once every fighter has been fielded, surviving bench fighters still
        // have one life. Move them with their saved progression instead of
        // ending a run that has living fighters outside the current party.
        var owners = assignments.ToDictionary(pair => pair.Value, pair => pair.Key);
        foreach (int role in fillOrder)
        {
            if (assignments.ContainsKey(role)) continue;
            foreach (int fighter in _reserveOrder)
            {
                if (!owners.TryGetValue(fighter, out int previousRole) || active.Contains(previousRole)) continue;
                assignments.Remove(previousRole);
                assignments.Add(role, fighter);
                owners[fighter] = role;
                fielded.Add(fighter);
                break;
            }
        }
        return position;
    }

    private static int[] ReadRoles(IEnumerable<int> roleIds)
    {
        if (roleIds is null) throw new ArgumentNullException(nameof(roleIds));
        int[] roles = roleIds.Take(AvatarPartyLayout.ResidentCapacity + 1).ToArray();
        if (roles.Length > AvatarPartyLayout.ResidentCapacity || roles.Distinct().Count() != roles.Length
            || roles.Any(id => !RoleSet.Contains(id)))
            throw new ArgumentException("Active party contains too many roles or an invalid or duplicate role.", nameof(roleIds));
        return roles.OrderBy(id => id).ToArray();
    }

    private static HashSet<int> ReadUniqueFighters(int[] ids, string field)
    {
        if (ids is null || ids.Length > Fighters.Length || ids.Distinct().Count() != ids.Length
            || ids.Any(id => !FighterSet.Contains(id)))
            throw new ArgumentException("Nuzlocke " + field + " identities are invalid.");
        return new HashSet<int>(ids);
    }

    private static bool SameAssignments(Dictionary<int, int> left, Dictionary<int, int> right) =>
        left.Count == right.Count && left.All(pair => right.TryGetValue(pair.Key, out int fighter) && pair.Value == fighter);

    private static int[] BuildReserveOrder(string seed)
    {
        using var sha = SHA256.Create();
        string reserveSeed = AvatarShuffleProfile.Hex(sha.ComputeHash(Encoding.UTF8.GetBytes(CurrentProfileId + "\nreserve\n" + seed)));
        var profile = AvatarShuffleProfile.Create(reserveSeed, false);
        return profile.RoleIds.Select(profile.GetDonor).ToArray();
    }
}

/// <summary>Managed data only; native pointers and assets never enter campaign storage.</summary>
public sealed class NuzlockeSnapshot
{
    public string ProfileId { get; set; } = NuzlockeCampaign.CurrentProfileId;
    public int Version { get; set; } = NuzlockeCampaign.CurrentVersion;
    public string Seed { get; set; } = "";
    public bool IncludesTir { get; set; }
    public string InitialFingerprint { get; set; } = "";
    public Dictionary<int, int> Assignments { get; set; } = new Dictionary<int, int>();
    public int[] ActiveRoleIds { get; set; } = Array.Empty<int>();
    public int[] FieldedHeroIds { get; set; } = Array.Empty<int>();
    public int[] RetiredHeroIds { get; set; } = Array.Empty<int>();
    public int[] ReserveOrder { get; set; } = Array.Empty<int>();
    public int ReservePosition { get; set; }
    public long LastBattleId { get; set; }
    public int[] LastKnockoutHeroIds { get; set; } = Array.Empty<int>();
}
