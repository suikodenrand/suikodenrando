using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace SuikodenRandomizer.Core.Discovery;

/// <summary>
/// Conservative decoder of a known linear prefix. It never searches through
/// operands, unknown instructions, choices or transfers for apparent opcodes.
/// It does not evaluate eventForm, EventCustomCheck, or runtime reachability.
/// </summary>
public static class DiscoveryReportBuilder
{
    public const string SupportedGameBuildSha256 = "e093f175eaf3734d293ca0a236c652dd7fcc72ea5fa10d7b21103edf12f11c3a";

    public static DiscoveryMapReport Build(DiscoveryMapSnapshot snapshot)
    {
        if (snapshot is null) throw new ArgumentNullException(nameof(snapshot));
        var report = new DiscoveryMapReport
        {
            ContentHash = DiscoveryFingerprint.Compute(snapshot),
            CaptureComplete = snapshot.IsComplete && CaptureLooksComplete(snapshot),
            CapturedEventCount = snapshot.Events.Count,
            DeclaredEventCount = snapshot.DeclaredEventCount,
            CapturedSourceEventCount = snapshot.SourceEvents.Count,
            DeclaredSourceEventCount = snapshot.DeclaredSourceEventCount
        };
        bool supported = snapshot.SchemaVersion == 1
            && string.Equals(snapshot.GameBuildSha256, SupportedGameBuildSha256, StringComparison.OrdinalIgnoreCase);
        foreach (DiscoveryEventRecord record in snapshot.Events)
            report.Events.Add(Analyze(record, snapshot.PartyMemberIds, "vil_ekp", supported));
        foreach (DiscoveryEventRecord record in snapshot.SourceEvents)
            report.SourceEvents.Add(Analyze(record, snapshot.PartyMemberIds, "EVENT_VL.eventDat", supported));
        report.CandidateEventCount = report.Events.Count(item => item.MembershipOperations.Any(operation => operation.IsGrantCandidate));
        report.GrantCandidateCount = report.Events.Sum(item => item.MembershipOperations.Count(operation => operation.IsGrantCandidate));
        report.LifecycleOperationCount = report.Events.Sum(item => item.MembershipOperations.Count(operation => !operation.IsGrantCandidate));
        report.UnresolvedEventCount = report.Events.Count(IsUnresolved);
        report.SourceCandidateEventCount = report.SourceEvents.Count(item => item.MembershipOperations.Any(operation => operation.IsGrantCandidate));
        report.SourceGrantCandidateCount = report.SourceEvents.Sum(item => item.MembershipOperations.Count(operation => operation.IsGrantCandidate));
        report.SourceLifecycleOperationCount = report.SourceEvents.Sum(item => item.MembershipOperations.Count(operation => !operation.IsGrantCandidate));
        report.SourceUnresolvedEventCount = report.SourceEvents.Count(IsUnresolved);
        return report;
    }

    public static string Format(DiscoveryMapSnapshot snapshot, DiscoveryMapReport report)
    {
        if (snapshot is null) throw new ArgumentNullException(nameof(snapshot));
        if (report is null) throw new ArgumentNullException(nameof(report));
        var text = new StringBuilder();
        text.AppendLine("SUIKODEN I — AUTOMATIC EVENT DISCOVERY");
        text.AppendLine("Read-only observations. No event was executed by discovery.");
        text.AppendLine("Map: village=" + Number(snapshot.Village) + ", map=" + Number(snapshot.Map)
            + ", area=" + Number(snapshot.Area) + "; HD=" + snapshot.HdMode
            + "; language=" + Number(snapshot.Language));
        text.AppendLine("Content SHA256: " + report.ContentHash);
        text.AppendLine("Captured at UTC: " + Clean(snapshot.CapturedAtUtc));
        text.AppendLine("Capture complete: " + report.CaptureComplete);
        text.AppendLine("Coverage: " + report.Coverage);
        text.AppendLine("Decoder: " + report.DecoderVersion);
        text.AppendLine();
        text.AppendLine("This is a candidate catalogue, not a list of proven recruitments or safe dialogue skips.");
        text.AppendLine("Only known linear command prefixes are decoded. Conditions, native custom rewrites,");
        text.AppendLine("branches and external/native event paths are not evaluated. Unknown instructions stop");
        text.AppendLine("analysis at their exact offset. No candidate found does not prove no recruitment exists.");
        text.AppendLine("Complete capture and complete interpretation are different: original bytes remain in JSON.");
        text.AppendLine("A membership-set candidate can be an initial acquisition or a return; it was not executed.");
        text.AppendLine("The primary and EVENT_VL source tables are reported separately; their relationship is unproven.");
        text.AppendLine();
        AppendTable(text, "PRIMARY TABLE: vil_ekp", snapshot.EventsState,
            report.CapturedEventCount, report.DeclaredEventCount, report.CandidateEventCount,
            report.GrantCandidateCount, report.LifecycleOperationCount, report.UnresolvedEventCount, report.Events);
        text.AppendLine();
        AppendTable(text, "SOURCE TABLE: EVENT_VL.eventDat", snapshot.SourceEventsState,
            report.CapturedSourceEventCount, report.DeclaredSourceEventCount, report.SourceCandidateEventCount,
            report.SourceGrantCandidateCount, report.SourceLifecycleOperationCount, report.SourceUnresolvedEventCount, report.SourceEvents);
        if (snapshot.Issues.Count > 0)
        {
            text.AppendLine(); text.AppendLine("CAPTURE ISSUES");
            foreach (string issue in snapshot.Issues) text.AppendLine("- " + Clean(issue));
        }
        return text.ToString();
    }

