extern alias GSD1;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SuikodenRandomizer.Core;
using SuikodenRandomizer.Core.Avatars;

namespace SuikodenRandomizer.AvatarPlugin;

internal sealed partial class AvatarRuntime
{
    private FixedRosterProfile? _fixedProfile;
    private FixedRosterAssignments? _fixedAssignments;
    private string _fixedSeed = "";
    private string? _fixedNewGameMode;
    private bool _fixedLoading;
    private bool _fixedStarted;
    private bool _fixedResume;
    private Dictionary<int, FixedRosterBaseSnapshot> _fixedFighters = new();
    private Dictionary<int, FixedRosterBaseSnapshot> _fixedStoryBases = new();
    private Dictionary<int, GSD1.PLAYER_DATA> _fixedNativeResidents = new();

    internal bool IsFixedRoster => CanExecute && _fixedProfile is not null;
    internal string CampaignFingerprint => _fixedProfile?.Fingerprint ?? Profile.Fingerprint;
    internal (string Mode, string Seed)? FixedRosterIdentity => IsFixedRoster
        ? (_fixedNewGameMode ?? "Fixed Roster", _fixedSeed)
        : null;

    private void ResetFixedRoster()
    {
        _fixedProfile = null;
        _fixedAssignments = null;
        _fixedSeed = "";
        _fixedNewGameMode = null;
        _fixedLoading = _fixedStarted = _fixedResume = false;
        _fixedFighters.Clear();
        _fixedStoryBases.Clear();
        _fixedNativeResidents.Clear();
    }

    private bool TryBeginFixedRoster()
    {
        string mode = AvatarPlugin.ConfiguredMode;
        if (string.Equals(mode, "Shuffle", StringComparison.OrdinalIgnoreCase)) return false;
        string seed = AvatarShuffleProfile.Create(AvatarPlugin.ConfiguredSeed).Seed;
        FixedRosterProfile profile;
        if (string.Equals(mode, "FixedRandom", StringComparison.OrdinalIgnoreCase))
            profile = FixedRosterProfile.CreateRandom(seed);
        else if (string.Equals(mode, "FixedChosen", StringComparison.OrdinalIgnoreCase))
            profile = FixedRosterProfile.CreateChosen(AvatarPlugin.ConfiguredCompanions.Split(',')
                .Select(value => int.Parse(value.Trim(), NumberStyles.None, CultureInfo.InvariantCulture)));
        else
            throw new InvalidOperationException("Unknown New Game mode. Use Shuffle, FixedChosen, or FixedRandom.");

        _fixedProfile = profile;
        _fixedAssignments = FixedRosterAssignments.Empty(profile);
        _fixedSeed = seed;
        _fixedNewGameMode = mode;
        _waiting = "native fixed roster initialization";
        _lastWait = "";
        AvatarPresentation.Reset();
        Trace("NEW GAME FIXED ROSTER: " + string.Join(", ", profile.HeroIds.Select(CombatCatalog.GetName))
            + "; seed=" + seed + "; profile=" + profile.Fingerprint);
        return true;
    }

    private bool TryLoadFixedRoster(GSD1.SaveDataManager.SaveData save)
    {
        if (!FixedRosterBindingStore.HasBindings) return false;
        var binding = FixedRosterBindingStore.Read(FixedRosterBindingStore.ComputeIdentity(save));
        if (binding is null) return false;
        var profile = FixedRosterProfile.CreateChosen(binding.Companions);
        _fixedAssignments = FixedRosterAssignments.Restore(profile, binding.Assignments);
        _fixedProfile = profile;
        _fixedSeed = binding.Seed;
        // The binding stores the chosen companions and seed, but omits their selection method.
        _fixedNewGameMode = null;
        _fixedFighters = binding.CompanionStates;
        _fixedStoryBases = binding.RoleBases;
        _fixedLoading = _fixedResume = true;
        Trace("FIXED ROSTER RESTORE: " + string.Join(", ", profile.HeroIds.Select(CombatCatalog.GetName))
            + "; saved assignments=" + binding.Assignments.Count);
        return true;
    }

