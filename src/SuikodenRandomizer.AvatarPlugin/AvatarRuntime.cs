extern alias GSD1;

using BepInEx;
using SuikodenRandomizer.Core;
using SuikodenRandomizer.Core.Avatars;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text;
using UnityEngine.SceneManagement;

namespace SuikodenRandomizer.AvatarPlugin;

internal sealed class AvatarRuntime
{
    internal static AvatarRuntime? Current { get; set; }
    internal static string Folder => Path.Combine(Paths.ConfigPath, "SuikodenRandomizer", "avatar-shuffle");
    internal AvatarShuffleCampaignState State { get; private set; } = AvatarShuffleCampaignState.CreateUnbound();
    internal AvatarShuffleProfile Profile => State.Profile ?? throw new InvalidOperationException("No shuffle campaign is bound.");
    internal string? FaultReason { get; private set; }
    internal bool IsAdmitted => FaultReason is null && State.Origin != RoleAvatarCampaignOrigin.UnboundSave;
    internal bool IsActive => IsAdmitted && State.IsInitialized && _proxies.Count == 75;
    internal string Status => FaultReason is not null ? "STOP: " + FaultReason
        : IsActive ? "75 combat roles | seed: " + Profile.Seed + " | story IDs retained"
        : IsAdmitted ? "Waiting for native roster and a bound story resident: " + _waiting
        : "Shuffle inactive: start a NEW GAME. Pilot/older saves are read-only.";

    private Dictionary<int, GSD1.PLAYER_DATA> _originals = new Dictionary<int, GSD1.PLAYER_DATA>();
    private Dictionary<int, GSD1.PLAYER_DATA> _proxies = new Dictionary<int, GSD1.PLAYER_DATA>();
    private GSD1.PlayerManager? _manager;
    private IntPtr _game;
    private bool _preparing, _wasTitle;
    private readonly Func<long> _timestamp;
    private bool _automaticCheckScheduled;
    private long _nextAutomaticCheck;
    private static readonly long AutomaticCheckInterval = Math.Max(1L, Stopwatch.Frequency / 4);
    private long _backgroundChecks, _deferredChecks, _forcedChecks, _rosterScans, _baseIndexPasses, _directCacheHits;
    internal string PerformanceStatus => "background checks=" + _backgroundChecks + "; deferred retries=" + _deferredChecks
        + "; forced checks=" + _forcedChecks + "; donor-roster scans=" + _rosterScans
        + "; native-base index passes=" + _baseIndexPasses + "; direct cache hits=" + _directCacheHits;
    private string _waiting = "native initialization", _lastWait = string.Empty;
    private string _readinessDetail = "native initialization";
    // Outgoing native scenes may still reference an older proxy. Retain those
    // wrappers until exit rather than destroying resources behind the scene.
    private readonly List<GSD1.PLAYER_DATA> _retired = new List<GSD1.PLAYER_DATA>();

    internal AvatarRuntime(Func<long>? timestamp = null) => _timestamp = timestamp ?? Stopwatch.GetTimestamp;

    internal int MapDisplayId(int canonicalId) => IsActive && canonicalId >= 0 && canonicalId <= 109
        ? Profile.GetDonor(canonicalId) : canonicalId;
    internal bool IsRole(int id) => IsAdmitted && Profile.IsRole(id);
    internal GSD1.PLAYER_DATA? ProxyFor(int id) => IsActive && _proxies.TryGetValue(id, out var proxy) ? proxy : null;
    internal GSD1.PLAYER_DATA? ProjectPlayerData(int id, GSD1.PLAYER_DATA? nativeResult)
    {
        if (!IsRole(id)) return nativeResult;
        var proxy = ProxyFor(id);
        // Ordinary native cache hits already return the assigned proxy. Do not
        // recheck all 75 cache entries or rebind their bases on every getter.
        if (proxy is not null && nativeResult?.Pointer == proxy.Pointer
            && _game == GSD1.OldSrcBase.game_work?.Pointer)
        { _directCacheHits++; return nativeResult; }
        if (!EnsureProxy("canonical player-data lookup", false)) return nativeResult;
        return ProxyFor(id) ?? nativeResult;
    }
    internal bool TryGetOriginal(int id, [NotNullWhen(true)] out GSD1.PLAYER_DATA? original)
    {
        if (_originals.TryGetValue(id, out var value)) { original = value; return true; }
        original = null; return false;
    }

