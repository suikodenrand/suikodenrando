extern alias GSD1;

using SuikodenRandomizer.Core;
using SuikodenRandomizer.Core.Avatars;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SuikodenRandomizer.AvatarPlugin;

internal sealed partial class AvatarRuntime
{
    private NuzlockeCampaign? _nuzCampaign;
    private NuzlockeCampaign? _nuzPublished;
    private bool _nuzStarted, _nuzLoading, _nuzResume, _nuzBattleOpen, _nuzRetryPending, _nuzRetryFade;
    private long _nuzBattleId;
    private readonly Dictionary<int, int> _nuzBattleHeroes = new();
    private readonly HashSet<int> _nuzKnockouts = new();
    private Dictionary<int, FixedRosterBaseSnapshot> _nuzTemplates = new();
    private Dictionary<int, FixedRosterBaseSnapshot> _nuzRoleBases = new();
    private Dictionary<int, int> _nuzRules = new();

    internal bool IsNuzlocke => _nuzCampaign is not null;
    private int NuzlockeDisplayId(int role)
    {
        int donor = _nuzCampaign!.GetDonor(role);
        return donor < 0 ? role : donor;
    }
    private string NuzlockeStatus => _nuzCampaign!.IsExhausted ? "Nuzlocke | companions exhausted | Tir remains"
        : "Nuzlocke | Tir protected | retired " + _nuzCampaign.RetiredHeroIds.Count + " / 75 | fresh reserves " + _nuzCampaign.RemainingReserves;

    private void ResetNuzlocke()
    {
        _nuzCampaign = _nuzPublished = null;
        _nuzStarted = _nuzLoading = _nuzResume = _nuzBattleOpen = _nuzRetryPending = _nuzRetryFade = false;
        _nuzBattleId = 0;
        _nuzTemplates.Clear(); _nuzRoleBases.Clear(); _nuzRules.Clear();
        _nuzBattleHeroes.Clear(); _nuzKnockouts.Clear();
    }

    private bool TryBeginNuzlocke()
    {
        if (!string.Equals(AvatarPlugin.ConfiguredMode, "Nuzlocke", StringComparison.OrdinalIgnoreCase)) return false;
        _nuzCampaign = NuzlockeCampaign.Create(AvatarPlugin.ConfiguredSeed);
        _waiting = "native Nuzlocke initialization"; _lastWait = "";
        _game = IntPtr.Zero; AvatarPresentation.Reset();
        Trace("NEW GAME NUZLOCKE seed=" + _nuzCampaign.Seed + "; companion pool=75; Tir protected; mode is experimental.");
        return true;
    }

    private bool TryLoadNuzlocke(GSD1.SaveDataManager.SaveData save)
    {
        if (!NuzlockeBindingStore.HasBindings) return false;
        var binding = NuzlockeBindingStore.Read(NuzlockeBindingStore.ComputeIdentity(save));
        if (binding is null) return false;
        _nuzCampaign = NuzlockeCampaign.Restore(binding.Campaign);
        _nuzPublished = _nuzCampaign;
        _nuzTemplates = binding.FighterStates;
        _nuzRoleBases = binding.RoleBases;
        _nuzRules = binding.RoleRules;
        _nuzLoading = _nuzResume = true;
        Trace("NUZLOCKE restored; retired=" + _nuzCampaign.RetiredHeroIds.Count);
        return true;
    }

    internal void CompleteNuzlockeLoad() => _nuzLoading = false;

    private void CheckNuzlockeSave()
    {
        if (_nuzBattleOpen) throw new InvalidOperationException("Save after the battle has finished.");
    }

    private string WriteNuzlockeBinding(GSD1.SaveDataManager.SaveData save)
    {
        CheckNuzlockeSave();
        string identity = NuzlockeBindingStore.ComputeIdentity(save);
        if (AvatarBindingStore.Read(AvatarSaveIdentity.Compute(save)) is not null
            || FixedRosterBindingStore.Read(identity) is not null)
            throw new InvalidOperationException("This snapshot already belongs to another campaign mode.");
        var roles = _nuzRoleBases.Keys.ToDictionary(id => id,
            id => FixedRosterBaseSnapshot.Capture(AvatarNativeData.FindBase(id)));
        NuzlockeBindingStore.Write(identity, new NuzlockeBinding
        {
            Campaign = _nuzCampaign!.Capture(), FighterStates = _nuzTemplates,
            RoleBases = roles, RoleRules = _nuzRules
        });
        return identity;
    }

