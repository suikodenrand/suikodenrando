using System;

namespace SuikodenRandomizer.AvatarPlugin;

internal sealed class AvatarSaveCall
{
    [ThreadStatic] internal static AvatarSaveCall? Current;
    internal AvatarSaveCall? Previous;
    internal AvatarRuntime Runtime = null!;
    internal long ExecutionEpoch;
    internal bool NativePassthrough;
    internal bool BindingFailed = true;
    internal bool NativeStorageFailed { get; set; }
    internal string Failure = "No verified avatar binding was recorded for the snapshot.";
}