    internal void CompleteFixedLoad() { if (CanExecute) _fixedLoading = false; }

    internal void CheckCampaignBindingWritable()
    {
        if (!IsAdmitted) throw new InvalidOperationException("Suikoden I campaign is not admitted in this execution epoch.");
        if (IsFixedRoster) FixedRosterBindingStore.CheckWritable();
        else AvatarBindingStore.CheckWritable();
    }

    internal string WriteCampaignBinding(GSD1.SaveDataManager.SaveData save)
    {
        if (!IsActive) throw new InvalidOperationException("Suikoden I campaign is not active in this execution epoch.");
        if (_fixedProfile is null)
        {
            string identity = AvatarSaveIdentity.Compute(save);
            if (FixedRosterBindingStore.HasBindings
                && FixedRosterBindingStore.Read(FixedRosterBindingStore.ComputeIdentity(save)) is not null)
                throw new InvalidOperationException("This snapshot already belongs to a fixed roster campaign.");
            AvatarBindingStore.Write(Profile, State, identity);
            return identity;
        }

        var pairs = _fixedAssignments!.Assignments.Where(pair => pair.Key != CombatCatalog.TirId)
            .ToDictionary(pair => pair.Key, pair => pair.Value);
        var states = new Dictionary<int, FixedRosterBaseSnapshot>(_fixedFighters);
        foreach (var pair in pairs)
            states[pair.Value] = CaptureFighter(AvatarNativeData.FindBase(pair.Key), pair.Value);
        if (AvatarBindingStore.Read(AvatarSaveIdentity.Compute(save)) is not null)
            throw new InvalidOperationException("This snapshot already belongs to a regular shuffle campaign.");
        string fixedIdentity = FixedRosterBindingStore.ComputeIdentity(save);
        FixedRosterBindingStore.Write(fixedIdentity, new FixedRosterBinding
        {
            Seed = _fixedSeed,
            Companions = _fixedProfile.CompanionIds.ToArray(),
            Assignments = pairs,
            CompanionStates = states,
            RoleBases = _fixedStoryBases
        });
        return fixedIdentity;
    }

    private static FixedRosterBaseSnapshot CaptureFighter(GSD1.PLAYER_BASE source, int fighterId)
    {
        var snapshot = FixedRosterBaseSnapshot.Capture(source);
        snapshot.CharacterId = fighterId;
        return snapshot;
    }

