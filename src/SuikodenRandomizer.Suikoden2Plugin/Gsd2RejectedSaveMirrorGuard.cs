using SuikodenRandomizer.Integration;

namespace SuikodenRandomizer.Suikoden2Plugin;

internal static class Gsd2RejectedSaveMirrorGuard
{
    internal static string Status => RejectedSaveMirrorGuard.Status;
    internal static void Ensure() => RejectedSaveMirrorGuard.Ensure(AllowMirrorWrite);

    // This decision is independent of reflection, file access and Harmony.
    internal static bool AllowMirrorWrite()
    {
        var scope = Gsd2SaveScope.Current;
        if (scope is null || !scope.BindCampaign || !Gsd2Runtime.MayOwnSaveContext()) return true;
        if (scope.StorageRejected) return false;
        return !scope.IsCurrent || !scope.InitializeCampPosition || scope.Ready;
    }
}
