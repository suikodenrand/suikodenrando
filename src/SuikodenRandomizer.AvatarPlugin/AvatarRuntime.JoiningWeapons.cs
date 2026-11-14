extern alias GSD1;

using SuikodenRandomizer.Core;
using SuikodenRandomizer.Core.Avatars;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace SuikodenRandomizer.AvatarPlugin;

internal sealed partial class AvatarRuntime
{
    internal JoiningWeaponPolicy JoiningWeaponPolicy { get; private set; } = JoiningWeaponPolicy.Donor;
    private bool _joiningWeaponsPrepared;
    private HashSet<int> _joiningWeaponFighters = new();
    private HashSet<int> _joiningWeaponRoles = new();
    private string JoiningWeaponSeed => _fixedProfile is not null ? _fixedSeed : Profile.Seed;

    private void ResetJoiningWeapons()
    {
        JoiningWeaponPolicy = JoiningWeaponPolicy.Donor;
        _joiningWeaponsPrepared = false;
        _joiningWeaponFighters.Clear();
        _joiningWeaponRoles.Clear();
    }

    private void BeginJoiningWeapons()
    {
        JoiningWeaponPolicy = AvatarPlugin.ConfiguredJoiningWeaponLevel switch
        {
            "MatchRole" => JoiningWeaponPolicy.MatchRole,
            "Level1" => JoiningWeaponPolicy.Level1,
            "TirVariance" => JoiningWeaponPolicy.TirVariance,
            _ => throw new InvalidOperationException("Joining weapon level must be TirVariance, MatchRole or Level1.")
        };
        _joiningWeaponsPrepared = true;
        Trace("JOINING WEAPON NEW GAME: policy=" + JoiningWeaponPolicy + "; story-protected loadouts retain native handling.");
    }

    private void RestoreJoiningWeapons(GSD1.SaveDataManager.SaveData save)
    {
        if (!CanExecute || (_fixedProfile is null && State.Origin == RoleAvatarCampaignOrigin.UnboundSave)) return;
        var binding = JoiningWeaponBindingStore.Read(AvatarSaveIdentity.Compute(save), CampaignFingerprint, JoiningWeaponSeed);
        JoiningWeaponPolicy = binding?.Policy ?? JoiningWeaponPolicy.Donor;
        _joiningWeaponFighters = new HashSet<int>(binding?.InitializedFighters ?? Array.Empty<int>());
        _joiningWeaponRoles = new HashSet<int>(binding?.InitializedRoles ?? Array.Empty<int>());
        if (_fixedProfile is null && _joiningWeaponFighters.Count != 0
            || _fixedProfile is not null && !_joiningWeaponFighters.IsSubsetOf(_fixedProfile.CompanionIds)
            || _fixedProfile is not null && JoiningWeaponPolicy != JoiningWeaponPolicy.Donor
                && _fixedAssignments!.Assignments.Values.Any(id => id != CombatCatalog.TirId && !_joiningWeaponFighters.Contains(id)))
            throw new InvalidOperationException("Joining weapon fighter history does not match the saved roster.");
        if (_fixedProfile is not null && _joiningWeaponRoles.Count != 0
            || _fixedProfile is null && _joiningWeaponRoles.Any(id => !Profile.IsRole(id) || Profile.KeepsCanonicalLoadout(id))
            || JoiningWeaponPolicy != JoiningWeaponPolicy.TirVariance && _joiningWeaponRoles.Count != 0)
            throw new InvalidOperationException("Joining weapon role history does not match the saved policy or roster.");
        if (_fixedProfile is null && JoiningWeaponPolicy == JoiningWeaponPolicy.TirVariance)
        {
            var party = save.party_data ?? throw new InvalidOperationException("Saved joining weapon party is missing.");
            var codes = new int[Math.Min(6, party.chara_code.Count)];
            var order = new int[Math.Min(6, party.narabi.Count)];
            for (int i = 0; i < codes.Length; i++) codes[i] = party.chara_code[i];
            for (int i = 0; i < order.Length; i++) order[i] = party.narabi[i];
            foreach (int slot in AvatarPartyLayout.GetActiveResidentSlots(codes, order, party.player_kazu))
                if (Profile.IsRole(codes[slot]) && !Profile.KeepsCanonicalLoadout(codes[slot]) && !_joiningWeaponRoles.Contains(codes[slot]))
                    throw new InvalidOperationException("Saved active role is missing its joining weapon initialization history: " + codes[slot]);
        }
        _joiningWeaponsPrepared = true;
        Trace("JOINING WEAPON RESTORED: policy=" + JoiningWeaponPolicy + "; initialized fighters="
            + string.Join(",", _joiningWeaponFighters.OrderBy(id => id))
            + "; initialized roles=" + string.Join(",", _joiningWeaponRoles.OrderBy(id => id))
            + (binding is null ? "; legacy save retains existing donor levels." : "; saved policy retained; configured setting ignored."));
    }