    internal void BeginNewGame()
    {
        try
        {
            RestoreCache();
            State = AvatarShuffleCampaignState.CreateNew(AvatarShuffleProfile.Create(AvatarPlugin.ConfiguredSeed));
            FaultReason = null; _game = IntPtr.Zero; _wasTitle = true;
            _automaticCheckScheduled = false;
            ResetPerformanceCounters();
            _waiting = "native initialization"; _lastWait = string.Empty;
            _readinessDetail = _waiting;
            AvatarPresentation.Reset();
            Trace("NEW GAME seed=" + Profile.Seed + "; mapping=" + Profile.Fingerprint
                + "; 75 roles. Non-combat, Tir, Ted and Odessa excluded from both pools.");
        }
        catch (Exception exception) { Fail("Shuffle New Game admission failed: " + exception.Message); }
    }

    internal void PrepareLoad(GSD1.SaveDataManager.SaveData save)
    {
        try
        {
            RestoreCache(); FaultReason = null;
            State = AvatarShuffleCampaignState.CreateUnbound(); _game = IntPtr.Zero;
            _automaticCheckScheduled = false;
            ResetPerformanceCounters();
            _lastWait = string.Empty; _waiting = "native reconstruction";
            _readinessDetail = _waiting;
            AvatarPresentation.Reset();
            string identity = AvatarSaveIdentity.Compute(save);
            State = AvatarBindingStore.Read(identity) ?? AvatarShuffleCampaignState.CreateUnbound();
            if (!IsAdmitted)
                Trace("Unbound save: no 0.8 shuffle mapping found. No pilot/older campaign conversion; saving read-only.");
            else Trace("BOUND SHUFFLE RESTORED before native reconstruction; seed=" + Profile.Seed
                + "; mapping=" + Profile.Fingerprint + "; configured seed ignored for this save.");
        }
        catch (Exception exception) { Fail("Shuffle save binding rejected: " + exception.Message); }
    }

    internal bool EnsureProxy(string reason, bool required, bool force = false)
    {
        if (!IsAdmitted || _preparing) return false;
        try
        {
            // Tick, native initialization and getters may all retry in the same
            // frame. Share one background budget across those entry points.
            // Save/load and native party completion, plus mandatory battle
            // checks, bypass the delay.
            long now = _timestamp();
            if (!required && !force && _automaticCheckScheduled && now < _nextAutomaticCheck)
            { _deferredChecks++; return false; }
            if (required || force) _forcedChecks++; else _backgroundChecks++;
            _automaticCheckScheduled = true;
            _nextAutomaticCheck = now + AutomaticCheckInterval;
            return EnsureProxiesCore(reason, required);
        }
        catch (Exception exception) { Fail("Shuffle preparation failed: " + exception.Message); return false; }
    }

    private bool WaitFor(string message, bool required)
    {
        _waiting = message;
        if (_lastWait != message) { Trace("WAIT: " + message); _lastWait = message; }
        if (required) Fail("Complete native roster unavailable at a required boundary: " + message);
        return false;
    }

