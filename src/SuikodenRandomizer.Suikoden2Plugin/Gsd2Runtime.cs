extern alias GSD2;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using SuikodenRandomizer.Core.Suikoden2;

namespace SuikodenRandomizer.Suikoden2Plugin;

internal sealed partial class Gsd2Runtime
{
    internal static Gsd2Runtime? Current { get; set; }
    internal static Func<bool> ExecutionAllowed { get; private set; } = () => true;
    internal static Func<long> OwnershipEpoch { get; private set; } = () => 0;
    internal static Func<bool> MayOwnSaveContext { get; private set; } = () => true;
    internal static void SetExecutionOwnership(Func<bool> executionAllowed, Func<long> ownershipEpoch, Func<bool>? mayOwnSaveContext = null)
    {
        ExecutionAllowed = executionAllowed ?? throw new ArgumentNullException(nameof(executionAllowed));
        OwnershipEpoch = ownershipEpoch ?? throw new ArgumentNullException(nameof(ownershipEpoch));
        MayOwnSaveContext = mayOwnSaveContext ?? executionAllowed;
        Current?.SuspendExecution();
    }
    private long _executionEpoch = OwnershipEpoch();
    private bool _executionSuspended;
    internal long ExecutionEpoch => _executionEpoch;
    internal bool OwnsExecution
    {
        get
        {
            // A host predicate may discover a transition and synchronously suspend us.
            bool allowed = ExecutionAllowed(); long epoch = OwnershipEpoch();
            return allowed && !_executionSuspended && _executionEpoch == epoch;
        }
    }
    internal bool MatchesExecution(long epoch) => OwnsExecution && _executionEpoch == epoch;
    internal Gsd2AvatarProfile Profile { get; private set; } = null!;
    internal string? FaultReason { get; private set; }
    internal bool IsAdmitted => OwnsExecution && Profile is not null && FaultReason is null;
    internal bool TablesReady => IsAdmitted && _projected is not null;
    internal bool IsActive => TablesReady && Gsd2Graphics.IsReady;
    // Dialogue consumers may only read a stable, resident publication. These
    // accessors expose admission/managed identities without changing projection.
    internal bool CanUseDialogueNames => ReferenceEquals(Current, this) && !_loading && !_saveScope
        && !_newGamePending && !_busy && TablesReady;
    internal IntPtr DialoguePublishedGame => _publishedGame;
    internal IntPtr DialoguePublishedCharacters => _projected?.Pointer ?? IntPtr.Zero;
    internal IntPtr DialoguePublishedNames => _projected is null ? IntPtr.Zero : _publishedNames;
    // The original GRChapterManager.Update dispatches world and battle chapters.
    // Unity runs our driver and the original Addressables loader coroutines independently.
    internal bool HoldNativeUpdate => OwnsExecution && (FaultReason is not null || (_projected is not null && !IsActive));
    internal bool IsNewCampaign { get; private set; }
    internal bool HasAdmittedCampaignBeforeSuspend { get; private set; }
    internal bool HasCampaignSaveProtection => Profile is not null || HasAdmittedCampaignBeforeSuspend;
    internal string ActiveStartPoint { get; private set; } = "Opening";
    internal string ActiveJoiningWeaponLevel { get; private set; } = "Donor";
    internal string Status => FaultReason is not null ? "Suikoden II stopped: " + FaultReason
        : !IsAdmitted ? "Suikoden II: native campaign (choose New Game to randomize)"
        : !IsActive ? "Suikoden II: preparing original character resources"
        : "Suikoden II | " + Profile.Mode + " | " + Profile.Seed;

    private GSD2.CHARA_DATA? _baseline, _projected;
    private Il2CppStructArray<int>? _originalNames;
    private Il2CppReferenceArray<GSD2.ARMS_DATA>? _originalArms;
    private IntPtr _observedData;
    private IntPtr _publishedGame;
    private IntPtr _publishedNames;
    private Gsd2FixedRosterAssignments? _fixed;
    private Dictionary<int, Gsd2FighterRecord> _fighters = new Dictionary<int, Gsd2FighterRecord>();
    private Dictionary<int, Gsd2FighterRecord> _canonicalRoles = new Dictionary<int, Gsd2FighterRecord>();
    private readonly HashSet<int> _joiningWeaponInitializedDonors = new();
    private bool _newGamePending, _loading, _busy, _saveScope;
    private string _partySignature = "";
    private readonly Gsd2CampStart _campStart = new();
    private bool _campSpawnPending, _campInitialSnapshotBound;
    private IntPtr _campMap;
    private readonly List<object> _retiredNativeState = new();
    private sealed class SuspendedPublication
    {
        internal IntPtr Game;
        internal GSD2.CHARA_DATA Baseline = null!, Projected = null!;
        internal Il2CppStructArray<int> Names = null!;
        internal Il2CppReferenceArray<GSD2.ARMS_DATA> Arms = null!;
    }
    private readonly List<SuspendedPublication> _suspendedPublications = new();
    internal bool NeedsCampInitialSavePosition => _campStart.IsApplied && !_campInitialSnapshotBound;
    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions { WriteIndented = true, PropertyNameCaseInsensitive = false };