    private bool EnsureNuzlocke(string reason, bool required)
    {
        var campaign = _nuzCampaign ?? throw new InvalidOperationException("Nuzlocke state missing.");
        var game = GSD1.OldSrcBase.game_work;
        var manager = GSD1.GlobalWork.Instance?.player_manager;
        var cache = manager?.player_cache;
        var party = game?.party_data;
        var residents = game?.player_data;
        if (game is null || manager is null || cache is null || party?.chara_code is null || residents is null)
            return WaitFor("native Nuzlocke roster is not ready", required);
        var slots = ActiveResidentSlots();
        var activeRoles = slots.Select(i => (int)party.chara_code[i]).ToArray();
        if (activeRoles.Any(id => !campaign.IsRole(id)))
            return WaitFor("unsupported Nuzlocke party role", required);
        var next = campaign.ReconcileActive(activeRoles);
        if (_nuzStarted && ReferenceEquals(next, _nuzPublished) && _game == game.Pointer
            && _manager?.Pointer == manager.Pointer
            && slots.All(slot => party.chara_code[slot] == CombatCatalog.TirId
                ? residents[slot]?.chara_id == CombatCatalog.TirId
                    && residents[slot]?.player_base?.Pointer == AvatarNativeData.FindBase(CombatCatalog.TirId).Pointer
                : _proxies.TryGetValue(party.chara_code[slot], out var proxy)
                && residents[slot]?.Pointer == proxy.Pointer
                && proxy.player_base?.Pointer == AvatarNativeData.FindBase(party.chara_code[slot]).Pointer
                && cache.TryGetValue(party.chara_code[slot], out var cached) && cached?.Pointer == proxy.Pointer))
            return true;
        CrashDiagnostics.Breadcrumb("nuz.prepare.begin", "reason=" + reason + "; activeRoles=" + string.Join(",", activeRoles));
        var needed = new HashSet<int>(activeRoles);
        needed.UnionWith(_nuzRoleBases.Keys);
        if (_nuzTemplates.Count == 0) needed.UnionWith(next.Assignments.Values);
        var bases = ReadyBaseIndex(needed);
        if (bases.Count != needed.Count) return WaitFor("Nuzlocke character bases are not ready", required);
        foreach (int slot in slots)
            if (!ResidentBound(slot, party.chara_code[slot], bases, allowExistingProxy: true))
                return WaitFor("Nuzlocke resident binding is pending", required);

        var originals = new Dictionary<int, GSD1.PLAYER_DATA>(_originals);
        foreach (int id in next.RoleIds)
        {
            if (id == CombatCatalog.TirId) continue;
            if (originals.ContainsKey(id)) continue;
            if (!cache.TryGetValue(id, out var asset) || asset?.chara_id != id
                || asset.field_data?.opd_data is null || asset.battle_data?.opd_data is null)
                return WaitFor("Nuzlocke asset " + id + " is not ready", required);
            originals.Add(id, asset);
        }
        if (_nuzTemplates.Count == 0)
        {
            var templates = new Dictionary<int, FixedRosterBaseSnapshot>();
            foreach (int fighter in next.Assignments.Values.Where(id => id != CombatCatalog.TirId))
            {
                var start = FixedRosterBaseSnapshot.Capture(bases[fighter]);
                start.Growth = originals[fighter].seicho_type.ToArray();
                templates.Add(fighter, start);
            }
            _nuzTemplates = templates;
        }

        // Native load restores the canonical bases from its own save. The sidecar
        // restores identity/rule metadata, and must never replay initial gear.
        var previous = _nuzPublished;
        var changed = new HashSet<int>();
        var writes = new Dictionary<int, GSD1.PLAYER_BASE>();
        var rules = new Dictionary<int, int>(_nuzRules);
        var roleRecords = new Dictionary<int, FixedRosterBaseSnapshot>(_nuzRoleBases);
        foreach (int role in activeRoles)
        {
            // The story protagonist retains his native resident, base and assets.
            if (role == CombatCatalog.TirId) continue;
            int fighter = next.GetDonor(role);
            if (fighter < 0) continue;
            int oldFighter = previous?.GetDonor(role) ?? -1;
            if (_nuzResume || (roleRecords.ContainsKey(role) && oldFighter == fighter)) continue;
            GSD1.PLAYER_BASE prepared;
            int sourceRole = previous?.Assignments.FirstOrDefault(pair => pair.Value == fighter).Key ?? -1;
            bool transfer = previous is not null && previous.FieldedHeroIds.Contains(fighter)
                && sourceRole != role && roleRecords.ContainsKey(sourceRole);
            if (transfer)
            {
                prepared = AvatarNativeData.CloneBase(bases[sourceRole]);
                rules[role] = rules[sourceRole];
            }
            else if (roleRecords.ContainsKey(role))
            {
                prepared = AvatarNativeData.CloneBase(bases[role]);
                prepared.hp = prepared.max_hp; prepared.status = 0;
            }
            else
            {
                prepared = _nuzTemplates[fighter].ToNative();
                prepared.hp = prepared.max_hp; prepared.status = 0;
                rules[role] = fighter;
            }
            prepared.chara_no = role;
            writes.Add(role, prepared);
            changed.Add(role);
            roleRecords[role] = FixedRosterBaseSnapshot.Capture(prepared);
        }

        var projectedRoles = roleRecords.Keys.Where(id => id != CombatCatalog.TirId && next.GetDonor(id) >= 0).ToArray();
        var proxies = new Dictionary<int, GSD1.PLAYER_DATA>();
        foreach (int role in projectedRoles)
        {
            int donor = next.GetDonor(role);
            if (!changed.Contains(role) && _proxies.TryGetValue(role, out var old)
                && old.player_base?.Pointer == bases[role].Pointer) proxies.Add(role, old);
            else
            {
                CrashDiagnostics.Breadcrumb("nuz.proxy.create.before", "role=" + role + "; donor=" + donor + "; rules=" + rules[role]);
                proxies.Add(role, AvatarNativeData.CreateProxy(originals[role], originals[donor], bases[role],
                    loadoutSource: originals[rules[role]]));
                CrashDiagnostics.Breadcrumb("nuz.proxy.create.after", "role=" + role);
            }
        }

        var cacheIds = _proxies.Keys.Union(proxies.Keys).Distinct().ToArray();
        var cacheBefore = cacheIds.ToDictionary(id => id, id => cache[id]);
        var basesBefore = writes.Keys.ToDictionary(id => id, id => AvatarNativeData.CloneBase(bases[id]));
        var residentsBefore = Enumerable.Range(0, residents.Count).Select(i => residents[i]).ToArray();
        var orderBefore = party.narabi.ToArray();
        byte countBefore = party.player_kazu;
        _preparing = true;
        CrashDiagnostics.Breadcrumb("nuz.publish.begin", "reason=" + reason + "; writes=" + string.Join(",", writes.Keys)
            + "; proxies=" + proxies.Count + "; retired=" + next.RetiredHeroIds.Count);
        try
        {
            foreach (var entry in writes)
            {
                CrashDiagnostics.Breadcrumb("nuz.publish.base.before", "role=" + entry.Key);
                AvatarNativeData.CopyBase(entry.Value, bases[entry.Key]);
                CrashDiagnostics.Breadcrumb("nuz.publish.base.after", "role=" + entry.Key);
            }
            CrashDiagnostics.Breadcrumb("nuz.publish.cache.before");
            foreach (int id in cacheIds) cache[id] = proxies.TryGetValue(id, out var proxy) ? proxy : originals[id];
            CrashDiagnostics.Breadcrumb("nuz.publish.residents.before");
            for (int i = 0; i < residents.Count; i++)
            {
                int role = i < party.chara_code.Count ? party.chara_code[i] : -1;
                if (proxies.TryGetValue(role, out var proxy)
                    && ResidentBound(i, role, bases, allowExistingProxy: true)) residents[i] = proxy;
                else if (residents[i] is { } old && _proxies.TryGetValue(old.chara_id, out var prior)
                    && old.Pointer == prior.Pointer) residents[i] = originals[old.chara_id];
            }
            var surviving = slots.Where(slot => next.GetDonor(party.chara_code[slot]) >= 0).ToArray();
            CrashDiagnostics.Breadcrumb("nuz.publish.order.before", "slots=" + string.Join(",", surviving));
            for (int i = 0; i < surviving.Length; i++) party.narabi[i] = (sbyte)surviving[i];
            for (int i = surviving.Length; i < party.narabi.Count; i++) party.narabi[i] = -1;
            party.player_kazu = (byte)surviving.Length;
            CrashDiagnostics.Breadcrumb("nuz.publish.verify.before");
            foreach (var entry in writes)
                if (AvatarNativeData.Fingerprint(bases[entry.Key]) != AvatarNativeData.Fingerprint(entry.Value))
                    throw new InvalidOperationException("Nuzlocke base publication did not verify.");
            foreach (int slot in surviving)
                if (party.chara_code[slot] == CombatCatalog.TirId
                    ? !ResidentBound(slot, CombatCatalog.TirId, bases)
                    : residents[slot]?.Pointer != proxies[party.chara_code[slot]].Pointer)
                    throw new InvalidOperationException("Nuzlocke resident publication did not verify.");
        }
        catch (Exception failure)
        {
            CrashDiagnostics.Exception("Nuzlocke publication; starting rollback", failure);
            var errors = new List<Exception> { failure };
            foreach (var entry in basesBefore)
                try { AvatarNativeData.CopyBase(entry.Value, bases[entry.Key]); } catch (Exception e) { errors.Add(e); }
            foreach (var entry in cacheBefore)
                try { cache[entry.Key] = entry.Value; } catch (Exception e) { errors.Add(e); }
            try
            {
                for (int i = 0; i < residents.Count; i++) residents[i] = residentsBefore[i];
                for (int i = 0; i < orderBefore.Length; i++) party.narabi[i] = orderBefore[i];
                party.player_kazu = countBefore;
            }
            catch (Exception e) { errors.Add(e); }
            throw new AggregateException("Nuzlocke publication failed.", errors);
        }
        finally { _preparing = false; }
        foreach (var entry in _proxies)
            if (!proxies.TryGetValue(entry.Key, out var current) || entry.Value.Pointer != current.Pointer)
                _retired.Add(entry.Value);
        _proxies = proxies; _originals = originals; _manager = manager; _game = game.Pointer;
        _nuzRules = rules; _nuzRoleBases = roleRecords;
        _nuzCampaign = _nuzPublished = next;
        _nuzResume = false; _nuzStarted = true;
        CrashDiagnostics.Breadcrumb("nuz.publish.complete", "activeRoles=" + string.Join(",", activeRoles));
        if (changed.Count != 0) { AvatarPresentation.Reset(); Trace("NUZLOCKE " + reason + "; retired=" + next.RetiredHeroIds.Count + "; reserves=" + next.RemainingReserves); }
        return true;
    }