    private static void AppendTable(StringBuilder text, string heading, string state,
        int captured, int? declared, int candidateEvents, int grants, int lifecycle, int unresolved,
        List<DiscoveryEventAnalysis> events)
    {
        text.AppendLine(heading);
        text.AppendLine("State: " + Clean(state) + "; records=" + Number(captured) + "/" + Number(declared)
            + "; candidate events=" + Number(candidateEvents) + "; membership-set sites=" + Number(grants)
            + "; lifecycle sites=" + Number(lifecycle) + "; unresolved events=" + Number(unresolved));
        foreach (DiscoveryEventAnalysis item in events)
        {
            text.AppendLine("Event " + Number(item.EventIndex) + ": " + item.Status
                + "; decoded instructions=" + Number(item.DecodedInstructionCount)
                + "; decoded bytes=" + Number(item.DecodedByteCount)
                + "; unexamined captured bytes=" + Number(item.UnexaminedByteCount));
            text.AppendLine("  " + Clean(item.Explanation));
            foreach (DiscoveryMembershipCandidate operation in item.MembershipOperations)
                text.AppendLine("  offset " + Number(operation.Offset) + " / opcode 0x" + operation.Opcode.ToString("X2", CultureInfo.InvariantCulture)
                    + ": " + operation.Operation + "; source=" + Number(operation.SourceId)
                    + "; operand=" + operation.OperandMode + "; " + operation.Meaning);
        }
    }