    private string? NormalizeJoiningWeapon(GSD1.PLAYER_BASE target, GSD1.PLAYER_BASE canonicalRole, int role, int donor, int? tirReference = null)
    {
        if (!_joiningWeaponsPrepared) throw new InvalidOperationException("Joining weapon policy is not ready.");
        if (JoiningWeaponPolicy == JoiningWeaponPolicy.Donor) return null;
        // Shuffle templates are copied before most heroes actually join. The
        // deferred party boundary supplies Tir's current level for this policy.
        if (JoiningWeaponPolicy == JoiningWeaponPolicy.TirVariance && tirReference is null) return null;
        byte before = target.buki_data.level;
        int delta = JoiningWeaponPolicy == JoiningWeaponPolicy.TirVariance ? JoiningWeaponDelta(role, donor) : 0;
        byte level = JoiningWeaponPolicy == JoiningWeaponPolicy.Level1 ? (byte)1 : JoiningWeaponPolicy == JoiningWeaponPolicy.TirVariance
            ? (byte)Math.Clamp(tirReference!.Value + delta, 1, 16) : (byte)Math.Clamp((int)canonicalRole.buki_data.level, 1, 16);
        target.buki_data.level = level;
        return "JOINING WEAPON INITIALIZED: role=" + role + "; donor=" + donor + "; policy=" + JoiningWeaponPolicy
            + "; weapon=" + target.buki_data.buki_id + "; before=" + before + "; after=" + target.buki_data.level
            + (JoiningWeaponPolicy == JoiningWeaponPolicy.TirVariance ? "; Tir=" + tirReference + "; delta=" + delta : "");
    }

    internal int JoiningWeaponDelta(int role, int donor)
    {
        // Hash each role/donor independently: party iteration, reload and other
        // randomizer draws never advance or change this deterministic stream.
        string key = "srr-s1-tir-weapon-v1|" + JoiningWeaponSeed.Length.ToString(CultureInfo.InvariantCulture)
            + ":" + JoiningWeaponSeed + "|" + CampaignFingerprint + "|" + (IsFixedRoster ? "fixed" : role.ToString(CultureInfo.InvariantCulture))
            + "|" + donor.ToString(CultureInfo.InvariantCulture) + "|";
        using var hash = SHA256.Create();
        for (int counter = 0; ; counter++)
        {
            var bytes = hash.ComputeHash(Encoding.UTF8.GetBytes(key + counter.ToString(CultureInfo.InvariantCulture)));
            // 252 is divisible by seven, so rejection avoids modulo bias.
            foreach (byte value in bytes) if (value < 252) return value % 7 - 3;
        }
    }

