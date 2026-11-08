using BepInEx;
using BepInEx.Logging;
using Il2CppInterop.Runtime;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

namespace SuikodenRandomizer.AvatarPlugin;

/// <summary>Best-effort evidence capture; no game recovery or native exception interception.</summary>
internal static class CrashDiagnostics
{
    private static readonly object Gate = new();
    private static CrashJournal? _journal;
    private static ManualLogSource? _log;
    private static DiagnosticListener? _listener;
    private static Application.LogCallback? _unityLog;
    private static Il2CppSystem.Action? _quitting;
    private static UnityAction<Scene, LoadSceneMode>? _sceneLoaded;
    private static UnityAction<Scene>? _sceneUnloaded;
    private static UnityAction<Scene, Scene>? _activeSceneChanged;
    private static string _bepLog = "", _playerLog = "", _avatarLog = "";
    private static string[] _crashRoots = Array.Empty<string>();
    private static bool _initialized;
    private static volatile bool _fatal;
    private static DateTime _rateWindow = DateTime.UtcNow;
    private static int _warningRateCount, _errorRateCount;
    [ThreadStatic] private static bool _insideCallback;

    internal static string? RootFolder => _journal?.RootFolder;

    internal static void Initialize(ManualLogSource log)
    {
        lock (Gate)
        {
            if (_initialized) return;
            _initialized = true; _log = log;
            try
            {
                // All Unity getters run here on the plugin's main thread, never in the threaded log callback.
                _bepLog = Path.Combine(Paths.BepInExRootPath, "LogOutput.log");
                _avatarLog = Path.Combine(AvatarRuntime.Folder, "avatar-shuffle.log");
                try { _playerLog = Application.consoleLogPath ?? ""; } catch { }
                var roots = new List<string>();
                try
                {
                    string company = Application.companyName ?? "";
                    string product = Application.productName ?? "";
                    if (ValidPathPart(company) && ValidPathPart(product))
                        roots.Add(Path.Combine(Path.GetTempPath(), company, product, "Crashes"));
                }
                catch { }
                try
                {
                    string cache = Application.temporaryCachePath ?? "";
                    if (Path.IsPathRooted(cache)) roots.Add(Path.Combine(cache, "Crashes"));
                }
                catch { }
                _crashRoots = roots.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                string root = Path.Combine(Paths.BepInExRootPath, "StarsOfDestiny-Diagnostics");
                Directory.CreateDirectory(root);
                IReadOnlyList<string> recovered = Array.Empty<string>();
                try { recovered = CrashJournal.RecoverIncompleteSessions(root, Sources); } catch { }
                string metadata = JsonSerializer.Serialize(new
                {
                    plugin = AvatarPlugin.DisplayName, version = AvatarPlugin.Version,
                    os = Environment.OSVersion.ToString(), framework = Environment.Version.ToString(),
                    process64Bit = Environment.Is64BitProcess,
                    gameBuild = global::SuikodenRandomizer.Plugin.BuildCompatibility.GameAssemblySha256
                });
                _journal = new CrashJournal(root, metadata);
                AppDomain.CurrentDomain.UnhandledException += Unhandled;
                TaskScheduler.UnobservedTaskException += Unobserved;
                _listener = new DiagnosticListener();
                BepInEx.Logging.Logger.Listeners.Add(_listener);
                InstallUnityCallbacks();
                foreach (string report in recovered) log.LogWarning("Previous unclean-session report saved: " + report);
                log.LogInfo("Crash diagnostics: " + root);
            }
            catch (Exception error)
            {
                try { log.LogWarning("Crash diagnostics could not initialize: " + error.Message); } catch { }
            }
        }
    }

    internal static void Breadcrumb(string eventName, string? details = null, bool durable = true)
    {
        try { _journal?.Append(eventName, details, durable); }
        catch { /* Diagnostics must not change game behavior. */ }
    }

    internal static void Exception(string context, Exception exception)
    {
        if (_insideCallback) return;
        try
        {
            _insideCallback = true;
            _journal?.Append("managed.exception", context + Environment.NewLine + exception);
        }
        catch { }
        finally { _insideCallback = false; }
    }

