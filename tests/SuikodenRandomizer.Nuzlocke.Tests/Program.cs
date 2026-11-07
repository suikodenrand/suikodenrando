using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using SuikodenRandomizer.Core;
using SuikodenRandomizer.Core.Avatars;

internal static class Program
{
    private static int _passed;
    private static readonly int[] Opening = { 8, 2, 6, 1, 108 };

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static void Case(string name, Action action)
    {
        action();
        _passed++;
        Console.WriteLine("PASS " + name);
    }

    private static void Reject<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new Exception("Expected " + typeof(T).Name);
    }

    private static string Json(NuzlockeCampaign campaign) => JsonSerializer.Serialize(campaign.Capture());

    private static void Main()
    {
        Case("Initial Nuzlocke mapping preserves regular shuffle and fixed Tir", () =>
        {
            var profile = AvatarShuffleProfile.Create("opening");
            var run = NuzlockeCampaign.Create("  opening  ");
            Check(run.Seed == "opening" && !run.IncludesTir && run.GetDonor(8) == 8, "Tir or seed changed");
            Check(run.Assignments.Count == 76 && profile.Assignments.All(pair => run.GetDonor(pair.Key) == pair.Value), "Initial mapping changed");
            Check(run.RemainingReserves == 76 && run.LivingHeroCount == 76 && !run.IsExhausted, "Initial roster size wrong");
            Check(run.GetDonor(27) == 27 && run.GetDonor(109) == -1, "Unsupported or unfilled lookup wrong");
        });
        Case("Opt-in Tir uses the exact 76-hero permutation", () =>
        {
            var profile = AvatarShuffleProfile.Create("with-tir", true);
            var run = NuzlockeCampaign.Create("with-tir", true);
            Check(run.IncludesTir && run.InitialFingerprint == profile.Fingerprint, "Wrong profile identity");
            Check(profile.Assignments.All(pair => run.GetDonor(pair.Key) == pair.Value), "76 mapping changed");
        });
        Case("Active fighters are fielded before a guest role draws a reserve", () =>
        {
            var before = NuzlockeCampaign.Create("opening");
            var after = before.ReconcileActive(Opening);
            Check(after.ActiveRoleIds.SequenceEqual(Opening.OrderBy(id => id)), "Active roles not canonical");
            Check(after.FieldedHeroIds.Count == Opening.Length && after.GetDonor(108) >= 0, "Guest role did not draw");
            Check(after.Assignments.Values.Distinct().Count() == 76, "Drawing guest duplicated an identity");
            Check(Opening.Where(role => role != 108).All(role => before.GetDonor(role) == after.GetDonor(role)), "Active initial fighter was stolen");
            Check(before.FieldedHeroIds.Count == 0 && before.GetDonor(108) == -1, "Previous state mutated");
            Check(ReferenceEquals(after, after.ReconcileActive(Opening.Reverse())), "Reorder changed campaign state");
        });
        Case("KO retires actual donor and replaces the same native role", () =>
        {
            var run = NuzlockeCampaign.Create("death").ReconcileActive(new[] { 8, 1, 2 });
            int victim = run.GetDonor(1);
            var after = run.CommitBattle(1, new[] { victim });
            Check(after.IsRetired(victim) && after.GetDonor(1) != victim && after.GetDonor(1) >= 0, "KO was not replaced");
            Check(!after.Assignments.Values.Contains(victim) && after.Assignments.Count == 75, "Dead fighter remained assigned");
            Check(after.GetDonor(8) == run.GetDonor(8) && after.GetDonor(2) == run.GetDonor(2), "Survivor changed");
            Check(after.FieldedHeroIds.Count == 4 && after.RemainingReserves == 72 && run.RetiredHeroIds.Count == 0, "Retirement state wrong");
        });
        Case("Consumed future-role vacancy draws again when it joins", () =>
        {
            var run = NuzlockeCampaign.Create("future").ReconcileActive(new[] { 8, 1 });
            var after = run.CommitBattle(1, new[] { run.GetDonor(1) });
            int consumedOwner = run.Assignments.Keys.Single(role => role != 1 && !after.Assignments.ContainsKey(role));
            var joined = after.ReconcileActive(new[] { 8, 1, consumedOwner });
            Check(joined.GetDonor(consumedOwner) >= 0 && joined.GetDonor(consumedOwner) != after.GetDonor(1), "Future role duplicated replacement");
            Check(joined.Assignments.Values.Distinct().Count() == joined.Assignments.Count, "Future join duplicated a fighter");
            Check(joined.RetiredHeroIds.All(id => !joined.Assignments.Values.Contains(id)), "Future join resurrected dead fighter");
        });
        Case("Reserve allocation is deterministic across KO callback order", () =>
        {
            var run = NuzlockeCampaign.Create("multi").ReconcileActive(Opening);
            int[] victims = new[] { run.GetDonor(1), run.GetDonor(2), run.GetDonor(6) };
            var first = run.CommitBattle(1, victims);
            var second = run.CommitBattle(1, victims.Reverse());
            Check(Json(first) == Json(second), "KO order changed reserves");
            Check(first.ActiveRoleIds.Select(first.GetDonor).Distinct().Count() == Opening.Length, "Simultaneous KO duplicated replacements");
        });
        Case("Repeated battle result cannot retire its fresh replacements", () =>
        {
            var run = NuzlockeCampaign.Create("duplicate").ReconcileActive(Opening);
            int victim = run.GetDonor(1);
            var after = run.CommitBattle(1, new[] { victim });
            Check(ReferenceEquals(after, after.CommitBattle(1, new[] { victim })), "Duplicate result changed state");
            Reject<InvalidOperationException>(() => after.CommitBattle(1, new[] { after.GetDonor(1) }));
            var later = after.CommitBattle(2, new[] { victim });
            Check(later.RetiredHeroIds.Count == 1 && later.GetDonor(1) == after.GetDonor(1), "Previously retired identity retired replacement");
            Reject<InvalidOperationException>(() => later.CommitBattle(1, new[] { victim }));
        });
        Case("Only fielded active fighters can be newly retired", () =>
        {
            var run = NuzlockeCampaign.Create("validation").ReconcileActive(new[] { 8, 1 });
            Reject<InvalidOperationException>(() => run.CommitBattle(1, new[] { run.GetDonor(2) }));
            var departed = run.ReconcileActive(new[] { 8 });
            Reject<InvalidOperationException>(() => departed.CommitBattle(1, new[] { run.GetDonor(1) }));
            Reject<ArgumentException>(() => run.CommitBattle(1, new[] { 108 }));
            Reject<ArgumentOutOfRangeException>(() => run.CommitBattle(0, Array.Empty<int>()));
        });
        Case("Tir KO can replace the combat identity while role eight remains", () =>
        {
            var run = NuzlockeCampaign.Create("tir").ReconcileActive(new[] { 8 });
            var after = run.CommitBattle(1, new[] { 8 });
            Check(after.IsRetired(8) && after.GetDonor(8) >= 0 && after.GetDonor(8) != 8, "Tir's native slot was lost");
            Check(after.ActiveRoleIds.SequenceEqual(new[] { 8 }), "Native protagonist role changed");
        });
        Case("Retired heroes never return even after all reserves are exhausted", () =>
        {
            var run = NuzlockeCampaign.Create("exhaustion").ReconcileActive(new[] { 8 });
            var seen = new HashSet<int>();
            for (int battle = 1; battle <= 76; battle++)
            {
                int fighter = run.GetDonor(8);
                Check(fighter >= 0 && seen.Add(fighter), "Fighter reused before exhaustion");
                run = run.CommitBattle(battle, new[] { fighter });
                Check(run.RetiredHeroIds.Count == battle && run.LivingHeroCount == 76 - battle, "Living count wrong");
                Check(NuzlockeCampaign.Restore(run.Capture()).GetDonor(8) == run.GetDonor(8), "Exhaustion restore failed");
            }
            Check(run.IsExhausted && run.RemainingReserves == 0 && run.GetDonor(8) == -1 && run.Assignments.Count == 0, "Exhausted run still alive");
            Check(run.ReconcileActive(Opening).Assignments.Count == 0, "Story join resurrected an exhausted run");
        });
        Case("Living bench fighters transfer after all fresh reserves have been fielded", () =>
        {
            var run = NuzlockeCampaign.Create("bench");
            int[] roles = run.Assignments.Keys.ToArray();
            foreach (int[] group in roles.Chunk(6)) run = run.ReconcileActive(group);
            Check(run.RemainingReserves == 0 && run.LivingHeroCount == 76, "Fielding consumed lives");
            run = run.ReconcileActive(new[] { 8 });
            run = run.CommitBattle(1, new[] { run.GetDonor(8) });
            Check(run.GetDonor(8) >= 0 && !run.IsExhausted && run.LivingHeroCount == 75, "Living bench fighter was not transferred");
            int survivorRole = run.Assignments.Keys.First();
            Check(run.ReconcileActive(new[] { survivorRole }).GetDonor(survivorRole) >= 0, "Returning live role unavailable");
        });
        Case("The final active survivor moves into a vacant protagonist role", () =>
        {
            var run = NuzlockeCampaign.Create("last-survivor").ReconcileActive(new[] { 8 });
            for (int battle = 1; battle <= 74; battle++) run = run.CommitBattle(battle, new[] { run.GetDonor(8) });
            int companionRole = run.Assignments.Keys.Single(role => role != 8);
            run = run.ReconcileActive(new[] { 8, companionRole });
            int survivor = run.GetDonor(companionRole);
            run = run.CommitBattle(75, new[] { run.GetDonor(8) });
            Check(run.LivingHeroCount == 1 && !run.IsExhausted && run.GetDonor(8) == survivor
                && run.GetDonor(companionRole) == -1, "Final survivor did not keep native leader occupied");
            Check(NuzlockeCampaign.Restore(run.Capture()).GetDonor(8) == survivor, "Final survivor did not restore");
        });
        Case("Snapshot roundtrip includes deaths, reserve position and retry identity", () =>
        {
            var run = NuzlockeCampaign.Create("save λ", true).ReconcileActive(Opening);
            run = run.CommitBattle(12, Opening.Take(2).Select(run.GetDonor));
            var dto = JsonSerializer.Deserialize<NuzlockeSnapshot>(Json(run))!;
            var restored = NuzlockeCampaign.Restore(dto);
            Check(Json(restored) == Json(run), "Campaign state changed across JSON");
            dto.Assignments.Clear();
            dto.FieldedHeroIds[0] = -1;
            Check(Json(restored) == Json(run), "DTO aliases live campaign");
            Check(ReferenceEquals(restored, restored.CommitBattle(12, run.Capture().LastKnockoutHeroIds)), "Retry identity was not restored");
        });
        Case("Malformed persisted mappings and retirement state are rejected", () =>
        {
            var run = NuzlockeCampaign.Create("bad-save").ReconcileActive(Opening);
            var dto = run.Capture(); dto.ProfileId = "unknown"; Reject<ArgumentException>(() => NuzlockeCampaign.Restore(dto));
            dto = run.Capture(); dto.InitialFingerprint = "wrong"; Reject<ArgumentException>(() => NuzlockeCampaign.Restore(dto));
            dto = run.Capture(); dto.Assignments.Remove(dto.Assignments.Keys.First()); Reject<ArgumentException>(() => NuzlockeCampaign.Restore(dto));
            dto = run.Capture(); dto.RetiredHeroIds = new[] { run.GetDonor(1) }; Reject<ArgumentException>(() => NuzlockeCampaign.Restore(dto));
            dto = run.Capture(); dto.FieldedHeroIds = Array.Empty<int>(); Reject<ArgumentException>(() => NuzlockeCampaign.Restore(dto));
            dto = run.Capture(); dto.ReserveOrder[0] = -1; Reject<ArgumentException>(() => NuzlockeCampaign.Restore(dto));
            dto = run.Capture(); dto.ReservePosition = 76; Reject<ArgumentException>(() => NuzlockeCampaign.Restore(dto));
            dto = run.Capture(); dto.LastBattleId = 1; dto.LastKnockoutHeroIds = new[] { run.GetDonor(1) }; Reject<ArgumentException>(() => NuzlockeCampaign.Restore(dto));
        });
        Case("Invalid active parties are rejected before mutation", () =>
        {
            var run = NuzlockeCampaign.Create("party");
            string before = Json(run);
            foreach (int[] invalid in new[] { new[] { 8, 8 }, new[] { 8, 27 }, new[] { -1 }, new[] { 110 }, new[] { 0, 1, 2, 3, 4, 5, 6 } })
                Reject<ArgumentException>(() => run.ReconcileActive(invalid));
            Check(Json(run) == before, "Invalid active input changed state");
        });
        Console.WriteLine(_passed + "/" + _passed + " Nuzlocke campaign checks passed.");
    }
}