    internal int MapDisplayId(int canonical)
    {
        if (!TablesReady) return canonical;
        return Profile.Mode == "Shuffle" ? Profile.GetDonor(canonical) : _fixed?.GetDonor(canonical) ?? canonical;
    }
    internal bool IsRole(int canonical) => IsAdmitted && (Profile.Mode == "Shuffle" ? Profile.IsRole(canonical) : _fixed?.Assignments.ContainsKey(canonical) == true);
    internal void Trace(string message) => Gsd2Plugin.PluginLog.LogInfo(message);
    internal void Fail(string message)
    {
        if (FaultReason is null) Gsd2Plugin.PluginLog.LogError(message);
        FaultReason = message;
    }
    internal void BeginNewGame()
    {
        if (!ObserveOwnedBoundary()) return;
        RestorePublishedTables();
        Profile = Gsd2Plugin.Mode switch
        {
            "Shuffle" => Gsd2AvatarProfile.Create(Gsd2Plugin.Seed, Gsd2Plugin.IncludeProtagonist),
            "FixedChosen" => Gsd2AvatarProfile.CreateChosen(Gsd2Plugin.Seed, Gsd2Plugin.Companions.Split(',').Select(value => int.Parse(value.Trim(), System.Globalization.CultureInfo.InvariantCulture))),
            "FixedRandom" => Gsd2AvatarProfile.CreateRandom(Gsd2Plugin.Seed),
            _ => throw new InvalidDataException("Unsupported Suikoden II mode: " + Gsd2Plugin.Mode)
        };
        HasAdmittedCampaignBeforeSuspend = false;
        FaultReason = null; IsNewCampaign = true; _newGamePending = true; _loading = false;
        _baseline = null; _originalNames = null; _originalArms = null; _observedData = IntPtr.Zero;
        ActiveStartPoint = Gsd2Plugin.StartPoint;
        ActiveJoiningWeaponLevel = ReadJoiningWeaponPolicy(Gsd2Plugin.JoiningWeaponLevel, false);
        _joiningWeaponInitializedDonors.Clear();
        ResetJoiningEquipment();
        ActiveJoiningEquipment = ReadJoiningEquipmentPolicy(Gsd2Plugin.JoiningEquipment, false);
        _campStart.Arm(ActiveStartPoint == Gsd2CampStart.Point);
        _campSpawnPending = false; _campInitialSnapshotBound = false; _campMap = IntPtr.Zero;
        _fixed = Profile.Mode == "Shuffle" ? null : Gsd2FixedRosterAssignments.Empty(Profile);
        _fighters.Clear(); _canonicalRoles.Clear(); _partySignature = ""; Gsd2Graphics.Reset();
        Trace("NEW GSD2 campaign " + Profile.ProfileId + "; seed=" + Profile.Seed + "; fingerprint=" + Profile.Fingerprint);
    }
    internal void CancelPendingNewGame()
    {
        if (!ObserveOwnedBoundary()) return;
        HasAdmittedCampaignBeforeSuspend = false;
        _campStart.Cancel(); _campSpawnPending = false; _campMap = IntPtr.Zero; _campInitialSnapshotBound = false;
        if (!_newGamePending && Profile is null && _projected is null) return;
        RestorePublishedTables();
        Profile = null!; _newGamePending = false; _fixed = null; _fighters.Clear(); _canonicalRoles.Clear(); IsNewCampaign = false;
        ActiveJoiningWeaponLevel = "Donor"; _joiningWeaponInitializedDonors.Clear();
        ResetJoiningEquipment();
        _baseline = null; _originalNames = null; _originalArms = null; _observedData = IntPtr.Zero;
        _partySignature = "";
        Gsd2Graphics.Reset();
    }
    internal void ObserveNativeDataset(bool refresh = false)
    {
        if (!OwnsExecution) return;
        var game = GSD2.OldSrcBase.game_work;
        var data = game?.chara_data;
        if (data is null || data.c_varia_dat?.Count < 84 || data.c_kotei_dat?.Count < 84 || data.ckd_info?.Count < 84) return;
        if (_projected is not null && data.Pointer == _projected.Pointer) return;
        if (!refresh && _observedData == data.Pointer && _baseline is not null) return;
        _baseline = Gsd2NativeData.Characters(data);
        var names = game!.c_teg_dat?.name;
        if (names is null || names.Count < 84) throw new InvalidDataException("Native Suikoden II name table is incomplete.");
        _originalNames = Gsd2NativeData.Names(names); _observedData = data.Pointer;
        _originalArms = Gsd2NativeData.Arms(game.arms_data);
        Trace("Captured detached original Suikoden II character tables.");
    }
    internal void CompleteNativeNewGame(bool finalNativeBoundary = false)
    {
        if (!_newGamePending || !IsAdmitted) return;
        // The legacy helper can run before options/import are finished. Camp
        // restoration is admitted only by the final modern New Game boundary.
        if (ActiveStartPoint == Gsd2CampStart.Point && !finalNativeBoundary) return;
        // Refresh at the final native boundary: initialization/import may have
        // updated metadata, names or weapon templates without replacing the object.
        ObserveNativeDataset(true);
        var game = GSD2.OldSrcBase.game_work;
        if (_baseline is null || game?.chara_data is null) return;
        if (ActiveStartPoint == Gsd2CampStart.Point)
        {
            try
            {
                Gsd2RejectedSaveMirrorGuard.Ensure();
                Trace(Gsd2RejectedSaveMirrorGuard.Status);
                Gsd2CampNativeGuard.ValidateFresh(game);
                if (!_campStart.ApplyAtNativeBoundary(game))
                    throw new InvalidOperationException("Camp start was not armed at the native initialization boundary.");
                _campSpawnPending = true;
                Trace("CAMP START applied before first native field initialization; fresh character data and choices retained.");
            }
            catch (Exception exception) { Fail("Suikoden II camp initialization refused: " + exception.Message); throw; }
        }
        // Capture native initialization/import results before applying any part of a permutation.
        var starts = Gsd2NativeData.Characters(game.chara_data);
        CaptureOriginalRoleEquipment(starts);
        var next = Gsd2NativeData.Characters(starts);
        var weaponInitializations = new List<(int Donor, string Log)>();
        if (Profile.Mode == "Shuffle")
        {
            foreach (var assignment in Profile.Assignments)
                if (!KeepCanonical(assignment.Key, assignment.Value))
                {
                    next.c_varia_dat[assignment.Key] = Gsd2NativeData.Variable(starts.c_varia_dat[assignment.Value]);
                    InitializeJoiningWeapon(next.c_varia_dat[assignment.Key], assignment.Key, assignment.Value,
                        starts.c_varia_dat[assignment.Key].buki_lv, weaponInitializations);
                }
            // RiouVariance is deferred until a donor first occupies a resident
            // party role. Capture Riou before any of those weapon writes.
            InitializeJoinedShuffleWeapons(next, weaponInitializations);
        }
        else
        {
            foreach (int donor in Profile.HeroIds)
                _fighters.Add(donor, Gsd2FighterRecord.Capture(starts.c_varia_dat[donor]));
            for (int role = 1; role <= 83; role++)
                _canonicalRoles.Add(role, Gsd2FighterRecord.Capture(starts.c_varia_dat[role]));
            ValidateFixedRecords();
        }
        Publish(next); _newGamePending = false;
        CommitJoiningWeaponInitializations(weaponInitializations);
        if (_fixed is not null) ReconcileFixedParty(true);
        else ObserveJoinedEquipment();
    }
    internal void ObserveCampMap(GSD2.MACHICON map)
    {
        if (!OwnsExecution || !_campSpawnPending || FaultReason is not null) return;
        try
        {
            if (_campMap != IntPtr.Zero) throw new InvalidOperationException("A second map initialized before the first camp spawn was verified.");
            Gsd2CampNativeGuard.ValidateInitialMap(map); _campMap = map.Pointer;
            Trace("CAMP START native map initialization matched the captured interior.");
        }
        catch (Exception exception) { Fail("Suikoden II camp map verification: " + exception.Message); }
    }
    internal void VerifyCampPlayer(GSD2.MACHICON map)
    {
        if (!OwnsExecution || !_campSpawnPending || FaultReason is not null) return;
        try
        {
            if (_campMap == IntPtr.Zero || map.Pointer != _campMap)
                throw new InvalidOperationException("Camp field player has no matching initial map observation.");
            if (!Gsd2CampNativeGuard.TryVerifyPlayer(map)) return;
            _campSpawnPending = false;
            Trace("CAMP START first native player position verified at167,206 before field input.");
        }
        catch (Exception exception) { Fail("Suikoden II camp spawn verification: " + exception.Message); }
    }
    internal void MarkCampInitialSnapshotBound()
    {
        if (!NeedsCampInitialSavePosition) throw new InvalidOperationException("Camp initial snapshot was already consumed or is not active.");
        _campInitialSnapshotBound = true;
        Trace("CAMP START initial autosave position and campaign binding verified.");
    }
    internal void Tick()
    {
        if (!OwnsExecution) { SuspendExecution(); return; }
        if (!IsAdmitted || _busy || _saveScope || _loading || _newGamePending) return;
        _busy = true;
        try
        {
            if (_fixed is not null && _projected is not null) ReconcileFixedParty(false);
            else ObserveJoinedShuffleWeapons();
            ObserveJoinedEquipment();
        }
        catch (Exception exception) { Fail("Suikoden II party projection failed: " + exception.Message); }
        finally { _busy = false; }
    }
    private static bool KeepCanonical(int role, int donor) => role == 1 || donor == 1 || role >= 80;
    private static string ReadJoiningWeaponPolicy(string? value, bool saved)
    {
        // Bindings made before this policy existed retain their earned state and
        // original donor starts, including donors not assigned in a fixed party yet.
        if (saved && (value is null || value == "Donor")) return "Donor";
        if (value == "RiouVariance" || value == "MatchRole" || value == "Level1") return value;
        throw new InvalidDataException("Unsupported Suikoden II joining weapon policy.");
    }
    private void InitializeJoiningWeapon(GSD2.C_VARIA_DAT fighter, int role, int donor, byte nativeRoleLevel,
        List<(int Donor, string Log)> pending, int? riouReference = null)
    {
        if (ActiveJoiningWeaponLevel == "Donor" || KeepCanonical(role, donor)
            || _joiningWeaponInitializedDonors.Contains(donor) || pending.Any(entry => entry.Donor == donor)) return;
        if (ActiveJoiningWeaponLevel == "RiouVariance" && riouReference is null) return;
        if (_baseline is null) throw new InvalidOperationException("Original Suikoden II weapon metadata unavailable.");
        byte before = fighter.buki_lv;
        int delta = ActiveJoiningWeaponLevel == "RiouVariance" ? JoiningWeaponDelta(role, donor) : 0;
        // Natural-weapon class 6 is read as weapon level 0 by native G2_arms_lv.
        // A normal weapon replacing a natural-weapon role must remain at least 1.
        byte after = (_baseline.ckd_info[donor][1] & 15) == 6 ? (byte)0
            : ActiveJoiningWeaponLevel == "Level1" ? (byte)1
            : ActiveJoiningWeaponLevel == "RiouVariance" ? (byte)Math.Clamp(riouReference!.Value + delta, 1, 16)
            : (byte)Math.Clamp((int)nativeRoleLevel, 1, 16);
        fighter.buki_lv = after;
        pending.Add((donor, "JOIN WEAPON S2 role=" + role + "; donor=" + donor + "; nativeRole=" + nativeRoleLevel
            + "; donorBefore=" + before + "; after=" + after + "; policy=" + ActiveJoiningWeaponLevel
            + (ActiveJoiningWeaponLevel == "RiouVariance" ? "; Riou=" + riouReference + "; delta=" + delta : "")));
    }
    internal int JoiningWeaponDelta(int role, int donor)
    {
        // A separate deterministic stream avoids rerolls on party reordering,
        // saving, loading or unrelated randomizer draws. Fixed fighters keep
        // the same offset even when first assigned to a different story role.
        string key = "srr-s2-riou-weapon-v1|" + Profile.Seed.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + ":" + Profile.Seed + "|" + Profile.Fingerprint + "|"
            + (Profile.Mode == "Shuffle" ? role.ToString(System.Globalization.CultureInfo.InvariantCulture) : "fixed")
            + "|" + donor.ToString(System.Globalization.CultureInfo.InvariantCulture) + "|";
        using var hash = SHA256.Create();
        for (int counter = 0; ; counter++)
            foreach (byte value in hash.ComputeHash(Encoding.UTF8.GetBytes(key + counter.ToString(System.Globalization.CultureInfo.InvariantCulture))))
                if (value < 252) return value % 7 - 3;
    }
    private static int RiouWeaponReference(GSD2.CHARA_DATA data)
    {
        // Canonical role1 keeps its native combat setup even when Riou's
        // appearance is shuffled, so it is the same reference S1 uses for Tir.
        int value = data.c_varia_dat[Gsd2CharacterCatalog.ProtagonistId].buki_lv;
        if (value < 1 || value > 16) throw new InvalidOperationException("Current canonical Riou weapon level is unavailable.");
        return value;
    }
    private int[] PendingJoinedShuffleRoles()
    {
        if (Profile.Mode != "Shuffle" || ActiveJoiningWeaponLevel != "RiouVariance") return Array.Empty<int>();
        var slots = GSD2.OldSrcBase.game_work?.party_data?.party_cha_no;
        if (slots is null || slots.Count < 6) throw new InvalidOperationException("Joining weapon party state is incomplete.");
        return Enumerable.Range(0, 6).Select(index => (int)slots[index]).Distinct()
            .Where(role => Profile.IsRole(role) && !KeepCanonical(role, Profile.GetDonor(role))
                && !_joiningWeaponInitializedDonors.Contains(Profile.GetDonor(role))).ToArray();
    }
    private void InitializeJoinedShuffleWeapons(GSD2.CHARA_DATA data, List<(int Donor, string Log)> pending)
    {
        int[] roles = PendingJoinedShuffleRoles();
        if (roles.Length == 0) return;
        int reference = RiouWeaponReference(data);
        foreach (int role in roles)
            InitializeJoiningWeapon(data.c_varia_dat[role], role, Profile.GetDonor(role),
                _baseline!.c_varia_dat[role].buki_lv, pending, reference);
    }
    private void ObserveJoinedShuffleWeapons()
    {
        if (_projected is null) return;
        int[] roles = PendingJoinedShuffleRoles();
        if (roles.Length == 0) return;
        var game = GSD2.OldSrcBase.game_work;
        var current = game?.chara_data;
        if (current is null || current.Pointer != _projected.Pointer)
            throw new InvalidOperationException("Joining weapon character publication is not current.");
        int reference = RiouWeaponReference(current);
        string party = string.Join(",", game!.party_data.party_cha_no.Take(6));
        var before = roles.ToDictionary(role => role, role => (Row: current.c_varia_dat[role], Level: current.c_varia_dat[role].buki_lv));
        var desired = new Dictionary<int, byte>();
        var pending = new List<(int Donor, string Log)>();
        foreach (int role in roles)
        {
            var detached = Gsd2NativeData.Variable(before[role].Row);
            InitializeJoiningWeapon(detached, role, Profile.GetDonor(role), _baseline!.c_varia_dat[role].buki_lv, pending, reference);
            desired.Add(role, detached.buki_lv);
        }
        // Update only weapon fields in place: battle/event coroutines may retain
        // references to these native rows, so publishing a replacement table is unsafe.
        try
        {
            foreach (var entry in desired) before[entry.Key].Row.buki_lv = entry.Value;
            foreach (var entry in desired)
                if (current.c_varia_dat[entry.Key].Pointer != before[entry.Key].Row.Pointer || before[entry.Key].Row.buki_lv != entry.Value)
                    throw new InvalidOperationException("Joining weapon write did not verify.");
            if (!OwnsExecution || game.chara_data.Pointer != current.Pointer || party != string.Join(",", game.party_data.party_cha_no.Take(6)))
                throw new InvalidOperationException("Joining weapon party changed during initialization.");
        }
        catch
        {
            foreach (var entry in before) entry.Value.Row.buki_lv = entry.Value.Level;
            throw;
        }
        CommitJoiningWeaponInitializations(pending);
    }
    private void CommitJoiningWeaponInitializations(List<(int Donor, string Log)> pending)
    {
        foreach (var entry in pending) { _joiningWeaponInitializedDonors.Add(entry.Donor); Trace(entry.Log); }
    }
    internal IReadOnlyDictionary<int, int> Assignments => Profile.Mode == "Shuffle" ? Profile.Assignments : _fixed!.Assignments;
    private void Publish(GSD2.CHARA_DATA next)
    {
        var game = GSD2.OldSrcBase.game_work ?? throw new InvalidOperationException("No native Suikoden II game state.");
        if (_baseline is null || _originalNames is null || _originalArms is null) throw new InvalidOperationException("Original Suikoden II templates unavailable.");
        // A departed fixed role must lose all donor static metadata. Preserve
        // earned state, but rebuild every static row from the canonical baseline.
        var rebased = Gsd2NativeData.Characters(_baseline);
        rebased.c_varia_dat = next.c_varia_dat;
        rebased.suki_value = next.suki_value;
        next = rebased;
        var names = Gsd2NativeData.Names(_originalNames);
        var arms = Gsd2NativeData.Arms(_originalArms);
        foreach (var assignment in Assignments)
        {
            int role = assignment.Key, donor = assignment.Value;
            if (role != 1) names[role] = _originalNames[donor];
            var row = Gsd2NativeData.Row(_baseline.ckd_info[KeepCanonical(role, donor) ? role : donor]);
            if (Profile.Mode != "Shuffle")
                row[1] = (byte)((row[1] & ~0x60) | (_baseline.ckd_info[role][1] & 0x60));
            next.ckd_info[role] = row;
            next.c_kotei_dat[role] = Gsd2NativeData.Fixed(_baseline.c_kotei_dat[KeepCanonical(role, donor) ? role : donor]);
            arms[role] = Gsd2NativeData.Arm(_originalArms[KeepCanonical(role, donor) ? role : donor]);
        }
        var previous = game.chara_data; var previousNames = game.c_teg_dat.name; var previousArms = game.arms_data;
        var namesPointer = names.Pointer;
        try { game.chara_data = next; game.c_teg_dat.name = names; game.arms_data = arms; _projected = next; _publishedGame = game.Pointer; _publishedNames = namesPointer; }
        catch { game.chara_data = previous; game.c_teg_dat.name = previousNames; game.arms_data = previousArms; throw; }
    }
    private void CaptureFighters()
    {
        var current = GSD2.OldSrcBase.game_work?.chara_data;
        if (_fixed is null || current is null) return;
        foreach (var assignment in _fixed.Assignments)
            if (KeepCanonical(assignment.Key, assignment.Value))
                _canonicalRoles[assignment.Key] = Gsd2FighterRecord.Capture(current.c_varia_dat[assignment.Key]);
            else
                _fighters[assignment.Value] = Gsd2FighterRecord.Capture(current.c_varia_dat[assignment.Key]);
    }
    private void ValidateFixedRecords()
    {
        if (_baseline is null) throw new InvalidOperationException("Canonical combat metadata unavailable.");
        foreach (var record in _fighters.Concat(_canonicalRoles))
        {
            record.Value.Validate();
            if (record.Value.WeaponLevel == 0 && (_baseline.ckd_info[record.Key][1] & 15) != 6)
                throw new InvalidDataException("An armed Suikoden II fighter has an invalid zero weapon level.");
        }
    }
    private void ReconcileFixedParty(bool force)
    {
        var game = GSD2.OldSrcBase.game_work;
        var slots = game?.party_data?.party_cha_no;
        if (_fixed is null || game?.chara_data is null || slots is null || slots.Count < 6) return;
        int[] roles = Enumerable.Range(0, 6).Select(index => (int)slots[index]).Where(id => id > 0).Distinct().ToArray();
        if (roles.Length == 0) return;
        string signature = string.Join(",", roles);
        if (!force && signature == _partySignature) return;
        var prior = _fixed;
        var nextAssignments = prior.Reconcile(roles);
        if (!force && ReferenceEquals(prior, nextAssignments))
        {
            _partySignature = signature;
            return;
        }
        CaptureFighters();
        var next = Gsd2NativeData.Characters(game.chara_data);
        int? riouReference = ActiveJoiningWeaponLevel == "RiouVariance" ? RiouWeaponReference(next) : null;
        var weaponInitializations = new List<(int Donor, string Log)>();
        var pendingEquipmentBefore = _pendingJoiningEquipment.ToDictionary(pair => pair.Key, pair => pair.Value.Copy());
        // Native events can update an inactive role before adding it to the party.
        foreach (int role in roles)
            if (!prior.Assignments.ContainsKey(role))
                _canonicalRoles[role] = Gsd2FighterRecord.Capture(next.c_varia_dat[role]);
        foreach (int departed in prior.Assignments.Keys.Where(role => !nextAssignments.Assignments.ContainsKey(role)))
            next.c_varia_dat[departed] = _canonicalRoles[departed].CreateNative();
        foreach (var assignment in nextAssignments.Assignments)
        {
            if (!prior.Assignments.TryGetValue(assignment.Key, out int old) || old != assignment.Value)
            {
                next.c_varia_dat[assignment.Key] = (KeepCanonical(assignment.Key, assignment.Value)
                    ? _canonicalRoles[assignment.Key] : _fighters[assignment.Value]).CreateNative();
                InitializeJoiningWeapon(next.c_varia_dat[assignment.Key], assignment.Key, assignment.Value,
                    _canonicalRoles[assignment.Key].WeaponLevel, weaponInitializations, riouReference);
                RememberFirstEquipment(assignment.Key, assignment.Value,
                    Gsd2EquipmentSnapshot.Capture(_canonicalRoles[assignment.Key].CreateNative()),
                    Gsd2EquipmentSnapshot.Capture(next.c_varia_dat[assignment.Key]));
            }
        }
        ValidateFixedRecords();
        _fixed = nextAssignments;
        try { Publish(next); CommitJoiningWeaponInitializations(weaponInitializations); _partySignature = signature; Gsd2Graphics.Reset(); }
        catch { _fixed = prior; _pendingJoiningEquipment = pendingEquipmentBefore; throw; }
        ObserveJoinedEquipment();
    }
    internal void PrepareLoad(string json)
    {
        if (!ObserveOwnedBoundary()) return;
        HasAdmittedCampaignBeforeSuspend = false;
        _campStart.Cancel(); _campSpawnPending = false; _campMap = IntPtr.Zero; _campInitialSnapshotBound = false;
        RestorePublishedTables(); Gsd2Graphics.Reset(); FaultReason = null; _projected = null;
        _baseline = null; _originalNames = null; _originalArms = null; _observedData = IntPtr.Zero;
        IsNewCampaign = false; _newGamePending = false; _loading = true; _partySignature = "";
        Profile = null!; _fixed = null; _fighters.Clear(); _canonicalRoles.Clear();
        ActiveJoiningWeaponLevel = "Donor"; _joiningWeaponInitializedDonors.Clear();
        ResetJoiningEquipment();
        string identity = Hash(json); string path = BindingPath(identity);
        if (!File.Exists(path)) { Trace("Unbound Suikoden II save: original behavior retained."); return; }
        var binding = ReadBinding(path);
        if (binding.Schema != 1 || binding.Game != "GSD2" || binding.SaveIdentity != identity || binding.Build != global::SuikodenRandomizer.Plugin.BuildCompatibility.GameAssemblySha256 || !binding.Initialized)
            throw new InvalidDataException("Suikoden II campaign identity/build does not match this save.");
        Profile = binding.Mode switch
        {
            "Shuffle" => Gsd2AvatarProfile.Create(binding.Seed, binding.IncludeProtagonist),
            "FixedChosen" => Gsd2AvatarProfile.CreateChosen(binding.Seed, binding.Companions),
            "FixedRandom" => Gsd2AvatarProfile.CreateRandom(binding.Seed),
            _ => throw new InvalidDataException("Invalid saved Suikoden II mode.")
        };
        if (Profile.Fingerprint != binding.ProfileFingerprint) throw new InvalidDataException("Suikoden II profile fingerprint mismatch.");
        if (binding.StartPoint != "Opening" && binding.StartPoint != Gsd2CampStart.Point) throw new InvalidDataException("Saved Suikoden II starting-point policy is unsupported by this build.");
        ActiveStartPoint = binding.StartPoint;
        ActiveJoiningWeaponLevel = ReadJoiningWeaponPolicy(binding.JoiningWeaponLevel, true);
        if (binding.JoiningWeaponInitializedDonors is null
            || binding.JoiningWeaponInitializedDonors.Distinct().Count() != binding.JoiningWeaponInitializedDonors.Length
            || (ActiveJoiningWeaponLevel == "Donor" && binding.JoiningWeaponInitializedDonors.Length != 0))
            throw new InvalidDataException("Saved Suikoden II joining weapon initialization records are invalid.");
        var allowedDonors = Profile.Mode == "Shuffle" ? Profile.Assignments.Values : Profile.HeroIds;
        foreach (int donor in binding.JoiningWeaponInitializedDonors)
        {
            if (!allowedDonors.Contains(donor)) throw new InvalidDataException("Saved Suikoden II joining weapon donor is outside this profile.");
            _joiningWeaponInitializedDonors.Add(donor);
        }
        if (Profile.Mode != "Shuffle")
        {
            _fixed = Gsd2FixedRosterAssignments.Restore(Profile, binding.Assignments);
            if (!Profile.HeroIds.OrderBy(id => id).SequenceEqual(binding.Fighters.Keys.OrderBy(id => id)))
                throw new InvalidDataException("Fixed fighter record identities do not match the saved roster.");
            foreach (var record in binding.Fighters) record.Value.Validate();
            _fighters = binding.Fighters;
            if (!Enumerable.Range(1, 83).SequenceEqual(binding.CanonicalRoles.Keys.OrderBy(id => id)))
                throw new InvalidDataException("Saved canonical Suikoden II role records are incomplete.");
            foreach (var record in binding.CanonicalRoles) record.Value.Validate();
            _canonicalRoles = binding.CanonicalRoles;
        }
        if (ActiveJoiningWeaponLevel != "Donor" && !(Profile.Mode == "Shuffle" && ActiveJoiningWeaponLevel == "RiouVariance")
            && Assignments.Any(pair => !KeepCanonical(pair.Key, pair.Value) && !_joiningWeaponInitializedDonors.Contains(pair.Value)))
            throw new InvalidDataException("Saved Suikoden II joining weapon initialization records are incomplete.");
        RestoreJoiningEquipment(binding);
        Trace("Restored Suikoden II campaign " + Profile.Fingerprint + "; current config ignored.");
    }
    internal void CompleteLoad(bool success)
    {
        if (!OwnsExecution) return;
        _loading = false;
        if (!success) { Fail("Native Suikoden II load failed."); return; }
        if (!IsAdmitted) return;
        // A load restores persistent, canonical-indexed state. Donor starts are NEVER reapplied.
        ObserveNativeDataset();
        if (_fixed is not null) ValidateFixedRecords();
        Publish(Gsd2NativeData.Characters(GSD2.OldSrcBase.game_work.chara_data));
        if (_fixed is null && PendingJoinedShuffleRoles().Length != 0)
            throw new InvalidDataException("Saved Suikoden II resident joining weapon records are incomplete.");
        if (PendingJoinedEquipmentRoles().Any(role => !_pendingJoiningEquipment.ContainsKey(EquipmentDonor(role))))
            throw new InvalidDataException("Saved Suikoden II resident recruitment equipment records are incomplete.");
        if (_fixed is not null) ReconcileFixedParty(true);
    }
    private GSD2.CHARA_DATA? _beforeSave;
    private Il2CppStructArray<int>? _namesBeforeSave;
    private Il2CppReferenceArray<GSD2.ARMS_DATA>? _armsBeforeSave;
    internal void BeginSave()
    {
        if (!OwnsExecution) return;
        if (FaultReason is not null) throw new InvalidOperationException(FaultReason);
        if (!IsAdmitted) return;
        if (!TablesReady || _newGamePending || _baseline is null) throw new InvalidOperationException("Suikoden II campaign is not initialized.");
        if (_saveScope) throw new InvalidOperationException("Nested Suikoden II save is unsupported.");
        // Fix's managed postfix can run even when another prefix rejects the
        // shared native store. Guard it before every admitted save, not only camp.
        Gsd2RejectedSaveMirrorGuard.Ensure();
        if (_fixed is not null) ReconcileFixedParty(false);
        else ObserveJoinedShuffleWeapons();
        ObserveJoinedEquipment();
        CaptureFighters();
        var game = GSD2.OldSrcBase.game_work;
        var canonical = Gsd2NativeData.Characters(_baseline);
        canonical.c_varia_dat = game.chara_data.c_varia_dat;
        canonical.suki_value = game.chara_data.suki_value;
        var names = Gsd2NativeData.Names(_originalNames!); var arms = Gsd2NativeData.Arms(_originalArms!);
        _beforeSave = game.chara_data; _namesBeforeSave = game.c_teg_dat.name; _armsBeforeSave = game.arms_data;
        _saveScope = true;
        try { game.chara_data = canonical; game.c_teg_dat.name = names; game.arms_data = arms; }
        catch { EndSave(); throw; }
    }
    internal void EndSave()
    {
        if (!OwnsExecution) return;
        if (!_saveScope) return;
        var game = GSD2.OldSrcBase.game_work;
        try { game.chara_data = _beforeSave; game.c_teg_dat.name = _namesBeforeSave; game.arms_data = _armsBeforeSave; }
        finally { _beforeSave = null; _namesBeforeSave = null; _armsBeforeSave = null; _saveScope = false; }
    }
    internal bool BindSnapshot(string json)
    {
        if (!OwnsExecution) return false;
        if (!IsAdmitted) throw new InvalidOperationException(FaultReason ?? "No admitted Suikoden II campaign can bind this snapshot.");
        if (!_saveScope) throw new InvalidOperationException("No native Suikoden II save scope.");
        long epoch = _executionEpoch;
        var binding = new Gsd2CampaignBinding
        {
            Initialized = true, SaveIdentity = Hash(json), Build = global::SuikodenRandomizer.Plugin.BuildCompatibility.GameAssemblySha256,
            Seed = Profile.Seed, Mode = Profile.Mode, IncludeProtagonist = Profile.IncludesProtagonist,
            Companions = Profile.CompanionIds.ToArray(), ProfileFingerprint = Profile.Fingerprint, StartPoint = ActiveStartPoint,
            JoiningWeaponLevel = ActiveJoiningWeaponLevel, JoiningWeaponInitializedDonors = _joiningWeaponInitializedDonors.OrderBy(id => id).ToArray(),
            JoiningEquipment = ActiveJoiningEquipment, JoiningEquipmentInitializedDonors = _joiningEquipmentInitializedDonors.OrderBy(id => id).ToArray(),
            OriginalRoleEquipment = _originalRoleEquipment.ToDictionary(pair => pair.Key, pair => pair.Value.Copy()),
            PendingJoiningEquipment = _pendingJoiningEquipment.ToDictionary(pair => pair.Key, pair => pair.Value.Copy()),
            Assignments = _fixed?.Assignments.ToDictionary(pair => pair.Key, pair => pair.Value) ?? new Dictionary<int, int>(), Fighters = _fighters,
            CanonicalRoles = _canonicalRoles
        };
        string path = BindingPath(binding.SaveIdentity);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string payload = BindingJson(binding);
        if (File.Exists(path)) RequireMatchingBinding(path, payload);
        else
        {
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true);
                    writer.Write(payload);
                    writer.Flush();
                    stream.Flush(true);
                }
                try { File.Move(temporary, path); }
                catch (IOException) when (File.Exists(path)) { RequireMatchingBinding(path, payload); }
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        return MatchesExecution(epoch);
    }

    private static Gsd2CampaignBinding ReadBinding(string path)
    {
        using var stream = File.OpenRead(path);
        if (stream.Length > 4 * 1024 * 1024) throw new InvalidDataException("Suikoden II binding is too large.");
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return JsonSerializer.Deserialize<Gsd2CampaignBinding>(reader.ReadToEnd(), JsonOptions)
            ?? throw new InvalidDataException("Empty Suikoden II campaign binding.");
    }

    private static void RequireMatchingBinding(string path, string expected)
    {
        if (BindingJson(ReadBinding(path)) != expected)
            throw new InvalidDataException("This native Suikoden II snapshot already belongs to a different campaign or policy state.");
    }

    private static string BindingJson(Gsd2CampaignBinding binding)
    {
        // Older bindings omit the donor defaults. Dictionary insertion order and
        // the order of initialized donor sets have no effect on a restored game.
        binding.JoiningWeaponLevel ??= "Donor";
        binding.JoiningEquipment ??= "Donor";
        binding.JoiningWeaponInitializedDonors = binding.JoiningWeaponInitializedDonors.OrderBy(id => id).ToArray();
        binding.JoiningEquipmentInitializedDonors = binding.JoiningEquipmentInitializedDonors.OrderBy(id => id).ToArray();
        binding.Assignments = Ordered(binding.Assignments);
        binding.Fighters = Ordered(binding.Fighters);
        binding.CanonicalRoles = Ordered(binding.CanonicalRoles);
        binding.OriginalRoleEquipment = Ordered(binding.OriginalRoleEquipment);
        binding.PendingJoiningEquipment = Ordered(binding.PendingJoiningEquipment);
        return JsonSerializer.Serialize(binding, JsonOptions);

        static Dictionary<int, T> Ordered<T>(Dictionary<int, T> values) =>
            values.OrderBy(pair => pair.Key).ToDictionary(pair => pair.Key, pair => pair.Value);
    }
    private void RestorePublishedTables()
    {
        if (!OwnsExecution) return;
        var game = GSD2.OldSrcBase.game_work;
        if (game is not null && game.Pointer == _publishedGame && _projected is not null && game.chara_data?.Pointer == _projected.Pointer && _baseline is not null)
        {
            // Preserve all earned state while removing this campaign's static projections.
            var canonical = Gsd2NativeData.Characters(_baseline); canonical.c_varia_dat = game.chara_data.c_varia_dat;
            canonical.suki_value = game.chara_data.suki_value;
            game.chara_data = canonical;
            if (_originalNames is not null) game.c_teg_dat.name = Gsd2NativeData.Names(_originalNames);
            if (_originalArms is not null) game.arms_data = Gsd2NativeData.Arms(_originalArms);
        }
        _projected = null; _publishedGame = IntPtr.Zero; _publishedNames = IntPtr.Zero;
    }
    // Only native title/new-game/load boundaries may accept a new ownership epoch.
    internal bool ObserveOwnedBoundary()
    {
        if (!ExecutionAllowed()) return false;
        long epoch = OwnershipEpoch();
        if (_executionSuspended || _executionEpoch != epoch) SuspendExecution();
        _executionEpoch = epoch; _executionSuspended = false;
        RestoreSuspendedPublications();
        return OwnsExecution;
    }
    private void RestoreSuspendedPublications()
    {
        if (_suspendedPublications.Count == 0 || !OwnsExecution) return;
        var game = GSD2.OldSrcBase.game_work;
        if (game is null) return;
        foreach (var old in _suspendedPublications)
        {
            if (game.Pointer != old.Game || game.chara_data?.Pointer != old.Projected.Pointer) continue;
            var previous = game.chara_data; var previousNames = game.c_teg_dat.name; var previousArms = game.arms_data;
            var canonical = Gsd2NativeData.Characters(old.Baseline);
            canonical.c_varia_dat = previous.c_varia_dat; canonical.suki_value = previous.suki_value;
            var names = Gsd2NativeData.Names(old.Names); var arms = Gsd2NativeData.Arms(old.Arms);
            try { game.chara_data = canonical; game.c_teg_dat.name = names; game.arms_data = arms; }
            catch { game.chara_data = previous; game.c_teg_dat.name = previousNames; game.arms_data = previousArms; throw; }
        }
        _suspendedPublications.Clear();
    }
    // Called by the host on exit even though the inactive engine is no longer ticked.
    // Do not query predicates, native getters, resident pointers or Unity objects here.
    internal void SuspendExecution()
    {
        if (_executionSuspended) return;
        _executionSuspended = true;
        if (Profile is not null) HasAdmittedCampaignBeforeSuspend = true;
        if (_publishedGame != IntPtr.Zero && _baseline is not null && _projected is not null
            && _originalNames is not null && _originalArms is not null)
            _suspendedPublications.Add(new SuspendedPublication { Game = _publishedGame,
                Baseline = _baseline, Projected = _projected, Names = _originalNames, Arms = _originalArms });
        foreach (object? value in new object?[] { _baseline, _projected, _originalNames, _originalArms,
            _beforeSave, _namesBeforeSave, _armsBeforeSave })
            if (value is not null) _retiredNativeState.Add(value);
        Profile = null!; FaultReason = null; IsNewCampaign = false;
        ActiveJoiningWeaponLevel = "Donor"; _joiningWeaponInitializedDonors.Clear();
        ResetJoiningEquipment();
        _baseline = null; _projected = null; _originalNames = null; _originalArms = null;
        _beforeSave = null; _namesBeforeSave = null; _armsBeforeSave = null;
        _observedData = IntPtr.Zero; _publishedGame = IntPtr.Zero; _publishedNames = IntPtr.Zero; _fixed = null; _fighters.Clear(); _canonicalRoles.Clear();
        _newGamePending = false; _loading = false; _busy = false; _saveScope = false;
        _partySignature = ""; _campStart.Cancel(); _campSpawnPending = false;
        _campInitialSnapshotBound = false; _campMap = IntPtr.Zero;
        Gsd2Graphics.Suspend();
    }
    internal static string Hash(string text)
    {
        using var sha = SHA256.Create(); return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    }
    private static string BindingPath(string hash) => Path.Combine(BepInEx.Paths.ConfigPath, "SuikodenRandomizer", "gsd2-campaigns", hash + ".json");
}

