extern alias GSD1;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SuikodenRandomizer.AvatarPlugin;

internal sealed partial class AvatarRuntime
{
    internal JoiningEquipmentPolicy JoiningEquipmentPolicy { get; private set; } = JoiningEquipmentPolicy.Donor;
    private bool _joiningEquipmentPrepared;
    private HashSet<int> _joiningEquipmentFighters = new();
    private HashSet<int> _joiningEquipmentRoles = new();
    private Dictionary<int, JoiningEquipmentInventory> _canonicalJoiningInventories = new();

    private void ResetJoiningEquipment()
    {
        JoiningEquipmentPolicy = JoiningEquipmentPolicy.Donor; _joiningEquipmentPrepared = false;
        _joiningEquipmentFighters = new(); _joiningEquipmentRoles = new(); _canonicalJoiningInventories = new();
    }
    private void BeginJoiningEquipment()
    {
        JoiningEquipmentPolicy = AvatarPlugin.ConfiguredJoiningEquipment switch
        {
            "MatchRole" => JoiningEquipmentPolicy.MatchRole,
            "Donor" => JoiningEquipmentPolicy.Donor,
            _ => throw new InvalidOperationException("Joining equipment must be MatchRole or Donor.")
        };
        _joiningEquipmentPrepared = true;
        Trace("JOINING EQUIPMENT NEW GAME: policy=" + JoiningEquipmentPolicy + "; complete role inventory; weapon/rune policy separate.");
    }
    private void RestoreJoiningEquipment(GSD1.SaveDataManager.SaveData save)
    {
        if (!CanExecute || (_fixedProfile is null && State.Origin == Core.Avatars.RoleAvatarCampaignOrigin.UnboundSave)) return;
        var binding = JoiningEquipmentBindingStore.Read(AvatarSaveIdentity.Compute(save), CampaignFingerprint, JoiningWeaponSeed);
        JoiningEquipmentPolicy = binding?.Policy ?? JoiningEquipmentPolicy.Donor;
        _joiningEquipmentFighters = new(binding?.InitializedFighters ?? Array.Empty<int>());
        _joiningEquipmentRoles = new(binding?.InitializedRoles ?? Array.Empty<int>());
        _canonicalJoiningInventories = binding?.CanonicalRoleInventories.ToDictionary(pair => pair.Key, pair => pair.Value.Clone()) ?? new();
        if (JoiningEquipmentPolicy == JoiningEquipmentPolicy.MatchRole)
        {
            if (_fixedProfile is null)
            {
                var expected = Profile.RoleIds.Where(id => !Profile.KeepsCanonicalLoadout(id)).ToHashSet();
                if (_joiningEquipmentFighters.Count != 0 || !_canonicalJoiningInventories.Keys.ToHashSet().SetEquals(expected)
                    || !_joiningEquipmentRoles.IsSubsetOf(expected))
                    throw new InvalidDataException("Joining equipment history does not match the saved Shuffle roles.");
                var party = save.party_data ?? throw new InvalidDataException("Saved joining equipment party is missing.");
                int[] codes = party.chara_code.Take(Math.Min(6, party.chara_code.Count)).Select(value => (int)value).ToArray();
                int[] order = party.narabi.Take(Math.Min(6, party.narabi.Count)).Select(value => (int)value).ToArray();
                foreach (int slot in Core.Avatars.AvatarPartyLayout.GetActiveResidentSlots(codes, order, party.player_kazu))
                    if (expected.Contains(codes[slot]) && !_joiningEquipmentRoles.Contains(codes[slot]))
                        throw new InvalidDataException("Saved active role has no joining equipment history: " + codes[slot]);
            }
            else if (_joiningEquipmentRoles.Count != 0 || _canonicalJoiningInventories.Count != 0
                || !_joiningEquipmentFighters.IsSubsetOf(_fixedProfile.CompanionIds)
                || _fixedAssignments!.Assignments.Values.Any(id => id != Core.CombatCatalog.TirId && !_joiningEquipmentFighters.Contains(id)))
                throw new InvalidDataException("Joining equipment history does not match the saved fixed roster.");
        }
        _joiningEquipmentPrepared = true;
        Trace("JOINING EQUIPMENT RESTORED: policy=" + JoiningEquipmentPolicy + "; first joins="
            + string.Join(",", (_fixedProfile is null ? _joiningEquipmentRoles : _joiningEquipmentFighters).OrderBy(id => id))
            + (binding is null ? "; legacy inventory retained; no retroactive equipment reset." : "; saved equipment policy retained."));
    }

