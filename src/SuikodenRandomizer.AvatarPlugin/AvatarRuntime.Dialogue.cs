extern alias GSD1;

using System;

namespace SuikodenRandomizer.AvatarPlugin;

internal sealed partial class AvatarRuntime
{
    /// <summary>
    /// Capture only identities of an already committed publication. This never
    /// admits, prepares, republishes, restores or mutates a native campaign.
    /// </summary>
    internal Func<bool>? CaptureDialoguePublication()
    {
        if (!ReferenceEquals(Current, this) || !IsActive || _preparing || _fixedLoading
            || AvatarSaveCall.Current is not null || _game == IntPtr.Zero || _manager is null) return null;
        long epoch = ExecutionEpoch;
        var state = State;
        var fixedProfile = _fixedProfile;
        var assignments = _fixedAssignments;
        var originals = _originals;
        var proxies = _proxies;
        var manager = _manager;
        IntPtr managerPointer = manager.Pointer;
        IntPtr gamePointer = _game;

        bool IsCurrent()
        {
            try
            {
                // Check ownership before reading any native singleton. An old
                // closure must not inspect a foreign game's resident pointers.
                return ReferenceEquals(Current, this) && MatchesExecution(epoch) && IsActive
                    && !_preparing && !_fixedLoading && AvatarSaveCall.Current is null
                    && ReferenceEquals(State, state) && ReferenceEquals(_fixedProfile, fixedProfile)
                    && ReferenceEquals(_fixedAssignments, assignments)
                    && ReferenceEquals(_originals, originals) && ReferenceEquals(_proxies, proxies)
                    && ReferenceEquals(_manager, manager) && _game == gamePointer
                    && GSD1.OldSrcBase.game_work?.Pointer == gamePointer
                    && GSD1.GlobalWork.Instance?.player_manager?.Pointer == managerPointer;
            }
            catch (Exception) { return false; }
        }
        return IsCurrent() ? IsCurrent : null;
    }
}
