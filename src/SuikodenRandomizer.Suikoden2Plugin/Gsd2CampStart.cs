extern alias GSD2;

using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime.InteropTypes.Arrays;

namespace SuikodenRandomizer.Suikoden2Plugin;

/// <summary>One fresh-world transaction for the captured first camp interior.</summary>
internal sealed class Gsd2CampStart
{
    internal const string Point = "ViktorCamp";
    internal const int SavePx = 167;
    internal const int SavePy = 206;

    // Same-run Data18 mirror d47406a9dc94b0ccd389ffa59e2c7e6b89941b0046e9f21a21fcbfd4e94ecc9c.
    // These are 27 observed opening/task-introduction bits; optional/import bits remain intact.
    private static readonly (int Index, byte Mask)[] CharacterBits = { (1, 0x47) };
    private static readonly (int Index, byte Mask)[] TownBits = { (0, 0x80), (3, 0x80) };
    private static readonly (int Index, byte Mask)[] EventBits =
    {
        (0, 0xA1), (1, 0xF9), (2, 0x39), (3, 0x03), (5, 0x80),
        (160, 0xC1), (161, 0x01), (236, 0x01)
    };

    private bool _armed;
    internal bool IsApplied { get; private set; }

    internal void Arm(bool requested)
    {
        _armed = requested;
        IsApplied = false;
    }

    internal void Cancel()
    {
        _armed = false;
        IsApplied = false;
    }

    internal bool ApplyAtNativeBoundary(GSD2.GAME_WORK game)
    {
        if (!_armed) return false;
        // Consume before native reads or writes, including any reentrant attempt.
        _armed = false;
        if (game is null) throw new InvalidOperationException("No native Suikoden II campaign state.");
        var world = game.game_data;
        var party = game.party_data;
        if (world is null || party is null)
            throw new InvalidOperationException("Suikoden II camp start requires initialized world and party data.");
        var position = world.deru_pos;
        var members = party.party_cha_no;
        var characterFlags = RequireBank(game.chara_flag, 128, "character");
        var townFlags = RequireBank(game.town_flag, 71, "town");
        var eventFlags = RequireBank(game.event_flag, 256, "event");
        RequireBank(game.scpoi_flag, 8, "scenario-point");
        RequireBank(game.t_box_flag, 32, "treasure-box");
        if (position?.Count != 2 || members?.Count != 8)
            throw new InvalidOperationException("Suikoden II camp start has an incompatible native party or position layout.");
        if (members[0] != 1 || party.party_info != 0)
            throw new InvalidOperationException("Suikoden II camp start requires the fresh solo protagonist party.");
        for (int i = 1; i < members.Count; i++)
            if (members[i] != 0)
                throw new InvalidOperationException("Suikoden II camp start cannot replace an existing party.");
        if (world.area_no != 0 || world.town_no != 0 || world.map_no != 0
            || world.area_no2 != 0 || world.town_no2 != 0 || world.mem_type != 0
            || position[0] != 0 || position[1] != 0)
            throw new InvalidOperationException("Suikoden II camp start requires the untouched initial native location.");
        RequireUnset(characterFlags, CharacterBits);
        RequireUnset(townFlags, TownBits);
        RequireUnset(eventFlags, EventBits);

        // Read all backups before the first write. Nothing below replaces a native object/array.
        var previousCharacter = ReadBits(characterFlags, CharacterBits);
        var previousTown = ReadBits(townFlags, TownBits);
        var previousEvent = ReadBits(eventFlags, EventBits);
        int previousArea = world.area_no, previousTownNo = world.town_no, previousMap = world.map_no;
        int previousArea2 = world.area_no2, previousTown2 = world.town_no2;
        byte previousMemoryType = world.mem_type, previousPartyInfo = party.party_info;
        int previousX = position[0], previousY = position[1];
        var undo = new List<Action>(20);
        void Write(Action apply, Action restore)
        {
            // A setter may write and then throw, so register its undo before calling it.
            undo.Add(restore);
            apply();
        }
        void SetBits(Il2CppStructArray<byte> bank, (int Index, byte Mask)[] bits, byte[] previous)
        {
            for (int i = 0; i < bits.Length; i++)
            {
                int index = bits[i].Index;
                byte original = previous[i], next = (byte)(original | bits[i].Mask);
                Write(() => bank[index] = next, () => bank[index] = original);
            }
        }
        try
        {
            SetBits(characterFlags, CharacterBits, previousCharacter);
            SetBits(townFlags, TownBits, previousTown);
            SetBits(eventFlags, EventBits, previousEvent);
            Write(() => party.party_info = 1, () => party.party_info = previousPartyInfo);
            Write(() => world.area_no = 1, () => world.area_no = previousArea);
            Write(() => world.town_no = 10, () => world.town_no = previousTownNo);
            Write(() => world.map_no = 2, () => world.map_no = previousMap);
            Write(() => world.area_no2 = 1, () => world.area_no2 = previousArea2);
            Write(() => world.town_no2 = 1, () => world.town_no2 = previousTown2);
            Write(() => world.mem_type = 1, () => world.mem_type = previousMemoryType);
            // Core(true) uses saved px/py*8, not the capture's stale entry coordinates.
            Write(() => position[0] = SavePx * 8, () => position[0] = previousX);
            Write(() => position[1] = SavePy * 8, () => position[1] = previousY);
            IsApplied = true;
            return true;
        }
        catch (Exception writeFailure)
        {
            var failures = new List<Exception> { writeFailure };
            // A failed restoration must not prevent attempts to restore the other fields.
            for (int i = undo.Count - 1; i >= 0; i--)
                try { undo[i](); }
                catch (Exception restoreFailure) { failures.Add(restoreFailure); }
            if (failures.Count > 1)
                throw new AggregateException("Suikoden II camp start rollback failed.", failures);
            throw;
        }
    }

    private static Il2CppStructArray<byte> RequireBank(Il2CppStructArray<byte> bank, int count, string name)
    {
        if (bank?.Count != count)
            throw new InvalidOperationException("Suikoden II camp start has an incompatible " + name + " flag layout.");
        return bank;
    }

    private static void RequireUnset(Il2CppStructArray<byte> bank, (int Index, byte Mask)[] bits)
    {
        foreach (var (index, mask) in bits)
            if ((bank[index] & mask) != 0)
                throw new InvalidOperationException("Suikoden II camp start cannot overwrite existing opening progression.");
    }

    private static byte[] ReadBits(Il2CppStructArray<byte> bank, (int Index, byte Mask)[] bits)
    {
        var previous = new byte[bits.Length];
        for (int i = 0; i < bits.Length; i++) previous[i] = bank[bits[i].Index];
        return previous;
    }
}
