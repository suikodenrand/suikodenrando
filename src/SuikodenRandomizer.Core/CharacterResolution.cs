using System;

namespace SuikodenRandomizer.Core;

public enum CharacterIdDomain
{
    RecruitmentSource,
    Gameplay
}

public static class CharacterResolution
{
    /// <summary>
    /// Resolve only IDs known to come from a canonical recruitment command.
    /// Roster selections and save reconstruction already contain gameplay IDs;
    /// a second lookup would remap permutation cycles to the wrong character.
    /// </summary>
    public static int Resolve(SeedManifest manifest, int requestedId, CharacterIdDomain domain)
    {
        if (manifest is null) throw new ArgumentNullException(nameof(manifest));
        if (domain == CharacterIdDomain.Gameplay) return requestedId;
        if (domain != CharacterIdDomain.RecruitmentSource)
            throw new ArgumentOutOfRangeException(nameof(domain));
        if (manifest.Mapping is null) throw new ArgumentException("Mapping is required.", nameof(manifest));
        return manifest.Mapping.TryGetValue(requestedId, out int rewardId) ? rewardId : requestedId;
    }
}