    private static DiscoveryEventAnalysis Analyze(DiscoveryEventRecord record,
        DiscoveryIntArray memberIds, string table, bool supported)
    {
        var result = new DiscoveryEventAnalysis { EventIndex = record.Index, Table = table };
        byte[]? bytes = record.EventCom.Values;
        int length = bytes?.Length ?? 0;
        if (record.IsNull) return Stop(result, "null-entry", "The captured event slot is null.", 0, null, length);
        if (!supported) return Stop(result, "unsupported-build-or-schema", "The decoder is only verified for schema 1 and the pinned native build.", 0, null, length);
        if (record.EventCom.State == DiscoveryArrayState.Null)
            return Stop(result, "null-script", "The command array was null; no command bytes were available for interpretation.", 0, null, length);
        if (record.EventCom.State == DiscoveryArrayState.Unavailable || bytes is null)
            return Stop(result, "unavailable-script", "The command array could not be captured; do not infer that the event has no recruitment.", 0, null, length);
        bool complete = record.EventCom.State == DiscoveryArrayState.Complete;
        bool truncated = record.EventCom.State == DiscoveryArrayState.Truncated;
        if ((!complete && !truncated) || record.EventCom.DeclaredLength is null
            || record.EventCom.DeclaredLength < length || (complete && record.EventCom.DeclaredLength != length))
            return Stop(result, "invalid-array-metadata", "Array state/declared length does not match the retained bytes; interpretation is withheld.", 0, null, length);
        if (length == 0)
            return Stop(result, complete ? "empty-script" : "truncated-capture", complete
                ? "The complete command array is empty. Native event paths are not evaluated."
                : "No command bytes were retained from this incomplete capture.", 0, null, length);

        int offset = 0;
        while (offset < length)
        {
            byte opcode = bytes[offset];
            if (opcode == 0xFD)
            {
                result.DecodedInstructionCount++;
                result.DecodedByteCount = offset + 1;
                return Stop(result, complete ? "native-termination" : "native-termination-in-incomplete-capture",
                    "Known native termination reached. Any trailing bytes are preserved but not scanned; conditions and custom rewrites remain unverified.",
                    offset, opcode, length);
            }
            if (opcode == 0x05 || opcode == 0x15 || opcode == 0x2F)
                return Stop(result, "unresolved-choice", "Choice or choice-result flow requires branch decoding; the remaining bytes are not searched.", offset, opcode, length);
            if (opcode >= 0xF0)
                return Stop(result, "unresolved-native-control", "Special native interpreter control requires separate decoding; the remaining bytes are not searched.", offset, opcode, length);

            int operands;
            switch (opcode)
            {
                // Exact widths from the native handlers documented in v033's
                // SCRIPT_CONTRACT.md. These are parsing widths, not a claim
                // that the commands or their side effects are safe to skip.
                case 0x00: operands = 5; break;
                case 0x01: case 0x0A: case 0x0B: case 0x3A: case 0x41: operands = 1; break;
                case 0x02: case 0x0C: case 0x24: case 0x25: case 0x46: operands = 0; break;
                case 0x12: operands = 2; break;
                case 0x1C: operands = 3; break;
                case 0x1F: case 0x45:
                    if (!Fits(length, offset, 1)) return TruncatedInstruction(result, offset, opcode, length, complete);
                    operands = bytes[offset + 1] == 0xFF ? 2 : 1;
                    break;
                case 0x4B:
                    if (!Fits(length, offset, 1)) return TruncatedInstruction(result, offset, opcode, length, complete);
                    // eventLevelChange modes 0/1/2 consume mode,id,value.
                    // Modes 3/4 delegate EXP handling; their additional native
                    // consumption is deliberately not inferred from this body.
                    if (bytes[offset + 1] > 2)
                        return Stop(result, "unresolved-level-mode", "Level/EXP command mode is outside the verified three-operand variants.", offset, opcode, length);
                    operands = 3;
                    break;
                default:
                    return Stop(result, "unknown-opcode", "Instruction width/control flow is not verified; analysis stops before this byte.", offset, opcode, length);
            }
            if (!Fits(length, offset, operands)) return TruncatedInstruction(result, offset, opcode, length, complete);
            if (opcode == 0x1F || opcode == 0x45)
                result.MembershipOperations.Add(Membership(record.Index, table, offset, opcode, bytes, memberIds));
            result.DecodedInstructionCount++;
            offset += operands + 1;
            result.DecodedByteCount = offset;
        }
        return Stop(result, complete ? "no-verified-termination" : "truncated-capture",
            complete ? "Known instructions reached the array end without a verified native terminator. No implicit termination was assumed."
                : "The retained command prefix ended before the complete native array was captured.", offset, null, length);
    }

    private static DiscoveryMembershipCandidate Membership(int index, string table, int offset,
        byte opcode, byte[] bytes, DiscoveryIntArray memberIds)
    {
        int operand = bytes[offset + 1];
        bool sentinel = operand == 0xFF;
        int? source = sentinel ? bytes[offset + 2] : opcode == 0x45 ? operand : ResolveMapId(memberIds, operand);
        var candidate = new DiscoveryMembershipCandidate
        {
            Table = table, EventIndex = index, Offset = offset, Opcode = opcode,
            SourceId = source, RawOperand = operand,
            OperandMode = sentinel ? "FF,explicit-id" : opcode == 0x45 ? "direct-id" : "map-party-member-index",
            IsGrantCandidate = opcode == 0x1F || !sentinel
        };
        if (opcode == 0x1F)
        {
            candidate.Operation = "AddPartyMember membership-set candidate";
            candidate.Meaning = "Sets canonical membership bits before native party loading; first acquisition versus repeat/return is unproven.";
            if (source is null) candidate.Meaning += " The map-local character ID could not be resolved.";
        }
        else if (!sentinel)
        {
            candidate.Operation = "eventMemberIn membership-set candidate";
            candidate.Meaning = "May clear prior absence/loss state and sets canonical membership bits; acquisition or return is unproven.";
        }
        else if (source == 75)
        {
            candidate.Operation = "eventMemberIn special lifecycle operation";
            candidate.Meaning = "FF,75 sets bit 4. This is not classified as a recruitment grant.";
        }
        else
        {
            candidate.Operation = "eventMemberIn membership toggle";
            candidate.Meaning = "FF,id toggles membership bit 1. Direction and lifecycle meaning require runtime context.";
        }
        return candidate;
    }

