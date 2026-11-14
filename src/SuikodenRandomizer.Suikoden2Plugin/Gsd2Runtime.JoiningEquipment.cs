extern alias GSD2;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SuikodenRandomizer.Suikoden2Plugin;

internal sealed class Gsd2PendingEquipment
{
    public int OriginalRole { get; set; }
    public Gsd2EquipmentSnapshot Loadout { get; set; } = new();
    public Gsd2EquipmentSnapshot ObservedLoadout { get; set; } = new();
    internal Gsd2PendingEquipment Copy() => new() { OriginalRole = OriginalRole, Loadout = Loadout.Copy(), ObservedLoadout = ObservedLoadout.Copy() };
}

internal sealed partial class Gsd2Runtime
{
    internal string ActiveJoiningEquipment { get; private set; } = "Donor";
    private readonly HashSet<int> _joiningEquipmentInitializedDonors = new();
    private Dictionary<int, Gsd2EquipmentSnapshot> _originalRoleEquipment = new();
    private Dictionary<int, Gsd2PendingEquipment> _pendingJoiningEquipment = new();
    private readonly HashSet<int> _equipmentBagWaitLogged = new();
    private string? _equipmentBagWaitSignature;

    private static string ReadJoiningEquipmentPolicy(string? value, bool saved)
    {
        if (saved && value is null) return "Donor";
        if (value == "Donor" || value == "MatchRole") return value;
        throw new InvalidDataException("Unsupported Suikoden II recruitment equipment policy.");
    }

    private void ResetJoiningEquipment()
    {
        ActiveJoiningEquipment = "Donor";
        _joiningEquipmentInitializedDonors.Clear(); _originalRoleEquipment.Clear();
        _pendingJoiningEquipment.Clear(); _equipmentBagWaitLogged.Clear(); _equipmentBagWaitSignature = null;
    }

    private void CaptureOriginalRoleEquipment(GSD2.CHARA_DATA starts)
    {
        if (ActiveJoiningEquipment != "MatchRole" || Profile.Mode != "Shuffle") return;
        // Native saved variable rows contain projected fighter state. Keep the
        // original starts separately for characters first recruited after load.
        _originalRoleEquipment = Profile.RoleIds.ToDictionary(role => role,
            role => Gsd2EquipmentSnapshot.Capture(starts.c_varia_dat[role]));
    }

    private int EquipmentDonor(int role) => Profile.Mode == "Shuffle" ? Profile.GetDonor(role) : _fixed!.GetDonor(role);
    private bool NeedsJoiningEquipment(int role, int donor) => ActiveJoiningEquipment == "MatchRole"
        && !KeepCanonical(role, donor) && !_joiningEquipmentInitializedDonors.Contains(donor);

    private void RememberFirstEquipment(int role, int donor, Gsd2EquipmentSnapshot original, Gsd2EquipmentSnapshot observed)
    {
        if (NeedsJoiningEquipment(role, donor) && !_pendingJoiningEquipment.ContainsKey(donor))
            _pendingJoiningEquipment.Add(donor, new Gsd2PendingEquipment { OriginalRole = role, Loadout = original.Copy(), ObservedLoadout = observed.Copy() });
    }

    private int[] PendingJoinedEquipmentRoles()
    {
        if (ActiveJoiningEquipment != "MatchRole") return Array.Empty<int>();
        var slots = GSD2.OldSrcBase.game_work?.party_data?.party_cha_no;
        if (slots is null || slots.Count < 6) throw new InvalidOperationException("Recruitment equipment party is incomplete.");
        return Enumerable.Range(0, 6).Select(index => (int)slots[index]).Distinct()
            .Where(role => (Profile.Mode == "Shuffle" ? Profile.IsRole(role) : _fixed!.Assignments.ContainsKey(role))
                && NeedsJoiningEquipment(role, EquipmentDonor(role))).ToArray();
    }

