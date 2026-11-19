using System;

namespace SuikodenRandomizer.Core.Combat;

public enum CtbEnemyShape { Circle, Triangle, Square, Diamond, Cross, Star }

/// <summary>A presentation identity scoped to one encounter, independent of enemy-list position.</summary>
public readonly struct CtbEnemyBadge
{
    internal CtbEnemyBadge(long encounter, int id, int slot, long nativeIdentity,
        CtbEnemyShape shape, byte red, byte green, byte blue)
    {
        Encounter = encounter;
        Id = id;
        Slot = slot;
        NativeIdentity = nativeIdentity;
        Shape = shape;
        Red = red;
        Green = green;
        Blue = blue;
    }

    public long Encounter { get; }
    public int Id { get; }
    public int Slot { get; }
    public long NativeIdentity { get; }
    public CtbEnemyShape Shape { get; }
    public byte Red { get; }
    public byte Green { get; }
    public byte Blue { get; }
    public uint Rgb => ((uint)Red << 16) | ((uint)Green << 8) | Blue;
    public string Label => "E" + Id;
}

/// <summary>
/// Stable badges for native combatant slots. KO preserves a binding for revival;
/// slot replacement retires its badge permanently for the rest of the encounter.
/// The caller must clear a departing actor before reassigning its native identity
/// to another slot. Display-list reordering does not change actor bindings.
/// </summary>
public sealed class CtbEnemyIdentity
{
    public const int PrimaryBadgeCount = 6;
    private readonly Binding[] _bindings;
    private int _nextId = 1;

    public CtbEnemyIdentity(int slotCount = 33)
    {
        if (slotCount <= 0) throw new ArgumentOutOfRangeException(nameof(slotCount));
        _bindings = new Binding[slotCount];
    }

    public int SlotCount => _bindings.Length;
    public long Encounter { get; private set; } = 1;
    public int AllocatedCount => _nextId - 1;

    /// <summary>
    /// Returns a badge only for a living actor. Identity zero removes the binding.
    /// A newly observed dead actor receives no badge until it first becomes alive.
    /// Repeated observations never change an existing actor's badge.
    /// </summary>
    public CtbEnemyBadge? Observe(int slot, long identity, bool alive)
    {
        if ((uint)slot >= (uint)_bindings.Length) throw new ArgumentOutOfRangeException(nameof(slot));
        if (identity == 0)
        {
            _bindings[slot] = default;
            return null;
        }
        for (int other = 0; other < _bindings.Length; other++)
            if (other != slot && _bindings[other].Identity == identity)
                throw new InvalidOperationException("One native actor identity cannot own two combatant slots.");

        ref Binding binding = ref _bindings[slot];
        if (binding.Identity != identity)
            binding = new Binding { Identity = identity };
        binding.Alive = alive;
        if (alive && !binding.Badge.HasValue)
        {
            if (_nextId == int.MaxValue)
                throw new InvalidOperationException("The encounter exhausted its presentation identity range.");
            binding.Badge = CreateBadge(slot, identity, _nextId++);
        }
        return alive ? binding.Badge : null;
    }

    /// <summary>Fail-closed lookup for target/marker adapters; missing, dead and stale actors return null.</summary>
    public CtbEnemyBadge? Get(int slot, long identity)
    {
        if ((uint)slot >= (uint)_bindings.Length || identity == 0) return null;
        ref Binding binding = ref _bindings[slot];
        return binding.Alive && binding.Identity == identity ? binding.Badge : null;
    }

    public bool Matches(CtbEnemyBadge badge)
    {
        CtbEnemyBadge? current = Get(badge.Slot, badge.NativeIdentity);
        return current.HasValue && current.Value.Encounter == badge.Encounter && current.Value.Id == badge.Id;
    }

    /// <summary>
    /// Pure zero-based list lookup. nativeIdentities is indexed by combatant slot,
    /// not list position. Native UI cursor conventions must be converted by the
    /// adapter; this method never guesses one-based/sentinel index semantics.
    /// </summary>
    public CtbEnemyBadge? ResolveNativeListTarget(ReadOnlySpan<int> listSlotMap, int index,
        ReadOnlySpan<long> nativeIdentities)
    {
        if ((uint)index >= (uint)listSlotMap.Length) return null;
        int slot = listSlotMap[index];
        if ((uint)slot >= (uint)nativeIdentities.Length) return null;
        return Get(slot, nativeIdentities[slot]);
    }

    /// <summary>Starts a fresh encounter; old badge values can no longer match current actors.</summary>
    public void Reset()
    {
        if (Encounter == long.MaxValue)
            throw new InvalidOperationException("The encounter generation range was exhausted.");
        Array.Clear(_bindings, 0, _bindings.Length);
        _nextId = 1;
        Encounter++;
    }

    private CtbEnemyBadge CreateBadge(int slot, long identity, int id)
    {
        // Okabe–Ito hues pair every colour with a different shape. After six,
        // the number remains unique even though the colour/shape pair cycles.
        switch ((id - 1) % PrimaryBadgeCount)
        {
            case 0: return new CtbEnemyBadge(Encounter, id, slot, identity, CtbEnemyShape.Circle, 0x56, 0xB4, 0xE9);
            case 1: return new CtbEnemyBadge(Encounter, id, slot, identity, CtbEnemyShape.Triangle, 0xE6, 0x9F, 0x00);
            case 2: return new CtbEnemyBadge(Encounter, id, slot, identity, CtbEnemyShape.Square, 0x00, 0x9E, 0x73);
            case 3: return new CtbEnemyBadge(Encounter, id, slot, identity, CtbEnemyShape.Diamond, 0xCC, 0x79, 0xA7);
            case 4: return new CtbEnemyBadge(Encounter, id, slot, identity, CtbEnemyShape.Cross, 0xD5, 0x5E, 0x00);
            default: return new CtbEnemyBadge(Encounter, id, slot, identity, CtbEnemyShape.Star, 0xF0, 0xE4, 0x42);
        }
    }

    private struct Binding
    {
        public long Identity;
        public bool Alive;
        public CtbEnemyBadge? Badge;
    }
}