    private bool EnsureProxiesCore(string reason, bool required)
    {
        if (!IsAdmitted || _preparing) return false;
        var game = GSD1.OldSrcBase.game_work;
        var manager = GSD1.GlobalWork.Instance?.player_manager;
        var cache = manager?.player_cache;
        if (game is null || manager is null || cache is null)
            return WaitFor("native game/cache not ready", required);
        // Resident slots include staged/inactive actors. A genuine live-base
        // anchor is enough; occupied slots need not equal active party size.
        // Keep this small gate ahead of the donor scan for opening performance.
        if (!State.IsInitialized && !StartingPartyBound())
            return WaitFor(_readinessDetail, required);
        if (_game == game.Pointer && _proxies.Count == 75)
        {
            bool current = true;
            foreach (int id in Profile.RoleIds)
                if (!cache.TryGetValue(id, out var value) || value?.Pointer != _proxies[id].Pointer)
                { current = false; break; }
            if (current)
            {
                string? bindingWait = BindResidentsCore();
                return bindingWait is null ? FaultReason is null : WaitFor(bindingWait, required);
            }
            // Reconstructing assets mid-run must not import mutable starts again.
            RestoreCache();
        }
        if (_proxies.Count != 0) RestoreCache();
        _rosterScans++;
        var originals = new Dictionary<int, GSD1.PLAYER_DATA>();
        // Index live records once; repeatedly searching the entire native array
        // for every one of the 75 roles made each retry quadratic.
        IReadOnlyList<int> activeSlots;
        try { activeSlots = ActiveResidentSlots(); }
        catch (InvalidOperationException exception) { return WaitFor("active party layout pending: " + exception.Message, required); }
        var codes = game.party_data.chara_code;
        var residents = game.player_data;
        var neededBases = new HashSet<int>(Profile.RoleIds);
        foreach (int slot in activeSlots) neededBases.Add(codes[slot]);
        var bases = ReadyBaseIndex(neededBases);
        var missing = new List<int>();
        foreach (int id in Profile.RoleIds)
        {
            if (!cache.TryGetValue(id, out var asset) || asset is null
                || asset.chara_id != id || asset.field_data?.opd_data is null
                || asset.battle_data?.opd_data is null)
            { missing.Add(id); continue; }
            originals.Add(id, asset);
            if (!bases.ContainsKey(id)) missing.Add(id);
        }
        if (missing.Count != 0) return WaitFor("missing initialized assets/bases: " + string.Join(",", missing), required);
        foreach (int slot in activeSlots)
            if (!ResidentBound(slot, codes[slot], bases))
                return WaitFor("active resident slot " + slot + " is not bound to its canonical live base", required);

        // Only publish into active slots or already-bound staged residents.
        // A reserved code with null/stale data is native work-in-progress.
        var publishSlots = new Dictionary<int, int>();
        for (int slot = 0; slot < Math.Min(6, Math.Min(codes.Count, residents.Count)); slot++)
            if (Profile.IsRole(codes[slot]) && ResidentBound(slot, codes[slot], bases))
                publishSlots.Add(slot, codes[slot]);

        _preparing = true;
        var prepared = new Dictionary<int, GSD1.PLAYER_DATA>();
        try
        {
            // Original asset references and all base snapshots precede every
            // publication. A donor can also be a source in the same permutation.
            foreach (int id in Profile.RoleIds)
                prepared.Add(id, AvatarNativeData.CreateProxy(originals[id], originals[Profile.GetDonor(id)], bases[id]));
            var originalResidents = new GSD1.PLAYER_DATA?[residents.Count];
            for (int n = 0; n < residents.Count; n++) originalResidents[n] = residents[n];
            string layout = CanonicalLayoutFingerprint();
            bool initialize = !State.IsInitialized;
            bool Apply()
            {
                var result = AvatarShuffleTransaction.Execute<GSD1.PLAYER_BASE>(Profile.RoleIds,
                    id => AvatarNativeData.CloneBase(bases[id]),
                    (id, snapshots) =>
                    {
                        int donor = Profile.GetDonor(id);
                        if (initialize)
                        {
                            AvatarNativeData.CopyBase(snapshots[donor], bases[id]);
                            bases[id].seicho_type = AvatarNativeData.CloneBytes(originals[donor].seicho_type);
                            bases[id].hp = bases[id].max_hp;
                            bases[id].status = 0;
                        }
                        prepared[id].player_base = bases[id];
                        cache[id] = prepared[id];
                        foreach (var slot in publishSlots)
                            if (slot.Value == id) residents[slot.Key] = prepared[id];
                    },
                    (id, before, after) =>
                    {
                        if (after.chara_no != id || prepared[id].chara_id != id
                            || prepared[id].player_base?.Pointer != bases[id].Pointer
                            || prepared[id].chara_name != originals[Profile.GetDonor(id)].chara_name
                            || originals[id].chara_id != id
                            || !cache.TryGetValue(id, out var actual) || actual?.Pointer != prepared[id].Pointer
                            || (!initialize && AvatarNativeData.Fingerprint(before) != AvatarNativeData.Fingerprint(after))) return false;
                        foreach (var slot in publishSlots)
                            if (slot.Value == id && residents[slot.Key]?.Pointer != prepared[id].Pointer) return false;
                        return true;
                    },
                    (id, before) =>
                    {
                        AvatarNativeData.CopyBase(before, bases[id]);
                        cache[id] = originals[id];
                        if (!cache.TryGetValue(id, out var restored) || restored?.Pointer != originals[id].Pointer)
                            throw new InvalidOperationException("Native cache rollback failed for role " + id + ".");
                        foreach (var slot in publishSlots)
                            if (slot.Value == id)
                            {
                                residents[slot.Key] = originalResidents[slot.Key];
                                if (residents[slot.Key]?.Pointer != originalResidents[slot.Key]?.Pointer)
                                    throw new InvalidOperationException("Native resident rollback failed for role " + id + ".");
                            }
                    },
                    (left, right) => AvatarNativeData.Fingerprint(left) == AvatarNativeData.Fingerprint(right),
                    () => CanonicalLayoutFingerprint() == layout);
                if (!result.Committed) throw new InvalidOperationException("Complete shuffle transaction rejected at "
                    + result.FailureStage + ": " + result.Failure?.Message);
                return true;
            }
            bool committed = initialize ? State.ApplyNewGame(Apply) : State.ReplayTemplates(Profile, Apply);
            if (!committed) throw new InvalidOperationException("Complete shuffle transaction did not commit.");
            _originals = originals; _proxies = prepared; _manager = manager; _game = game.Pointer;
            AvatarPresentation.Reset();
            Trace((initialize ? "INITIALIZED ALL 75 ONCE" : "REPLAYED ALL 75 WITHOUT STATS RESET")
                + "; " + reason + "; seed=" + Profile.Seed + "; mapping=" + Profile.Fingerprint);
            TracePartyLayout("shuffle initialized");
            VerifyFinalParty(reason);
            WriteSpoiler();
            return FaultReason is null;
        }
        finally
        {
            // If construction failed before publication, keep only detached
            // wrappers alive; no partially prepared proxy becomes active.
            if (!ReferenceEquals(_proxies, prepared)) foreach (var proxy in prepared.Values) _retired.Add(proxy);
            _preparing = false;
        }
    }