internal sealed class Gsd2CampaignBinding
{
    public int Schema { get; set; } = 1;
    public string Game { get; set; } = "GSD2";
    public bool Initialized { get; set; }
    public string SaveIdentity { get; set; } = "";
    public string Build { get; set; } = "";
    public string Seed { get; set; } = "";
    public string Mode { get; set; } = "";
    public bool IncludeProtagonist { get; set; }
    public int[] Companions { get; set; } = Array.Empty<int>();
    public string ProfileFingerprint { get; set; } = "";
    public string StartPoint { get; set; } = "Opening";
    public string? JoiningWeaponLevel { get; set; }
    public int[] JoiningWeaponInitializedDonors { get; set; } = Array.Empty<int>();
    public string? JoiningEquipment { get; set; }
    public int[] JoiningEquipmentInitializedDonors { get; set; } = Array.Empty<int>();
    public Dictionary<int, Gsd2EquipmentSnapshot> OriginalRoleEquipment { get; set; } = new();
    public Dictionary<int, Gsd2PendingEquipment> PendingJoiningEquipment { get; set; } = new();
    public Dictionary<int, int> Assignments { get; set; } = new Dictionary<int, int>();
    public Dictionary<int, Gsd2FighterRecord> Fighters { get; set; } = new Dictionary<int, Gsd2FighterRecord>();
    public Dictionary<int, Gsd2FighterRecord> CanonicalRoles { get; set; } = new Dictionary<int, Gsd2FighterRecord>();
}
