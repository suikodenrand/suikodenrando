using System;
using System.Collections.Generic;
using System.Linq;

namespace SuikodenRandomizer.Core;

public static class SeedValidator
{
    public static void ValidateAndThrow(SeedManifest manifest)
    {
        IReadOnlyList<string> errors = Validate(manifest);
        if (errors.Count > 0) throw new SeedValidationException(errors);
    }

    // Validation never normalizes or mutates caller-owned configuration.
    public static IReadOnlyList<string> Validate(SeedManifest? manifest)
    {
        var errors = new List<string>();
        if (manifest is null)
        {
            errors.Add("the document was empty");
            return errors;
        }
        if (manifest.SchemaVersion != SeedManifest.CurrentSchemaVersion)
        {
            errors.Add($"schemaVersion {manifest.SchemaVersion} is unsupported; expected {SeedManifest.CurrentSchemaVersion}. Back up the old active-seed.json and generate a version-2 seed; do not reuse an old randomized save with a different mapping");
        }
        if (manifest.AlgorithmVersion != SeedManifest.CurrentAlgorithmVersion)
            errors.Add($"algorithmVersion {manifest.AlgorithmVersion} is unsupported; expected {SeedManifest.CurrentAlgorithmVersion}");
        if (manifest.Game != SeedManifest.SupportedGame)
            errors.Add($"game must be \"{SeedManifest.SupportedGame}\"");
        bool experimental = SeedProfiles.IsExperimental(manifest);
        if (manifest.Profile != SeedProfiles.OptionalRecruitsV1 && !experimental)
            errors.Add("profile must be optional-recruits-v1 or all-playable-experimental-v1; found " + manifest.Profile);
        if (string.IsNullOrWhiteSpace(manifest.Seed))
            errors.Add("seed must not be empty");
        else if (manifest.Seed.Length > 256)
            errors.Add("seed must be at most 256 UTF-16 characters");

        if (manifest.StartingParty is null)
            errors.Add("startingParty must be an object");
        else if (experimental)
        {
            if (!manifest.StartingParty.Enabled)
                errors.Add("the all-playable experiment requires startingParty.enabled=true");
            if (manifest.StartingParty.SourceIds is null
                || manifest.StartingParty.SourceIds.Count != SeedProfiles.ExperimentalStarterIds.Count
                || !new HashSet<int>(manifest.StartingParty.SourceIds).SetEquals(SeedProfiles.ExperimentalStarterIds))
                errors.Add("experimental startingParty.sourceIds must contain exactly 1, 2, 6, 8, 108");
        }
        else
        {
            if (manifest.StartingParty.Enabled)
                errors.Add("starting-party randomization is not supported by this profile; startingParty.enabled must be false");
            if (manifest.StartingParty.SourceIds is null)
                errors.Add("startingParty.sourceIds must be an empty array");
            else if (manifest.StartingParty.SourceIds.Count != 0)
                errors.Add("startingParty.sourceIds must be empty; starter identities are protected in this profile");
        }
        if (manifest.Diagnostics is null) errors.Add("diagnostics must be an object");

        if (manifest.Mapping is null || manifest.Mapping.Count == 0)
        {
            errors.Add("mapping must contain the complete selected-profile permutation");
            return errors;
        }

        var expected = new HashSet<int>(experimental ? SeedProfiles.AllPlayableRecruitIds : SeedProfiles.OptionalRecruitIds);
        int[] missing = expected.Except(manifest.Mapping.Keys).OrderBy(id => id).ToArray();
        int[] extra = manifest.Mapping.Keys.Except(expected).OrderBy(id => id).ToArray();
        if (missing.Length > 0)
            errors.Add("mapping is missing selected-profile source assignments: " + string.Join(", ", missing));
        if (extra.Length > 0)
            errors.Add("source IDs are outside the selected profile: " + string.Join(", ", extra));

        foreach (KeyValuePair<int, int> pair in manifest.Mapping.OrderBy(pair => pair.Key))
        {
            if (!experimental && (pair.Key == CombatCatalog.TirId || pair.Value == CombatCatalog.TirId))
                errors.Add("Tir (ID 8) cannot be a randomized source or reward");
            if (!(experimental ? SeedProfiles.IsAllPlayable(pair.Key) : CombatCatalog.IsNormalCombat(pair.Key)))
                errors.Add($"source ID {pair.Key} is not a playable recruit in the selected profile");
            if (!(experimental ? SeedProfiles.IsAllPlayable(pair.Value) : CombatCatalog.IsNormalCombat(pair.Value)))
                errors.Add($"reward ID {pair.Value} is not a playable recruit in the selected profile");
            if (!expected.Contains(pair.Value))
                errors.Add($"reward ID {pair.Value} is outside the selected profile; protected characters cannot be rewards");
            if (pair.Key == pair.Value)
                errors.Add($"source ID {pair.Key} maps to itself; this profile requires every pool character to change");
        }

        int[] duplicates = manifest.Mapping.GroupBy(pair => pair.Value)
            .Where(group => group.Count() > 1).Select(group => group.Key).OrderBy(id => id).ToArray();
        if (duplicates.Length > 0)
            errors.Add("reward IDs must be one-to-one; duplicates: " + string.Join(", ", duplicates));
        if (!new HashSet<int>(manifest.Mapping.Values).SetEquals(manifest.Mapping.Keys))
            errors.Add("mapping must be closed: its source and reward ID sets must match, so untouched characters cannot be duplicated");
        return errors;
    }
}