    private Dictionary<int, GSD1.PLAYER_BASE> ReadyBaseIndex(IEnumerable<int> ids)
    {
        var wanted = new HashSet<int>(ids);
        var found = new Dictionary<int, GSD1.PLAYER_BASE>();
        var records = GSD1.OldSrcBase.game_work?.player_base;
        if (records is null || wanted.Count == 0) return found;
        _baseIndexPasses++;
        for (int n = 0; n < records.Count; n++)
        {
            var candidate = records[n];
            if (candidate is null) continue;
            int id = candidate.chara_no;
            if (!wanted.Contains(id) || candidate.level == 0 || candidate.max_hp == 0) continue;
            try { AvatarNativeData.ValidateBase(candidate); }
            catch (InvalidOperationException) { continue; } // Native construction is still incomplete.
            if (found.ContainsKey(id)) throw new InvalidOperationException("Duplicate initialized native role base " + id + ".");
            found.Add(id, candidate);
        }
        return found;
    }

    private IReadOnlyList<int> ActiveResidentSlots()
    {
        var party = GSD1.OldSrcBase.game_work?.party_data;
        if (party?.chara_code is null || party.narabi is null)
            throw new InvalidOperationException("Native active party arrays are not ready.");
        var codes = new int[Math.Min(6, party.chara_code.Count)];
        var order = new int[Math.Min(6, party.narabi.Count)];
        for (int n = 0; n < codes.Length; n++) codes[n] = party.chara_code[n];
        for (int n = 0; n < order.Length; n++) order[n] = party.narabi[n];
        return AvatarPartyLayout.GetActiveResidentSlots(codes, order, party.player_kazu);
    }

