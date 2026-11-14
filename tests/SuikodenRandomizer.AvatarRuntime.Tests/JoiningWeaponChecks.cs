extern alias GSD1;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using SuikodenRandomizer.AvatarPlugin;
using SuikodenRandomizer.Core;
using SuikodenRandomizer.Core.Avatars;

internal static partial class Program
{
    private static void RunJoiningWeaponChecks()
    {
        Case("Gremio receiving Kasim's weapon thirteen joins with weapon one under both new policies", () =>
        {
            string seed = Enumerable.Range(0, 10000).Select(index => "joining-weapon-regression-" + index)
                .First(value => AvatarShuffleProfile.Create(value).GetDonor(2) == 16);
            foreach (string policy in new[] { "MatchRole", "Level1" })
            {
                var w = new World(false);
                AvatarPlugin.ConfiguredSeed = seed; AvatarPlugin.ConfiguredJoiningWeaponLevel = policy;
                w.Base(2).buki_data.level = 1; w.Base(16).buki_data.level = 13; w.Base(16).buki_data.buki_id = 14;
                var kasim = AvatarNativeData.CloneBase(w.Base(16));
                w.Runtime.BeginNewGame(); w.Ready();
                var gremio = w.Base(2);
                Check(w.Runtime.Profile.GetDonor(2) == 16 && gremio.buki_data.level == 1 && gremio.buki_data.buki_id == 14,
                    "Kasim replacing Gremio kept the overpowered joining level under " + policy);
                Check(gremio.level == kasim.level && gremio.max_hp == kasim.max_hp && gremio.noryoku.SequenceEqual(kasim.noryoku)
                    && gremio.Equipment == kasim.Equipment && gremio.Rune == kasim.Rune
                    && gremio.monsyo_data!.monsyo_id == kasim.monsyo_data!.monsyo_id,
                    "Weapon balancing changed Kasim's character level, stats, rune or other equipment");
            }
        });

        Case("Shuffle MatchRole retains donor weapon identity with every role's pristine joining level", () =>
        {
            var w = new World();
            foreach (var pair in w.Bases)
            {
                pair.Value.buki_data.buki_id = (byte)pair.Key;
                pair.Value.buki_data.level = (byte)(1 + pair.Key % 16);
            }
            w.Base(16).buki_data.level = 13;
            var before = w.Bases.ToDictionary(pair => pair.Key, pair => AvatarNativeData.CloneBase(pair.Value));
            string mapping = w.Runtime.Profile.Fingerprint;
            w.Ready();
            foreach (int role in w.Runtime.Profile.RoleIds)
            {
                int donor = w.Runtime.Profile.GetDonor(role);
                Check(w.Base(role).buki_data.buki_id == before[donor].buki_data.buki_id
                    && w.Base(role).buki_data.level == before[role].buki_data.level,
                    "Weapon identity or canonical joining level changed for role " + role);
                Check(w.Base(role).Equipment == before[donor].Equipment && w.Base(role).Rune == before[donor].Rune,
                    "Joining level altered other donor equipment");
            }
            Check(w.Runtime.Profile.Fingerprint == mapping, "Balance policy changed the existing avatar mapping");
        });

        Case("Level1 sets newly copied weapons to native level one and retains protected story loadouts", () =>
        {
            var w = new World(false);
            AvatarPlugin.ConfiguredJoiningWeaponLevel = "Level1";
            AvatarPlugin.ConfiguredIncludeTir = true;
            foreach (var pair in w.Bases) { pair.Value.buki_data.level = 13; pair.Value.buki_data.buki_id = (byte)pair.Key; }
            w.Runtime.BeginNewGame();
            var protectedBases = w.Runtime.Profile.RoleIds.Where(w.Runtime.Profile.KeepsCanonicalLoadout)
                .ToDictionary(id => id, id => AvatarNativeData.Fingerprint(w.Base(id)));
            w.Ready();
            foreach (int role in w.Runtime.Profile.RoleIds)
                if (w.Runtime.Profile.KeepsCanonicalLoadout(role))
                    Check(AvatarNativeData.Fingerprint(w.Base(role)) == protectedBases[role], "Protected story loadout changed");
                else
                    Check(w.Base(role).buki_data.level == 1 && w.Base(role).buki_data.buki_id == w.Runtime.Profile.GetDonor(role),
                        "Level1 failed or replaced donor weapon identity");
            Check(w.Base(CombatCatalog.TedId).buki_data.level == 13 && w.Base(CombatCatalog.OdessaId).buki_data.level == 13,
                "Excluded story characters changed");
        });

        Case("Sharpened weapons survive template replay, save reload and changed future settings", () =>
        {
            var w = new World(); w.Base(2).buki_data.level = 2; w.Ready();
            w.Base(2).buki_data.level = 8;
            byte weapon = w.Base(2).buki_data.buki_id;
            var save = WeaponSave('6');
            w.Runtime.WriteCampaignBinding(save); w.Runtime.WriteJoiningWeaponBinding(save);
            AvatarPlugin.ConfiguredJoiningWeaponLevel = "Level1";
            w.Runtime.PrepareLoad(save); w.Ready();
            Check(w.Runtime.JoiningWeaponPolicy == JoiningWeaponPolicy.MatchRole
                && w.Base(2).buki_data.level == 8 && w.Base(2).buki_data.buki_id == weapon,
                "Reload lowered a sharpened weapon or adopted future settings");
            w.Cache[2] = w.Originals[2]; w.Ready();
            Check(w.Base(2).buki_data.level == 8, "Mid-run template reconstruction reset sharpening");
        });

        Case("Legacy shuffle saves missing policy bindings retain their existing weapons", () =>
        {
            var w = new World(); w.Ready(); w.Base(2).buki_data.level = 13;
            var save = WeaponSave('7'); w.Runtime.WriteCampaignBinding(save);
            AvatarPlugin.ConfiguredJoiningWeaponLevel = "Level1";
            w.Runtime.PrepareLoad(save); w.Ready();
            Check(w.Runtime.JoiningWeaponPolicy == JoiningWeaponPolicy.Donor && w.Base(2).buki_data.level == 13,
                "Existing campaign was retroactively lowered");
            w.Runtime.WriteJoiningWeaponBinding(save);
            var saved = JoiningWeaponBindingStore.Read(save.Identity, w.Runtime.CampaignFingerprint, w.Runtime.Profile.Seed);
            Check(saved?.Policy == JoiningWeaponPolicy.Donor, "Legacy policy was not explicitly preserved on next save");
        });

        Case("Fixed roster normalizes a donor once and carries sharpening through role changes and reload", () =>
        {
            var w = FixedWeaponWorld("MatchRole");
            ActivateWeaponRole(w, 1); w.Ready();
            Check(w.Runtime.MapDisplayId(2) == 16 && w.Base(2).buki_data.level == 2 && w.Base(2).buki_data.buki_id == 16,
                "Kasim did not adopt Gremio's joining weapon level");
            w.Base(2).buki_data.level = 8;
            var save = WeaponSave('8'); w.Runtime.WriteCampaignBinding(save); w.Runtime.WriteJoiningWeaponBinding(save);
            AvatarPlugin.ConfiguredJoiningWeaponLevel = "Level1";
            w.Runtime.PrepareLoad(save); w.Game.player_data[1] = w.Originals[2]; w.Runtime.CompleteFixedLoad(); w.Ready();
            Check(w.Runtime.JoiningWeaponPolicy == JoiningWeaponPolicy.MatchRole && w.Base(2).buki_data.level == 8,
                "Fixed reload lost policy or sharpening");
            ActivateWeaponRole(w, 2); w.Ready();
            Check(w.Runtime.MapDisplayId(6) == 16 && w.Base(6).buki_data.level == 8 && w.Base(6).buki_data.buki_id == 16,
                "Role change reapplied joining level to an initialized fighter");
            ActivateWeaponRole(w, 1); w.Ready();
            Check(w.Base(2).buki_data.level == 8, "Rejoining original role reset earned weapon levels");
            Check(w.Base(6).buki_data.level == 4, "Departed story role was not restored");
        });

        Case("Fixed Level1 initializes later unused donors only when their first story role activates", () =>
        {
            var w = FixedWeaponWorld("Level1"); ActivateWeaponRole(w, 1); w.Ready();
            Check(w.Base(2).buki_data.level == 1 && w.Base(11).buki_data.level == 13,
                "First use changed an unused fighter's native base");
            w.Game.party_data.narabi[2] = 2; w.Game.party_data.player_kazu = 3; w.Ready();
            Check(w.Runtime.MapDisplayId(6) == 11 && w.Base(6).buki_data.level == 1 && w.Base(6).buki_data.buki_id == 11,
                "Later donor did not receive its first-use policy");
        });

        Case("Fixed legacy saves retain earned weapons through later story role changes", () =>
        {
            var w = FixedWeaponWorld("MatchRole"); ActivateWeaponRole(w, 1); w.Ready(); w.Base(2).buki_data.level = 13;
            var save = WeaponSave('9'); w.Runtime.WriteCampaignBinding(save);
            AvatarPlugin.ConfiguredJoiningWeaponLevel = "Level1";
            w.Runtime.PrepareLoad(save); w.Game.player_data[1] = w.Originals[2]; w.Runtime.CompleteFixedLoad(); w.Ready();
            ActivateWeaponRole(w, 2); w.Ready();
            Check(w.Runtime.JoiningWeaponPolicy == JoiningWeaponPolicy.Donor && w.Base(6).buki_data.level == 13,
                "Legacy fixed roster was normalized on a later assignment");
        });

        Case("Failed fixed native publication rolls back weapons and leaves donor initialization uncommitted", () =>
        {
            var w = FixedWeaponWorld("MatchRole"); w.Ready();
            string before = AvatarNativeData.Fingerprint(w.Base(2));
            int messages = AvatarPlugin.PluginLog.Messages.Count;
            ActivateWeaponRole(w, 1); AvatarNativeData.FailCopyRoleOnce = 2;
            Check(!w.Runtime.EnsureProxy("weapon failure", true) && w.Runtime.FaultReason is not null,
                "Injected native failure did not fail safely");
            var initialized = (HashSet<int>)typeof(AvatarRuntime).GetField("_joiningWeaponFighters", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(w.Runtime)!;
            Check(AvatarNativeData.Fingerprint(w.Base(2)) == before && initialized.Count == 0,
                "Failed transaction consumed a fighter's first initialization or changed the weapon");
            Check(!AvatarPlugin.PluginLog.Messages.Skip(messages).Any(line => line.Contains("JOINING WEAPON INITIALIZED")),
                "Rolled back initialization was logged as successful");
        });

        Case("Joining policy sidecars are immutable and reject changed campaign identity", () =>
        {
            var w = new World(); w.Ready(); var save = WeaponSave('a');
            w.Runtime.WriteJoiningWeaponBinding(save); w.Runtime.WriteJoiningWeaponBinding(save);
            bool changed = false;
            try { JoiningWeaponBindingStore.Write(save.Identity, w.Runtime.CampaignFingerprint, w.Runtime.Profile.Seed,
                new JoiningWeaponBinding(JoiningWeaponPolicy.Level1, Array.Empty<int>())); }
            catch (InvalidDataException) { changed = true; }
            Check(changed, "Existing native snapshot policy was overwritten");
            bool wrongCampaign = false;
            try { JoiningWeaponBindingStore.Read(save.Identity, new string('0', 64), w.Runtime.Profile.Seed); }
            catch (InvalidDataException) { wrongCampaign = true; }
            Check(wrongCampaign, "Policy binding crossed campaign boundaries");
        });

        Case("Malformed joining policy bindings stop load without changing native weapons", () =>
        {
            var w = new World(); w.Ready(); w.Base(2).buki_data.level = 8;
            var save = WeaponSave('b'); w.Runtime.WriteCampaignBinding(save); w.Runtime.WriteJoiningWeaponBinding(save);
            string path = Path.Combine(AvatarRuntime.Folder, "joining-weapon-save-bindings", save.Identity + ".json");
            File.WriteAllText(path, File.ReadAllText(path).Replace("\"MatchRole\"", "\"Unknown\""));
            w.Runtime.PrepareLoad(save);
            Check(w.Runtime.FaultReason is not null && !w.Runtime.IsAdmitted && w.Base(2).buki_data.level == 8,
                "Malformed policy loaded or touched native weapons");
        });

        Case("Fixed saved initialization history must include active fighters and only chosen donors", () =>
        {
            var w = FixedWeaponWorld("MatchRole"); ActivateWeaponRole(w, 1); w.Ready();
            var save = WeaponSave('c'); w.Runtime.WriteCampaignBinding(save);
            JoiningWeaponBindingStore.Write(save.Identity, w.Runtime.CampaignFingerprint, "agilen-080",
                new JoiningWeaponBinding(JoiningWeaponPolicy.MatchRole, Array.Empty<int>()));
            w.Runtime.PrepareLoad(save);
            Check(w.Runtime.FaultReason is not null && !w.Runtime.IsAdmitted, "Incomplete first-use history was accepted");
        });

        Case("Internal legacy policy is not selectable and native role levels are bounded to one through sixteen", () =>
        {
            var w = new World(false); AvatarPlugin.ConfiguredJoiningWeaponLevel = "Donor"; w.Runtime.BeginNewGame();
            Check(w.Runtime.FaultReason is not null && AvatarNativeData.CopyCalls == 0, "Internal legacy policy became a new-game option");
            w = new World(); w.Base(2).buki_data.level = 0; w.Base(6).buki_data.level = 255; w.Ready();
            Check(w.Base(2).buki_data.level == 1 && w.Base(6).buki_data.level == 16,
                "Native role weapon levels escaped the legal one through sixteen range");
        });
    }

    private static GSD1.SaveDataManager.SaveData WeaponSave(char id) => new() { Identity = new string(id, 64) };
    private static World FixedWeaponWorld(string policy)
    {
        var w = new World(false); w.StageOpening();
        AvatarPlugin.ConfiguredMode = "FixedChosen"; AvatarPlugin.ConfiguredCompanions = "16,11,12,13,14";
        AvatarPlugin.ConfiguredJoiningWeaponLevel = policy;
        foreach (int id in new[] { 16, 11, 12, 13, 14 }) { w.Base(id).buki_data.level = 13; w.Base(id).buki_data.buki_id = (byte)id; }
        w.Base(2).buki_data.level = 2; w.Base(6).buki_data.level = 4;
        w.Runtime.BeginNewGame(); return w;
    }
    private static void ActivateWeaponRole(World w, int slot)
    {
        w.Game.party_data.narabi[1] = (sbyte)slot; w.Game.party_data.player_kazu = 2;
    }
}
