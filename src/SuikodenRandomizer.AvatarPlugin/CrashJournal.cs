using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace SuikodenRandomizer.AvatarPlugin;

/// <summary>File-only diagnostics. It never dereferences game objects or handles native exceptions.</summary>
internal sealed class CrashJournal : IDisposable
{
    internal const long SegmentLimit = 2 * 1024 * 1024;
    internal const long ReportLimit = 128 * 1024 * 1024;
    internal const long DumpLimit = 64 * 1024 * 1024;
    internal const long LogLimit = 4 * 1024 * 1024;
    private const int Segments = 3, SessionRetention = 6, ReportRetention = 4;
    private readonly object _gate = new();
    private readonly string _root;
    private FileStream? _stream;
    private StreamWriter? _writer;
    private long _bytes, _sequence;
    private bool _disposed;

    internal string SessionFolder { get; }
    internal string SessionId { get; }
    internal DateTime StartedUtc { get; }
    internal string RootFolder => _root;

    internal CrashJournal(string root, string metadata, DateTime? startedUtc = null)
    {
        _root = Path.GetFullPath(root);
        StartedUtc = startedUtc ?? DateTime.UtcNow;
        SessionId = StartedUtc.ToString("yyyyMMddTHHmmssfffZ", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
        SessionFolder = Path.Combine(_root, "sessions", "session-" + SessionId);
        Directory.CreateDirectory(SessionFolder);
        WriteDurable(Path.Combine(SessionFolder, "session.json"), JsonSerializer.Serialize(new
        {
            SessionId, StartedUtc, Metadata = Limit(metadata, 16384)
        }));
        WriteDurable(Path.Combine(SessionFolder, "active.marker"), SessionId);
        OpenSegment();
        Append("session.start", metadata);
        try { Prune(_root, SessionFolder); } catch { }
    }

    internal void Append(string eventName, string? details = null, bool durable = true)
    {
        lock (_gate)
        {
            if (_disposed) return;
            string line = JsonSerializer.Serialize(new
            {
                utc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                sequence = ++_sequence,
                thread = Environment.CurrentManagedThreadId,
                @event = Limit(eventName, 160),
                details = Limit(details ?? "", 32768)
            });
            long length = Encoding.UTF8.GetByteCount(line) + 1;
            if (_bytes + length > SegmentLimit) Rotate();
            _writer!.WriteLine(line);
            _bytes += length;
            if (durable) Flush();
        }
    }

    internal void Flush()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _writer?.Flush();
            _stream?.Flush(true);
        }
    }

    internal string Export(string reason, IEnumerable<CrashSource> sources)
    {
        lock (_gate)
        {
            Append("report.export", reason);
            Flush();
            return CreateReport(_root, SessionFolder, reason, sources);
        }
    }

    internal void Complete()
    {
        lock (_gate)
        {
            if (_disposed) return;
            Append("session.clean_shutdown");
            Flush();
            File.Delete(Path.Combine(SessionFolder, "active.marker"));
            Dispose();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            try { Flush(); }
            finally
            {
                _disposed = true;
                _writer?.Dispose();
                _stream?.Dispose();
                _writer = null; _stream = null;
            }
        }
    }

    internal static IReadOnlyList<string> RecoverIncompleteSessions(string root, Func<DateTime, IEnumerable<CrashSource>> sources)
    {
        var reports = new List<string>();
        string sessions = Path.Combine(root, "sessions");
        if (!Directory.Exists(sessions)) return reports;
        foreach (string folder in Directory.EnumerateDirectories(sessions, "session-*")
            .Where(path => !IsReparsePoint(path)).OrderByDescending(path => path, StringComparer.Ordinal).Take(SessionRetention).Reverse())
        {
            if (!File.Exists(Path.Combine(folder, "active.marker")) || File.Exists(Path.Combine(folder, "reported.marker"))) continue;
            try
            {
                DateTime start = Directory.GetCreationTimeUtc(folder);
                string metadataPath = Path.Combine(folder, "session.json");
                if (File.Exists(metadataPath) && new FileInfo(metadataPath).Length <= 65536 && !IsReparsePoint(metadataPath))
                {
                    using var metadata = JsonDocument.Parse(File.ReadAllText(metadataPath));
                    if (metadata.RootElement.TryGetProperty("StartedUtc", out var started)) start = started.GetDateTime();
                }
                string report = CreateReport(root, folder, "Previous session ended without a clean shutdown signal. This can indicate a crash, forced close, or power loss.", sources(start));
                WriteDurable(Path.Combine(folder, "reported.marker"), Path.GetFileName(report));
                reports.Add(report);
            }
            catch { /* A diagnostic failure must not prevent the next game launch. */ }
        }
        return reports;
    }

    private void OpenSegment()
    {
        _stream = new FileStream(Path.Combine(SessionFolder, "journal-0.jsonl"), FileMode.Create, FileAccess.Write,
            FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.SequentialScan);
        _writer = new StreamWriter(_stream, new UTF8Encoding(false), 4096, true) { NewLine = "\n" };
        _bytes = 0;
    }

    private void Rotate()
    {
        Flush();
        _writer?.Dispose(); _stream?.Dispose();
        File.Delete(Path.Combine(SessionFolder, "journal-" + (Segments - 1) + ".jsonl"));
        for (int index = Segments - 2; index >= 0; index--)
        {
            string source = Path.Combine(SessionFolder, "journal-" + index + ".jsonl");
            if (File.Exists(source)) File.Move(source, Path.Combine(SessionFolder, "journal-" + (index + 1) + ".jsonl"));
        }
        OpenSegment();
    }

    private static string CreateReport(string root, string session, string reason, IEnumerable<CrashSource> sources)
    {
        Directory.CreateDirectory(root);
        string sessionName = Path.GetFileName(session);
        string id = sessionName.StartsWith("session-", StringComparison.Ordinal) ? sessionName.Substring(8) : "unknown";
        string report = Path.Combine(root, "stars-crash-report-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ", CultureInfo.InvariantCulture)
            + "-" + SafeName(id) + "-" + Guid.NewGuid().ToString("N").Substring(0, 6) + ".zip");
        string temporary = report + ".tmp";
        var entries = new List<object>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        int count = 0;
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
            using (var zip = new ZipArchive(output, ZipArchiveMode.Create))
            {
                var ownFiles = Directory.EnumerateFiles(session).OrderBy(path => path, StringComparer.Ordinal)
                    .Select(path => new CrashSource(path, "session/" + Path.GetFileName(path), false));
                foreach (var source in ownFiles.Concat(sources).Take(200))
                {
                    string name = string.Join("/", source.ArchivePath.Replace('\\', '/').Split('/').Where(part => part.Length > 0).Select(SafeName));
                    string status = "included";
                    long length = 0, copied = 0;
                    try
                    {
                        if (!names.Add(name) || count >= 100) status = "duplicate name or file-count limit";
                        else if (IsReparsePoint(source.Path)) status = "symbolic links are excluded";
                        else
                        {
                            bool dump = source.Path.EndsWith(".dmp", StringComparison.OrdinalIgnoreCase)
                                || source.Path.EndsWith(".mdmp", StringComparison.OrdinalIgnoreCase);
                            // A native dump must be closed by its writer before inclusion;
                            // live text logs remain readable while the game is writing them.
                            using var input = new FileStream(source.Path, FileMode.Open, FileAccess.Read,
                                dump ? FileShare.Read : FileShare.ReadWrite | FileShare.Delete);
                            length = input.Length;
                            long allowed = dump ? DumpLimit : LogLimit;
                            if (dump && length > allowed) status = "dump exceeds per-file limit; binary dumps are never truncated";
                            else if (!source.Tail && length > allowed) status = "file exceeds per-file limit";
                            else
                            {
                                copied = Math.Min(length, allowed);
                                if (total + copied > ReportLimit) { status = "report byte limit"; copied = 0; }
                                else
                                {
                                    if (source.Tail && length > copied) input.Seek(length - copied, SeekOrigin.Begin);
                                    using var destination = zip.CreateEntry(name, CompressionLevel.Fastest).Open();
                                    CopyBounded(input, destination, copied);
                                    total += copied; count++;
                                    if (length > copied) status = "included tail";
                                }
                            }
                        }
                    }
                    catch (Exception error) { status = "unavailable: " + error.GetType().Name; copied = 0; }
                    entries.Add(new { archive = name, source = source.Path, bytes = length, copiedBytes = copied, status });
                }
                using var manifest = new StreamWriter(zip.CreateEntry("report.json", CompressionLevel.Fastest).Open(), new UTF8Encoding(false));
                manifest.Write(JsonSerializer.Serialize(new
                {
                    reason, createdUtc = DateTime.UtcNow, session = sessionName,
                    note = "The last journal event is a breadcrumb, not proof of the crash cause. Managed handlers cannot catch every native crash. No dump is synthesized by this reporter.",
                    includedBytes = total, files = entries
                }, new JsonSerializerOptions { WriteIndented = true }));
            }
            File.Move(temporary, report);
            try { Prune(root, session); } catch { }
            return report;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static void CopyBounded(Stream source, Stream destination, long length)
    {
        byte[] buffer = new byte[65536];
        while (length > 0)
        {
            int read = source.Read(buffer, 0, (int)Math.Min(buffer.Length, length));
            if (read == 0) break;
            destination.Write(buffer, 0, read); length -= read;
        }
    }

    private static void Prune(string root, string keepSession)
    {
        foreach (string file in Directory.EnumerateFiles(root, "stars-crash-report-*.zip")
            .OrderByDescending(path => path, StringComparer.Ordinal).Skip(ReportRetention))
            try { if (!IsReparsePoint(file)) File.Delete(file); } catch { }
        string sessions = Path.Combine(root, "sessions");
        if (!Directory.Exists(sessions)) return;
        foreach (string folder in Directory.EnumerateDirectories(sessions, "session-*")
            .OrderByDescending(path => path, StringComparer.Ordinal).Skip(SessionRetention))
        {
            if (folder == keepSession || IsReparsePoint(folder)) continue;
            try
            {
                // Session folders contain only files; never recursively follow an unexpected directory.
                if (Directory.EnumerateDirectories(folder).Any()) continue;
                foreach (string file in Directory.EnumerateFiles(folder))
                    if (!IsReparsePoint(file)) File.Delete(file);
                Directory.Delete(folder);
            }
            catch { }
        }
    }

    internal static bool IsReparsePoint(string path) => (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
    private static string SafeName(string value)
    {
        string safe = new string(value.Take(120).Select(character => char.IsLetterOrDigit(character)
            || character == '.' || character == '-' || character == '_' ? character : '_').ToArray());
        return safe.Length == 0 || safe == "." || safe == ".." ? "unnamed" : safe;
    }
    private static string Limit(string value, int maximum) => value.Length <= maximum ? value : value.Substring(0, maximum) + " [truncated]";
    private static void WriteDurable(string path, string value)
    {
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        byte[] bytes = new UTF8Encoding(false).GetBytes(value);
        stream.Write(bytes, 0, bytes.Length); stream.Flush(true);
    }
}

internal sealed class CrashSource
{
    internal string Path { get; }
    internal string ArchivePath { get; }
    internal bool Tail { get; }
    internal CrashSource(string path, string archivePath, bool tail = true)
    { Path = path; ArchivePath = archivePath; Tail = tail; }
}