    private bool ObserveJoinedShuffleWeapons(bool required)
    {
        if (!IsActive || IsFixedRoster || JoiningWeaponPolicy != JoiningWeaponPolicy.TirVariance) return true;
        var game = GSD1.OldSrcBase.game_work ?? throw new InvalidOperationException("Joining weapon game state is unavailable.");
        var pending = new Dictionary<int, int>();
        foreach (int slot in ActiveResidentSlots())
        {
            int role = game.party_data.chara_code[slot];
            if (Profile.IsRole(role) && !Profile.KeepsCanonicalLoadout(role) && !_joiningWeaponRoles.Contains(role)) pending.Add(role, slot);
        }
        if (pending.Count == 0) return true;
        var bases = ReadyBaseIndex(pending.Keys.Append(CombatCatalog.TirId));
        if (!bases.TryGetValue(CombatCatalog.TirId, out var tir) || tir.buki_data.level < 1 || tir.buki_data.level > 16)
            return WaitFor("waiting for the current canonical Tir weapon level", required);
        int tirReference = tir.buki_data.level;
        foreach (var pair in pending)
            if (!ResidentBound(pair.Value, pair.Key, bases))
                return WaitFor("joining weapon active role is not bound to its canonical live base: " + pair.Key, required);
        var before = pending.Keys.ToDictionary(id => id, id => AvatarNativeData.CloneBase(bases[id]));
        var weapons = pending.Keys.ToDictionary(id => id, id => bases[id].buki_data);
        var logs = new List<string>();
        string layout = CanonicalLayoutFingerprint();
        try
        {
            foreach (int role in pending.Keys)
            {
                var expected = AvatarNativeData.CloneBase(before[role]);
                string? log = NormalizeJoiningWeapon(expected, before[role], role, Profile.GetDonor(role), tirReference);
                weapons[role].level = expected.buki_data.level;
                if (bases[role].buki_data.Pointer != weapons[role].Pointer
                    || AvatarNativeData.Fingerprint(bases[role]) != AvatarNativeData.Fingerprint(expected))
                    throw new InvalidOperationException("Joining weapon write did not verify for role " + role);
                if (log is not null) logs.Add(log);
            }
            if (CanonicalLayoutFingerprint() != layout) throw new InvalidOperationException("Party changed during joining weapon initialization.");
        }
        catch (Exception failure)
        {
            var rollback = new List<Exception>();
            foreach (var pair in before)
                try
                {
                    weapons[pair.Key].level = pair.Value.buki_data.level;
                    if (bases[pair.Key].buki_data.Pointer != weapons[pair.Key].Pointer
                        || AvatarNativeData.Fingerprint(bases[pair.Key]) != AvatarNativeData.Fingerprint(pair.Value))
                        throw new InvalidOperationException("Joining weapon rollback did not verify for role " + pair.Key);
                }
                catch (Exception error) { rollback.Add(error); }
            if (rollback.Count != 0) { rollback.Insert(0, failure); throw new AggregateException("Joining weapon write and rollback failed.", rollback); }
            throw;
        }
        _joiningWeaponRoles.UnionWith(pending.Keys);
        foreach (string log in logs) Trace(log);
        return true;
    }

    internal void CheckJoiningWeaponBindingWritable()
    {
        if (!_joiningWeaponsPrepared) throw new InvalidOperationException("Joining weapon policy is not ready for saving.");
        JoiningWeaponBindingStore.CheckWritable();
    }

    internal void WriteJoiningWeaponBinding(GSD1.SaveDataManager.SaveData save)
    {
        if (!IsActive || !_joiningWeaponsPrepared)
            throw new InvalidOperationException("Joining weapon policy is not ready for this native snapshot.");
        if (!IsFixedRoster && JoiningWeaponPolicy == JoiningWeaponPolicy.TirVariance)
        {
            var codes = GSD1.OldSrcBase.game_work!.party_data.chara_code;
            if (ActiveResidentSlots().Any(slot => Profile.IsRole(codes[slot]) && !Profile.KeepsCanonicalLoadout(codes[slot])
                    && !_joiningWeaponRoles.Contains(codes[slot])))
                throw new InvalidOperationException("Active joining weapon levels must be initialized before capturing the native snapshot.");
        }
        JoiningWeaponBindingStore.Write(AvatarSaveIdentity.Compute(save), CampaignFingerprint, JoiningWeaponSeed,
            new JoiningWeaponBinding(JoiningWeaponPolicy, _joiningWeaponFighters, _joiningWeaponRoles));
    }
}