    private bool ResidentBound(int slot, int id, IReadOnlyDictionary<int, GSD1.PLAYER_BASE> bases,
        bool allowExistingProxy = false)
    {
        var residents = GSD1.OldSrcBase.game_work?.player_data;
        if (residents is null || slot < 0 || slot >= residents.Count || !bases.TryGetValue(id, out var liveBase)) return false;
        var player = residents[slot];
        if (player?.chara_id != id || player.player_base?.chara_no != id) return false;
        if (player.player_base.Pointer == liveBase.Pointer) return true;
        // A proxy may still refer to an outgoing base during native rebuild.
        // Only our exact, canonical proxy may be rebound across that boundary.
        return allowExistingProxy && _proxies.TryGetValue(id, out var proxy) && player.Pointer == proxy.Pointer;
    }

    internal bool HasMappedPartyRole()
    {
        if (!IsAdmitted) return false;
        var codes = GSD1.OldSrcBase.game_work?.party_data?.chara_code;
        if (codes is null) return false;
        // Battle admission is about active membership, not staged reservations.
        foreach (int slot in ActiveResidentSlots()) if (Profile.IsRole(codes[slot])) return true;
        return false;
    }

    internal bool StartingPartyBound()
    {
        var game = GSD1.OldSrcBase.game_work;
        var codes = game?.party_data?.chara_code;
        var residents = game?.player_data;
        if (game is null || codes is null || residents is null)
        { _readinessDetail = "native resident arrays are not ready"; return false; }
        IReadOnlyList<int> active;
        try { active = ActiveResidentSlots(); }
        catch (InvalidOperationException exception)
        { _readinessDetail = "active party layout pending: " + exception.Message; return false; }
        var candidateSlots = new List<int>();
        var needed = new HashSet<int>();
        for (int slot = 0; slot < Math.Min(6, Math.Min(codes.Count, residents.Count)); slot++)
        {
            int id = codes[slot];
            var player = residents[slot];
            if (Profile.IsRole(id) && player?.chara_id == id && player.player_base?.chara_no == id)
            { candidateSlots.Add(slot); needed.Add(id); }
        }
        if (candidateSlots.Count == 0)
        { _readinessDetail = "no eligible resident is bound yet (staged residents are allowed)"; return false; }
        foreach (int slot in active) needed.Add(codes[slot]);
        var bases = ReadyBaseIndex(needed);
        foreach (int slot in active)
            if (!ResidentBound(slot, codes[slot], bases))
            { _readinessDetail = "active slot " + slot + " awaits its canonical live base"; return false; }
        foreach (int slot in candidateSlots)
            if (ResidentBound(slot, codes[slot], bases))
            {
                _readinessDetail = "ready: canonical resident anchor role=" + codes[slot] + "; slot=" + slot
                    + "; active count=" + active.Count + "; staged residency accepted";
                return true;
            }
        _readinessDetail = "eligible resident anchors await their exact current-world base pointers";
        return false;
    }

    private string? BindResidentsCore()
    {
        if (_proxies.Count != 75) return null;
        var game = GSD1.OldSrcBase.game_work;
        var codes = game?.party_data?.chara_code;
        var residents = game?.player_data;
        if (game is null || codes is null || residents is null) return "native resident arrays unavailable during rebinding";
        IReadOnlyList<int> active;
        try { active = ActiveResidentSlots(); }
        catch (InvalidOperationException exception) { return "active layout pending: " + exception.Message; }
        var needed = new HashSet<int>(Profile.RoleIds);
        foreach (int slot in active) needed.Add(codes[slot]);
        var bases = ReadyBaseIndex(needed);
        foreach (int id in Profile.RoleIds)
            if (!bases.ContainsKey(id)) return "native canonical role base missing during rebinding: " + id;
        foreach (int slot in active)
            if (!ResidentBound(slot, codes[slot], bases, allowExistingProxy: true))
                return "active slot " + slot + " awaits native binding during rebinding";
        var publish = new List<int>();
        for (int slot = 0; slot < Math.Min(6, Math.Min(codes.Count, residents.Count)); slot++)
            if (Profile.IsRole(codes[slot]) && ResidentBound(slot, codes[slot], bases, allowExistingProxy: true)) publish.Add(slot);
        foreach (int id in Profile.RoleIds)
            if (_proxies[id].player_base?.Pointer != bases[id].Pointer) _proxies[id].player_base = bases[id];
        foreach (int slot in publish) residents[slot] = _proxies[codes[slot]];
        return null;
    }

