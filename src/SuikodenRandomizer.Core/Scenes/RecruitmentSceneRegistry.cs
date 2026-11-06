using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace SuikodenRandomizer.Core.Scenes;

public sealed class SceneMapBinding
{
    public SceneMapBinding(int area, int village) { Area = area; Village = village; }
    public int Area { get; }
    public int Village { get; }
}

public sealed class SceneMembershipSite
{
    public SceneMembershipSite(int offset, int opcode, bool guarded)
    { Offset = offset; Opcode = opcode; GuardedTransition = guarded; }
    public int Offset { get; }
    public int Opcode { get; }
    // A guard proves a local zero->member transition, not lifetime history.
    public bool GuardedTransition { get; }
}

/// <summary>Copied observations only. No live native references or condition execution.</summary>
public sealed class SceneObservation
{
    public int Area { get; set; }
    public int Village { get; set; }
    public int EventTableIndex { get; set; }
    public int EventIndex { get; set; }
    public int PhysicalMapIndex { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Flags { get; set; }
    public int Image { get; set; }
    public IReadOnlyList<byte>? Commands { get; set; }
    public IReadOnlyList<byte>? Conditions { get; set; }
    public IReadOnlyList<byte>? Placement { get; set; }
}

public sealed class RecruitmentSceneDefinition
{
    internal RecruitmentSceneDefinition(string key, int sourceId, string sourceName, string assetName,
        int eventTableIndex, int eventIndex, int physicalMapIndex, int x, int y, int flags, int image,
        SceneMapBinding[] bindings, byte[] commands, byte[] conditions, byte[] placement, SceneMembershipSite[] sites)
    {
        Key = key; SourceId = sourceId; SourceName = sourceName; AssetName = assetName;
        EventTableIndex = eventTableIndex; EventIndex = eventIndex; PhysicalMapIndex = physicalMapIndex;
        X = x; Y = y; Flags = flags; Image = image;
        Bindings = Array.AsReadOnly((SceneMapBinding[])bindings.Clone());
        Commands = Array.AsReadOnly((byte[])commands.Clone());
        Conditions = Array.AsReadOnly((byte[])conditions.Clone());
        Placement = Array.AsReadOnly((byte[])placement.Clone());
        MembershipSites = Array.AsReadOnly((SceneMembershipSite[])sites.Clone());
    }
    public string Key { get; }
    public int SourceId { get; }
    public string SourceName { get; }
    public string AssetName { get; }
    public int EventTableIndex { get; }
    public int EventIndex { get; }
    public int PhysicalMapIndex { get; }
    public int X { get; }
    public int Y { get; }
    public int Flags { get; }
    public int Image { get; }
    public ReadOnlyCollection<SceneMapBinding> Bindings { get; }
    public ReadOnlyCollection<byte> Commands { get; }
    public ReadOnlyCollection<byte> Conditions { get; }
    public ReadOnlyCollection<byte> Placement { get; }
    public ReadOnlyCollection<SceneMembershipSite> MembershipSites { get; }
    public bool IsQuickPilot => SourceId == 91 || SourceId == 92 || SourceId == 95;
    public bool IsNewQuickPilot => SourceId == 92 || SourceId == 95;
    public bool MatchesMap(int area, int village, int eventTableIndex) =>
        EventTableIndex == eventTableIndex && Bindings.Any(b => b.Area == area && b.Village == village);
    public bool Matches(SceneObservation observation) => observation is not null
        && MatchesMap(observation.Area, observation.Village, observation.EventTableIndex)
        && EventIndex == observation.EventIndex && PhysicalMapIndex == observation.PhysicalMapIndex
        && X == observation.X && Y == observation.Y && Flags == observation.Flags && Image == observation.Image
        && SameBytes(Commands, observation.Commands) && SameBytes(Conditions, observation.Conditions)
        && SameBytes(Placement, observation.Placement);
    public static bool SameBytes(IReadOnlyList<byte> expected, IReadOnlyList<byte>? actual)
    {
        if (actual is null || actual.Count != expected.Count) return false;
        for (int i = 0; i < expected.Count; i++) if (expected[i] != actual[i]) return false;
        return true;
    }
}

public static partial class RecruitmentSceneRegistry
{
    public const string NativeBuildSha256 = "e093f175eaf3734d293ca0a236c652dd7fcc72ea5fa10d7b21103edf12f11c3a";
    private static readonly ReadOnlyCollection<RecruitmentSceneDefinition> Data = Array.AsReadOnly(CreateEntries());
    public static IReadOnlyList<RecruitmentSceneDefinition> Entries => Data;
    public static RecruitmentSceneDefinition? Match(SceneObservation observation)
    {
        RecruitmentSceneDefinition? found = null;
        foreach (var entry in Data)
        {
            if (!entry.Matches(observation)) continue;
            if (found is not null) return null; // Ambiguity never authorizes a patch.
            found = entry;
        }
        return found;
    }
    public static RecruitmentSceneDefinition? ByKey(string key) => Data.FirstOrDefault(e => e.Key == key);
}