    private bool EnsureFixedRoster(string reason, bool required)
    {
        var profile = _fixedProfile ?? throw new InvalidOperationException("Fixed roster profile is missing.");
        var previous = _fixedAssignments ?? throw new InvalidOperationException("Fixed roster assignments are missing.");
        var game = GSD1.OldSrcBase.game_work;
        var manager = GSD1.GlobalWork.Instance?.player_manager;
        var cache = manager?.player_cache;
        var residents = game?.player_data;
        var codes = game?.party_data?.chara_code;
        if (game is null || manager is null || cache is null || residents is null || codes is null)
            return WaitFor("native fixed roster party is not ready", required);
        RestorePendingCaches(manager);
        if (_fixedStarted && (_game != game.Pointer || _manager?.Pointer != manager.Pointer))
            throw new InvalidOperationException("Native world changed outside the supported fixed roster load boundary.");

        IReadOnlyList<int> activeSlots;
        try { activeSlots = ActiveResidentSlots(); }
        catch (InvalidOperationException exception) { return WaitFor(exception.Message, required); }
        if (!_fixedStarted && activeSlots.Count == 0)
            return WaitFor("waiting for the first active party", required);

        var slots = new Dictionary<int, int>();
        foreach (int slot in activeSlots)
        {
            int role = codes[slot];
            if (!profile.IsStoryRole(role))
            {
                if (required) return WaitFor("unsupported active role " + role, true);
                continue;
            }
            slots.Add(role, slot);
        }

        if (_fixedResume && !previous.Assignments.Keys.ToHashSet().SetEquals(
                slots.Keys.Where(role => role != CombatCatalog.TirId)))
            return WaitFor("waiting for the saved fixed roster party to reconstruct", required);
        if (slots.Keys.Count(role => role != CombatCatalog.TirId) > FixedRosterProfile.CompanionCount)
            return WaitFor("the active party has more than five companion roles", required);
        var next = previous.Reconcile(slots.Keys);
        if (_fixedStarted && ReferenceEquals(next, previous) && FixedResidentsAreCurrent(slots))
            return true;

        // Capture all incoming, outgoing, and starting records before touching any live data.
        var needed = new HashSet<int>(previous.Assignments.Keys);
        needed.UnionWith(slots.Keys);
        if (JoiningWeaponPolicy == JoiningWeaponPolicy.TirVariance) needed.Add(CombatCatalog.TirId);
        if (!_fixedStarted && !_fixedResume) needed.UnionWith(profile.CompanionIds);
        var bases = ReadyBaseIndex(needed);
        if (bases.Count != needed.Count) return WaitFor("waiting for fixed roster live bases", required);
        if (JoiningEquipmentPolicy == JoiningEquipmentPolicy.MatchRole && !JoiningEquipmentNative.Ready)
            return WaitFor("waiting for native joining equipment definitions", required);
        int? tirWeaponReference = JoiningWeaponPolicy == JoiningWeaponPolicy.TirVariance ? bases[CombatCatalog.TirId].buki_data.level : null;
        if (tirWeaponReference is < 1 or > 16) return WaitFor("waiting for the current canonical Tir weapon level", required);
        foreach (var entry in slots)
            if (!ResidentBound(entry.Value, entry.Key, bases))
                return WaitFor("waiting for fixed roster active slot " + entry.Value, required);

        var originals = new Dictionary<int, GSD1.PLAYER_DATA>(_originals);
        foreach (int id in profile.CompanionIds.Concat(slots.Keys).Distinct())
        {
            if (originals.ContainsKey(id)) continue;
            if (!cache.TryGetValue(id, out var original) || original is null || original.chara_id != id)
                return WaitFor("waiting for fixed roster asset " + id, required);
            originals.Add(id, original);
        }
        foreach (int id in profile.CompanionIds)
            if (originals[id].field_data?.opd_data is null || originals[id].battle_data?.opd_data is null)
                return WaitFor("waiting for fixed roster fighter assets " + id, required);

        var fighters = new Dictionary<int, FixedRosterBaseSnapshot>(_fixedFighters);
        var storyBases = new Dictionary<int, FixedRosterBaseSnapshot>(_fixedStoryBases);
        var nativeResidents = new Dictionary<int, GSD1.PLAYER_DATA>(_fixedNativeResidents);
        var initializedWeapons = new HashSet<int>(_joiningWeaponFighters);
        var initializedEquipment = new HashSet<int>(_joiningEquipmentFighters);
        var weaponLogs = new List<string>();
        var equipmentLogs = new List<string>();
        var writes = new Dictionary<int, GSD1.PLAYER_BASE>();
        var projections = new Dictionary<int, GSD1.PLAYER_DATA>();
        var departing = previous.Assignments.Where(pair => pair.Key != CombatCatalog.TirId
            && !next.Assignments.ContainsKey(pair.Key)).ToArray();

        _preparing = true;
        try
        {
            if (!_fixedStarted && !_fixedResume)
                foreach (int fighter in profile.CompanionIds)
                {
                    var start = CaptureFighter(bases[fighter], fighter);
                    start.Growth = originals[fighter].seicho_type.ToArray();
                    start.Hp = start.MaxHp;
                    start.Status = 0;
                    fighters.Add(fighter, start);
                }

            foreach (var pair in departing)
            {
                fighters[pair.Value] = CaptureFighter(bases[pair.Key], pair.Value);
                writes.Add(pair.Key, storyBases[pair.Key].ToNative());
                storyBases.Remove(pair.Key);
            }

            foreach (var pair in next.Assignments)
            {
                int role = pair.Key;
                if (role == CombatCatalog.TirId) continue;
                var resident = residents[slots[role]]
                    ?? throw new InvalidOperationException("Bound fixed roster resident disappeared.");
                bool retained = _proxies.TryGetValue(role, out var oldProxy);
                if (!retained || resident.Pointer != oldProxy!.Pointer) nativeResidents[role] = resident;
                if (!_fixedResume && !retained)
                {
                    storyBases.Add(role, FixedRosterBaseSnapshot.Capture(bases[role]));
                    var incoming = fighters[pair.Value].ToNative();
                    if (JoiningEquipmentPolicy == JoiningEquipmentPolicy.MatchRole && initializedEquipment.Add(pair.Value))
                    {
                        // This story role is still native and unprojected here;
                        // use its current first-join kit before writing the fighter.
                        equipmentLogs.Add(JoiningEquipmentNative.Apply(incoming, JoiningEquipmentNative.Capture(bases[role]),
                            originals[pair.Value], role, pair.Value));
                        fighters[pair.Value] = CaptureFighter(incoming, pair.Value);
                    }
                    if (initializedWeapons.Add(pair.Value))
                    {
                        string? weaponLog = NormalizeJoiningWeapon(incoming, bases[role], role, pair.Value, tirWeaponReference);
                        if (weaponLog is not null) weaponLogs.Add(weaponLog);
                        fighters[pair.Value] = CaptureFighter(incoming, pair.Value);
                    }
                    writes.Add(role, incoming);
                }
                projections.Add(role, retained && oldProxy!.player_base?.Pointer == bases[role].Pointer
                    ? oldProxy : AvatarNativeData.CreateProxy(originals[role],
                    originals[pair.Value], bases[role]));
            }

            // Retire only projections that are no longer active, including staged resident references.
            var residentWrites = new Dictionary<int, GSD1.PLAYER_DATA>();
            for (int i = 0; i < Math.Min(codes.Count, residents.Count); i++)
                if (residents[i] is { } current && _proxies.TryGetValue(current.chara_id, out var old)
                    && current.Pointer == old.Pointer && !projections.ContainsKey(current.chara_id))
                    residentWrites.Add(i, nativeResidents[current.chara_id]);
            foreach (var entry in slots)
                if (projections.TryGetValue(entry.Key, out var proxy)) residentWrites[entry.Value] = proxy;

            var cacheWrites = new Dictionary<int, GSD1.PLAYER_DATA>(projections);
            foreach (var pair in departing)
                if (cache.TryGetValue(pair.Key, out var current) && current?.Pointer == _proxies[pair.Key].Pointer)
                    cacheWrites.Add(pair.Key, originals[pair.Key]);
            var beforeCache = cacheWrites.Keys.ToDictionary(id => id, id => cache[id]);
            var beforeResidents = residentWrites.Keys.ToDictionary(i => i, i => residents[i]);
            var beforeBases = writes.Keys.ToDictionary(id => id, id => AvatarNativeData.CloneBase(bases[id]));
            string layout = CanonicalLayoutFingerprint();

            try
            {
                foreach (var entry in writes)
                {
                    var live = bases[entry.Key];
                    entry.Value.chara_no = live.chara_no;
                    AvatarNativeData.CopyBase(entry.Value, live);
                    if (AvatarNativeData.Fingerprint(live) != AvatarNativeData.Fingerprint(entry.Value))
                        throw new InvalidOperationException("Fixed roster stats or equipment did not verify for role " + entry.Key + ".");
                }
                foreach (var entry in cacheWrites) cache[entry.Key] = entry.Value;
                foreach (var entry in residentWrites) residents[entry.Key] = entry.Value;
                if (CanonicalLayoutFingerprint() != layout
                    || cacheWrites.Any(entry => cache[entry.Key]?.Pointer != entry.Value.Pointer)
                    || residentWrites.Any(entry => residents[entry.Key]?.Pointer != entry.Value.Pointer)
                    || projections.Any(entry => entry.Value.chara_id != entry.Key
                        || entry.Value.player_base?.Pointer != bases[entry.Key].Pointer))
                    throw new InvalidOperationException("Native fixed roster publication did not verify.");
            }
            catch (Exception failure)
            {
                var rollback = new List<Exception>();
                foreach (var entry in beforeResidents)
                    try { residents[entry.Key] = entry.Value; } catch (Exception error) { rollback.Add(error); }
                foreach (var entry in beforeCache)
                    try { cache[entry.Key] = entry.Value; } catch (Exception error) { rollback.Add(error); }
                foreach (var entry in beforeBases)
                    try { AvatarNativeData.CopyBase(entry.Value, bases[entry.Key]); } catch (Exception error) { rollback.Add(error); }
                foreach (var proxy in projections.Values) _retired.Add(proxy);
                if (rollback.Count != 0)
                {
                    rollback.Insert(0, failure);
                    throw new AggregateException("Fixed roster publication and rollback failed.", rollback);
                }
                throw;
            }

            foreach (var old in _proxies)
                if (!projections.TryGetValue(old.Key, out var replacement) || replacement.Pointer != old.Value.Pointer)
                    _retired.Add(old.Value);
            foreach (var pair in departing) nativeResidents.Remove(pair.Key);
            _fixedNativeResidents = nativeResidents;
            _fixedFighters = fighters;
            _joiningWeaponFighters = initializedWeapons;
            _joiningEquipmentFighters = initializedEquipment;
            _fixedStoryBases = storyBases;
            _fixedAssignments = next;
            _proxies = projections;
            _originals = originals;
            _manager = manager;
            _game = game.Pointer;
            _fixedStarted = true;
            _fixedResume = false;
            foreach (string weaponLog in weaponLogs) Trace(weaponLog);
            foreach (string equipmentLog in equipmentLogs) Trace(equipmentLog);
            AvatarPresentation.Reset();
            Trace("FIXED ROSTER " + reason + ": " + string.Join(", ", next.Assignments.Select(pair => pair.Key + "->" + pair.Value)));
            return true;
        }
        finally { _preparing = false; }
    }

