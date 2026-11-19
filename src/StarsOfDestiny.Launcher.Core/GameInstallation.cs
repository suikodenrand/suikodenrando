using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Versioning;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace StarsOfDestiny.Launcher.Core;

public sealed record GameInstallationStatus(string GameExePath, string? HostVersion, bool HostInstalled,
    bool FixInstalled, bool AtbActive, string? AtbDormantPath, string? AtbVersion);

public static class GameInstallation
{
    public const string GameExeName = "Suikoden I and II HD Remaster.exe";
    public static Version RiouJoiningWeaponMinimumVersion { get; } = new(0, 14, 3);
    public static Version JoiningEquipmentMinimumVersion { get; } = new(0, 14, 4);
    public static Version CtbMinimumVersion { get; } = new(0, 11, 0);
    private const string GameFolderName = "Suikoden I and II HD Remaster";
    private const string AtbFileName = "SuikodenRandomizer.S1BattlePreview.dll";
    private const string ReviewedAtbHash = "C3BB58521254035F2599F6C3EDA1EEF2F4D68517A7B7A3C60C561085019A2E9F";

    public static string? DetectGameFolder()
    {
        var candidates = new List<string>();
        var steamRoots = new List<string>();
        // A launcher installed inside a game copy belongs to that copy first.
        var parent = new DirectoryInfo(AppContext.BaseDirectory);
        for (int depth = 0; parent is not null && depth < 5; depth++, parent = parent.Parent)
            candidates.Add(parent.FullName);
        if (OperatingSystem.IsWindows())
        {
            foreach (RegistryView view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                Add(candidates, ReadRegistry(RegistryHive.LocalMachine, view,
                    @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Steam App 1932640", "InstallLocation"));
                Add(steamRoots, ReadRegistry(RegistryHive.LocalMachine, view, @"Software\Valve\Steam", "InstallPath"));
            }
            Add(steamRoots, ReadRegistry(RegistryHive.CurrentUser, RegistryView.Default, @"Software\Valve\Steam", "SteamPath"));
        }
        Add(steamRoots, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"));
        foreach (string steamRoot in steamRoots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            candidates.Add(Path.Combine(steamRoot, "steamapps", "common", GameFolderName));
            string libraries = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(libraries)) continue;
            try
            {
                foreach (Match match in Regex.Matches(File.ReadAllText(libraries), "\"path\"\\s+\"([^\"]+)\""))
                {
                    string libraryRoot = match.Groups[1].Value.Replace("\\\\", "\\", StringComparison.Ordinal);
                    candidates.Add(Path.Combine(libraryRoot, "steamapps", "common", GameFolderName));
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        candidates.Add(Path.Combine("G:" + Path.DirectorySeparatorChar, "SteamLibrary", "steamapps", "common", GameFolderName));
        foreach (string candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
            if (File.Exists(Path.Combine(candidate, GameExeName))) return Path.GetFullPath(candidate);
        return null;
    }

    public static bool IsGameRunning(string gameFolder)
    {
        string expected = Path.GetFullPath(Path.Combine(gameFolder, GameExeName));
        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(GameExeName)))
        {
            using (process)
            {
                try
                {
                    string? actual = process.MainModule?.FileName;
                    if (actual is null || string.Equals(Path.GetFullPath(actual), expected, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                catch (InvalidOperationException) { }
                catch (Win32Exception) { return true; }
                catch (NotSupportedException) { return true; }
            }
        }
        return false;
    }

    public static GameInstallationStatus Inspect(string gameFolder)
    {
        string root = Path.GetFullPath(gameFolder);
        string plugins = Path.Combine(root, "BepInEx", "plugins");
        string? host = PluginFiles(plugins, "SuikodenRandomizer.dll").FirstOrDefault();
        string[] atbCopies = PluginFiles(plugins, AtbFileName).ToArray();
        string dormant = Path.Combine(root, "BepInEx", AtbFileName);
        string? dormantPath = File.Exists(dormant) ? dormant : null;
        return new GameInstallationStatus(Path.Combine(root, GameExeName), ReadVersion(host), host is not null,
            PluginFiles(plugins, "Suikoden_Fix.dll").Any(), atbCopies.Length != 0, dormantPath,
            ReadVersion(atbCopies.FirstOrDefault() ?? dormantPath));
    }

    public static void RequireRiouJoiningWeaponSupport(string gameFolder, string policy)
    {
        if (!string.Equals(policy, "RiouVariance", StringComparison.OrdinalIgnoreCase)) return;
        if (!Version.TryParse(Inspect(gameFolder).HostVersion, out var version) || version < RiouJoiningWeaponMinimumVersion)
            throw new InvalidOperationException("Riou Â±3 joining weapon levels require Stars of Destiny plug-in "
                + RiouJoiningWeaponMinimumVersion + " or later. Install the updated mod, or choose Match original character or Level 1.");
    }

    public static void RequireJoiningEquipmentSupport(string gameFolder, string policy)
    {
        if (!string.Equals(policy, "MatchRole", StringComparison.OrdinalIgnoreCase)) return;
        if (!Version.TryParse(Inspect(gameFolder).HostVersion, out var version) || version < JoiningEquipmentMinimumVersion)
            throw new InvalidOperationException("Original-role starting inventory requires Stars of Destiny plug-in "
                + JoiningEquipmentMinimumVersion + " or later. Install the updated mod, or choose Replacement's starting inventory.");
    }

    public static string ActivateAtb(string gameFolder)
    {
        string root = RequireGameFolder(gameFolder);
        // The DLL and its portrait directory are published together. Two launcher instances
        // must observe the completed state before either decides to replace an empty folder.
        string directory = Path.TrimEndingDirectorySeparator(root).ToUpperInvariant();
        string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(directory)));
        using var gate = new Mutex(false, "StarsOfDestiny.BattlePreview." + key);
        try
        {
            if (!gate.WaitOne(TimeSpan.FromSeconds(15)))
                throw new IOException("Another launcher is activating the battle preview. Try again when it finishes.");
        }
        catch (AbandonedMutexException) { /* This caller owns the mutex and will inspect the installation again. */ }
        try { return ActivateAtbUnderLock(root); }
        finally { gate.ReleaseMutex(); }
    }

    private static string ActivateAtbUnderLock(string root)
    {
        RequireClosed(root);
        string plugins = Path.Combine(root, "BepInEx", "plugins");
        string[] active = PluginFiles(plugins, AtbFileName).ToArray();
        if (active.Length > 1) throw new InvalidOperationException("Multiple ATB plugins are installed. Keep one copy before enabling ATB.");
        if (active.Length == 1)
        {
            RequireReviewedAtb(active[0]);
            PrepareCtbAssets(root, active[0], active[0]);
            return active[0];
        }
        string dormant = Path.Combine(root, "BepInEx", AtbFileName);
        if (!File.Exists(dormant)) throw new FileNotFoundException("The reviewed S1 ATB preview is not available in this installation.", dormant);
        RequireReviewedAtb(dormant);
        Directory.CreateDirectory(plugins);
        string target = Path.Combine(plugins, AtbFileName);
        string temporary = target + "." + Guid.NewGuid().ToString("N") + ".pending";
        try
        {
            File.Copy(dormant, temporary);
            RequireReviewedAtb(temporary);
            PrepareCtbAssets(root, temporary, target);
            RequireClosed(root);
            try { File.Move(temporary, target); }
            catch (IOException) when (File.Exists(target)) { RequireReviewedAtb(target); }
            RequireReviewedAtb(target);
            return target;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static void PrepareCtbAssets(string root, string reviewedDll, string target)
    {
        if (!Version.TryParse(ReadVersion(reviewedDll), out var version) || version < new Version(0, 12, 0)) return;
        string source = Path.Combine(root, "BepInEx", "CTBAssets", "Enemies");
        string destination = Path.Combine(Path.GetDirectoryName(target)!, "CTBAssets", "Enemies");
        CtbEnemyAssets.Install(source, destination, alreadyActive: string.Equals(reviewedDll, target, StringComparison.OrdinalIgnoreCase));
    }

    public static void RequireCtbSupport(string gameFolder)
    {
        var status = Inspect(gameFolder);
        if (!status.AtbActive || !Version.TryParse(status.AtbVersion, out var version) || version < CtbMinimumVersion)
            throw new InvalidOperationException("Activate the S1 battle preview in Suikoden I Battle & ATB before enabling CTB.");
    }

    public static void LaunchGame(string gameFolder)
    {
        string root = RequireGameFolder(gameFolder);
        if (IsGameRunning(root)) throw new InvalidOperationException("Suikoden is already running.");
        Process.Start(new ProcessStartInfo(Path.Combine(root, GameExeName)) { UseShellExecute = true, WorkingDirectory = root });
    }

    private static string RequireGameFolder(string gameFolder)
    {
        string root = Path.GetFullPath(gameFolder);
        if (!File.Exists(Path.Combine(root, GameExeName))) throw new DirectoryNotFoundException("Choose the folder containing " + GameExeName + ".");
        return root;
    }

    private static void RequireClosed(string root)
    {
        if (IsGameRunning(root)) throw new InvalidOperationException("Close Suikoden before changing its plugins or settings.");
    }

    private static void RequireReviewedAtb(string path)
    {
        string expectedHash = ReviewedAtbHashForBuild();
        using var stream = File.OpenRead(path);
        if (!string.Equals(Convert.ToHexString(SHA256.HashData(stream)), expectedHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The ATB preview differs from the reviewed 0.10.1 build. Keep the existing files and select a verified preview.");
    }

    private static string ReviewedAtbHashForBuild()
    {
        var hashes = typeof(GameInstallation).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Where(attribute => attribute.Key == "StarsOfDestiny.ReviewedAtbSha256")
            .Select(attribute => attribute.Value).ToArray();
        if (hashes.Length == 0) return ReviewedAtbHash;
        if (hashes.Length != 1 || hashes[0] is not string hash
            || !Regex.IsMatch(hash, @"\A[0-9a-fA-F]{64}\z"))
            throw new InvalidDataException("This launcher build has an invalid reviewed ATB SHA-256 value.");
        return hash;
    }

    private static IEnumerable<string> PluginFiles(string folder, string name) => Directory.Exists(folder)
        ? Directory.EnumerateFiles(folder, name, SearchOption.AllDirectories).OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
        : Enumerable.Empty<string>();

    private static string? ReadVersion(string? path)
    {
        if (path is null) return null;
        try { return AssemblyName.GetAssemblyName(path).Version?.ToString(); }
        catch (BadImageFormatException) { return null; }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    private static void Add(List<string> paths, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) paths.Add(value);
    }

    [SupportedOSPlatform("windows")]
    private static string? ReadRegistry(RegistryHive hive, RegistryView view, string path, string name)
    {
        try
        {
            using var root = RegistryKey.OpenBaseKey(hive, view);
            using var key = root.OpenSubKey(path);
            return key?.GetValue(name) as string;
        }
        catch (SecurityException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
        catch (IOException) { return null; }
    }
}
