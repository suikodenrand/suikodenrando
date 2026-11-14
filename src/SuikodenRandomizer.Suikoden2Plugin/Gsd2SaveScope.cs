using System;

namespace SuikodenRandomizer.Suikoden2Plugin;

internal sealed class Gsd2SaveScope
{
    [ThreadStatic] internal static Gsd2SaveScope? Current;
    internal Gsd2SaveScope? Previous;
    internal Gsd2Runtime? Runtime;
    internal long OwnerEpoch = Gsd2Runtime.OwnershipEpoch();
    internal bool IsCurrent => Gsd2Runtime.ExecutionAllowed() && OwnerEpoch == Gsd2Runtime.OwnershipEpoch()
        && (Runtime is null || (ReferenceEquals(Runtime, Gsd2Runtime.Current) && Runtime.MatchesExecution(OwnerEpoch)));
    internal bool Ready;
    internal bool StorageRejected;
    internal bool BindCampaign;
    internal bool CaptureAllowed;
    internal bool InitializeCampPosition;
    internal IntPtr CampSnapshot;
    internal bool CampPositionPrepared;
    internal string? CampPositionFailure;
    internal string Failure = "Suikoden II save has no matching campaign binding.";
}