    internal void BeginNuzlockeBattle()
    {
        if (!IsNuzlocke || _nuzBattleOpen) return;
        CrashDiagnostics.Breadcrumb("nuz.battle.begin");
        _nuzBattleOpen = true; _nuzRetryPending = false;
        _nuzBattleId = _nuzCampaign!.LastBattleId + 1;
        _nuzBattleHeroes.Clear(); _nuzKnockouts.Clear();
        var battle = GSD1.BattleBase.battle_work ?? throw new InvalidOperationException("No battle data.");
        if (battle.player_kazu < 1 || battle.player_kazu > AvatarPartyLayout.ResidentCapacity)
            throw new InvalidOperationException("Unsupported Nuzlocke battle party size.");
        for (int i = 1; i <= battle.player_kazu; i++)
        {
            int role = battle.p_battle_data[i].player_data.chara_id;
            int fighter = _nuzCampaign.GetDonor(role);
            if (fighter < 0 || _nuzBattleHeroes.Values.Contains(fighter))
                throw new InvalidOperationException("Invalid Nuzlocke battle identity.");
            _nuzBattleHeroes.Add(i, fighter);
        }
    }

    internal void ObserveNuzlockeKnockouts()
    {
        if (!IsNuzlocke || !_nuzBattleOpen) return;
        var battle = GSD1.BattleBase.battle_work;
        if (battle?.pm_data is null) return;
        foreach (var entry in _nuzBattleHeroes)
        {
            if (entry.Value == CombatCatalog.TirId) continue;
            var fighter = battle.pm_data[entry.Key];
            if (fighter is not null && fighter.IsDead && fighter.hp == 0 && _nuzKnockouts.Add(entry.Value))
            {
                CrashDiagnostics.Breadcrumb("nuz.ko.confirmed", "battle=" + _nuzBattleId + "; slot=" + entry.Key + "; fighter=" + entry.Value);
                Trace("NUZLOCKE KO " + CombatCatalog.GetName(entry.Value));
            }
            if (fighter is not null && _nuzKnockouts.Contains(entry.Value))
            {
                fighter.hp = 0; fighter.dead_flg = 1; fighter.finished_flg = 1;
            }
        }
    }

