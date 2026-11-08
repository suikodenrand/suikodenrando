using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using SuikodenRandomizer.AvatarPlugin;

internal static class Program
{
    private static int _passed;
    private static void Main()
    {
        string root = Path.Combine(Path.GetTempPath(), "stars-diagnostics-check-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Check("Critical breadcrumbs survive before normal shutdown", () =>
            {
                using var journal = new CrashJournal(Path.Combine(root, "durable"), "test");
                journal.Append("battle.cleanup.before", "identity=3");
                string text = File.ReadAllText(Path.Combine(journal.SessionFolder, "journal-0.jsonl"));
                Require(text.Contains("battle.cleanup.before") && text.Contains("identity=3"), "flushed breadcrumb missing");
                Require(File.Exists(Path.Combine(journal.SessionFolder, "active.marker")), "active marker missing");
            });
            Check("Concurrent callbacks keep valid ordered journal records", () =>
            {
                using var journal = new CrashJournal(Path.Combine(root, "threads"), "test");
                Parallel.For(0, 100, i => journal.Append("worker", "number=" + i, false));
                journal.Flush();
                var sequences = File.ReadLines(Path.Combine(journal.SessionFolder, "journal-0.jsonl"))
                    .Select(line => { using var d = JsonDocument.Parse(line); return d.RootElement.GetProperty("sequence").GetInt64(); }).ToArray();
                Require(sequences.Length == 101 && sequences.SequenceEqual(Enumerable.Range(1, 101).Select(i => (long)i)), "lost or interleaved writes");
            });
            Check("Oversized noisy sessions retain bounded complete records", () =>
            {
                using var journal = new CrashJournal(Path.Combine(root, "rotation"), "test");
                for (int i = 0; i < 260; i++) journal.Append("noise", new string('x', 40000), false);
                journal.Flush();
                var files = Directory.GetFiles(journal.SessionFolder, "journal-*.jsonl");
                Require(files.Length == 3 && files.All(path => new FileInfo(path).Length <= CrashJournal.SegmentLimit), "unbounded segments");
                foreach (string file in files) foreach (string line in File.ReadLines(file)) { using var document = JsonDocument.Parse(line); }
                Require(File.ReadAllText(files[0]).Contains("[truncated]"), "oversized payload not bounded");
            });
            Check("Normal shutdown prevents false automatic crash reports", () =>
            {
                string folder = Path.Combine(root, "clean");
                using (var journal = new CrashJournal(folder, "test")) journal.Complete();
                Require(CrashJournal.RecoverIncompleteSessions(folder, _ => Array.Empty<CrashSource>()).Count == 0, "normal session reported");
            });
            Check("Abrupt sessions are reported once on the next launch", () =>
            {
                string folder = Path.Combine(root, "recover"), session;
                using (var journal = new CrashJournal(folder, "test")) { session = journal.SessionFolder; journal.Append("last.before.native"); }
                var reports = CrashJournal.RecoverIncompleteSessions(folder, _ => Array.Empty<CrashSource>());
                Require(reports.Count == 1 && File.Exists(Path.Combine(session, "reported.marker")), "unclean report missing");
                using var zip = ZipFile.OpenRead(reports[0]);
                Require(Read(zip.GetEntry("session/journal-0.jsonl")!).Contains("last.before.native"), "prior journal lost");
                Require(Read(zip.GetEntry("report.json")!).Contains("forced close"), "report overclaims a crash");
                Require(CrashJournal.RecoverIncompleteSessions(folder, _ => Array.Empty<CrashSource>()).Count == 0, "duplicate recovery reports");
            });
            Check("Recovery exports the newest incomplete session last", () =>
            {
                string folder = Path.Combine(root, "recovery-order"), latestSession;
                using (var older = new CrashJournal(folder, "older", DateTime.UtcNow.AddMinutes(-10))) older.Append("older.crash");
                using (var latest = new CrashJournal(folder, "newest", DateTime.UtcNow))
                { latestSession = Path.GetFileName(latest.SessionFolder); latest.Append("newest.crash"); }
                var reports = CrashJournal.RecoverIncompleteSessions(folder, _ => Array.Empty<CrashSource>());
                Require(reports.Count == 2, "incomplete session reports missing");
                using var zip = ZipFile.OpenRead(reports[reports.Count - 1]);
                using var manifest = JsonDocument.Parse(Read(zip.GetEntry("report.json")!));
                Require(manifest.RootElement.GetProperty("session").GetString() == latestSession, "last recovery report belongs to an older session");
            });
            Check("Large text logs include only their bounded tail", () =>
            {
                string log = Path.Combine(root, "large.log");
                File.WriteAllText(log, new string('a', 5 * 1024 * 1024) + "END-OF-LOG");
                using var journal = new CrashJournal(Path.Combine(root, "tail"), "test");
                string report = journal.Export("manual", new[] { new CrashSource(log, "logs/large.log") });
                using var zip = ZipFile.OpenRead(report);
                var entry = zip.GetEntry("logs/large.log")!;
                Require(entry.Length == CrashJournal.LogLimit && Read(entry).EndsWith("END-OF-LOG", StringComparison.Ordinal), "wrong log bytes captured");
            });
            Check("Oversized native dumps are omitted intact with an explanation", () =>
            {
                string dump = Path.Combine(root, "too-big.dmp");
                using (var stream = File.Create(dump)) stream.SetLength(CrashJournal.DumpLimit + 1);
                using var journal = new CrashJournal(Path.Combine(root, "dump"), "test");
                string report = journal.Export("manual", new[] { new CrashSource(dump, "unity-crash/too-big.dmp", false) });
                using var zip = ZipFile.OpenRead(report);
                Require(zip.GetEntry("unity-crash/too-big.dmp") is null, "dump was truncated into archive");
                string manifest = Read(zip.GetEntry("report.json")!);
                Require(manifest.Contains("never truncated") && manifest.Contains("too-big.dmp"), "omission metadata missing");
            });
            Check("Report byte budget stops multiple native dumps", () =>
            {
                var sources = new List<CrashSource>();
                for (int i = 0; i < 3; i++)
                {
                    string dump = Path.Combine(root, "budget-" + i + ".dmp");
                    using (var stream = File.Create(dump)) stream.SetLength(50L * 1024 * 1024);
                    sources.Add(new CrashSource(dump, "unity-crash/budget-" + i + ".dmp", false));
                }
                using var journal = new CrashJournal(Path.Combine(root, "budget"), "test");
                using var zip = ZipFile.OpenRead(journal.Export("manual", sources));
                Require(zip.Entries.Count(entry => entry.FullName.EndsWith(".dmp", StringComparison.Ordinal)) == 2, "report byte budget not enforced");
                Require(Read(zip.GetEntry("report.json")!).Contains("report byte limit"), "byte omission not explained");
            });
            Check("Missing or changing external logs cannot prevent an export", () =>
            {
                using var journal = new CrashJournal(Path.Combine(root, "missing"), "test");
                using var zip = ZipFile.OpenRead(journal.Export("manual", new[] { new CrashSource(Path.Combine(root, "absent.log"), "logs/absent.log") }));
                Require(zip.GetEntry("session/journal-0.jsonl") is not null && Read(zip.GetEntry("report.json")!).Contains("unavailable"), "external log failure lost core evidence");
            });
            Check("Archive names cannot escape their ZIP folder", () =>
            {
                string log = Path.Combine(root, "name.log"); File.WriteAllText(log, "safe");
                using var journal = new CrashJournal(Path.Combine(root, "names"), "test");
                using var zip = ZipFile.OpenRead(journal.Export("manual", new[] { new CrashSource(log, "../../outside.log") }));
                Require(zip.Entries.All(entry => !entry.FullName.StartsWith('/') && !entry.FullName.Split('/').Contains("..")), "unsafe archive path");
            });
            Check("Report retention stays bounded across repeated manual exports", () =>
            {
                string folder = Path.Combine(root, "retention");
                using var journal = new CrashJournal(folder, "test");
                for (int i = 0; i < 7; i++) journal.Export("manual-" + i, Array.Empty<CrashSource>());
                Require(Directory.GetFiles(folder, "stars-crash-report-*.zip").Length == 4, "report retention failed");
            });
            if (!OperatingSystem.IsWindows()) Check("Diagnostic collection refuses symbolic links", () =>
            {
                string target = Path.Combine(root, "private.txt"), link = Path.Combine(root, "linked.log");
                File.WriteAllText(target, "private-data"); File.CreateSymbolicLink(link, target);
                using var journal = new CrashJournal(Path.Combine(root, "links"), "test");
                using var zip = ZipFile.OpenRead(journal.Export("manual", new[] { new CrashSource(link, "logs/linked.log") }));
                Require(zip.GetEntry("logs/linked.log") is null && Read(zip.GetEntry("report.json")!).Contains("symbolic links"), "linked source was followed");
            });
            Console.WriteLine(_passed + "/" + _passed + " crash-journal checks passed.");
            Console.WriteLine("Scope: production file journal and ZIP collector. Native game crashes and Unity/IL2CPP callbacks require live Windows testing.");
        }
        finally { Directory.Delete(root, true); }
    }
    private static void Check(string name, Action action) { action(); _passed++; Console.WriteLine("PASS " + name); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static string Read(ZipArchiveEntry entry) { using var reader = new StreamReader(entry.Open()); return reader.ReadToEnd(); }
}
