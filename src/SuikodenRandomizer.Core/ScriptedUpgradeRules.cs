using System;

namespace SuikodenRandomizer.Core;

/// <summary>Only these reviewed EVENTSPL modes install character-specific plot equipment.</summary>
public static class ScriptedUpgradeRules
{
    public static int? RequiredCanonicalActor(byte mode) => mode == 14 ? CombatCatalog.TirId
        : mode == 16 ? 35 : (int?)null;

    public static bool RequiresUnsupportedReplacement(byte mode, Func<int, int> resolveSource)
    {
        if (resolveSource is null) throw new ArgumentNullException(nameof(resolveSource));
        int? source = RequiredCanonicalActor(mode);
        return source.HasValue && resolveSource(source.Value) != source.Value;
    }
}