    private bool ObserveJoinedShuffleEquipment(bool required)
    {
        if (!IsActive || IsFixedRoster || JoiningEquipmentPolicy != JoiningEquipmentPolicy.MatchRole) return true;
        var game = GSD1.OldSrcBase.game_work ?? throw new InvalidOperationException("Joining equipment game state is unavailable.");
        var pending = new Dictionary<int, int>();
        foreach (int slot in ActiveResidentSlots())
        {
            int role = game.party_data.chara_code[slot];
            if (Profile.IsRole(role) && !Profile.KeepsCanonicalLoadout(role) && !_joiningEquipmentRoles.Contains(role)) pending.Add(role, slot);
        }
        if (pending.Count == 0) return true;
        if (!JoiningEquipmentNative.Ready) return WaitFor("waiting for native joining equipment definitions", required);
        var bases = ReadyBaseIndex(pending.Keys);
        foreach (var pair in pending)
            if (!ResidentBound(pair.Value, pair.Key, bases))
                return WaitFor("joining equipment role is not bound to its canonical live base: " + pair.Key, required);
        var before = pending.Keys.ToDictionary(id => id, id => AvatarNativeData.CloneBase(bases[id]));
        var arrays = pending.Keys.ToDictionary(id => id, id => bases[id].item);
        var logs = new List<string>();
        var expected = new Dictionary<int, GSD1.PLAYER_BASE>();
        // All plans and allocations precede live writes, including catalog,
        // compatibility and capacity validation for every simultaneous join.
        foreach (int role in pending.Keys)
        {
            if (!_canonicalJoiningInventories.TryGetValue(role, out var inventory)
                || !_originals.TryGetValue(Profile.GetDonor(role), out var donor))
                throw new InvalidOperationException("Original role inventory or replacement rules are missing: " + role);
            var planned = AvatarNativeData.CloneBase(before[role]);
            logs.Add(JoiningEquipmentNative.Apply(planned, inventory, donor, role, Profile.GetDonor(role)));
            expected.Add(role, planned);
        }
        string layout = CanonicalLayoutFingerprint();
        try
        {
            foreach (int role in pending.Keys)
            {
                bases[role].item = expected[role].item;
                bases[role].item_kazu = expected[role].item_kazu;
                if (AvatarNativeData.Fingerprint(bases[role]) != AvatarNativeData.Fingerprint(expected[role]))
                    throw new InvalidOperationException("Joining equipment write did not verify for role " + role);
            }
            if (CanonicalLayoutFingerprint() != layout) throw new InvalidOperationException("Party changed during joining equipment initialization.");
        }
        catch (Exception failure)
        {
            var rollback = new List<Exception>();
            foreach (int role in pending.Keys)
                try
                {
                    bases[role].item = arrays[role]; bases[role].item_kazu = before[role].item_kazu;
                    if (AvatarNativeData.Fingerprint(bases[role]) != AvatarNativeData.Fingerprint(before[role]))
                        throw new InvalidOperationException("Joining equipment rollback did not verify for role " + role);
                }
                catch (Exception error) { rollback.Add(error); }
            if (rollback.Count != 0) { rollback.Insert(0, failure); throw new AggregateException("Joining equipment write and rollback failed.", rollback); }
            throw;
        }
        _joiningEquipmentRoles.UnionWith(pending.Keys);
        foreach (string log in logs) Trace(log);
        return true;
    }
    internal void CheckJoiningEquipmentBindingWritable()
    {
        if (!_joiningEquipmentPrepared) throw new InvalidOperationException("Joining equipment policy is not ready for saving.");
        JoiningEquipmentBindingStore.CheckWritable();
    }
    internal void WriteJoiningEquipmentBinding(GSD1.SaveDataManager.SaveData save)
    {
        if (!IsActive || !_joiningEquipmentPrepared) throw new InvalidOperationException("Joining equipment policy is not ready for this snapshot.");
        if (!IsFixedRoster && JoiningEquipmentPolicy == JoiningEquipmentPolicy.MatchRole)
        {
            var codes = GSD1.OldSrcBase.game_work!.party_data.chara_code;
            if (ActiveResidentSlots().Any(slot => Profile.IsRole(codes[slot]) && !Profile.KeepsCanonicalLoadout(codes[slot])
                && !_joiningEquipmentRoles.Contains(codes[slot])))
                throw new InvalidOperationException("Active joining equipment must initialize before capturing the native snapshot.");
        }
        JoiningEquipmentBindingStore.Write(AvatarSaveIdentity.Compute(save), CampaignFingerprint, JoiningWeaponSeed,
            new JoiningEquipmentBinding(JoiningEquipmentPolicy, _joiningEquipmentFighters, _joiningEquipmentRoles, _canonicalJoiningInventories));
    }
}
