using System;
using System.Collections.Generic;

namespace SuikodenRandomizer.Core.Scenes;

/// <summary>Copied event headers only. Never evaluates native placement predicates.</summary>
public readonly struct SceneActorRecord
{
    public SceneActorRecord(int index, int image, int flags, int? placementLength)
    { Index = index; Image = image; Flags = flags; PlacementLength = placementLength; }
    public int Index { get; }
    public int Image { get; }
    public int Flags { get; }
    public int? PlacementLength { get; }
}

public static class SceneActorAdmission
{
    /// <summary>
    /// Native eventHumanPutChk returns 1 for null/empty eventPut. InitHumanSprite
    /// associates that record with the previous actor slot without making sprites.
    /// Nonempty conditions are NOT evaluated here: multiple potential emitters
    /// still fail closed, even if their predicates might be mutually exclusive.
    /// </summary>
    public static bool TrySelect(int sourceId, int expectedIndex, IReadOnlyList<SceneActorRecord> records,
        out int potentialEmitters, out int aliasRecords)
    {
        potentialEmitters = 0; aliasRecords = 0;
        int matchedIndex = -1;
        if (sourceId < 0 || expectedIndex < 0 || records is null) return false;
        foreach (var record in records)
        {
            if (record.Image != sourceId || (record.Flags & 0x101) == 0) continue;
            if (record.PlacementLength is null || record.PlacementLength == 0) { aliasRecords++; continue; }
            if (record.PlacementLength.Value < 0) return false;
            potentialEmitters++;
            matchedIndex = record.Index;
        }
        return potentialEmitters == 1 && matchedIndex == expectedIndex;
    }
}