    private void ObserveJoinedEquipment()
    {
        if (_projected is null) return;
        int[] roles = PendingJoinedEquipmentRoles();
        if (roles.Length == 0) return;
        var game = GSD2.OldSrcBase.game_work ?? throw new InvalidOperationException("Equipment game state is missing.");
        var current = game.chara_data;
        var baseline = _baseline ?? throw new InvalidOperationException("Original equipment metadata is unavailable.");
        if (current is null || current.Pointer != _projected.Pointer)
            throw new InvalidOperationException("Recruitment equipment publication is not current.");
        // A full bag can defer the first join transaction. Never erase gear,
        // consumed uses or newly granted items while waiting for bag capacity.
        var unchanged = new List<int>();
        foreach (int role in roles)
        {
            int donor = EquipmentDonor(role);
            if (_pendingJoiningEquipment.TryGetValue(donor, out var pending)
                && !pending.ObservedLoadout.Same(Gsd2EquipmentSnapshot.Capture(current.c_varia_dat[role])))
            {
                _joiningEquipmentInitializedDonors.Add(donor); _pendingJoiningEquipment.Remove(donor); _equipmentBagWaitLogged.Remove(donor);
                Trace("JOIN EQUIPMENT S2 skipped role=" + role + "; donor=" + donor + "; inventory changed while waiting; earned items and equipment preserved.");
            }
            else unchanged.Add(role);
        }
        roles = unchanged.ToArray();
        if (roles.Length == 0) return;
        var bag = game.party_data.party_item ?? throw new InvalidOperationException("Suikoden II shared inventory is missing.");
        var bagRows = bag.ToArray();
        if (bagRows.Any(item => item is null)) throw new InvalidOperationException("Suikoden II shared inventory is incomplete.");
        var bagBefore = bagRows.Select(item => (Id: item.item_no, Uses: item.use_cnt)).ToArray();
        int[] empty = Enumerable.Range(0, bagRows.Length).Where(index => bagBefore[index].Id == 0 && bagBefore[index].Uses == 0).ToArray();
        string waitSignature = string.Join(",", roles.Select(role => role + ":" + EquipmentDonor(role) + ":"
            + (_pendingJoiningEquipment.TryGetValue(EquipmentDonor(role), out var pending) ? pending.OriginalRole : role)))
            + ":" + string.Join(",", empty);
        if (_equipmentBagWaitSignature == waitSignature) return;
        string party = string.Join(",", game.party_data.party_cha_no.Take(6));
        var before = roles.ToDictionary(role => role, role => Gsd2NativeData.Variable(current.c_varia_dat[role]));
        var rows = roles.ToDictionary(role => role, role => current.c_varia_dat[role]);
        var desired = new Dictionary<int, Gsd2EquipmentSnapshot>();
        foreach (int role in roles)
        {
            int donor = EquipmentDonor(role);
            if (!_pendingJoiningEquipment.TryGetValue(donor, out var pending))
            {
                if (Profile.Mode != "Shuffle" || !_originalRoleEquipment.TryGetValue(role, out var original))
                    throw new InvalidDataException("Missing first-role Suikoden II recruitment equipment for donor " + donor + ".");
                RememberFirstEquipment(role, donor, original, Gsd2EquipmentSnapshot.Capture(rows[role])); pending = _pendingJoiningEquipment[donor];
            }
            desired.Add(role, Gsd2EquipmentBalance.Plan(game, baseline, pending.OriginalRole, donor, pending.Loadout));
        }
        byte[] transfers = roles.SelectMany(role => desired[role].TransferToBag).ToArray();
        if (transfers.Length > empty.Length)
        {
            _equipmentBagWaitSignature = waitSignature;
            foreach (int role in roles)
            {
                int donor = EquipmentDonor(role);
                if (_equipmentBagWaitLogged.Add(donor))
                    Trace("JOIN EQUIPMENT S2 pending role=" + role + "; donor=" + donor
                        + "; shared inventory needs " + transfers.Length + " free slots; initialization will retry when space is available.");
            }
            return;
        }
        var bagExpected = (ValueTuple<byte, byte>[])bagBefore.Clone();
        for (int index = 0; index < transfers.Length; index++) bagExpected[empty[index]] = (transfers[index], 0x10);
        void VerifyBag((byte Id, byte Uses)[] expected)
        {
            if (game.party_data.party_item.Pointer != bag.Pointer || bag.Count != expected.Length)
                throw new InvalidOperationException("Recruitment shared inventory reference changed.");
            for (int index = 0; index < expected.Length; index++)
                if (bag[index].Pointer != bagRows[index].Pointer || bagRows[index].item_no != expected[index].Id
                    || bagRows[index].use_cnt != expected[index].Uses)
                    throw new InvalidOperationException("Recruitment shared inventory write did not verify.");
        }
        try
        {
            foreach (var entry in desired) entry.Value.Apply(rows[entry.Key]);
            for (int index = 0; index < transfers.Length; index++)
            {
                bagRows[empty[index]].item_no = transfers[index];
                // G2_kind_no_get/CIT_KIND encode kind in the high nibble;
                // G2_data_get/G2_def_val identify kind1 as the armor catalog.
                bagRows[empty[index]].use_cnt = 0x10;
            }
            foreach (int role in roles)
            {
                var expected = Gsd2NativeData.Variable(before[role]); desired[role].Apply(expected);
                if (current.c_varia_dat[role].Pointer != rows[role].Pointer || !SameFighter(expected, rows[role]))
                    throw new InvalidOperationException("Recruitment equipment write did not verify.");
            }
            VerifyBag(bagExpected);
            if (!OwnsExecution || game.chara_data.Pointer != current.Pointer
                || party != string.Join(",", game.party_data.party_cha_no.Take(6)))
                throw new InvalidOperationException("Recruitment equipment party changed during initialization.");
        }
        catch (Exception failure)
        {
            var rollback = new List<Exception>();
            foreach (int role in roles)
                try
                {
                    Gsd2EquipmentSnapshot.Capture(before[role]).Apply(rows[role]);
                    if (current.c_varia_dat[role].Pointer != rows[role].Pointer || !SameFighter(before[role], rows[role]))
                        throw new InvalidOperationException("Recruitment equipment rollback did not verify.");
                }
                catch (Exception error) { rollback.Add(error); }
            try
            {
                for (int index = 0; index < transfers.Length; index++)
                {
                    int target = empty[index]; bagRows[target].item_no = bagBefore[target].Id;
                    bagRows[target].use_cnt = bagBefore[target].Uses;
                }
                VerifyBag(bagBefore);
            }
            catch (Exception error) { rollback.Add(error); }
            if (rollback.Count != 0)
            {
                rollback.Insert(0, failure);
                throw new AggregateException("Recruitment equipment write and rollback failed.", rollback);
            }
            throw;
        }
        _equipmentBagWaitSignature = null;
        foreach (int role in roles)
        {
            int donor = EquipmentDonor(role); var planned = desired[role];
            _joiningEquipmentInitializedDonors.Add(donor); _pendingJoiningEquipment.Remove(donor); _equipmentBagWaitLogged.Remove(donor);
            Trace("JOIN EQUIPMENT S2 role=" + role + "; donor=" + donor + "; policy=" + ActiveJoiningEquipment
                + "; armor=" + string.Join(",", planned.Armor) + "; items=" + string.Join(",", planned.Items)
                + "; uses=" + string.Join(",", planned.ItemUses) + "; sharedBag=" + string.Join(",", planned.TransferToBag));
            foreach (string decision in planned.Decisions) Trace("JOIN EQUIPMENT S2 role=" + role + "; donor=" + donor + "; " + decision);
        }
    }