    internal bool TakeNuzlockeRetry()
    {
        if (!_nuzRetryPending) return false;
        if (_nuzCampaign!.IsExhausted) return false;
        CrashDiagnostics.Breadcrumb("nuz.retry.prepare", "battle=" + _nuzBattleId);
        if (!EnsureProxy("Nuzlocke reserve retry", true, force: true))
            throw new InvalidOperationException("Replacement team is unavailable.");
        if (GSD1.OldSrcBase.game_work.party_data.player_kazu == 0)
            throw new InvalidOperationException("No surviving Nuzlocke fighters can enter the battle.");
        RecoverNuzlockeTir(fullRecovery: true);
        _nuzRetryPending = false;
        _nuzRetryFade = true;
        DiagnosticCheckpoint("nuz.retry.ready");
        Trace("NUZLOCKE full wipe; retrying the same encounter with reserves.");
        return true;
    }

    private void RecoverNuzlockeTir(bool fullRecovery)
    {
        var party = GSD1.OldSrcBase.game_work?.party_data;
        if (party?.chara_code is null
            || !ActiveResidentSlots().Any(slot => party.chara_code[slot] == CombatCatalog.TirId)) return;
        var protagonist = AvatarNativeData.FindBase(CombatCatalog.TirId);
        if (!fullRecovery && protagonist.hp != 0) return;
        CrashDiagnostics.Breadcrumb("nuz.tir.recovery.before", "base=0x" + protagonist.Pointer.ToString("X") + "; full=" + fullRecovery);
        protagonist.hp = 1;
        if (fullRecovery) protagonist.hp = protagonist.max_hp;
        protagonist.status = 0;
        CrashDiagnostics.Breadcrumb("nuz.tir.recovery.after", "hp=" + protagonist.hp);
        Trace("NUZLOCKE protected Tir recovered; HP=" + protagonist.hp);
    }

