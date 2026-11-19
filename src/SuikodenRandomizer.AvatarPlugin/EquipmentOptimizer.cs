extern alias GSD1;

using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using Il2CppInterop.Runtime.InteropTypes.Arrays;

namespace SuikodenRandomizer.AvatarPlugin;

internal static class EquipmentOptimizer
{
    private enum Sources { Personal, Party, Warehouse, PartyAndWarehouse }
    private static ConfigEntry<Sources>? _sources;
    private static Sources Scope => _sources?.Value ?? Sources.PartyAndWarehouse;
    internal static string SourceLabel => Scope switch
    {
        Sources.Personal => "Own pockets",
        Sources.Party => "Party",
        Sources.Warehouse => "Own + Warehouse",
        _ => "Party + Warehouse"
    };

    internal static void Configure(ConfigFile config) =>
        _sources = config.Bind("Inventory", "OptimizeSources", Sources.PartyAndWarehouse,
            "Equipment Optimize always considers the selected fighter's pockets. Party adds loose gear from active teammates. Warehouse adds stored gear after recruiting Roc.");

    internal static void CycleSources(int direction)
    {
        if (_sources is { } entry && direction != 0)
            entry.Value = (Sources)(((int)Scope + Math.Sign(direction) + 4) % 4);
    }

    internal static string Optimize(GSD1.PLAYER_DATA target)
    {
        try
        {
            if (AvatarRuntime.Current?.OwnsExecution != true || GSD1.G_cmd_c.command_work is not { } menu
                || menu.player_data is not { } roster || menu.player_kazu is < 1 or > 6
                || menu.player_kazu > roster.Count || (uint)menu.selected_player >= (uint)menu.player_kazu
                || roster[menu.selected_player]?.Pointer != target.Pointer)
                return "Equipment menu unavailable";

            bool useParty = Scope is Sources.Party or Sources.PartyAndWarehouse;
            var store = Scope is Sources.Warehouse or Sources.PartyAndWarehouse ? EquipmentWarehouse.Capture() : null;
            var owners = new List<GSD1.PLAYER_BASE> { target.player_base };
            if (useParty)
                for (int i = 0; i < menu.player_kazu; i++)
                    if (roster[i]?.player_base is { } bag && owners.All(existing => existing.Pointer != bag.Pointer))
                        owners.Add(bag);
            var before = owners.Select(JoiningEquipmentNative.Capture).ToArray();
            var table = GSD1.G_cmd_c.soubi_table;
            var slotTypes = new byte[6];
            for (int i = 0; i < table.Count; i++)
            {
                var row = table[i];
                if (row.item_data is >= 1 and <= 5) slotTypes[row.item_data] = row.item_type;
            }
            if (slotTypes.Skip(1).Any(type => type == 0))
                throw new InvalidOperationException("Equipment slot definitions are incomplete.");
            var catalog = JoiningEquipmentNative.Catalog();
            var wearability = new Dictionary<ushort, bool>();
            bool CanWear(ushort id)
            {
                if (!wearability.TryGetValue(id, out bool allowed))
                {
                    var definition = GSD1.G_item_c_data.item_data_table[id]?.TryCast<GSD1.BOUGU_DATA>();
                    allowed = definition != null && GSD1.h_omise_c.soubi_kanou_check(target, definition) != 0;
                    wearability.Add(id, allowed);
                }
                return allowed;
            }
            var plan = EquipmentPlan.Create(before, 0, catalog, slotTypes, CanWear,
                store?.Counts ?? new Dictionary<ushort, int>(), useParty,
                (id, data) => store?.CanStore(id, data) == true,
                delta => store?.CanApply(delta) ?? delta.Count == 0);
            string label = GSD1.Fhuman_c.GetCharacterName(target.player_base.chara_no, false);
            if (!plan.Changed)
            {
                AvatarRuntime.Current?.Trace($"OPTIMIZE: {label}, sources={Scope}, no available upgrade.");
                return "No available upgrade";
            }

            var replacements = new List<(GSD1.PLAYER_BASE Owner, byte BeforeCount, byte AfterCount,
                Il2CppReferenceArray<GSD1.PLAYER_ITEM> Before, Il2CppReferenceArray<GSD1.PLAYER_ITEM> After)>();
            for (int i = 0; i < owners.Count; i++)
            {
                if (plan.Bags[i].Same(before[i])) continue;
                replacements.Add((owners[i], before[i].Count, plan.Bags[i].Count, owners[i].item, JoiningEquipmentNative.NativeItems(plan.Bags[i])));
            }
            bool stockApplied = false;
            int written = 0;
            try
            {
                if (plan.WarehouseDelta.Count != 0)
                {
                    if (store is null) throw new InvalidOperationException("Warehouse unavailable.");
                    store.Apply(plan.WarehouseDelta);
                    stockApplied = true;
                }
                foreach (var change in replacements)
                {
                    written++;
                    change.Owner.item = change.After;
                    change.Owner.item_kazu = change.AfterCount;
                }
            }
            catch
            {
                for (int i = written - 1; i >= 0; i--)
                {
                    var change = replacements[i];
                    change.Owner.item = change.Before;
                    change.Owner.item_kazu = change.BeforeCount;
                }
                if (stockApplied) store!.Restore();
                throw;
            }
            string stock = string.Join(", ", plan.WarehouseDelta.Select(e => $"{e.Key}={e.Value:+0;-0}"));
            AvatarRuntime.Current?.Trace($"OPTIMIZE: {label}, sources={Scope}, changed slots={plan.Upgrades}, " +
                $"equipped [{Worn(before[0])}] -> [{Worn(plan.Bags[0])}], warehouse [{stock}].");
            return plan.Upgrades == 1 ? "1 slot upgraded" : $"{plan.Upgrades} slots upgraded";
        }
        catch (Exception error)
        {
            AvatarPlugin.PluginLog.LogWarning("Equipment Optimize: " + error.Message);
            return "Could not optimize equipment";
        }
    }

    private static string Worn(JoiningEquipmentInventory bag) =>
        string.Join(", ", bag.Items.Take(bag.Count).Where(item => (item.Equipped & 0x7f) != 0)
            .OrderBy(item => item.Equipped & 0x7f).Select(item => $"{item.Equipped & 0x7f}={item.Id}"));
}
