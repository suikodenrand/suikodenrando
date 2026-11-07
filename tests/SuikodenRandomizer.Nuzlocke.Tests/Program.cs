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
            Check(run.RemainingReserves == 75 && run.LivingHeroCount == 75 && !run.IsExhausted, "Initial roster size wrong");
            Check(run.GetDonor(27) == 27 && run.GetDonor(109) == -1, "Unsupported or unfilled lookup wrong");
        });
        Case("Nuzlocke ignores opt-in Tir and keeps the ordinary 75-hero permutation", () =>
        {
            var profile = AvatarShuffleProfile.Create("with-tir", false);
            var run = NuzlockeCampaign.Create("with-tir", true);
            Check(!run.IncludesTir && run.InitialFingerprint == profile.Fingerprint, "Tir setting altered Nuzlocke identity");
            Check(profile.Assignments.All(pair => run.GetDonor(pair.Key) == pair.Value), "75 mapping changed");
            Check(Json(run) == Json(NuzlockeCampaign.Create("with-tir", false)), "Ignored option changed state");
            Check(run.GetDonor(8) == 8 && !run.ReserveOrder.Contains(8), "Tir was shuffled or reserved");
            Check(AvatarShuffleProfile.Create("with-tir", true).IncludesTir, "Ordinary shuffle Tir option changed");
        });
        Case("Active fighters are fielded before a guest role draws a reserve", () =>
        {
            var before = NuzlockeCampaign.Create("opening");
            var after = before.ReconcileActive(Opening);
            Check(after.ActiveRoleIds.SequenceEqual(Opening.OrderBy(id => id)), "Active roles not canonical");
            Check(after.FieldedHeroIds.Count == Opening.Length - 1 && after.GetDonor(108) >= 0, "Guest role did not draw");
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
            Check(after.FieldedHeroIds.Count == 3 && after.RemainingReserves == 72 && run.RetiredHeroIds.Count == 0, "Retirement state wrong");
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
        Case("Tir KO is ignored and keeps the canonical protagonist alive", () =>
        {
            var run = NuzlockeCampaign.Create("tir", true).ReconcileActive(new[] { 8 });
            var after = run.CommitBattle(1, new[] { 8 });
            Check(!after.IsRetired(8) && after.GetDonor(8) == 8, "Tir was retired or replaced");
            Check(after.FieldedHeroIds.Count == 0 && after.RemainingReserves == 75
                && after.LivingHeroCount == 75 && after.RetiredHeroIds.Count == 0, "Tir consumed a companion life");
            Check(after.ActiveRoleIds.SequenceEqual(new[] { 8 }), "Native protagonist role changed");
            Check(after.Capture().LastKnockoutHeroIds.Length == 0, "Tir entered retirement history");
            Check(ReferenceEquals(after, after.CommitBattle(1, Array.Empty<int>())), "Ignored Tir changed duplicate result identity");
            Check(NuzlockeCampaign.Restore(after.Capture()).GetDonor(8) == 8, "Protected Tir did not restore");
        });
        Case("Tir KO alongside companions retires only the companions", () =>
        {
            var run = NuzlockeCampaign.Create("mixed-ko").ReconcileActive(Opening);
            int victim = run.GetDonor(1);
            var after = run.CommitBattle(1, new[] { 8, victim });
            Check(after.RetiredHeroIds.SequenceEqual(new[] { victim }) && after.GetDonor(8) == 8, "Mixed KO retired Tir");
            Check(after.GetDonor(1) >= 0 && after.GetDonor(1) != victim, "Companion did not get a replacement");
            Check(ReferenceEquals(after, after.CommitBattle(1, new[] { victim })), "Tir filtered inconsistently on replay");
        });
        Case("All 75 companion lives can exhaust while canonical Tir remains", () =>
        {
            var run = NuzlockeCampaign.Create("exhaustion").ReconcileActive(new[] { 8, 1 });
            var seen = new HashSet<int>();
            for (int battle = 1; battle <= 75; battle++)
            {
                int fighter = run.GetDonor(1);
                Check(fighter >= 0 && fighter != 8 && seen.Add(fighter), "Fighter reused before exhaustion");
                run = run.CommitBattle(battle, new[] { 8, fighter });
                Check(run.RetiredHeroIds.Count == battle && run.LivingHeroCount == 75 - battle, "Living count wrong");
                Check(run.GetDonor(8) == 8 && !run.IsRetired(8) && !run.FieldedHeroIds.Contains(8), "Tir entered companion pool");
                Check(NuzlockeCampaign.Restore(run.Capture()).GetDonor(1) == run.GetDonor(1), "Exhaustion restore failed");
            }
            Check(run.IsExhausted && run.RemainingReserves == 0 && run.GetDonor(1) == -1
                && run.GetDonor(8) == 8 && run.Assignments.Count == 1, "Exhaustion removed Tir or kept a companion alive");
            var joined = run.ReconcileActive(Opening);
            Check(joined.Assignments.Count == 1 && joined.GetDonor(8) == 8, "Story join resurrected an exhausted companion");
            Check(joined.CommitBattle(76, new[] { 8 }).IsExhausted, "Tir KO changed companion exhaustion");
            Check(NuzlockeCampaign.Restore(run.ReconcileActive(new[] { 1 }).Capture()).GetDonor(8) == 8,
                "An inactive Tir invalidated exhausted companion vacancy");
        });
        Case("Living bench fighters transfer after all fresh companions have been fielded", () =>
        {
            var run = NuzlockeCampaign.Create("bench");
            int[] roles = run.Assignments.Keys.ToArray();
            foreach (int[] group in roles.Chunk(6)) run = run.ReconcileActive(group);
            Check(run.RemainingReserves == 0 && run.LivingHeroCount == 75, "Fielding consumed lives");
            run = run.ReconcileActive(new[] { 8, 1 });
            run = run.CommitBattle(1, new[] { run.GetDonor(1) });
            Check(run.GetDonor(1) >= 0 && run.GetDonor(1) != 8 && !run.IsExhausted
                && run.LivingHeroCount == 74 && run.GetDonor(8) == 8, "Bench transfer moved protected Tir");
            int survivorRole = run.Assignments.Keys.First(role => role != 8);
            Check(run.ReconcileActive(new[] { survivorRole }).GetDonor(survivorRole) >= 0, "Returning live role unavailable");
        });
        Case("The final companion survivor stays outside Tir's protected role", () =>
        {
            var run = NuzlockeCampaign.Create("last-survivor").ReconcileActive(new[] { 8, 1 });
            for (int battle = 1; battle <= 73; battle++) run = run.CommitBattle(battle, new[] { run.GetDonor(1) });
            int companionRole = run.Assignments.Keys.Single(role => role != 8 && role != 1);
            run = run.ReconcileActive(new[] { 8, 1, companionRole });
            int survivor = run.GetDonor(companionRole);
            run = run.CommitBattle(74, new[] { 8, run.GetDonor(1) });
            Check(run.LivingHeroCount == 1 && !run.IsExhausted && run.GetDonor(8) == 8
                && run.GetDonor(companionRole) == survivor && run.GetDonor(1) == -1,
                "Final companion moved into Tir's role or filled an unavailable slot");
            Check(NuzlockeCampaign.Restore(run.Capture()).GetDonor(companionRole) == survivor, "Final companion did not restore");
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
        Case("Protected Tir snapshots reject incompatible old states and Tir corruption", () =>
        {
            var run = NuzlockeCampaign.Create("old-save").ReconcileActive(Opening);
            var dto = run.Capture(); dto.Version = 1; dto.RetiredHeroIds = new[] { 8 };
            try { NuzlockeCampaign.Restore(dto); throw new Exception("Legacy state accepted"); }
            catch (ArgumentException error)
            {
                Check(error.Message.Contains("0.8.8") && error.Message.Contains("Start a new Nuzlocke run"),
                    "Legacy state rejection omitted recovery instructions");
            }
            dto = run.Capture(); dto.Assignments[8] = run.GetDonor(1); Reject<ArgumentException>(() => NuzlockeCampaign.Restore(dto));
            dto = run.Capture(); dto.Assignments[1] = 8; Reject<ArgumentException>(() => NuzlockeCampaign.Restore(dto));
            dto = run.Capture(); dto.Assignments.Remove(8); Reject<ArgumentException>(() => NuzlockeCampaign.Restore(dto));
            dto = run.Capture(); dto.RetiredHeroIds = new[] { 8 }; Reject<ArgumentException>(() => NuzlockeCampaign.Restore(dto));
            dto = run.Capture(); dto.FieldedHeroIds = new[] { 8 }; Reject<ArgumentException>(() => NuzlockeCampaign.Restore(dto));
            dto = run.Capture(); dto.ReserveOrder[0] = 8; Reject<ArgumentException>(() => NuzlockeCampaign.Restore(dto));
            dto = run.Capture(); dto.IncludesTir = true; Reject<ArgumentException>(() => NuzlockeCampaign.Restore(dto));
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