    private bool FixedResidentsAreCurrent(Dictionary<int, int> slots)
    {
        var game = GSD1.OldSrcBase.game_work;
        var cache = _manager?.player_cache;
        if (game is null || cache is null) return false;
        foreach (var entry in slots)
        {
            if (entry.Key == CombatCatalog.TirId) continue;
            if (!_proxies.TryGetValue(entry.Key, out var proxy)
                || proxy.player_base?.chara_no != entry.Key
                || game.player_data[entry.Value]?.Pointer != proxy.Pointer
                || !cache.TryGetValue(entry.Key, out var cached) || cached?.Pointer != proxy.Pointer)
                return false;
        }
        return true;
    }

    internal void VerifyFixedBattleParty()
    {
        if (!IsFixedRoster) return;
        var party = GSD1.BattleBase.battle_work?.p_battle_data
            ?? throw new InvalidOperationException("Fixed roster battle party is unavailable.");
        var fighters = new HashSet<int>();
        for (int i = 0; i < party.Count; i++)
        {
            var player = party[i]?.player_data;
            if (player is null) continue;
            int role = player.chara_id;
            if (!_fixedAssignments!.Assignments.TryGetValue(role, out int fighter)
                || !fighters.Add(fighter)
                || (role != CombatCatalog.TirId && player.Pointer != ProxyFor(role)?.Pointer))
                throw new InvalidOperationException("Unsupported fixed roster battle participant at slot " + i + ", role " + role + ".");
        }
    }
}