    internal static string? Export(string reason = "manual F6")
    {
        try
        {
            var journal = _journal;
            if (journal is null) return null;
            string path = journal.Export(reason, Sources(journal.StartedUtc));
            try { _log?.LogInfo("Diagnostic report saved: " + path); } catch { }
            return path;
        }
        catch (Exception error)
        {
            Breadcrumb("report.failed", error.ToString());
            return null;
        }
    }

    internal static void Shutdown()
    {
        try
        {
            Breadcrumb("application.quitting", _fatal ? "fatal managed exception recorded" : "normal Unity shutdown signal");
            if (_fatal) _journal?.Dispose(); else _journal?.Complete();
        }
        catch { }
    }

    private static void InstallUnityCallbacks()
    {
        try
        {
            _unityLog = DelegateSupport.ConvertDelegate<Application.LogCallback>(new Action<string, string, LogType>(UnityLog));
            if (_unityLog is not null) Application.add_logMessageReceivedThreaded(_unityLog);
        }
        catch (Exception error) { Exception("Installing Unity log callback", error); }
        try
        {
            _quitting = DelegateSupport.ConvertDelegate<Il2CppSystem.Action>(new Action(Shutdown));
            if (_quitting is not null) Application.quitting += _quitting;
        }
        catch (Exception error) { Exception("Installing Unity shutdown callback", error); }
        try
        {
            _sceneLoaded = DelegateSupport.ConvertDelegate<UnityAction<Scene, LoadSceneMode>>(new Action<Scene, LoadSceneMode>(SceneLoaded));
            _sceneUnloaded = DelegateSupport.ConvertDelegate<UnityAction<Scene>>(new Action<Scene>(SceneUnloaded));
            _activeSceneChanged = DelegateSupport.ConvertDelegate<UnityAction<Scene, Scene>>(new Action<Scene, Scene>(ActiveSceneChanged));
            if (_sceneLoaded is not null) SceneManager.sceneLoaded += _sceneLoaded;
            if (_sceneUnloaded is not null) SceneManager.sceneUnloaded += _sceneUnloaded;
            if (_activeSceneChanged is not null) SceneManager.activeSceneChanged += _activeSceneChanged;
        }
        catch (Exception error) { Exception("Installing Unity scene callbacks", error); }
    }

    private static void SceneLoaded(Scene scene, LoadSceneMode mode) => SceneEvent("scene.loaded", scene, "mode=" + mode);
    private static void SceneUnloaded(Scene scene) => SceneEvent("scene.unloaded", scene, "");
    private static void ActiveSceneChanged(Scene previous, Scene next)
    {
        SceneEvent("scene.active.previous", previous, "");
        SceneEvent("scene.active.next", next, "");
    }
    private static void SceneEvent(string eventName, Scene scene, string extra)
    {
        try { Breadcrumb(eventName, "handle=" + scene.handle + "; index=" + scene.buildIndex + "; name=" + scene.name + "; " + extra); }
        catch (Exception error) { Exception("Recording " + eventName, error); }
    }