    internal bool TakeNuzlockeRetryFade()
    {
        if (!_nuzRetryFade) return false;
        _nuzRetryFade = false;
        return true;
    }

    internal void FinishNuzlockeBattle(bool nativeDefeat)
    {
        if (!IsNuzlocke || !_nuzBattleOpen) return;
        CrashDiagnostics.Breadcrumb("nuz.battle.finish.begin", "battle=" + _nuzBattleId + "; defeatHint=" + nativeDefeat
            + "; knockouts=" + string.Join(",", _nuzKnockouts));
        ObserveNuzlockeKnockouts();
        var next = _nuzCampaign!.CommitBattle(_nuzBattleId, _nuzKnockouts);
        CrashDiagnostics.Breadcrumb("nuz.battle.retirement.committed", "retired=" + next.RetiredHeroIds.Count);
        _nuzCampaign = next;
        _nuzBattleOpen = false;
        // Native overlay cleanup can change defeat routing. Only the actual
        // game-over callback consumes this flag and requests a retry.
        _nuzRetryPending = !next.IsExhausted;
        if (!EnsureProxy("battle retirement", true, force: true))
            throw new InvalidOperationException("Nuzlocke replacement preparation failed.");
        RecoverNuzlockeTir(fullRecovery: false);
        DiagnosticCheckpoint("nuz.battle.finish.complete");
        if (next.IsExhausted) Trace("NUZLOCKE companion pool exhausted; Tir remains; next full defeat ends the run.");
    }
}