    private static int? ResolveMapId(DiscoveryIntArray ids, int index)
    {
        if (ids.State != DiscoveryArrayState.Complete && ids.State != DiscoveryArrayState.Truncated) return null;
        if (ids.Values is null || ids.DeclaredLength is null || ids.DeclaredLength < ids.Values.Length
            || (ids.State == DiscoveryArrayState.Complete && ids.DeclaredLength != ids.Values.Length)
            || index < 0 || index >= ids.Values.Length || ids.Values[index] < 0) return null;
        return ids.Values[index];
    }

    private static DiscoveryEventAnalysis TruncatedInstruction(DiscoveryEventAnalysis result,
        int offset, byte opcode, int length, bool complete) => Stop(result,
            complete ? "truncated-instruction" : "truncated-capture",
            complete ? "The complete captured array ends inside this instruction; no membership operation is inferred from incomplete operands."
                : "The capture limit or read failure cut this instruction; no missing operand is invented.", offset, opcode, length);

    private static bool Fits(int length, int offset, int operands) => length - offset - 1 >= operands;

    private static DiscoveryEventAnalysis Stop(DiscoveryEventAnalysis result, string status,
        string explanation, int offset, int? opcode, int length)
    {
        result.Status = status; result.Explanation = explanation; result.StopOffset = offset;
        result.StopOpcode = opcode; result.UnexaminedByteCount = Math.Max(0, length - result.DecodedByteCount);
        return result;
    }

    private static bool IsUnresolved(DiscoveryEventAnalysis item) =>
        (item.Status != "native-termination" && item.Status != "null-entry" && item.Status != "empty-script")
        || item.MembershipOperations.Any(operation => operation.SourceId is null);

    private static bool CaptureLooksComplete(DiscoveryMapSnapshot snapshot) => snapshot.Issues.Count == 0
        && TableComplete(snapshot.EventsState, snapshot.DeclaredEventCount, snapshot.Events.Count)
        && TableComplete(snapshot.SourceEventsState, snapshot.DeclaredSourceEventCount, snapshot.SourceEvents.Count)
        && (snapshot.SourceTableState == DiscoveryArrayState.Complete || snapshot.SourceTableState == DiscoveryArrayState.Null)
        && ArrayComplete(snapshot.PartyMemberIds.State, snapshot.PartyMemberIds.DeclaredLength,
            snapshot.PartyMemberIds.Values?.Length, snapshot.PartyMemberIds.Issue)
        && ArrayComplete(snapshot.MapMessageIds.State, snapshot.MapMessageIds.DeclaredLength,
            snapshot.MapMessageIds.Values?.Length, snapshot.MapMessageIds.Issue)
        && TableComplete(snapshot.NpcImagesState, snapshot.DeclaredNpcCount, snapshot.NpcImages.Count)
        && snapshot.NpcImages.All(item => item.Issue is null && (item.IsNull
            || (item.FpNo.HasValue && (item.OpdState == DiscoveryArrayState.Complete || item.OpdState == DiscoveryArrayState.Null))))
        && snapshot.Events.All(RecordComplete) && snapshot.SourceEvents.All(RecordComplete);

    private static bool RecordComplete(DiscoveryEventRecord record) => record.Issues.Count == 0 && (record.IsNull
        || (ByteArrayComplete(record.EventPut) && ByteArrayComplete(record.EventForm) && ByteArrayComplete(record.EventCom)));

    private static bool ByteArrayComplete(DiscoveryByteArray array) =>
        ArrayComplete(array.State, array.DeclaredLength, array.Values?.Length, array.Issue);

    private static bool TableComplete(string state, int? declared, int actual) =>
        state == DiscoveryArrayState.Complete ? declared == actual
            : state == DiscoveryArrayState.Null && actual == 0 && (!declared.HasValue || declared == 0);

    private static bool ArrayComplete(string state, int? declared, int? actual, string? issue) => issue is null
        && (state == DiscoveryArrayState.Complete ? actual.HasValue && actual == declared
            : state == DiscoveryArrayState.Null && !actual.HasValue && !declared.HasValue);

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
    private static string Number(int? value) => value.HasValue ? Number(value.Value) : "unresolved";
    private static string Clean(string? value) => (value ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ');
}
