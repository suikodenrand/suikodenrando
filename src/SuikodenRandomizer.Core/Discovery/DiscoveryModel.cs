using System;
using System.Collections.Generic;

namespace SuikodenRandomizer.Core.Discovery;

/// <summary>Ordinary managed copies only; never retain game/native wrappers.</summary>
public sealed class DiscoveryMapSnapshot
{
    public int SchemaVersion { get; set; } = 1;
    public string GameBuildSha256 { get; set; } = string.Empty;
    public string PluginVersion { get; set; } = string.Empty;
    public string CapturedAtUtc { get; set; } = string.Empty;
    public int Village { get; set; }
    public int Map { get; set; }
    public int Area { get; set; }
    public bool HdMode { get; set; }
    public int Language { get; set; }
    public string Chapter { get; set; } = string.Empty;
    public string EventsState { get; set; } = DiscoveryArrayState.Unavailable;
    public int? DeclaredEventCount { get; set; }
    public List<DiscoveryEventRecord> Events { get; set; } = new List<DiscoveryEventRecord>();
    public DiscoveryIntArray PartyMemberIds { get; set; } = new DiscoveryIntArray();
    public string SourceTableState { get; set; } = DiscoveryArrayState.Unavailable;
    public string SourceEventsState { get; set; } = DiscoveryArrayState.Unavailable;
    public int? DeclaredSourceEventCount { get; set; }
    public List<DiscoveryEventRecord> SourceEvents { get; set; } = new List<DiscoveryEventRecord>();
    public Dictionary<string, int> MapMetadata { get; set; } = new Dictionary<string, int>(StringComparer.Ordinal);
    public DiscoveryIntArray MapMessageIds { get; set; } = new DiscoveryIntArray();
    public string NpcImagesState { get; set; } = DiscoveryArrayState.Unavailable;
    public int? DeclaredNpcCount { get; set; }
    public List<DiscoveryNpcRecord> NpcImages { get; set; } = new List<DiscoveryNpcRecord>();
    public bool IsComplete { get; set; }
    public List<string> Issues { get; set; } = new List<string>();
}

public static class DiscoveryArrayState
{
    public const string Complete = "complete";
    public const string Null = "null";
    public const string Unavailable = "unavailable";
    public const string Truncated = "truncated";
}

/// <summary>JSON encoders normally represent Values as base64; no bytes are omitted.</summary>
public sealed class DiscoveryByteArray
{
    public string State { get; set; } = DiscoveryArrayState.Unavailable;
    public int? DeclaredLength { get; set; }
    public byte[]? Values { get; set; }
    public string? Issue { get; set; }
}

public sealed class DiscoveryIntArray
{
    public string State { get; set; } = DiscoveryArrayState.Unavailable;
    public int? DeclaredLength { get; set; }
    public int[]? Values { get; set; }
    public string? Issue { get; set; }
}

public sealed class DiscoveryEventRecord
{
    public int Index { get; set; }
    public bool IsNull { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Flag1 { get; set; }
    public int Image { get; set; }
    public int PutNo { get; set; }
    public DiscoveryByteArray EventPut { get; set; } = new DiscoveryByteArray();
    public DiscoveryByteArray EventForm { get; set; } = new DiscoveryByteArray();
    public DiscoveryByteArray EventCom { get; set; } = new DiscoveryByteArray();
    public List<string> Issues { get; set; } = new List<string>();
}

public sealed class DiscoveryNpcRecord
{
    public int Index { get; set; }
    public bool IsNull { get; set; }
    public int? FpNo { get; set; }
    public int? OpdIndex { get; set; }
    public string OpdState { get; set; } = DiscoveryArrayState.Unavailable;
    public string? OpdId { get; set; }
    public string? OpdPath { get; set; }
    public int? OpdAnimationCount { get; set; }
    public string? Issue { get; set; }
}

public sealed class DiscoveryMembershipCandidate
{
    public string Table { get; set; } = "vil_ekp";
    public int EventIndex { get; set; }
    public int Offset { get; set; }
    public int Opcode { get; set; }
    public string Operation { get; set; } = string.Empty;
    public string OperandMode { get; set; } = string.Empty;
    public int? SourceId { get; set; }
    public int RawOperand { get; set; }
    public bool IsGrantCandidate { get; set; }
    public string Meaning { get; set; } = string.Empty;
}

public sealed class DiscoveryEventAnalysis
{
    public string Table { get; set; } = "vil_ekp";
    public int EventIndex { get; set; }
    public string Status { get; set; } = string.Empty;
    public int DecodedInstructionCount { get; set; }
    public int DecodedByteCount { get; set; }
    public int? StopOffset { get; set; }
    public int? StopOpcode { get; set; }
    public int UnexaminedByteCount { get; set; }
    public string Explanation { get; set; } = string.Empty;
    public List<DiscoveryMembershipCandidate> MembershipOperations { get; set; } = new List<DiscoveryMembershipCandidate>();
}

public sealed class DiscoveryMapReport
{
    public string DecoderVersion { get; set; } = "linear-prefix-v1";
    public string ContentHash { get; set; } = string.Empty;
    public string Coverage { get; set; } = "Observed loaded map only; not a whole-game inventory.";
    public bool CaptureComplete { get; set; }
    public int CapturedEventCount { get; set; }
    public int? DeclaredEventCount { get; set; }
    public int CandidateEventCount { get; set; }
    public int GrantCandidateCount { get; set; }
    public int LifecycleOperationCount { get; set; }
    public int UnresolvedEventCount { get; set; }
    public List<DiscoveryEventAnalysis> Events { get; set; } = new List<DiscoveryEventAnalysis>();
    public int CapturedSourceEventCount { get; set; }
    public int? DeclaredSourceEventCount { get; set; }
    public int SourceCandidateEventCount { get; set; }
    public int SourceGrantCandidateCount { get; set; }
    public int SourceLifecycleOperationCount { get; set; }
    public int SourceUnresolvedEventCount { get; set; }
    public List<DiscoveryEventAnalysis> SourceEvents { get; set; } = new List<DiscoveryEventAnalysis>();
}
