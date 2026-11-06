using System;
using System.Collections.Generic;

namespace SuikodenRandomizer.Core;

/// <summary>
/// Immutable presentation-command plan for the recorded Kai recruitment.
/// The caller must separately validate the live scene, run and native window
/// state. Creating this plan does not establish that a native event is eligible.
/// </summary>
public sealed class KaiQuickRecruitmentPlan
{
    /// <summary>The first native grant opcode. Nothing at or after it is skipped.</summary>
    public const int EndOffset = 30;

    private readonly byte[] _script;

    private readonly struct Instruction
    {
        internal Instruction(int offset, int opcode, int operands, bool suppress)
        {
            Offset = offset;
            Opcode = opcode;
            Operands = operands;
            Suppress = suppress;
        }

        internal int Offset { get; }
        internal int Opcode { get; }
        internal int Operands { get; }
        internal bool Suppress { get; }
        internal int NextOffset => Offset + 1 + Operands;
    }

    // GoEventCommand consumes the opcode before calling eventCommandFunction.
    // Operand counts come from the supplied native handlers, not a guessed
    // byte scan. The sound command deliberately remains in the native stream.
    private static readonly Instruction[] Instructions =
    {
        new Instruction(0, 0x0B, 1, true),  // MWINOP: main dialogue window
        new Instruction(2, 0x12, 2, true),  // EVENTNAME: window, original speaker
        new Instruction(5, 0x1C, 3, true),  // STORYMSG: window, text table, row
        new Instruction(9, 0x02, 0, true),  // PUSHMARU: confirmation wait
        new Instruction(10, 0x0C, 0, true), // MWINCL: close main dialogue window
        new Instruction(11, 0x41, 1, false),// EVENTSND: preserved native sound
        new Instruction(13, 0x00, 5, true), // WINOP: window, x, y, width, height
        new Instruction(19, 0x12, 2, true), // EVENTNAME
        new Instruction(22, 0x1C, 3, true), // STORYMSG: second captured message
        new Instruction(26, 0x02, 0, true), // PUSHMARU
        new Instruction(27, 0x01, 1, true), // WINCL: close the selected window
        new Instruction(29, 0x46, 0, true)  // EVENTWWAIT: wait for closed windows
    };

    private KaiQuickRecruitmentPlan(byte[] script) => _script = script;

    /// <summary>All expected dispatches, including the preserved sound command.</summary>
    public int CommandCount => Instructions.Length;

    public int SuppressedCommandCount => Instructions.Length - 1;

    /// <summary>
    /// Requires the existing captured script ranges and a runtime-validated
    /// presentation-only opening. Bytes 0..4 and 21 were absent from the trace:
    /// accepting their decoded structure is not a claim they were observed.
    /// The opening's layout operand is arbitrary presentation data. Bytes 45..48
    /// are also unspecified at admission, but all 49 bytes are captured for
    /// subsequent mutation checks. Native conditions, grants and cleanup remain
    /// outside this plan.
    /// </summary>
    public static bool TryCreate(IReadOnlyList<byte>? script,
        out KaiQuickRecruitmentPlan? plan, out string reason)
    {
        plan = null;
        reason = string.Empty;
        if (script is null || script.Count != KaiSceneContract.ScriptLength)
        {
            reason = "Kai quick recruitment requires the 49-byte scene script";
            return false;
        }

        var captured = new byte[KaiSceneContract.ScriptLength];
        for (int index = 0; index < captured.Length; index++) captured[index] = script[index];

        // Reuse the unchanged partial-byte contract. The metadata arguments
        // here are its reference values; actual native identity is the caller's
        // independent responsibility, not something this script-only API reads.
        if (!KaiSceneContract.MatchesEventDefinition(KaiSceneContract.EventRecordIndex,
            KaiSceneContract.SourceId, 54, 12, 3, captured))
        {
            reason = "the captured Kai command ranges do not match";
            return false;
        }
        if (captured[0] != 0x0B || captured[2] != 0x12 || captured[3] != 0
            || captured[4] != KaiSceneContract.SourceId || captured[21] != KaiSceneContract.SourceId)
        {
            reason = "the runtime opening is not a main window plus Kai speaker, or the second speaker differs";
            return false;
        }

        plan = new KaiQuickRecruitmentPlan(captured);
        return true;
    }

    /// <summary>Compares the complete runtime snapshot without exposing it.</summary>
    public bool MatchesScript(IReadOnlyList<byte>? script)
    {
        if (script is null || script.Count != _script.Length) return false;
        for (int index = 0; index < _script.Length; index++)
            if (script[index] != _script[index]) return false;
        return true;
    }

    /// <summary>
    /// Resolves only an exact presentation dispatch after its opcode has been
    /// consumed. On success nextIndex is the first byte after its operands.
    /// Sound, choices, branches, grants and every other command return false.
    /// The caller must additionally enforce session sequencing and live guards.
    /// </summary>
    public bool TryGetSuppressedCommand(int operandIndex, int opcode, out int nextIndex)
    {
        nextIndex = operandIndex;
        foreach (Instruction instruction in Instructions)
        {
            if (instruction.Suppress && operandIndex == instruction.Offset + 1
                && opcode == instruction.Opcode)
            {
                nextIndex = instruction.NextOffset;
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Describes the entire expected path, including the native sound at 11.
    /// EndOffset signals that suppression is finished before the first grant.
    /// This method is a description, not an instruction to skip a native step.
    /// </summary>
    public bool TryGetExpectedNextOpcodeOffset(int currentOpcodeOffset, out int nextOffset)
    {
        nextOffset = currentOpcodeOffset;
        foreach (Instruction instruction in Instructions)
        {
            if (instruction.Offset == currentOpcodeOffset)
            {
                nextOffset = instruction.NextOffset;
                return true;
            }
        }
        return false;
    }
}