    internal void BindResidents()
    {
        string? pending = BindResidentsCore();
        if (pending is not null) throw new InvalidOperationException(pending);
    }

    internal void VerifyFinalParty(string reason)
    {
        if (!IsActive) return;
        var game = GSD1.OldSrcBase.game_work ?? throw new InvalidOperationException("No native game state.");
        var codes = game.party_data.chara_code;
        var data = game.player_data;
        var active = ActiveResidentSlots();
        var activeIds = new HashSet<int>();
        foreach (int slot in active) activeIds.Add(codes[slot]);
        var activeBases = ReadyBaseIndex(activeIds);
        foreach (int slot in active)
        {
            int id = codes[slot];
            var resident = slot < data.Count ? data[slot] : null;
            if (resident is null || resident.chara_id != id || resident.player_base?.chara_no != id)
                throw new InvalidOperationException("Active party identities are not canonical code/data/base triplets.");
            if (!activeBases.TryGetValue(id, out var liveBase) || resident.player_base.Pointer != liveBase.Pointer)
                throw new InvalidOperationException("Active party resident is not bound to its current-world canonical base.");
            if (!Profile.IsRole(id)) continue;
            int donor = Profile.GetDonor(id);
            if (resident.Pointer != _proxies[id].Pointer || resident.chara_name != _originals[donor].chara_name)
                throw new InvalidOperationException("Active party resident lost its assigned independent avatar/name.");
            Trace("AVATAR CHECK " + reason + ": active slot=" + slot + "; role=" + id + "; donor=" + donor
                + "; canonical data/base=" + resident.chara_id + "/" + resident.player_base.chara_no
                + "; name=0x" + resident.chara_name.ToString("X8") + "; level=" + resident.player_base.level
                + "; HP=" + resident.player_base.hp + "/" + resident.player_base.max_hp + "; EXP=" + resident.player_base.exp);
        }
        Trace("IDENTITY CHECK " + reason + ": active party IDs canonical; count=" + active.Count
            + "; inactive/staged slots are not active members");
    }

    internal void TracePartyLayout(string reason)
    {
        try
        {
            var game = GSD1.OldSrcBase.game_work;
            var party = game?.party_data;
            if (game is null || party?.chara_code is null || party.narabi is null || game.player_data is null)
            { Trace("PARTY LAYOUT " + reason + ": native arrays unavailable; readiness=" + _readinessDetail); return; }
            var codes = new List<short>(); var order = new List<sbyte>();
            foreach (short code in party.chara_code) codes.Add(code);
            foreach (sbyte slot in party.narabi) order.Add(slot);
            Trace("PARTY LAYOUT " + reason + ": active count=" + party.player_kazu
                + "; codes=[" + string.Join(",", codes) + "]; order=[" + string.Join(",", order)
                + "]; readiness=" + _readinessDetail);
            var active = new HashSet<int>();
            try { foreach (int slot in ActiveResidentSlots()) active.Add(slot); }
            catch (InvalidOperationException exception) { Trace("PARTY LAYOUT invalid active order: " + exception.Message); }
            for (int slot = 0; slot < Math.Min(6, party.chara_code.Count); slot++)
            {
                var player = slot < game.player_data.Count ? game.player_data[slot] : null;
                var persistent = player?.player_base;
                Trace("PARTY SLOT " + slot + ": active=" + active.Contains(slot) + "; code=" + party.chara_code[slot]
                    + "; data=" + (player is null ? "null" : player.chara_id.ToString())
                    + "; base=" + (persistent is null ? "null" : persistent.chara_no.ToString())
                    + "; base pointer=" + (persistent is null ? "null" : persistent.Pointer.ToString("X")));
            }
        }
        catch (Exception exception) { Trace("Party layout diagnostic unavailable: " + exception.Message); }
    }