    private static void UnityLog(string condition, string stackTrace, LogType type)
    {
        if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert && type != LogType.Warning) return;
        CaptureLog("unity." + type, condition + Environment.NewLine + stackTrace, type == LogType.Warning);
    }

    private static void CaptureLog(string name, string message, bool warning)
    {
        if (_insideCallback) return;
        try
        {
            _insideCallback = true;
            lock (Gate)
            {
                if ((DateTime.UtcNow - _rateWindow).TotalSeconds >= 60)
                { _rateWindow = DateTime.UtcNow; _warningRateCount = _errorRateCount = 0; }
                int count = warning ? ++_warningRateCount : ++_errorRateCount;
                int limit = warning ? 40 : 100;
                if (count > limit)
                {
                    if (count == limit + 1) Breadcrumb("log.rate_limit", "Further " + (warning ? "warning" : "error") + " callbacks are omitted for this minute.");
                    return;
                }
            }
            // No Unity getters or BepInEx log writes: this callback may run on a worker thread.
            Breadcrumb(name, message);
        }
        catch { }
        finally { _insideCallback = false; }
    }

    private static void Unhandled(object sender, UnhandledExceptionEventArgs arguments)
    {
        if (arguments.IsTerminating) _fatal = true;
        if (arguments.ExceptionObject is Exception error) Exception("AppDomain.UnhandledException; terminating=" + arguments.IsTerminating, error);
        else Breadcrumb("managed.unhandled", "terminating=" + arguments.IsTerminating + "; " + arguments.ExceptionObject);
        try { _journal?.Flush(); } catch { }
        // Leave the active marker for recovery next launch; never swallow the exception.
    }
    private static void Unobserved(object? sender, UnobservedTaskExceptionEventArgs arguments) =>
        Exception("TaskScheduler.UnobservedTaskException (not suppressed)", arguments.Exception);

    private static IEnumerable<CrashSource> Sources(DateTime sessionStart)
    {
        var sources = new List<CrashSource>();
        if (!string.IsNullOrEmpty(_bepLog)) sources.Add(new CrashSource(_bepLog, "logs/BepInEx-LogOutput-at-export.log"));
        if (!string.IsNullOrEmpty(_avatarLog)) sources.Add(new CrashSource(_avatarLog, "logs/avatar-shuffle.log"));
        if (!string.IsNullOrEmpty(_playerLog))
        {
            sources.Add(new CrashSource(_playerLog, "logs/Unity-Player.log"));
            string? folder = Path.GetDirectoryName(_playerLog);
            if (folder is not null) sources.Add(new CrashSource(Path.Combine(folder, "Player-prev.log"), "logs/Unity-Player-prev.log"));
        }
        foreach (string root in _crashRoots)
        {
            try
            {
                if (!Directory.Exists(root) || CrashJournal.IsReparsePoint(root)) continue;
                var folders = Directory.EnumerateDirectories(root).Where(path => !CrashJournal.IsReparsePoint(path))
                    .OrderByDescending(path => Directory.GetLastWriteTimeUtc(path)).Take(8).Prepend(root).ToArray();
                foreach (string folder in folders)
                {
                    foreach (string file in Directory.EnumerateFiles(folder).Take(100))
                    {
                        string extension = Path.GetExtension(file).ToLowerInvariant();
                        if (extension != ".log" && extension != ".txt" && extension != ".dmp" && extension != ".mdmp") continue;
                        if (File.GetLastWriteTimeUtc(file) < sessionStart.AddMinutes(-2)) continue;
                        sources.Add(new CrashSource(file, "unity-crash/" + Path.GetFileName(folder) + "/" + Path.GetFileName(file),
                            extension != ".dmp" && extension != ".mdmp"));
                    }
                }
            }
            catch { }
        }
        return sources;
    }

    private static bool ValidPathPart(string value) => value.Length > 0 && value != "." && value != ".."
        && value.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && !value.Contains('/') && !value.Contains('\\');

    private sealed class DiagnosticListener : ILogListener
    {
        public LogLevel LogLevelFilter => LogLevel.Warning | LogLevel.Error | LogLevel.Fatal;
        public void LogEvent(object sender, LogEventArgs eventArgs)
        {
            if ((eventArgs.Level & (LogLevel.Warning | LogLevel.Error | LogLevel.Fatal)) == 0) return;
            try
            {
                object? data = eventArgs.Data;
                // Log arguments can be arbitrary mod objects. Do not invoke an
                // IL2CPP object's ToString from a worker-thread log callback.
                string message = data is string text ? text : data is Exception error ? error.ToString()
                    : data is null ? "(null)" : "(log argument type " + data.GetType().FullName + ")";
                CaptureLog("bepinex." + eventArgs.Level, eventArgs.Source.SourceName + ": " + message,
                    (eventArgs.Level & (LogLevel.Error | LogLevel.Fatal)) == 0);
            }
            catch { }
        }
        public void Dispose() { }
    }
}
