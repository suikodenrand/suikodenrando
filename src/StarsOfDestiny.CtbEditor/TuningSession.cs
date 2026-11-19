using System.Security.Cryptography;
using System.Text;
using SuikodenRandomizer.Shared;

namespace StarsOfDestiny.CtbEditor;

internal sealed class TuningSession
{
    internal string GameFolder { get; }
    internal string ProfilePath { get; }
    internal CtbTuningProfile Profile { get; }
    private readonly IReadOnlyDictionary<int, int>? _lastParty;
    private string? _version;

    internal TuningSession(string folder)
    {
        GameFolder = Path.GetFullPath(folder);
        var config = Path.Combine(GameFolder, "BepInEx", "config");
        if (!File.Exists(Path.Combine(GameFolder, Program.GameExe)) || !Directory.Exists(config))
            throw new IOException("Choose the Suikoden I and II HD Remaster game folder with BepInEx installed.");
        ProfilePath = Path.Combine(config, CtbTuningProfile.FileName);
        _version = FileVersion();
        Profile = CtbTuningProfile.Load(ProfilePath);
        CheckVersion();
        _lastParty = CtbTuningLastParty.Read(Path.Combine(GameFolder, "BepInEx", "cache", CtbTuningLastParty.FileName));
    }

    internal int? LastBattleSpd(int starId) => _lastParty != null && _lastParty.TryGetValue(starId, out int speed) ? speed : null;

    internal void Save()
    {
        // Serialize check-and-replace across separate editor windows for this installation.
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ProfilePath.ToUpperInvariant())));
        using var gate = new Mutex(false, @"Local\StarsOfDestiny.CtbEditor." + key);
        try
        {
            try
            {
                if (!gate.WaitOne(1000)) throw new IOException("Another editor is saving. Try again.");
            }
            catch (AbandonedMutexException) { } // The previous process exited; we now own the mutex.
            try
            {
                CheckVersion();
                // This is the hash of the exact file we published. Reopening the destination
                // could fail after commit, or observe an unrelated replacement as our own.
                _version = Profile.Save(ProfilePath);
            }
            finally { gate.ReleaseMutex(); }
        }
        catch (UnauthorizedAccessException error) { throw new IOException("Access to the tuning profile was denied.", error); }
    }

    private string? FileVersion()
    {
        try
        {
            using var input = new FileStream(ProfilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return Convert.ToHexString(SHA256.HashData(input));
        }
        catch (FileNotFoundException) { return null; }
    }

    private void CheckVersion()
    {
        if (FileVersion() != _version)
            throw new IOException("The profile was changed by another editor. Reload it before saving.");
    }
}