    internal string CanonicalLayoutFingerprint()
    {
        var game = GSD1.OldSrcBase.game_work ?? throw new InvalidOperationException("No game state.");
        var b = new StringBuilder();
        foreach (byte flag in game.member_flag) b.Append(flag).Append(',');
        foreach (short code in game.party_data.chara_code) b.Append(code).Append(',');
        foreach (sbyte slot in game.party_data.narabi) b.Append(slot).Append(',');
        b.Append(game.party_data.player_kazu);
        return AvatarSaveIdentity.Hash(Encoding.UTF8.GetBytes(b.ToString()));
    }

    internal void Tick()
    {
        try
        {
            if (SceneManager.GetActiveScene().name != "GSD1") return;
            bool title = GSD1.ChapterManager.GR1Instance?.activeChapter?.TryCast<GSD1.TitleChapter>() is not null;
            if (title && !_wasTitle)
            {
                RestoreCache(); State = AvatarShuffleCampaignState.CreateUnbound(); FaultReason = null;
                AvatarPresentation.Reset();
            }
            _wasTitle = title;
            if (!title && IsAdmitted && (!IsActive || _game != GSD1.OldSrcBase.game_work?.Pointer))
                EnsureProxy("native initialization observation", false);
        }
        catch (Exception exception) { Fail("Shuffle lifecycle observation failed: " + exception.Message); }
    }

    private void RestoreCache()
    {
        var cache = _manager?.player_cache;
        foreach (var entry in _proxies)
        {
            if (cache is not null && _originals.TryGetValue(entry.Key, out var original)
                && cache.TryGetValue(entry.Key, out var current) && current?.Pointer == entry.Value.Pointer)
                cache[entry.Key] = original;
            _retired.Add(entry.Value);
        }
        _proxies = new Dictionary<int, GSD1.PLAYER_DATA>();
        _originals = new Dictionary<int, GSD1.PLAYER_DATA>();
        _manager = null; _game = IntPtr.Zero;
    }

    private void WriteSpoiler()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            var csv = new StringBuilder("RoleId,OriginalRole,DonorId,Avatar\r\n");
            foreach (int id in Profile.RoleIds)
            {
                int donor = Profile.GetDonor(id);
                csv.Append(id).Append(',').Append(CombatCatalog.GetName(id)).Append(',')
                    .Append(donor).Append(',').Append(CombatCatalog.GetName(donor)).Append("\r\n");
            }
            File.WriteAllText(Path.Combine(Folder, "mapping-spoiler.csv"), csv.ToString(), new UTF8Encoding(false));
        }
        catch (Exception exception) { Trace("Optional mapping spoiler unavailable: " + exception.Message); }
    }

    internal void Fail(string reason)
    {
        if (FaultReason is not null) return;
        FaultReason = reason; Trace("STOP — saving blocked: " + reason);
    }
    private void ResetPerformanceCounters()
    {
        _backgroundChecks = _deferredChecks = _forcedChecks = _rosterScans = _baseIndexPasses = _directCacheHits = 0;
    }
    internal void Trace(string message)
    {
        AvatarPlugin.PluginLog.LogInfo("[SRR AVATAR] " + message);
        try
        {
            Directory.CreateDirectory(Folder);
            File.AppendAllText(Path.Combine(Folder, "avatar-shuffle.log"), DateTime.UtcNow.ToString("O") + " " + message + Environment.NewLine);
        }
        catch (Exception exception) { AvatarPlugin.PluginLog.LogWarning("[SRR AVATAR] Diagnostic file unavailable: " + exception.Message); }
    }
}
