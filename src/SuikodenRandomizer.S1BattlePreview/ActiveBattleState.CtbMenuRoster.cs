extern alias GSD1;

using System;
using SuikodenRandomizer.Shared;

namespace SuikodenRandomizer.S1BattlePreview;

internal static partial class ActiveBattleState
{
    // Native menus store indices. An earlier action can remove a target or use
    // an item before the hero confirms the row they originally saw.
    private sealed class CtbMenuRoster
    {
        private readonly (IntPtr Actor, bool Alive)[] _targets;
        private readonly QueuedResource[] _personalItems;
        private readonly int _runeId;

        internal CtbMenuRoster(GSD1.BATTLE_WORK work, int slot)
        {
            _targets = new (IntPtr, bool)[work.pm_kazu + 1];
            for (int i = 1; i < _targets.Length; i++)
            {
                var target = ReadNativeTarget(work, i);
                _targets[i] = (target?.Pointer ?? IntPtr.Zero, IsLiving(target));
            }
            var hero = GetHeroBase(work, slot);
            _runeId = hero?.monsyo_data?.monsyo_id ?? 0;
            var pockets = hero?.item;
            int count = SharedBattleItemBridge.Enabled || pockets is null ? 0
                : Math.Min(S1BattleItemAddress.Pockets, Math.Min((int)hero!.item_kazu, pockets.Count));
            _personalItems = new QueuedResource[count];
            for (int i = 0; i < count; i++)
                if (pockets![i] is { item_id: > 0 } item)
                    _personalItems[i] = new(item.item_id, hero!.Pointer, item.Pointer);
        }

        internal bool Accepts(GSD1.BATTLE_WORK work, int slot, GSD1.PM_DATA actor, NativeCommand command)
        {
            if (command.Kind == Defend) return true;
            if (command.Target > 0)
            {
                var target = ReadNativeTarget(work, command.Target);
                if (target is null || command.Target >= _targets.Length
                    || _targets[command.Target] != (target.Pointer, IsLiving(target))) return false;
            }
            if (command.Kind == Attack)
                return Array.Exists(ReadTargets(work, slot, actor), target => target.Slot == command.Target);
            var available = ReadQueuedResource(work, slot, command, reservedItem: true);
            if (available.Id <= 0) return false;
            if (command.Kind == Rune) return (actor.status & RuneCommandBlocked) == 0 && available.Id == _runeId;
            // The host captured the item the player actually picked in the item list.
            // Only a standalone preview has to fall back to the menu-opening pocket.
            return command.Kind == Item && (SharedBattleItemBridge.Enabled
                || (command.Resource >= 0 && command.Resource < _personalItems.Length
                    && available == _personalItems[command.Resource]));
        }
    }
}