    private static bool SameFighter(GSD2.C_VARIA_DAT left, GSD2.C_VARIA_DAT right) =>
        System.Text.Json.JsonSerializer.Serialize(Gsd2FighterRecord.Capture(left))
        == System.Text.Json.JsonSerializer.Serialize(Gsd2FighterRecord.Capture(right));

    private void RestoreJoiningEquipment(Gsd2CampaignBinding binding)
    {
        ActiveJoiningEquipment = ReadJoiningEquipmentPolicy(binding.JoiningEquipment, true);
        if (binding.JoiningEquipmentInitializedDonors is null || binding.OriginalRoleEquipment is null || binding.PendingJoiningEquipment is null
            || binding.JoiningEquipmentInitializedDonors.Distinct().Count() != binding.JoiningEquipmentInitializedDonors.Length)
            throw new InvalidDataException("Saved Suikoden II recruitment equipment history is invalid.");
        var allowed = Profile.Mode == "Shuffle" ? Profile.Assignments.Where(pair => !KeepCanonical(pair.Key, pair.Value)).Select(pair => pair.Value) : Profile.CompanionIds;
        if (binding.JoiningEquipmentInitializedDonors.Any(id => !allowed.Contains(id))
            || ActiveJoiningEquipment == "Donor" && (binding.JoiningEquipmentInitializedDonors.Length != 0
                || binding.OriginalRoleEquipment.Count != 0 || binding.PendingJoiningEquipment.Count != 0))
            throw new InvalidDataException("Saved recruitment equipment history does not match its policy or roster.");
        if (ActiveJoiningEquipment == "MatchRole" && Profile.Mode == "Shuffle")
        {
            if (!Profile.RoleIds.OrderBy(id => id).SequenceEqual(binding.OriginalRoleEquipment.Keys.OrderBy(id => id)))
                throw new InvalidDataException("Saved original Suikoden II recruitment loadouts are incomplete.");
        }
        else if (binding.OriginalRoleEquipment.Count != 0)
            throw new InvalidDataException("A fixed roster cannot contain shuffle role equipment records.");
        foreach (var value in binding.OriginalRoleEquipment.Values)
            (value ?? throw new InvalidDataException("Saved original equipment record is missing.")).Validate();
        foreach (var entry in binding.PendingJoiningEquipment)
        {
            var pending = entry.Value;
            if (!allowed.Contains(entry.Key) || binding.JoiningEquipmentInitializedDonors.Contains(entry.Key)
                || pending?.Loadout is null || pending.ObservedLoadout is null || pending.OriginalRole < 1 || pending.OriginalRole > 83
                || KeepCanonical(pending.OriginalRole, entry.Key)
                || Profile.Mode == "Shuffle" && (!Profile.IsRole(pending.OriginalRole) || Profile.GetDonor(pending.OriginalRole) != entry.Key))
                throw new InvalidDataException("Saved pending recruitment equipment is invalid.");
            pending.Loadout.Validate();
            pending.ObservedLoadout.Validate();
        }
        _joiningEquipmentInitializedDonors.UnionWith(binding.JoiningEquipmentInitializedDonors);
        _originalRoleEquipment = binding.OriginalRoleEquipment.ToDictionary(pair => pair.Key, pair => pair.Value.Copy());
        _pendingJoiningEquipment = binding.PendingJoiningEquipment.ToDictionary(pair => pair.Key, pair => pair.Value.Copy());
        if (ActiveJoiningEquipment == "MatchRole" && Profile.Mode != "Shuffle"
            && Assignments.Any(pair => !KeepCanonical(pair.Key, pair.Value)
                && !_joiningEquipmentInitializedDonors.Contains(pair.Value) && !_pendingJoiningEquipment.ContainsKey(pair.Value)))
            throw new InvalidDataException("Saved fixed recruitment equipment initialization history is incomplete.");
    }
}
